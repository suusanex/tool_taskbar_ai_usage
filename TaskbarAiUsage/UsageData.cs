using System.Globalization;
using System.Text.Json;

namespace TaskbarAiUsage;

public sealed record UsageMetric(int RemainingPercent, DateTimeOffset? ResetsAt);

public sealed record UsageValues(UsageMetric? Codex5Hour, UsageMetric? CodexWeekly, UsageMetric? GrokWeekly, string? CodexError, string? GrokError)
{
    public static UsageValues Empty(string? codexError = null, string? grokError = null) => new(null, null, null, codexError, grokError);
}

public static class UsageParser
{
    public static UsageValues Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("Tokscale output must be a JSON array.");

        var providers = document.RootElement.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Object).ToArray();
        var codex = providers.Where(p => ProviderIs(p, "Codex")).ToArray();
        var grok = providers.FirstOrDefault(p => ProviderIs(p, "Grok"));
        UsageMetric? fiveHour = null;
        UsageMetric? codexWeekly = null;
        UsageMetric? grokWeekly = null;
        string? codexError = null;
        string? grokError = null;

        if (codex.Length == 0)
            codexError = "Tokscale did not return a Codex provider.";
        else
        {
            var selected = SelectCodex(codex);
            if (selected is null)
                codexError = "Multiple Codex accounts were returned without exactly one active account.";
            else
            {
                fiveHour = FindMetric(selected.Value, "5h", out var fiveError);
                codexWeekly = FindMetric(selected.Value, "Weekly", out var weekError);
                codexError = Combine(fiveError, weekError);
            }
        }

        if (grok.ValueKind == JsonValueKind.Undefined)
            grokError = "Tokscale did not return a Grok provider.";
        else
        {
            grokWeekly = FindMetric(grok, "Weekly", out grokError);
        }

        return new(fiveHour, codexWeekly, grokWeekly, codexError, grokError);
    }

    private static JsonElement? SelectCodex(JsonElement[] entries)
    {
        if (entries.Length == 1)
            return entries[0];

        var active = entries.Where(entry =>
            entry.TryGetProperty("account", out var account) &&
            account.ValueKind == JsonValueKind.Object &&
            account.TryGetProperty("is_active", out var isActive) &&
            isActive.ValueKind == JsonValueKind.True).ToArray();
        return active.Length == 1 ? active[0] : null;
    }

    private static UsageMetric? FindMetric(JsonElement provider, string label, out string? error)
    {
        error = null;
        if (provider.ValueKind != JsonValueKind.Object || !provider.TryGetProperty("metrics", out var metrics) || metrics.ValueKind != JsonValueKind.Array)
        {
            error = $"Tokscale did not return {label} metrics.";
            return null;
        }

        foreach (var metric in metrics.EnumerateArray())
        {
            if (metric.ValueKind != JsonValueKind.Object)
                continue;
            if (!metric.TryGetProperty("label", out var labelValue) || labelValue.ValueKind != JsonValueKind.String ||
                !string.Equals(labelValue.GetString(), label, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!metric.TryGetProperty("remaining_percent", out var percent) || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDecimal(out var value))
            {
                error = $"Tokscale returned an invalid {label} percentage.";
                return null;
            }
            if (value is < 0 or > 100)
            {
                error = $"Tokscale returned an out-of-range {label} percentage.";
                return null;
            }

            DateTimeOffset? reset = null;
            if (metric.TryGetProperty("resets_at", out var resetValue) && resetValue.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(resetValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                reset = parsed;
            return new(decimal.ToInt32(decimal.Truncate(value)), reset);
        }

        error = $"Tokscale did not return the {label} metric.";
        return null;
    }

    private static bool ProviderIs(JsonElement provider, string expected) =>
        provider.ValueKind == JsonValueKind.Object && provider.TryGetProperty("provider", out var name) && name.ValueKind == JsonValueKind.String &&
        string.Equals(name.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static string? Combine(string? first, string? second) =>
        first is null ? second : second is null ? first : $"{first} {second}";
}
