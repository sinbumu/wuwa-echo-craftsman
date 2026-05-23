using System.IO;
using System.Text.Json.Serialization;

namespace WutheringWavesEchoCraftsman.Models;

public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public bool DryRun { get; set; } = true;

    public int TargetLevel { get; set; } = 5;

    public int RemainingCount { get; set; } = 1;

    public int RequiredValidSubstatCount { get; set; } = 2;

    public Dictionary<string, RegionRect> Regions { get; set; } = CreateDefaultRegions();

    public Dictionary<string, string> Assets { get; set; } = CreateDefaultAssets();

    public List<SubstatRule> SubstatRules { get; set; } = [];

    [JsonIgnore]
    public bool HasRequiredCalibration =>
        CalibrationTargets.RequiredRegionKeys.All(key => Regions.TryGetValue(key, out var region) && !region.IsEmpty)
        && CalibrationTargets.RequiredAssetKeys.All(key => Assets.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path));

    public static Dictionary<string, RegionRect> CreateDefaultRegions() =>
        CalibrationTargets.RequiredRegionKeys.ToDictionary(key => key, _ => RegionRect.Empty);

    public static Dictionary<string, string> CreateDefaultAssets() =>
        CalibrationTargets.RequiredAssetKeys.ToDictionary(key => key, key => Path.Combine("data", "assets", key));
}

public sealed record SubstatRule(string Key, double MinValue, bool Enabled);

public static class CalibrationTargets
{
    public static readonly string[] RequiredRegionKeys =
    [
        "roi_list",
        "roi_level",
        "roi_substat",
        "roi_material",
    ];

    public static readonly string[] RequiredAssetKeys =
    [
        "template_plus_zero.png",
        "icon_discard.png",
        "icon_exp.png",
        "btn_enhance_tab.png",
        "btn_slot_plus.png",
        "btn_enhance_confirm.png",
        "btn_optimize_tab.png",
        "btn_optimize_confirm.png",
    ];
}
