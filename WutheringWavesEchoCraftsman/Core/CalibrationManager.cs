using System.Drawing;
using System.IO;
using System.Text.Json;
using WutheringWavesEchoCraftsman.Models;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class CalibrationManager
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public CalibrationManager(string? appRoot = null)
    {
        AppRoot = appRoot ?? AppContext.BaseDirectory;
        DataDirectory = Path.Combine(AppRoot, "data");
        AssetsDirectory = Path.Combine(DataDirectory, "assets");
        ConfigPath = Path.Combine(DataDirectory, "config.json");
    }

    public string AppRoot { get; }

    public string DataDirectory { get; }

    public string AssetsDirectory { get; }

    public string ConfigPath { get; }

    public AppConfig LoadOrCreate()
    {
        EnsureDirectories();

        if (!File.Exists(ConfigPath))
        {
            var newConfig = new AppConfig();
            Save(newConfig);
            return newConfig;
        }

        var json = File.ReadAllText(ConfigPath);
        var loadedConfig = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
        EnsureDefaults(loadedConfig);
        return loadedConfig;
    }

    public void Save(AppConfig config)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public string SaveAsset(Bitmap bitmap, string fileName)
    {
        EnsureDirectories();
        var path = Path.Combine(AssetsDirectory, fileName);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return Path.GetRelativePath(AppRoot, path);
    }

    public string ResolvePath(string relativeOrAbsolutePath)
    {
        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(AppRoot, relativeOrAbsolutePath);
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AssetsDirectory);
    }

    private static void EnsureDefaults(AppConfig config)
    {
        foreach (var key in CalibrationTargets.RequiredRegionKeys)
        {
            config.Regions.TryAdd(key, RegionRect.Empty);
        }

        foreach (var key in CalibrationTargets.RequiredAssetKeys)
        {
            config.Assets.TryAdd(key, Path.Combine("data", "assets", key));
        }

        if (config.StartDelaySeconds <= 0)
        {
            config.StartDelaySeconds = 3;
        }

        if (config.ActionDelayMs <= 0)
        {
            config.ActionDelayMs = 800;
        }

        if (config.CompletionOverlayDelayMs <= 0)
        {
            config.CompletionOverlayDelayMs = 1800;
        }

        if (config.ExpMaterialSlotsToUse <= 0)
        {
            config.ExpMaterialSlotsToUse = 1;
        }

        if (config.ExpMaterialClickDelayMs <= 0)
        {
            config.ExpMaterialClickDelayMs = 150;
        }
    }
}
