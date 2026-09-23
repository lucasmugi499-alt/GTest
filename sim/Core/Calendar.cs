namespace Cascade.Sim.Core;

/// <summary>Maps sim days to calendar dates. Weekly systems run on Mondays, monthly on the 1st (spec, D-004).</summary>
public sealed class Calendar
{
    public DateOnly Start { get; }

    public Calendar(DateOnly start) => Start = start;

    public DateOnly DateOf(int day) => Start.AddDays(day);
    public bool IsWeeklyDay(int day) => DateOf(day).DayOfWeek == DayOfWeek.Monday;
    public bool IsMonthlyDay(int day) => DateOf(day).Day == 1;

    /// <summary>"Day 4 · Fri 7 Mar 2031" — invariant, never localized.</summary>
    public string Describe(int day)
    {
        var d = DateOf(day);
        return $"Day {day} · {d.DayOfWeek.ToString()[..3]} {d.Day} {Months[d.Month - 1]} {d.Year}";
    }

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}
