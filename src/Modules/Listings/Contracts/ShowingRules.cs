using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Listings.Contracts;

/// <summary>
/// The seller's showing rules, embedded in the listing document because they are always read with it.
/// Times are wall clock times in the listing's IANA time zone; dates are local dates.
/// </summary>
public sealed class ShowingRules
{
    public int SlotMinutes { get; set; } = 30;
    public int BufferMinutes { get; set; }
    public int LeadTimeHours { get; set; } = 12;
    public int HorizonDays { get; set; } = 14;
    public bool AutoConfirm { get; set; }
    public List<WeeklyWindow> Weekly { get; set; } = [];
    public List<string> BlackoutDates { get; set; } = [];

    public IEnumerable<string> Validate()
    {
        if (SlotMinutes is < 10 or > 240) yield return "slotMinutes must be between 10 and 240.";
        if (BufferMinutes is < 0 or > 120) yield return "bufferMinutes must be between 0 and 120.";
        if (LeadTimeHours is < 0 or > 336) yield return "leadTimeHours must be between 0 and 336.";
        if (HorizonDays is < 1 or > 90) yield return "horizonDays must be between 1 and 90.";
        foreach (var w in Weekly)
        {
            if (!Enum.TryParse<IsoDayOfWeek>(w.DayOfWeek, true, out var d) || d == IsoDayOfWeek.None)
                yield return $"'{w.DayOfWeek}' is not a day of week.";
            if (!TimeOfDay.TryParse(w.Start, out var s) || !TimeOfDay.TryParse(w.End, out var e))
                yield return $"Window {w.DayOfWeek} {w.Start}-{w.End} must use HH:mm.";
            else if (e <= s) yield return $"Window {w.DayOfWeek} {w.Start}-{w.End} must end after it starts.";
        }
        foreach (var group in Weekly.Where(w => TimeOfDay.TryParse(w.Start, out _) && TimeOfDay.TryParse(w.End, out _))
                     .GroupBy(w => w.DayOfWeek, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(w => TimeOfDay.Parse(w.Start)).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (TimeOfDay.Parse(ordered[i].Start) < TimeOfDay.Parse(ordered[i - 1].End))
                    yield return $"Windows on {group.Key} overlap.";
        }
        foreach (var b in BlackoutDates)
            if (!LocalDatePattern.Iso.Parse(b).Success) yield return $"Blackout date '{b}' must be yyyy-MM-dd.";
    }
}

public sealed class WeeklyWindow
{
    public string DayOfWeek { get; set; } = "Saturday";
    public string Start { get; set; } = "09:00";
    public string End { get; set; } = "12:00";

    public IsoDayOfWeek Day => Enum.Parse<IsoDayOfWeek>(DayOfWeek, true);
    public LocalTime StartTime => TimeOfDay.Parse(Start);
    public LocalTime EndTime => TimeOfDay.Parse(End);
}

public static class TimeOfDay
{
    private static readonly LocalTimePattern Pattern = LocalTimePattern.CreateWithInvariantCulture("HH:mm");
    public static bool TryParse(string? s, out LocalTime t)
    {
        var r = Pattern.Parse(s ?? "");
        t = r.Success ? r.Value : default;
        // "24:00" is a common way to write end of day.
        if (!r.Success && s == "24:00") { t = LocalTime.MaxValue; return true; }
        return r.Success;
    }
    public static LocalTime Parse(string s) => TryParse(s, out var t) ? t : throw new FormatException(s);
    public static string Format(LocalTime t) => Pattern.Format(t);
}
