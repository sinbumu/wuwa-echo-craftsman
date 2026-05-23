using System.Globalization;
using System.Text.RegularExpressions;

namespace WutheringWavesEchoCraftsman.Models;

public sealed record SubstatInfo(string Key, string DisplayName, string[] Aliases)
{
    public static IReadOnlyList<SubstatInfo> All { get; } =
    [
        new("crit_rate", "크리티컬", ["크리티컬", "크리", "치명타", "crit"]),
        new("crit_damage", "크리티컬 피해", ["크리티컬피해", "크피", "치명타피해", "critdamage"]),
        new("atk_percent", "공격력(%)", ["공격력%", "공격력퍼센트", "atk%"]),
        new("atk_flat", "공격력", ["공격력", "atk"]),
        new("hp_percent", "HP(%)", ["hp%", "hp퍼센트", "체력%", "체력퍼센트"]),
        new("hp_flat", "HP", ["hp", "체력"]),
        new("def_percent", "방어력(%)", ["방어력%", "방어력퍼센트", "def%"]),
        new("def_flat", "방어력", ["방어력", "def"]),
        new("energy_regen", "공명 효율", ["공명효율", "공효", "에너지회복", "energyregen"]),
        new("basic_damage", "일반 공격 피해", ["일반공격피해", "일반피증", "평타피증"]),
        new("heavy_damage", "강공격 피해", ["강공격피해", "강공피증"]),
        new("skill_damage", "공명 스킬 피해", ["공명스킬피해", "스킬피증"]),
        new("liberation_damage", "공명 해방 피해", ["공명해방피해", "해방피증"]),
    ];

    public static string NormalizeText(string text)
    {
        return Regex.Replace(text, @"[\s\p{P}\p{S}]+", string.Empty).ToLowerInvariant();
    }

    public static SubstatInfo? FindByText(string text)
    {
        var normalized = NormalizeText(text);

        return All.FirstOrDefault(stat =>
            NormalizeText(stat.DisplayName) == normalized
            || stat.Aliases.Any(alias => normalized.Contains(NormalizeText(alias), StringComparison.Ordinal)));
    }

    public static IReadOnlyList<ParsedSubstat> ParseLines(string ocrText)
    {
        return ocrText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLine)
            .Where(parsed => parsed is not null)
            .Cast<ParsedSubstat>()
            .ToArray();
    }

    private static ParsedSubstat? ParseLine(string line)
    {
        var stat = FindByText(line);
        if (stat is null)
        {
            return null;
        }

        var match = Regex.Match(line, @"[-+]?\d+(?:[.,]\d+)?");
        var value = match.Success
            ? double.Parse(match.Value.Replace(',', '.'), CultureInfo.InvariantCulture)
            : 0;

        return new ParsedSubstat(stat.Key, stat.DisplayName, value, line);
    }
}

public sealed record ParsedSubstat(string Key, string DisplayName, double Value, string RawText);
