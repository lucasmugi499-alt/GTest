using System.Globalization;
using Cascade.Sim;
using Cascade.Sim.Content;
using Cascade.Sim.Narrative;
using Cascade.Sim.Scheduling;
using Godot;

/// <summary>
/// Owns the simulation and moves its clock. The UI reads snapshots from here and sends orders through here;
/// nothing in the game project touches sim state directly.
/// Speeds (D-011): 1 = 4 s per day, 2 = 2 s, 3 = 1 s; in Crisis Time a step is one hour, so hours pass 24 times faster.
/// </summary>
public partial class SimController : Node
{
    public Simulation Sim { get; private set; } = null!;
    public SimSnapshot Snapshot { get; private set; } = null!;
    /// <summary>0 = paused.</summary>
    public int Speed { get; private set; }
    public bool AutoPaused { get; private set; }
    /// <summary>For automated runs: answer every decision with this and run flat out.</summary>
    public AutoMode? Autopilot { get; set; }

    private readonly HashSet<long> _answered = [];
    private double _accumulated;
    private bool _lastStepWasHour;
    private static readonly double[] SecondsPerDay = [0, 4, 2, 1];

    public event Action? Changed;
    public event Action<DecisionView>? DecisionNeeded;
    public event Action? Finished;

    public void Start(ulong? seed = null)
    {
        var content = ContentSet.Load(ContentDir());
        Sim = new Simulation(content, seed ?? content.Scenario.DefaultSeed);
        Snapshot = SimSnapshot.Of(Sim);
        Changed?.Invoke();
    }

    /// <summary>The repo's content folder: next to the Godot project in development.</summary>
    private static string ContentDir()
    {
        var beside = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "content"));
        return System.IO.File.Exists(System.IO.Path.Combine(beside, ContentSet.BalanceFile)) ? beside : ContentSet.FindContentDir();
    }

    public void SetSpeed(int speed)
    {
        speed = Math.Clamp(speed, 0, 3);
        if (speed == Speed && !(speed > 0 && AutoPaused)) return;
        Speed = speed;
        if (Speed > 0) AutoPaused = false;
        _accumulated = 0;
        Changed?.Invoke();
    }

    public bool IsAnswered(long seq) => _answered.Contains(seq);

    public void Choose(DecisionView d, int choice)
    {
        Sim.Orders.Enqueue(new ChooseStoryletOrder(Sim.World.Nations.Player, d.Seq, choice));
        _answered.Add(d.Seq);
        Changed?.Invoke();
    }

    public void SendOrder(Order order)
    {
        Sim.Orders.Enqueue(order);
        Changed?.Invoke();
    }

    /// <summary>Advance one step now (the Step button, and every tick of the clock).</summary>
    public void StepOnce()
    {
        if (Sim.IsFinished) return;
        _lastStepWasHour = Sim.StepHour() == StepResult.HourAdvanced;
        _answered.RemoveWhere(seq => Sim.World.Storylets.Find(seq) is { Pending: false });
        Snapshot = SimSnapshot.Of(Sim);
        Changed?.Invoke();
        if (Sim.IsFinished) { Speed = 0; Finished?.Invoke(); return; }
        var major = Snapshot.Decisions.FirstOrDefault(d => d.Major && !_answered.Contains(d.Seq));
        if (major is not null && Autopilot is null)
        {
            Speed = 0;
            AutoPaused = true;
            DecisionNeeded?.Invoke(major);
        }
    }

    public override void _Process(double delta)
    {
        if (Sim is null || Sim.IsFinished) return;
        if (Autopilot is { } mode)
        {
            // Automated run: many steps per frame, the autopilot answering everything.
            for (int i = 0; i < 48 && !Sim.IsFinished && Autopilot is not null; i++)
            {
                Cascade.Sim.Narrative.Autopilot.Answer(Sim, mode, _answered);
                StepOnce();
            }
            return;
        }
        if (Speed == 0) return;
        _accumulated += delta;
        double step = SecondsPerDay[Speed] / (_lastStepWasHour || Sim.IsCrisisDay ? 24 : 1);
        if (_accumulated >= step)
        {
            _accumulated -= step;
            StepOnce();
        }
    }

    public string When()
    {
        var day = Sim.Calendar.Describe(Math.Min(Sim.Day, Sim.Scenario.LastDay));
        return Sim.Hour >= 0 ? $"{day}  {Sim.Hour:00}:00" : day;
    }

    public static string N(double x, string format = "0") => x.ToString(format, CultureInfo.InvariantCulture);
}
