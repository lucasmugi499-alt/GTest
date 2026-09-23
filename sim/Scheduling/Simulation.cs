using Cascade.Sim.Content;
using Cascade.Sim.Core;
using Cascade.Sim.World;

namespace Cascade.Sim.Scheduling;

/// <summary>Lets tests and later milestones plug real systems into the fixed pipeline slots.</summary>
public sealed class SimulationOptions
{
    public Dictionary<PhaseId, IPhase> Phases { get; } = [];
    public List<IPeriodicSystem>? Weekly { get; set; }
    public List<IPeriodicSystem>? Monthly { get; set; }
    public List<ICrisisSignal> CrisisSignals { get; } = [];
}

public enum StepResult
{
    /// <summary>One crisis hour ran; the day isn't finished.</summary>
    HourAdvanced,
    /// <summary>The day's daily, weekly and monthly passes ran; <see cref="Simulation.Day"/> moved on.</summary>
    DayCompleted,
}

/// <summary>
/// The scheduler (spec Tick pipeline). One day is:
/// 1. Decide which provinces are in Crisis Time.
/// 2. If any are: 24 hourly passes of the hourly phases, over those provinces only.
/// 3. Phases 0–11 in order, committing after each.
/// 4. Weekly systems on Mondays, monthly systems on the 1st.
/// 5. Clear crisis flags that have been stable long enough.
/// </summary>
public sealed class Simulation
{
    /// <summary>Phases allowed to run hourly: spec's 2, 5, 7, 8, plus orders and timed events (D-019).</summary>
    public static readonly PhaseId[] HourlyPhases =
        [PhaseId.Orders, PhaseId.ScheduledEvents, PhaseId.GridDispatch, PhaseId.Consumption, PhaseId.Operations, PhaseId.Information];

    private readonly IPhase[] _phases;
    private readonly IPeriodicSystem[] _weekly;
    private readonly IPeriodicSystem[] _monthly;
    private readonly ICrisisSignal[] _signals;
    private readonly OrdersPhase _ordersPhase;

    private List<int> _crisisToday = [];
    /// <summary>-1: the day hasn't begun. 0–23: next crisis hour to run. 24: hours done, daily pass next.</summary>
    private int _cursor = -1;

    public ContentSet Content { get; }
    public Balance Balance => Content.Balance;
    public ScenarioDef Scenario => Content.Scenario;
    public ulong Seed { get; }
    public Calendar Calendar { get; }
    public SimWorld World { get; }
    public OrderQueue Orders { get; } = new();
    public EventQueue Events { get; } = new();

    /// <summary>The day currently being played (or about to be).</summary>
    public int Day { get; private set; }
    /// <summary>The next crisis hour to run, or -1 when not inside a crisis day.</summary>
    public int Hour => _cursor is >= 0 and < 24 && _crisisToday.Count > 0 ? _cursor : -1;
    public bool IsCrisisDay => _crisisToday.Count > 0;
    public IReadOnlyList<int> CrisisProvinces => _crisisToday;
    public IReadOnlyList<AppliedOrder> AppliedOrders => _ordersPhase.Applied;
    /// <summary>True once the scenario's last day has been played.</summary>
    public bool IsFinished => Day > Scenario.LastDay;

    public Simulation(ContentSet content, ulong seed, SimulationOptions? options = null)
    {
        options ??= new SimulationOptions();
        Content = content;
        Seed = seed;
        Calendar = new Calendar(content.Scenario.StartDate);
        World = new SimWorld(content.Scenario);

        _ordersPhase = new OrdersPhase(Orders);
        _phases = new IPhase[12];
        for (int i = 0; i < 12; i++)
        {
            var id = (PhaseId)i;
            _phases[i] = options.Phases.TryGetValue(id, out var p) ? p : DefaultPhase(id);
            if (_phases[i].Id != id) throw new ArgumentException($"Phase in slot {id} reports id {_phases[i].Id}.");
            if (_phases[i] is IHourlyPhase && !HourlyPhases.Contains(id))
                throw new ArgumentException($"Phase {id} may not run hourly (spec Crisis sub-ticks).");
        }
        _weekly = (options.Weekly ?? DefaultWeekly()).ToArray();
        _monthly = (options.Monthly ?? DefaultMonthly()).ToArray();
        _signals = options.CrisisSignals.ToArray();
    }

    public static Simulation Create(string? contentDir = null, ulong? seed = null, SimulationOptions? options = null)
    {
        var content = ContentSet.Load(contentDir ?? ContentSet.FindContentDir());
        return new Simulation(content, seed ?? content.Scenario.DefaultSeed, options);
    }

    private IPhase DefaultPhase(PhaseId id) => id switch
    {
        PhaseId.Orders => _ordersPhase,
        PhaseId.ScheduledEvents => new ScheduledEventsPhase(),
        _ when HourlyPhases.Contains(id) => new StubHourlyPhase(id),
        _ => new StubPhase(id),
    };

    private static List<IPeriodicSystem> DefaultWeekly() =>
    [
        new StubSystem(SystemId.WeeklyMarkets),
        new StubSystem(SystemId.WeeklyBonds),
        new StubSystem(SystemId.WeeklyFactions),
        new StubSystem(SystemId.WeeklyPoliticalCapital),
        new StubSystem(SystemId.WeeklyCorporations),
        new StubSystem(SystemId.WeeklyThreatRecognition),
        new StubSystem(SystemId.WeeklyAiReplan),
        new StubSystem(SystemId.WeeklyForecast),
    ];

    private static List<IPeriodicSystem> DefaultMonthly() =>
    [
        new StubSystem(SystemId.MonthlyResearch),
        new StubSystem(SystemId.MonthlyConstruction),
        new StubSystem(SystemId.MonthlyDemographics),
        new StubSystem(SystemId.MonthlyTraining),
        new StubSystem(SystemId.MonthlyBudget),
        new StubSystem(SystemId.MonthlyGovernmentDrift),
        new StubSystem(SystemId.MonthlyInsurgency),
    ];

    /// <summary>
    /// Advances by one crisis hour if today is a crisis day and hours remain, otherwise finishes the day.
    /// The UI uses this to play Crisis Time hour by hour.
    /// </summary>
    public StepResult StepHour()
    {
        if (IsFinished) throw new InvalidOperationException($"The scenario ended after day {Scenario.LastDay}.");
        if (_cursor < 0) BeginDay();

        if (_crisisToday.Count > 0 && _cursor < 24)
        {
            RunHour(_cursor);
            _cursor++;
            return StepResult.HourAdvanced;
        }

        FinishDay();
        return StepResult.DayCompleted;
    }

    /// <summary>Plays the rest of the current day, including any crisis hours.</summary>
    public void StepDay()
    {
        while (StepHour() != StepResult.DayCompleted) { }
    }

    /// <summary>Plays days until <paramref name="lastDay"/> has been completed.</summary>
    public void RunThrough(int lastDay)
    {
        while (Day <= lastDay) StepDay();
    }

    private void BeginDay()
    {
        var p = World.Provinces;
        _crisisToday = [];
        for (int i = 0; i < p.Count; i++)
        {
            bool crisis = p.InCrisis[i] || AnySignal(i) || Events.HasTimedEventIn(Day, i);
            if (crisis && !p.InCrisis[i])
            {
                p.InCrisis.Set(i, true);
                p.StableHours.Set(i, 0);
            }
            if (crisis) _crisisToday.Add(i);
        }
        World.Commit();
        _cursor = 0;
    }

    private void RunHour(int hour)
    {
        var ctx = Context(hour, (TickSlot)hour);
        foreach (var id in HourlyPhases)
        {
            if (_phases[(int)id] is IHourlyPhase hp) hp.RunHourly(ctx);
            World.Commit();
        }

        // Crisis monitor: any unstable hour resets the count toward clearing the flag.
        var p = World.Provinces;
        foreach (int i in _crisisToday)
            p.StableHours.Set(i, AnySignal(i) ? 0 : p.StableHours[i] + 1);
        World.Commit();
    }

    private void FinishDay()
    {
        var daily = Context(-1, TickSlot.Daily);
        foreach (var phase in _phases)
        {
            phase.RunDaily(daily);
            World.Commit();
        }

        if (Calendar.IsWeeklyDay(Day)) RunPeriodic(_weekly, TickSlot.Weekly);
        if (Calendar.IsMonthlyDay(Day)) RunPeriodic(_monthly, TickSlot.Monthly);

        var p = World.Provinces;
        int clearAfter = Balance.Sim.Crisis.ClearAfterStableHours;
        foreach (int i in _crisisToday)
        {
            if (p.StableHours[i] >= clearAfter)
            {
                p.InCrisis.Set(i, false);
                p.StableHours.Set(i, 0);
            }
        }
        World.Commit();

        Day++;
        _cursor = -1;
        _crisisToday = [];
    }

    private void RunPeriodic(IPeriodicSystem[] systems, TickSlot slot)
    {
        var ctx = Context(-1, slot);
        foreach (var s in systems)
        {
            s.Run(ctx);
            World.Commit();
        }
    }

    private bool AnySignal(int province)
    {
        foreach (var s in _signals)
            if (s.IsUnstable(World, province)) return true;
        return false;
    }

    private TickContext Context(int hour, TickSlot slot) => new()
    {
        World = World,
        Balance = Balance,
        Calendar = Calendar,
        Seed = Seed,
        Day = Day,
        Hour = hour,
        Slot = slot,
        CrisisProvinces = _crisisToday,
        Events = Events,
    };

    /// <summary>
    /// Hash of the full sim state (spec Desync check). Two runs with the same seed and the same orders
    /// must produce the same hash at every step.
    /// </summary>
    public ulong StateHash()
    {
        var h = new StateHasher();
        h.Section("cascade-state-v1").Add(Scenario.Id).Add(Seed).Add(Day).Add(_cursor);
        h.Add(_crisisToday.Count);
        foreach (int i in _crisisToday) h.Add(i);
        h.Add(World).Add(Orders).Add(Events);
        return h.Value;
    }
}
