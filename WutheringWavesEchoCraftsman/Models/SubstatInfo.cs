using System.Globalization;
using System.Text.RegularExpressions;

namespace WutheringWavesEchoCraftsman.Models;

public sealed record SubstatInfo(string Key, string DisplayName, double MinValue, double MaxValue, string[] Aliases)
{
    public static IReadOnlyList<SubstatInfo> All { get; } =
    [
        new("crit_rate", "크리티컬", 6.3, 10.5, ["크리티컬", "크리", "치명타", "crit"]),
        new("crit_damage", "크리티컬 피해", 12.6, 21.0, ["크리티컬피해", "크피", "치명타피해", "critdamage"]),
        new("atk_percent", "공격력(%)", 6.4, 11.6, ["공격력%", "공격력퍼센트", "공격력비율", "atk%"]),
        new("atk_flat", "공격력", 30, 60, ["공격력", "atk"]),
        new("hp_percent", "HP(%)", 6.4, 11.6, ["hp%", "hp퍼센트", "hp비율", "체력%", "체력퍼센트", "체력비율"]),
        new("hp_flat", "HP", 320, 580, ["hp", "체력"]),
        new("def_percent", "방어력(%)", 8.1, 14.7, ["방어력%", "방어력퍼센트", "방어력비율", "def%"]),
        new("def_flat", "방어력", 30, 70, ["방어력", "def"]),
        new("energy_regen", "공명 효율", 6.8, 12.4, ["공명효율", "공효", "에너지회복", "energyregen"]),
        new("basic_damage", "일반 공격 피해", 6.4, 11.6, ["일반공격피해", "일반공격피해보너스", "일반피증", "평타피증"]),
        new("heavy_damage", "강공격 피해", 6.4, 11.6, ["강공격피해", "강공격피해보너스", "강공피증"]),
        new("skill_damage", "공명 스킬 피해", 6.4, 11.6, ["공명스킬피해", "공명스킬피해보너스", "스킬피증"]),
        new("liberation_damage", "공명 해방 피해", 6.4, 11.6, ["공명해방피해", "공명해방피해보너스", "해방피증"]),
    ];

    public static string NormalizeText(string text)
    {
        var normalized = text
            .Replace("％", "%", StringComparison.Ordinal)
            .Replace("﹪", "%", StringComparison.Ordinal)
            .Replace("°", "%", StringComparison.Ordinal)
            .Replace("퍼센트", "%", StringComparison.Ordinal)
            .Replace("percent", "%", StringComparison.OrdinalIgnoreCase);

        return Regex.Replace(normalized, @"[^\p{L}\p{N}%]+", string.Empty).ToLowerInvariant();
    }

    public static SubstatInfo? FindByText(string text)
    {
        var normalized = NormalizeText(text);
        if (normalized.Length == 0)
        {
            return null;
        }

        var hasPercentMarker = normalized.Contains('%', StringComparison.Ordinal);

        var exactMatch = All.FirstOrDefault(stat => NormalizeText(stat.DisplayName) == normalized)
            ?? All.FirstOrDefault(stat => stat.Aliases.Any(alias => NormalizeText(alias) == normalized));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        return All
            .SelectMany(stat => stat.Aliases.Select(alias => new
            {
                Stat = stat,
                NormalizedAlias = NormalizeText(alias),
            }))
            .Where(item => !item.NormalizedAlias.Contains('%', StringComparison.Ordinal) || hasPercentMarker)
            .OrderByDescending(item => item.NormalizedAlias.Length)
            .FirstOrDefault(item => normalized.Contains(item.NormalizedAlias, StringComparison.Ordinal))
            ?.Stat;
    }

    public static IReadOnlyList<ParsedSubstat> ParseLines(string ocrText)
    {
        var lines = ocrText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeOcrLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !IsIgnorableNoiseLine(line))
            .ToArray();

        var sequential = ParseSequential(lines);
        var batched = ParseBatched(lines);
        return sequential.Count >= batched.Count ? sequential : batched;
    }

    private static List<ParsedSubstat> ParseSequential(string[] lines)
    {
        var parsed = new List<ParsedSubstat>();
        var orphanValues = new Queue<ParsedSubstatValue>();
        SubstatInfo? pendingStat = null;
        string? pendingRaw = null;

        foreach (var line in lines)
        {
            if (IsLockedSubstatHint(line))
            {
                continue;
            }

            var stat = FindByText(line);
            var inlineValue = TryExtractValue(line);

            if (stat is not null && inlineValue is not null)
            {
                FlushPendingStat(parsed, ref pendingStat, ref pendingRaw, orphanValues);
                AddParsedStat(parsed, line, stat, inlineValue);
                continue;
            }

            if (stat is not null)
            {
                FlushPendingStat(parsed, ref pendingStat, ref pendingRaw, orphanValues);
                pendingStat = stat;
                pendingRaw = line;
                continue;
            }

            var value = TryExtractValue(line);
            if (value is null)
            {
                continue;
            }

            if (pendingStat is not null)
            {
                var chosen = ChooseValueForStat(pendingStat, value, orphanValues);
                AddParsedStat(parsed, pendingRaw!, pendingStat, chosen);
                pendingStat = null;
                pendingRaw = null;
                continue;
            }

            orphanValues.Enqueue(value);
        }

        FlushPendingStat(parsed, ref pendingStat, ref pendingRaw, orphanValues);
        return parsed;
    }

    private static List<ParsedSubstat> ParseBatched(string[] lines)
    {
        var parsed = new List<ParsedSubstat>();
        var nameOnlyLines = new List<(string RawText, SubstatInfo Stat)>();

        foreach (var line in lines)
        {
            if (IsLockedSubstatHint(line))
            {
                continue;
            }

            var stat = FindByText(line);
            if (stat is null)
            {
                continue;
            }

            var value = TryExtractValue(line);
            if (value is not null)
            {
                stat = ResolveStatVariant(stat, value);
                parsed.Add(new ParsedSubstat(
                    stat.Key,
                    stat.DisplayName,
                    NormalizeValueForStat(stat, value.Value),
                    line));
                continue;
            }

            nameOnlyLines.Add((line, stat));
        }

        var valueOnlyLines = lines
            .Where(line => !IsLockedSubstatHint(line) && FindByText(line) is null)
            .Select(TryExtractValue)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        for (var index = 0; index < nameOnlyLines.Count; index++)
        {
            var item = nameOnlyLines[index];
            var parsedValue = index < valueOnlyLines.Length ? valueOnlyLines[index] : (ParsedSubstatValue?)null;
            var stat = ResolveStatVariant(item.Stat, parsedValue);
            parsed.Add(new ParsedSubstat(
                stat.Key,
                stat.DisplayName,
                parsedValue is null ? 0 : NormalizeValueForStat(stat, parsedValue.Value),
                item.RawText));
        }

        return parsed;
    }

    private static ParsedSubstatValue ChooseValueForStat(
        SubstatInfo stat,
        ParsedSubstatValue current,
        Queue<ParsedSubstatValue> orphanValues)
    {
        if (orphanValues.Count == 0)
        {
            return current;
        }

        var orphan = orphanValues.Peek();
        var currentResolved = ResolveStatVariant(stat, current);
        var orphanResolved = ResolveStatVariant(stat, orphan);
        var currentFits = IsInRange(currentResolved, NormalizeValueForStat(currentResolved, current.Value));
        var orphanFits = IsInRange(orphanResolved, NormalizeValueForStat(orphanResolved, orphan.Value));

        if (orphanFits && !currentFits)
        {
            orphanValues.Dequeue();
            orphanValues.Enqueue(current);
            return orphan;
        }

        if (orphanFits && currentFits)
        {
            return orphanValues.Dequeue();
        }

        return current;
    }

    private static void FlushPendingStat(
        List<ParsedSubstat> parsed,
        ref SubstatInfo? pendingStat,
        ref string? pendingRaw,
        Queue<ParsedSubstatValue> orphanValues)
    {
        if (pendingStat is null || pendingRaw is null)
        {
            return;
        }

        ParsedSubstatValue? value = orphanValues.Count > 0 ? orphanValues.Dequeue() : null;
        AddParsedStat(parsed, pendingRaw, pendingStat, value);
        pendingStat = null;
        pendingRaw = null;
    }

    private static void AddParsedStat(
        List<ParsedSubstat> parsed,
        string rawText,
        SubstatInfo stat,
        ParsedSubstatValue? value)
    {
        stat = ResolveStatVariant(stat, value);
        parsed.Add(new ParsedSubstat(
            stat.Key,
            stat.DisplayName,
            value is null ? 0 : NormalizeValueForStat(stat, value.Value),
            rawText));
    }

    private static string SanitizeOcrLine(string line)
    {
        var trimmed = line.Trim();
        trimmed = Regex.Replace(trimmed, @"^[+＋十×Xx\s]+", string.Empty);
        return trimmed.Trim();
    }

    private static bool IsIgnorableNoiseLine(string line)
    {
        var normalized = NormalizeText(line);
        return normalized.Length == 0 || normalized is "+" or "x";
    }

    private static ParsedSubstatValue? TryExtractValue(string line)
    {
        var match = Regex.Match(line, @"(\d+(?:[.,]\d+)?)\s*[%％﹪°]?");
        if (!match.Success)
        {
            return null;
        }

        var hasPercentMarker = line.Contains('%', StringComparison.Ordinal)
            || line.Contains('％', StringComparison.Ordinal)
            || line.Contains('﹪', StringComparison.Ordinal)
            || line.Contains('°', StringComparison.Ordinal)
            || NormalizeText(line).Contains('%', StringComparison.Ordinal);
        var value = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return new ParsedSubstatValue(value, hasPercentMarker);
    }

    private static bool IsLockedSubstatHint(string line)
    {
        var normalized = NormalizeText(line);
        return normalized.Contains("강화", StringComparison.Ordinal)
            || normalized.Contains("속성", StringComparison.Ordinal)
            || normalized.Contains("까지", StringComparison.Ordinal);
    }

    private static double NormalizeValueForStat(SubstatInfo stat, double value)
    {
        if (IsInRange(stat, value))
        {
            return value;
        }

        var adjusted = value;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            adjusted /= 10;
            if (IsInRange(stat, adjusted))
            {
                return adjusted;
            }
        }

        return value;
    }

    private static bool IsInRange(SubstatInfo stat, double value)
    {
        return value >= stat.MinValue && value <= stat.MaxValue;
    }

    private static SubstatInfo ResolveStatVariant(SubstatInfo stat, ParsedSubstatValue? value)
    {
        if (value is null)
        {
            return stat;
        }

        var percentVariantKey = stat.Key switch
        {
            "atk_flat" or "atk_percent" => "atk_percent",
            "hp_flat" or "hp_percent" => "hp_percent",
            "def_flat" or "def_percent" => "def_percent",
            _ => null,
        };
        var flatVariantKey = stat.Key switch
        {
            "atk_flat" or "atk_percent" => "atk_flat",
            "hp_flat" or "hp_percent" => "hp_flat",
            "def_flat" or "def_percent" => "def_flat",
            _ => null,
        };

        if (percentVariantKey is null || flatVariantKey is null)
        {
            return stat;
        }

        var percentVariant = All.First(item => item.Key == percentVariantKey);
        var flatVariant = All.First(item => item.Key == flatVariantKey);

        if (value.HasPercentMarker)
        {
            return percentVariant;
        }

        var fitsPercent = IsInRange(percentVariant, value.Value);
        var fitsFlat = IsInRange(flatVariant, value.Value);
        return (fitsPercent, fitsFlat) switch
        {
            (true, false) => percentVariant,
            (false, true) => flatVariant,
            _ => stat,
        };
    }
}

public sealed record ParsedSubstat(string Key, string DisplayName, double Value, string RawText);

public sealed record ParsedSubstatValue(double Value, bool HasPercentMarker);
