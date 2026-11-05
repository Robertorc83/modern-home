using System.Text.RegularExpressions;
using HomeShowing.Ai.Application;
using NodaTime;

namespace HomeShowing.Ai.Infrastructure;

/// <summary>
/// Deterministic parser for common phrasing, so the app works fully without an API key:
/// weekdays, weekends, day names, mornings / afternoons / evenings, "after 6", "before 11", "9-12",
/// "between 2 and 5", "except Thursday", "today", "tomorrow". Clauses are split on commas, semicolons and "and".
/// </summary>
public sealed partial class RuleBasedAvailabilityParser : IAvailabilityParser
{
    public string Provider => "rule-based";

    private static readonly (string Name, string[] Days)[] DayWords =
    [
        ("weekdays?|work ?days?", ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]),
        ("weekends?", ["Saturday", "Sunday"]),
        ("every ?day|daily|any ?day|all week", WindowRules.AllDays),
        ("mon(day)?s?", ["Monday"]), ("tue(s|sday)?s?", ["Tuesday"]), ("wed(nesday)?s?", ["Wednesday"]),
        ("thu(r|rs|rsday)?s?", ["Thursday"]), ("fri(day)?s?", ["Friday"]), ("sat(urday)?s?", ["Saturday"]), ("sun(day)?s?", ["Sunday"]),
    ];

    private static readonly (string Pattern, int Start, int End)[] Parts =
    [
        ("early mornings?", 7, 9), ("mornings?|am\\b", 8, 12), ("lunch(time)?|midday|noon", 12, 13), ("afternoons?", 12, 17),
        ("evenings?|nights?|after work", 17, 21),
    ];

    public Task<ParsedAvailability> ParseAsync(string text, DateTimeZone zone, ZonedDateTime now, CancellationToken ct)
    {
        var warnings = new List<string>();
        var windows = new List<AvailabilityWindow>();
        var normalized = text.ToLowerInvariant().Replace("–", "-").Replace("—", "-");
        normalized = Today().Replace(normalized, now.DayOfWeek.ToString().ToLowerInvariant());
        normalized = Tomorrow().Replace(normalized, now.Date.PlusDays(1).DayOfWeek.ToString().ToLowerInvariant());
        normalized = Between().Replace(normalized, "$1-$2"); // "between 2 and 5" must survive the split on "and"
        var clauses = ClauseSplit().Split(normalized).Select(c => c.Trim()).Where(c => c.Length > 0).ToList();

        string[]? previousDays = null;
        foreach (var clause in clauses)
        {
            var (include, exclude) = SplitExcept(clause);
            var days = FindDays(include);
            var excluded = FindDays(exclude);
            var time = FindTime(include);

            // "..., except Thursday": a bare exclusion applies to the previous clause.
            if (days.Count == 0 && time is null && excluded.Count > 0 && windows.Count > 0)
            {
                var last = windows[^1];
                windows[^1] = last with { Days = last.Days.Except(excluded).ToList() };
                continue;
            }
            if (days.Count == 0 && time is null) { if (clause.Length > 2) warnings.Add($"Skipped \"{clause}\": no day or time I recognise."); continue; }

            var effectiveDays = days.Count > 0 ? days.ToArray() : previousDays ?? WindowRules.AllDays;
            effectiveDays = effectiveDays.Except(excluded).ToArray();
            var (start, end) = time ?? (9, 18);
            if (time is null) warnings.Add($"No time given for \"{clause}\"; assumed 9 AM to 6 PM.");
            windows.Add(new AvailabilityWindow(effectiveDays.OrderBy(d => Array.IndexOf(WindowRules.AllDays, d)).ToList(), Fmt(start), Fmt(end)));
            previousDays = effectiveDays;
        }
        return Task.FromResult(new ParsedAvailability(windows, warnings));
    }

    private static (string Include, string Exclude) SplitExcept(string clause)
    {
        var m = Except().Match(clause);
        return m.Success ? (clause[..m.Index], clause[(m.Index + m.Length)..]) : (clause, "");
    }

    private static List<string> FindDays(string s)
    {
        var found = new List<string>();
        foreach (var (pattern, days) in DayWords)
            if (Regex.IsMatch(s, $@"\b({pattern})\b")) found.AddRange(days);
        return found.Distinct().ToList();
    }

    private static (double Start, double End)? FindTime(string s)
    {
        (double S, double E)? part = null;
        foreach (var (pattern, start, end) in Parts)
            if (Regex.IsMatch(s, $@"\b({pattern})")) { part = (start, end); break; }

        var range = Range().Match(s);
        if (range.Success)
        {
            var a = Hour(range.Groups["h1"].Value, range.Groups["m1"].Value, range.Groups["ap1"].Value.Length > 0 ? range.Groups["ap1"].Value : range.Groups["ap2"].Value, part);
            var b = Hour(range.Groups["h2"].Value, range.Groups["m2"].Value, range.Groups["ap2"].Value, part, after: a);
            if (b > a) return (a, b);
        }
        var after = After().Match(s);
        if (after.Success)
        {
            var a = Hour(after.Groups["h"].Value, after.Groups["m"].Value, after.Groups["ap"].Value, part ?? (17, 21));
            var end = part is { } p && p.E > a ? p.E : Math.Max(a + 3, 21);
            return (a, Math.Min(end, 24));
        }
        var before = Before().Match(s);
        if (before.Success)
        {
            var b = Hour(before.Groups["h"].Value, before.Groups["m"].Value, before.Groups["ap"].Value, part);
            var start = part is { } p && p.S < b ? p.S : 8;
            return b > start ? (start, b) : null;
        }
        return part;
    }

    /// <summary>"6" means 6 PM in an evening context or for 1-7 (nobody tours a house at 6 AM); "11" means 11 AM.</summary>
    private static double Hour(string h, string m, string ampm, (double S, double E)? part, double? after = null)
    {
        var hour = int.Parse(h) % 24;
        var minutes = string.IsNullOrEmpty(m) ? 0 : int.Parse(m) / 60.0;
        if (ampm.StartsWith('p') && hour < 12) hour += 12;
        else if (ampm.StartsWith('a') && hour == 12) hour = 0;
        else if (ampm.Length == 0 && hour < 12)
        {
            if (part is { S: >= 12 } || hour <= 7 || (after is { } a && hour + 12 > a && hour <= a)) hour += 12;
        }
        return hour + minutes;
    }

    private static string Fmt(double hours)
    {
        if (hours >= 24) return "24:00";
        var h = (int)hours;
        var m = (int)Math.Round((hours - h) * 60);
        return $"{h:00}:{m:00}";
    }

    [GeneratedRegex(@"\s*(?:,|;|\band\b|\bor\b|\bplus\b|\balso\b|&)\s*")]
    private static partial Regex ClauseSplit();
    [GeneratedRegex(@"\b(except|but not|other than|excluding|not on|apart from)\b")]
    private static partial Regex Except();
    [GeneratedRegex(@"(?:from\s+|between\s+)?(?<h1>\d{1,2})(?::(?<m1>\d{2}))?\s*(?<ap1>am|pm|a\.m\.|p\.m\.)?\s*(?:-|to|until|till|and)\s*(?<h2>\d{1,2})(?::(?<m2>\d{2}))?\s*(?<ap2>am|pm|a\.m\.|p\.m\.)?")]
    private static partial Regex Range();
    [GeneratedRegex(@"\b(?:after|from|past|starting(?: at)?)\s+(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?")]
    private static partial Regex After();
    [GeneratedRegex(@"\b(?:before|until|till|by)\s+(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?")]
    private static partial Regex Before();
    [GeneratedRegex(@"\bbetween\s+(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)\s+and\s+(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)")]
    private static partial Regex Between();
    [GeneratedRegex(@"\btoday\b")]
    private static partial Regex Today();
    [GeneratedRegex(@"\btomorrow\b")]
    private static partial Regex Tomorrow();
}
