namespace Cascade.Sim.Scheduling;

/// <summary>The twelve daily phases, in spec order (Tick pipeline). The numbers are the run order.</summary>
public enum PhaseId
{
    Orders = 0,
    ScheduledEvents = 1,
    GridDispatch = 2,
    Production = 3,
    Logistics = 4,
    Consumption = 5,
    Military = 6,
    Operations = 7,
    Information = 8,
    Society = 9,
    Narrative = 10,
    Record = 11,
}

/// <summary>
/// The "system" coordinate of every random roll. Values are part of the replay format: never renumber,
/// only append. Phases use their phase number; weekly systems start at 100, monthly at 200.
/// </summary>
public enum SystemId : uint
{
    Orders = 0,
    ScheduledEvents = 1,
    GridDispatch = 2,
    Production = 3,
    Logistics = 4,
    Consumption = 5,
    Military = 6,
    Operations = 7,
    Information = 8,
    Society = 9,
    Narrative = 10,
    Record = 11,

    // Weekly (spec: Weekly and monthly phases), in spec order.
    WeeklyMarkets = 100,
    WeeklyBonds = 101,
    WeeklyFactions = 102,
    WeeklyPoliticalCapital = 103,
    WeeklyCorporations = 104,
    WeeklyThreatRecognition = 105,
    WeeklyAiReplan = 106,
    WeeklyForecast = 107,

    // Monthly, in spec order.
    MonthlyResearch = 200,
    MonthlyConstruction = 201,
    MonthlyDemographics = 202,
    MonthlyTraining = 203,
    MonthlyBudget = 204,
    MonthlyGovernmentDrift = 205,
    MonthlyInsurgency = 206,

    /// <summary>Reserved for tests and tools.</summary>
    TestProbe = 900,
}

/// <summary>Which kind of entity a roll or event concerns. Part of the replay format: never renumber.</summary>
public enum EntityKind : byte
{
    None = 0,
    Nation = 1,
    Province = 2,
}

/// <summary>Packs (kind, id) into the "entity" coordinate of a random roll.</summary>
public static class EntityRef
{
    public static ulong Of(EntityKind kind, int id) => ((ulong)kind << 32) | (uint)id;
    public static readonly ulong None = 0;
}
