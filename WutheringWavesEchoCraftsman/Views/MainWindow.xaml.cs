using System.IO;
using System.Windows;
using System.Windows.Interop;
using WutheringWavesEchoCraftsman.Core;
using WutheringWavesEchoCraftsman.Models;
using WutheringWavesEchoCraftsman.Services;

namespace WutheringWavesEchoCraftsman.Views;

public partial class MainWindow : Window
{
    private readonly CalibrationManager _calibrationManager = new();
    private readonly ScreenCapturer _screenCapturer = new();
    private readonly VisionProcessor _visionProcessor = new();
    private readonly DatabaseService _databaseService;
    private AppConfig _config;
    private CancellationTokenSource? _automationCancellation;
    private GlobalHotKeyManager? _hotKeyManager;

    public MainWindow()
    {
        InitializeComponent();
        _databaseService = new DatabaseService(Path.Combine(_calibrationManager.DataDirectory, "history.sqlite3"));
        _config = _calibrationManager.LoadOrCreate();
        LoadConfigToUi();
        AppendLog("앱 초기화 완료");
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hotKeyManager = new GlobalHotKeyManager(helper.Handle);
        _hotKeyManager.HotKeyPressed += OnHotKeyPressed;
        _hotKeyManager.Register(1, 0, 0x74); // F5
        _hotKeyManager.Register(2, 0, 0x75); // F6
        AppendLog("글로벌 핫키 등록: F5 시작, F6 정지");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (System.Windows.Application.Current.ShutdownMode != ShutdownMode.OnExplicitShutdown)
        {
            e.Cancel = true;
            Hide();
            AppendLog("창을 닫지 않고 트레이로 숨겼습니다.");
        }
    }

    private void OnHotKeyPressed(object? sender, int id)
    {
        Dispatcher.Invoke(async () =>
        {
            if (id == 1)
            {
                await StartAutomationAsync();
            }
            else if (id == 2)
            {
                StopAutomation();
            }
        });
    }

    private void LoadConfigToUi()
    {
        DryRunCheckBox.IsChecked = _config.DryRun;
        TargetLevelTextBox.Text = _config.TargetLevel.ToString();
        RemainingCountTextBox.Text = _config.RemainingCount.ToString();
        RequiredCountTextBox.Text = _config.RequiredValidSubstatCount.ToString();
    }

    private void SaveConfigFromUi()
    {
        _config.DryRun = DryRunCheckBox.IsChecked == true;
        _config.TargetLevel = ParseInt(TargetLevelTextBox.Text, 5);
        _config.RemainingCount = ParseInt(RemainingCountTextBox.Text, 1);
        _config.RequiredValidSubstatCount = ParseInt(RequiredCountTextBox.Text, 2);

        if (_config.SubstatRules.Count == 0)
        {
            _config.SubstatRules = SubstatInfo.All
                .Take(2)
                .Select(stat => new SubstatRule(stat.Key, 0, true))
                .ToList();
        }

        _calibrationManager.Save(_config);
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        AppendLog("설정 저장 완료");
    }

    private void CapturePoc_Click(object sender, RoutedEventArgs e)
    {
        using var capture = _screenCapturer.CaptureVirtualScreen();
        var path = Path.Combine(_calibrationManager.DataDirectory, "poc_capture.png");
        _screenCapturer.SavePng(capture, path);
        AppendLog($"화면 캡처 저장: {path}");
    }

    private async void OcrPoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var capture = _config.Regions.TryGetValue("roi_level", out var region) && !region.IsEmpty
                ? _screenCapturer.CaptureRegion(region)
                : _screenCapturer.CaptureVirtualScreen();

            using var processed = _visionProcessor.PreprocessForOcr(capture);
            var text = await _visionProcessor.RecognizeTextAsync(processed);
            AppendLog($"OCR 결과:{Environment.NewLine}{text}");
        }
        catch (Exception ex)
        {
            AppendLog($"OCR 테스트 실패: {ex.Message}");
        }
    }

    private void InputPoc_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        var input = new InputController(_config.DryRun, AppendLog);
        input.PressKey(VirtualKeys.C);
        AppendLog("입력 PoC 실행 완료");
    }

    private async void Calibration_Click(object sender, RoutedEventArgs e)
    {
        await RunCalibrationAsync();
    }

    private async void StartAutomation_Click(object sender, RoutedEventArgs e)
    {
        await StartAutomationAsync();
    }

    private void StopAutomation_Click(object sender, RoutedEventArgs e)
    {
        StopAutomation();
    }

    private async void History_Click(object sender, RoutedEventArgs e)
    {
        var window = new HistoryWindow(_databaseService) { Owner = this };
        await window.LoadRecordsAsync();
        window.Show();
    }

    private async Task RunCalibrationAsync()
    {
        SaveConfigFromUi();
        AppendLog("3초 후 캘리브레이션 캡처를 시작합니다.");
        await Task.Delay(3000);

        using var screenshot = _screenCapturer.CaptureVirtualScreen();
        var steps = CalibrationStep.CreateDefaultSteps();

        foreach (var step in steps)
        {
            var result = await CalibrationOverlay.CaptureAsync(screenshot, step);
            if (result is null)
            {
                AppendLog("캘리브레이션 취소");
                return;
            }

            if (step.Kind == CalibrationStepKind.Region)
            {
                _config.Regions[step.Key] = result.Region;
            }
            else
            {
                using var assetBitmap = screenshot.Clone(result.Region.ToRectangle(), screenshot.PixelFormat);
                var relativePath = _calibrationManager.SaveAsset(assetBitmap, step.Key);
                _config.Assets[step.Key] = relativePath;
            }

            AppendLog($"캘리브레이션 저장: {step.Key}");
        }

        _calibrationManager.Save(_config);
        AppendLog("캘리브레이션 완료");
    }

    private async Task StartAutomationAsync()
    {
        if (_automationCancellation is not null)
        {
            AppendLog("자동화가 이미 실행 중입니다.");
            return;
        }

        SaveConfigFromUi();
        _automationCancellation = new CancellationTokenSource();
        var input = new InputController(_config.DryRun, AppendLog);
        var automator = new EchoAutomator(_config, _calibrationManager, _screenCapturer, _visionProcessor, input, _databaseService, AppendLog);

        try
        {
            await Task.Run(async () => await automator.RunAsync(_automationCancellation.Token), _automationCancellation.Token);
        }
        catch (OperationCanceledException ex)
        {
            AppendLog($"자동화 취소: {ex.Message}");
        }
        catch (Exception ex)
        {
            AppendLog($"자동화 오류: {ex.Message}");
        }
        finally
        {
            _automationCancellation.Dispose();
            _automationCancellation = null;
            _calibrationManager.Save(_config);
        }
    }

    private void StopAutomation()
    {
        _automationCancellation?.Cancel();
        AppendLog("정지 요청 전송");
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogTextBox.AppendText(line + Environment.NewLine);
            LogTextBox.ScrollToEnd();
            StatusTextBlock.Text = message;
        });
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out var value) ? value : fallback;
    }
}
