using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using HomeShowing.Ai.Application;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace HomeShowing.Ai.Infrastructure;

/// <summary>
/// Claude Haiku with structured output (a strict JSON schema), chosen because parsing a sentence is a small,
/// latency sensitive task. The model's output is still validated server side (<see cref="WindowRules.Validate"/>),
/// and any failure falls back to the deterministic parser so the buyer is never stuck.
/// </summary>
public sealed class ClaudeAvailabilityParser(AnthropicClient client, RuleBasedAvailabilityParser fallback, ILogger<ClaudeAvailabilityParser> log) : IAvailabilityParser
{
    public const string Model = "claude-haiku-5-5";
    public string Provider => Model;

    private const string System = """
        You convert a home buyer's description of when they are free to tour homes into weekly time windows.
        Rules:
        - Output only windows the buyer actually described. Days are full English names (Monday..Sunday).
        - Times are 24 hour HH:mm in the buyer's own time zone. Use "24:00" only for end of day.
        - Defaults when the buyer is vague: morning 08:00-12:00, afternoon 12:00-17:00, evening 17:00-21:00,
          "after N" ends at 21:00 unless they say otherwise; bare hours 1-7 mean PM.
        - "weekdays" = Monday-Friday, "weekends" = Saturday and Sunday. Apply "except ..." exclusions.
        - Resolve "today", "tomorrow", "this weekend" against the current date given in the message.
        - If something is ambiguous, pick the most likely reading and explain it in one short note.
        """;

    private static readonly Dictionary<string, JsonElement> Schema = new()
    {
        ["type"] = J("object"),
        ["additionalProperties"] = J(false),
        ["required"] = J(new[] { "windows", "notes" }),
        ["properties"] = J(new
        {
            windows = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "days", "start", "end" },
                    properties = new
                    {
                        days = new { type = "array", items = new { type = "string", @enum = WindowRules.AllDays } },
                        start = new { type = "string", description = "HH:mm, 24 hour" },
                        end = new { type = "string", description = "HH:mm, 24 hour, or 24:00" },
                    },
                },
            },
            notes = new { type = "array", items = new { type = "string" } },
        }),
    };

    public async Task<ParsedAvailability> ParseAsync(string text, DateTimeZone zone, ZonedDateTime now, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var message = await client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 2048,
                System = System,
                Messages = [new UserMessageParam
                {
                    Content = $"Now: {now.LocalDateTime:yyyy-MM-dd HH:mm} ({now.DayOfWeek}), time zone {zone.Id}.\nBuyer: \"{text}\"",
                }],
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = Schema } },
            }, timeout.Token);

            var json = string.Concat(message.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            var result = JsonSerializer.Deserialize<ModelOutput>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                         ?? throw new InvalidOperationException("Empty model output.");
            return new ParsedAvailability(result.Windows.Select(w => new AvailabilityWindow(w.Days, w.Start, w.End)).ToList(), result.Notes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Claude availability parsing failed; using the rule based parser.");
            var fb = await fallback.ParseAsync(text, zone, now, ct);
            return fb with { Warnings = [.. fb.Warnings, "The AI parser was unavailable, so a rule based parser read this instead."] };
        }
    }

    private static JsonElement J(object v) => JsonSerializer.SerializeToElement(v);

    private sealed record ModelWindow(List<string> Days, string Start, string End);
    private sealed record ModelOutput(List<ModelWindow> Windows, List<string> Notes);
}
