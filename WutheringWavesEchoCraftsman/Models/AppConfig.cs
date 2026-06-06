using System.IO;
using System.Text.Json.Serialization;

namespace WutheringWavesEchoCraftsman.Models;

public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public bool DryRun { get; set; } = false;

    public bool DarkMode { get; set; } = false;

    public int TargetLevel { get; set; } = 5;

    public int RemainingCount { get; set; } = 1;

    public int RequiredValidSubstatCount { get; set; } = 2;

    public int TargetOptimizeCount { get; set; } = 1;

    public int StartDelaySeconds { get; set; } = 3;

    public int ActionDelayMs { get; set; } = 800;

    public int CompletionOverlayDelayMs { get; set; } = 1800;

    public int ExpMaterialSlotsToUse { get; set; } = 1;

    public int ExpMaterialClickDelayMs { get; set; } = 150;

    public int OptimizeCountClickDelayMs { get; set; } = 150;

    public bool UseDiscardEchoMaterials { get; set; } = false;

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

public sealed record SubstatRule(string Key, double MinValue, bool Enabled, bool Required = false);

public static class CalibrationTargets
{
    public static readonly string[] RequiredRegionKeys =
    [
        "roi_list",
        "roi_enhance_tab",
        "roi_staged_auto_input",
        "roi_echo_material_input",
        "roi_echo_material_list",
        "roi_enhance_confirm",
        "roi_enhance_complete_close",
        "roi_current_level",
        "roi_substat",
    ];

    public static readonly string[] RequiredAssetKeys =
    [
        "template_plus_zero.png",
        "template_discard_echo.png",
    ];
}
