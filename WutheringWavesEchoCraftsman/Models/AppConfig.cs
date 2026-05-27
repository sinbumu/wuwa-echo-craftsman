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

    public int TargetOptimizeCount { get; set; } = 1;

    public int StartDelaySeconds { get; set; } = 3;

    public int ActionDelayMs { get; set; } = 800;

    public int CompletionOverlayDelayMs { get; set; } = 1800;

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
        "roi_enhance_tab",
        "roi_expected_level",
        "roi_slot_plus",
        "roi_enhance_confirm",
        "roi_enhance_complete_close",
        "roi_optimize_tab",
        "roi_material",
        "roi_exp_material_1",
        "roi_exp_material_2",
        "roi_exp_material_3",
        "roi_exp_material_4",
        "roi_substat",
        "roi_optimize_count",
        "roi_optimize_minus",
        "roi_optimize_plus",
        "roi_optimize_confirm",
        "roi_optimize_complete_close",
    ];

    public static readonly string[] RequiredAssetKeys =
    [
        "template_plus_zero.png",
        "icon_discard.png",
    ];
}
