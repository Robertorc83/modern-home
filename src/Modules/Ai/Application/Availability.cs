using System.Globalization;
using HomeShowing.BuildingBlocks.Results;
using NodaTime;
using NodaTime.Text;

namespace HomeShowing.Ai.Application;

/// <summary>A recurring weekly window in the buyer's zone, e.g. Mon-Fri 18:00-21:00.</summary>
public sealed record AvailabilityWindow(IReadOnlyList<string> Days, string Start, string End);

public sealed record ParsedAvailability(IReadOnlyList<AvailabilityWindow> Windows, IReadOnlyList<string> Warnings);

public sealed record ParseRequest(string Text, string TimeZone, string? Now = null);

public sealed record LocalRangeView(string Start, string End);

public sealed record ParseResponse(string Provider, IReadOnlyList<AvailabilityWindow> Windows, IReadOnlyList<LocalRangeView> Ranges, string Summary, IReadOnlyList<string> Warnings);

/// <summary>
/// Turns free text availability into structured windows. Implementations: Claude (when ANTHROPIC_API_KEY is set)
/// and a deterministic rule based parser (always available). Output is never used directly: the UI shows it to the
/// buyer for review before searching.
/// </summary>
public interface IAvailabilityParser
{
    string Provider { get; }
    Task<ParsedAvailability> ParseAsync(string text, DateTimeZone zone, ZonedDateTime now, CancellationToken ct);
}

public sealed class AvailabilityParsingService(IAvailabilityParser parser, IClock clock)
{
    public const int HorizonDays = 14;
    public string Provider => parser.Provider;

    public async Task<Result<ParseResponse>> ParseAsync(ParseRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return Error.Validation("Describe when you are free, e.g. \"weekday evenings after 6, Saturday morning\".");
        if (req.Text.Length > 500) return Error.Validation("Keep it under 500 characters.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(req.TimeZone ?? "");
        if (zone is null) return Error.Validation($"timeZone '{req.TimeZone}' is not an IANA zone.");
        var instant = clock.GetCurrentInstant();
        if (req.Now is not null && OffsetDateTimePattern.ExtendedIso.Parse(req.Now) is { Success: true } o) instant = o.Value.ToInstant();
        else if (req.Now is not null && InstantPattern.ExtendedIso.Parse(req.Now) is { Success: true } i) instant = i.Value;
        var now = instant.InZone(zone);

        var parsed = await parser.ParseAsync(req.Text, zone, now, ct);
        var windows = WindowRules.Validate(parsed.Windows, out var problems);
        var warnings = parsed.Warnings.Concat(problems).ToList();
        if (windows.Count == 0) warnings.Add("I couldn't find any days or times in that. Try \"weekday evenings\" or \"Saturday 9-12\".");
        return new ParseResponse(parser.Provider, windows, Expand(windows, now), Summarize(windows), warnings);
    }

    /// <summary>Expands weekly windows into concrete local ranges for the next two weeks, skipping the past.</summary>
    public static IReadOnlyList<LocalRangeView> Expand(IReadOnlyList<AvailabilityWindow> windows, ZonedDateTime now)
    {
        var pattern = LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");
        var ranges = new List<(LocalDateTime S, LocalDateTime E)>();
        for (var d = 0; d < HorizonDays; d++)
        {
            var date = now.Date.PlusDays(d);
            foreach (var w in windows.Where(w => w.Days.Contains(date.DayOfWeek.ToString())))
            {
                var start = date + WindowRules.Time(w.Start);
                var end = w.End == "24:00" ? date.PlusDays(1).AtMidnight() : date + WindowRules.Time(w.End);
                if (end <= now.LocalDateTime) continue;
                if (start < now.LocalDateTime) start = RoundUp(now.LocalDateTime);
                if (end > start) ranges.Add((start, end));
            }
        }
        return ranges.OrderBy(r => r.S).Select(r => new LocalRangeView(pattern.Format(r.S), pattern.Format(r.E))).ToList();
    }

    private static LocalDateTime RoundUp(LocalDateTime t)
    {
        var minutes = (t.Minute / 15 + 1) * 15;
        return t.Date.At(new LocalTime(t.Hour, 0)).PlusMinutes(minutes);
    }

    public static string Summarize(IReadOnlyList<AvailabilityWindow> windows)
    {
        if (windows.Count == 0) return "No availability found.";
        static string Fmt(string hhmm) => hhmm == "24:00" ? "midnight" : LocalTimePattern.CreateWithInvariantCulture("h:mm tt").Format(WindowRules.Time(hhmm));
        return string.Join("; ", windows.Select(w => $"{DayList(w.Days)} {Fmt(w.Start)}–{Fmt(w.End)}"));
    }

    private static string DayList(IReadOnlyList<string> days)
    {
        var set = days.ToHashSet();
        string[] weekdays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
        if (set.Count == 7) return "Every day";
        if (set.SetEquals(weekdays)) return "Weekdays";
        if (set.SetEquals(["Saturday", "Sunday"])) return "Weekends";
        if (set.IsSubsetOf(weekdays) && set.Count == 4) return $"Weekdays except {weekdays.Except(set).Single()}";
        return string.Join(", ", WindowRules.AllDays.Where(set.Contains).Select(d => d[..3]));
    }
}

public static class WindowRules
{
    public static readonly string[] AllDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
    private static readonly LocalTimePattern Hhmm = LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    public static LocalTime Time(string s) => s == "24:00" ? LocalTime.MaxValue : Hhmm.Parse(s).Value;

    /// <summary>Server side validation of any parser's output (the model's included): known days, HH:mm, start before end.</summary>
    public static List<AvailabilityWindow> Validate(IEnumerable<AvailabilityWindow> input, out List<string> problems)
    {
        problems = [];
        var ok = new List<AvailabilityWindow>();
        foreach (var w in input.Take(21))
        {
            var days = w.Days.Select(d => AllDays.FirstOrDefault(a => a.Equals(d, StringComparison.OrdinalIgnoreCase))).Where(d => d is not null).Cast<string>().Distinct()
                .OrderBy(d => Array.IndexOf(AllDays, d)).ToList();
            var startOk = Hhmm.Parse(w.Start ?? "").Success;
            var endOk = w.End == "24:00" || Hhmm.Parse(w.End ?? "").Success;
            if (days.Count == 0 || !startOk || !endOk) { problems.Add($"Ignored an unreadable window ({string.Join("/", w.Days)} {w.Start}-{w.End})."); continue; }
            if (Time(w.End!) <= Time(w.Start!)) { problems.Add($"Ignored {w.Start}-{w.End}: it ends before it starts."); continue; }
            ok.Add(new AvailabilityWindow(days, w.Start!, w.End!));
        }
        return ok;
    }
}
