using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows;
using WutheringWavesEchoCraftsman.Core;
using WutheringWavesEchoCraftsman.Models;

namespace WutheringWavesEchoCraftsman.Views;

public partial class CalibrationWindow : Window
{
    private readonly AppConfig _config;
    private readonly CalibrationManager _calibrationManager;
    private readonly ScreenCapturer _screenCapturer;
    private readonly Action<string> _log;
    private readonly IReadOnlyList<CalibrationStep> _steps = CalibrationStep.CreateDefaultSteps();
    private readonly ObservableCollection<CalibrationStatusItem> _items = [];

    public CalibrationWindow(
        AppConfig config,
        CalibrationManager calibrationManager,
        ScreenCapturer screenCapturer,
        Action<string> log)
    {
        InitializeComponent();
        _config = config;
        _calibrationManager = calibrationManager;
        _screenCapturer = screenCapturer;
        _log = log;
        CalibrationDataGrid.ItemsSource = _items;
        RefreshItems();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshItems();
    }

    private async void CaptureSelected_Click(object sender, RoutedEventArgs e)
    {
        if (CalibrationDataGrid.SelectedItem is not CalibrationStatusItem item)
        {
            System.Windows.MessageBox.Show(this, "다시 캡처할 항목을 먼저 선택하세요.", "초기 설정", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await CaptureStepsAsync([item.Step]);
    }

    private async void CaptureSelectedScreen_Click(object sender, RoutedEventArgs e)
    {
        if (CalibrationDataGrid.SelectedItem is not CalibrationStatusItem item)
        {
            System.Windows.MessageBox.Show(this, "다시 캡처할 화면 단계의 항목을 먼저 선택하세요.", "초기 설정", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var steps = _steps.Where(step => step.Screen == item.Step.Screen).ToArray();
        await CaptureStepsAsync(steps);
    }

    private async void CaptureAll_Click(object sender, RoutedEventArgs e)
    {
        await CaptureStepsAsync(_steps);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        Owner?.Show();
        Owner?.Activate();
        base.OnClosed(e);
    }

    private async Task CaptureStepsAsync(IReadOnlyList<CalibrationStep> steps)
    {
        foreach (var group in steps.GroupBy(step => step.Screen))
        {
            if (!await PrepareScreenAsync(group.Key))
            {
                _log("캘리브레이션 취소");
                Show();
                Activate();
                return;
            }

            using var screenshot = _screenCapturer.CaptureVirtualScreen();
            _log($"{CalibrationGuide.GetScreenTitle(group.Key)} 캡처 완료");

            foreach (var step in group)
            {
                var result = await CalibrationOverlay.CaptureAsync(screenshot, step);
                if (result is null)
                {
                    _log("캘리브레이션 취소");
                    Show();
                    Activate();
                    return;
                }

                SaveCalibrationResult(screenshot, result);
                _log($"캘리브레이션 저장: {step.ScreenTitle} / {step.Key}");
            }
        }

        _calibrationManager.Save(_config);
        RefreshItems();
        Show();
        Activate();
        _log("캘리브레이션 저장 완료");
    }

    private async Task<bool> PrepareScreenAsync(CalibrationScreen screen)
    {
        var instruction = CalibrationGuide.GetPreparationMessage(screen);
        _log(instruction.Replace(Environment.NewLine, " "));

        Show();
        Activate();
        var result = System.Windows.MessageBox.Show(
            this,
            instruction + Environment.NewLine + Environment.NewLine + "해당 화면을 준비한 뒤 [확인]을 누르세요. 확인 후 3초 뒤 캡처합니다.",
            "캘리브레이션 화면 준비",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (result != MessageBoxResult.OK)
        {
            return false;
        }

        Owner?.Hide();
        Hide();
        _log($"{CalibrationGuide.GetScreenTitle(screen)} 준비: 3초 후 캡처");
        await Task.Delay(3000);
        return true;
    }

    private void SaveCalibrationResult(Bitmap screenshot, CalibrationResult result)
    {
        if (result.Step.Kind == CalibrationStepKind.Region)
        {
            _config.Regions[result.Step.Key] = result.Region;
            return;
        }

        using var assetBitmap = screenshot.Clone(result.Region.ToRectangle(), screenshot.PixelFormat);
        var relativePath = _calibrationManager.SaveAsset(assetBitmap, result.Step.Key);
        _config.Assets[result.Step.Key] = relativePath;
    }

    private void RefreshItems()
    {
        _items.Clear();

        foreach (var step in _steps)
        {
            _items.Add(CalibrationStatusItem.From(step, _config, _calibrationManager));
        }
    }
}

public sealed class CalibrationStatusItem
{
    private CalibrationStatusItem(CalibrationStep step, string status, string value)
    {
        Step = step;
        Key = step.Key;
        Title = step.Title;
        ScreenTitle = step.ScreenTitle;
        Kind = step.Kind == CalibrationStepKind.Region ? "ROI" : "Asset";
        Status = status;
        Value = value;
    }

    public CalibrationStep Step { get; }

    public string Key { get; }

    public string Title { get; }

    public string ScreenTitle { get; }

    public string Kind { get; }

    public string Status { get; }

    public string Value { get; }

    public static CalibrationStatusItem From(CalibrationStep step, AppConfig config, CalibrationManager calibrationManager)
    {
        if (step.Kind == CalibrationStepKind.Region)
        {
            var hasRegion = config.Regions.TryGetValue(step.Key, out var region) && !region.IsEmpty;
            var value = hasRegion
                ? $"X={region!.X}, Y={region.Y}, W={region.Width}, H={region.Height}"
                : "(미설정)";

            return new CalibrationStatusItem(step, hasRegion ? "저장됨" : "미설정", value);
        }

        var hasPath = config.Assets.TryGetValue(step.Key, out var path) && !string.IsNullOrWhiteSpace(path);
        var absolutePath = hasPath ? calibrationManager.ResolvePath(path!) : string.Empty;
        var exists = hasPath && File.Exists(absolutePath);
        var assetValue = hasPath ? path! : "(미설정)";
        return new CalibrationStatusItem(step, exists ? "저장됨" : "미설정", assetValue);
    }
}
