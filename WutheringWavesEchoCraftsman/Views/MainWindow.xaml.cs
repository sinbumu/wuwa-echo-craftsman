using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using Media = System.Windows.Media;
using WutheringWavesEchoCraftsman.Core;
using WutheringWavesEchoCraftsman.Models;
using WutheringWavesEchoCraftsman.Services;
using Wpf.Ui.Appearance;

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
        ApplyTheme(_config.DarkMode);
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
        if (!App.IsExplicitShutdownRequested)
        {
            e.Cancel = true;
            foreach (Window window in System.Windows.Application.Current.Windows.Cast<Window>().Where(window => window != this).ToArray())
            {
                window.Close();
            }

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
        DarkModeCheckBox.IsChecked = _config.DarkMode;
        TargetLevelTextBox.Text = _config.TargetLevel.ToString();
        RemainingCountTextBox.Text = _config.RemainingCount.ToString();
        OptimizeCountTextBox.Text = _config.TargetOptimizeCount.ToString();
        StartDelayTextBox.Text = _config.StartDelaySeconds.ToString();
        ActionDelayTextBox.Text = _config.ActionDelayMs.ToString();
        CompletionDelayTextBox.Text = _config.CompletionOverlayDelayMs.ToString();
        ExpMaterialSlotsTextBox.Text = _config.ExpMaterialSlotsToUse.ToString();
        UseDiscardEchoMaterialsCheckBox.IsChecked = _config.UseDiscardEchoMaterials;
        ExpMaterialDelayTextBox.Text = _config.ExpMaterialClickDelayMs.ToString();
        OptimizeCountDelayTextBox.Text = _config.OptimizeCountClickDelayMs.ToString();
    }

    private void SaveConfigFromUi()
    {
        _config.DryRun = DryRunCheckBox.IsChecked == true;
        _config.DarkMode = DarkModeCheckBox.IsChecked == true;
        _config.TargetLevel = ParseInt(TargetLevelTextBox.Text, 5);
        _config.RemainingCount = ParseInt(RemainingCountTextBox.Text, 1);
        _config.TargetOptimizeCount = ParseInt(OptimizeCountTextBox.Text, 1);
        _config.StartDelaySeconds = Math.Max(0, ParseInt(StartDelayTextBox.Text, 3));
        _config.ActionDelayMs = Math.Max(100, ParseInt(ActionDelayTextBox.Text, 800));
        _config.CompletionOverlayDelayMs = Math.Max(300, ParseInt(CompletionDelayTextBox.Text, 1800));
        _config.ExpMaterialSlotsToUse = Math.Clamp(ParseInt(ExpMaterialSlotsTextBox.Text, 1), 1, 4);
        _config.UseDiscardEchoMaterials = UseDiscardEchoMaterialsCheckBox.IsChecked == true;
        _config.ExpMaterialClickDelayMs = Math.Max(50, ParseInt(ExpMaterialDelayTextBox.Text, 150));
        _config.OptimizeCountClickDelayMs = Math.Max(50, ParseInt(OptimizeCountDelayTextBox.Text, 150));

        if (_config.SubstatRules.Count == 0)
        {
            _config.SubstatRules = SubstatInfo.All
                .Take(2)
                .Select(stat => new SubstatRule(stat.Key, 0, true))
                .ToList();
        }

        _calibrationManager.Save(_config);
        ApplyTheme(_config.DarkMode);
    }

    private void DarkModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _config.DarkMode = DarkModeCheckBox.IsChecked == true;
        ApplyTheme(_config.DarkMode);
    }

    private static void ApplyTheme(bool darkMode)
    {
        ApplicationThemeManager.Apply(darkMode ? ApplicationTheme.Dark : ApplicationTheme.Light);

        SetBrushResource("AppBackgroundBrush", darkMode ? "#111827" : "#F6F7FB");
        SetBrushResource("CardBackgroundBrush", darkMode ? "#1F2937" : "#FFFFFF");
        SetBrushResource("CardBorderBrush", darkMode ? "#374151" : "#E5E7EB");
        SetBrushResource("PrimaryTextBrush", darkMode ? "#F9FAFB" : "#111827");
        SetBrushResource("SecondaryTextBrush", darkMode ? "#D1D5DB" : "#4B5563");
    }

    private static void SetBrushResource(string key, string color)
    {
        System.Windows.Application.Current.Resources[key] = new Media.SolidColorBrush(
            (Media.Color)Media.ColorConverter.ConvertFromString(color));
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
            using var capture = _config.Regions.TryGetValue("roi_expected_level", out var region) && !region.IsEmpty
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

    private async void FreeformOcrPoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppendLog("3초 후 화면을 캡처합니다. OCR로 읽을 화면을 준비하세요.");
            Hide();
            await Task.Delay(3000);

            using var screenshot = _screenCapturer.CaptureVirtualScreen();
            Show();
            Activate();

            var step = new CalibrationStep(
                "poc_freeform_ocr",
                "임의 OCR 테스트 영역",
                CalibrationStepKind.Region,
                CalibrationScreen.Optimize,
                "OCR 원문을 확인할 영역을 드래그하세요. 저장하지 않고 로그에만 출력합니다.");
            var result = await CalibrationOverlay.CaptureAsync(screenshot, step);
            if (result is null)
            {
                AppendLog("임의 OCR 테스트 취소");
                return;
            }

            using var crop = screenshot.Clone(result.Region.ToRectangle(), screenshot.PixelFormat);
            var text = await _visionProcessor.RecognizeTextAsync(crop);
            AppendLog($"임의 OCR 원문 ({result.Region.X},{result.Region.Y},{result.Region.Width},{result.Region.Height}):{Environment.NewLine}{text}");
        }
        catch (Exception ex)
        {
            AppendLog($"임의 OCR 테스트 실패: {ex.Message}");
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private void InputPoc_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        var input = new InputController(_config.DryRun, AppendLog);
        input.PressKey(VirtualKeys.C);
        AppendLog("입력 PoC 실행 완료");
    }

    private async void TestSearch_Click(object sender, RoutedEventArgs e)
    {
        await RunStepTestAsync("SEARCH", async input =>
        {
            var listRegion = _config.Regions["roi_list"];
            using var listCapture = _screenCapturer.CaptureRegion(listRegion);
            var matches = FindAssets(listCapture, "template_plus_zero.png", 0.85);
            var match = matches.FirstOrDefault(new TemplateMatchResult(false, 0, 0, 0));
            AppendLog($"TEST SEARCH: +0 후보 {matches.Count}개, 선택 confidence={match.Confidence:0.000}, local=({match.CenterX}, {match.CenterY})");
            if (!match.Success)
            {
                return;
            }

            input.Click(listRegion.X + match.CenterX, listRegion.Y + match.CenterY);
            await Task.Delay(_config.ActionDelayMs);
            ClickRegion(input, "roi_enhance_tab");
        });
    }

    private async void TestMaterial_Click(object sender, RoutedEventArgs e)
    {
        await RunStepTestAsync("재료 1회 투입", async input =>
        {
            ClickRegion(input, "roi_slot_plus");
            await Task.Delay(_config.ActionDelayMs);

            var materialRegion = _config.Regions["roi_material"];
            using var materialCapture = _screenCapturer.CaptureRegion(materialRegion);
            var discard = FindAsset(materialCapture, "icon_discard.png", 0.80);
            AppendLog($"TEST MATERIAL: 폐기 에코 confidence={discard.Confidence:0.000}");

            if (discard.Success)
            {
                input.Click(materialRegion.X + discard.CenterX, materialRegion.Y + discard.CenterY);
                return;
            }

            var expRegion = Enumerable.Range(1, 4)
                .Select(index => $"roi_exp_material_{index}")
                .Take(Math.Clamp(_config.ExpMaterialSlotsToUse, 1, 4))
                .Select(key => _config.Regions.TryGetValue(key, out var region) ? region : RegionRect.Empty)
                .FirstOrDefault(region => !region.IsEmpty) ?? RegionRect.Empty;

            if (expRegion.IsEmpty)
            {
                AppendLog("TEST MATERIAL: 설정된 음파통 영역이 없습니다.");
                return;
            }

            input.Click(expRegion.X + expRegion.Width / 2, expRegion.Y + expRegion.Height / 2);
        });
    }

    private async void TestExpectedLevel_Click(object sender, RoutedEventArgs e)
    {
        await RunStepTestAsync("예상 레벨 OCR", async _ =>
        {
            var level = await ReadNumberFromRegionAsync("roi_expected_level");
            AppendLog($"TEST OCR: 강화 후 예상 레벨={level}");
        });
    }

    private async void TestOptimizeCount_Click(object sender, RoutedEventArgs e)
    {
        await RunStepTestAsync("옵티 횟수 OCR", async _ =>
        {
            var count = await ReadNumberFromRegionAsync("roi_optimize_count", min: 1, max: 5);
            AppendLog($"TEST OCR: 옵티마이즈 시행 횟수={count}");
        });
    }

    private async void Calibration_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        var window = new CalibrationWindow(_config, _calibrationManager, _screenCapturer, AppendLog)
        {
            Owner = this,
        };
        window.Show();
        await Task.CompletedTask;
    }

    private async void SubstatSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        var window = new SubstatSettingsWindow(_config, _calibrationManager.Save)
        {
            Owner = this,
        };
        window.ShowDialog();
        await Task.CompletedTask;
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
        var steps = CalibrationStep.CreateDefaultSteps();

        foreach (var screenGroup in steps.GroupBy(step => step.Screen))
        {
            if (!await PrepareCalibrationScreenAsync(screenGroup.Key))
            {
                AppendLog("캘리브레이션 취소");
                return;
            }

            using var screenshot = _screenCapturer.CaptureVirtualScreen();
            AppendLog($"{GetCalibrationScreenTitle(screenGroup.Key)} 캡처 완료");

            foreach (var step in screenGroup)
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

                AppendLog($"캘리브레이션 저장: {step.ScreenTitle} / {step.Key}");
            }
        }

        _calibrationManager.Save(_config);
        AppendLog("캘리브레이션 완료");
    }

    private async Task<bool> PrepareCalibrationScreenAsync(CalibrationScreen screen)
    {
        var instruction = GetCalibrationPreparationMessage(screen);
        AppendLog(instruction.Replace(Environment.NewLine, " "));

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

        Hide();
        AppendLog($"{GetCalibrationScreenTitle(screen)} 준비: 3초 후 캡처");
        await Task.Delay(3000);
        Show();
        Activate();
        return true;
    }

    private static string GetCalibrationScreenTitle(CalibrationScreen screen)
    {
        return screen switch
        {
            CalibrationScreen.EchoList => "에코 목록 화면",
            CalibrationScreen.Enhance => "에코 강화 화면",
            CalibrationScreen.MaterialList => "에코 강화 재료 리스트 화면",
            CalibrationScreen.Optimize => "에코 옵티마이즈 화면",
            _ => "캘리브레이션 화면",
        };
    }

    private static string GetCalibrationPreparationMessage(CalibrationScreen screen)
    {
        return screen switch
        {
            CalibrationScreen.EchoList =>
                "1/4 에코 목록 화면을 준비하세요." + Environment.NewLine
                + "- 캐릭터 > 에코 탭의 에코 목록 화면으로 이동하세요." + Environment.NewLine
                + "- 목표 세트/코스트 필터와 레벨 오름차순 정렬을 적용하세요." + Environment.NewLine
                + "- +0 에코가 보이고, 에코 선택 시 육성 버튼이 보이는 상태가 좋습니다.",

            CalibrationScreen.Enhance =>
                "2/4 에코 강화 기본 화면을 준비하세요." + Environment.NewLine
                + "- 목록에서 +0 에코를 선택하고 육성 버튼을 눌러 강화 화면으로 이동하세요." + Environment.NewLine
                + "- 현재 레벨 텍스트가 보이게 하세요." + Environment.NewLine
                + "- 아직 재료 슬롯 + 버튼은 누르지 마세요." + Environment.NewLine
                + "- 현재 화면에서 재료 슬롯 + 버튼, 강화 버튼, 옵티마이즈/튜닝 탭 버튼이 보이는 상태로 준비하세요.",

            CalibrationScreen.MaterialList =>
                "3/4 에코 강화 재료 리스트 화면을 준비하세요." + Environment.NewLine
                + "- 에코 강화 화면에서 재료 슬롯 + 버튼 또는 재료 투입 영역을 클릭하세요." + Environment.NewLine
                + "- 우측에 강화 재료/강화된 에코 목록이 열린 상태로 준비하세요." + Environment.NewLine
                + "- 폐기 에코 아이콘 또는 음파통 아이콘이 실제로 보이게 스크롤/필터를 맞춰두세요.",

            CalibrationScreen.Optimize =>
                "4/4 에코 옵티마이즈 화면을 준비하세요." + Environment.NewLine
                + "- 강화 화면에서 옵티마이즈/튜닝 탭으로 이동하세요." + Environment.NewLine
                + "- 부옵션 텍스트 영역과 옵티마이즈 실행/해금 버튼이 보이는 상태로 준비하세요.",

            _ => "캘리브레이션할 화면을 준비하세요.",
        };
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
            await PrepareForGameInputAsync("자동화 시작");
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
            Show();
            Activate();
        }
    }

    private void StopAutomation()
    {
        _automationCancellation?.Cancel();
        AppendLog("정지 요청 전송");
    }

    private async Task RunStepTestAsync(string name, Func<InputController, Task> action)
    {
        SaveConfigFromUi();
        var input = new InputController(_config.DryRun, AppendLog);

        try
        {
            await PrepareForGameInputAsync($"단계 테스트: {name}");
            await action(input);
            AppendLog($"단계 테스트 완료: {name}");
        }
        catch (Exception ex)
        {
            AppendLog($"단계 테스트 실패({name}): {ex.Message}");
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private async Task PrepareForGameInputAsync(string purpose)
    {
        AppendLog($"{purpose}: {_config.StartDelaySeconds}초 후 실행합니다. 명조 창을 포커스하세요.");
        Hide();
        await Task.Delay(TimeSpan.FromSeconds(_config.StartDelaySeconds));
    }

    private void ClickRegion(InputController input, string regionKey)
    {
        var region = _config.Regions[regionKey];
        if (region.IsEmpty)
        {
            throw new InvalidOperationException($"{regionKey} 영역이 설정되지 않았습니다.");
        }

        var x = region.X + region.Width / 2;
        var y = region.Y + region.Height / 2;
        AppendLog($"{regionKey}: 중앙 클릭 ({x}, {y})");
        input.Click(x, y);
    }

    private TemplateMatchResult FindAsset(Bitmap source, string assetName, double threshold)
    {
        if (!_config.Assets.TryGetValue(assetName, out var relativePath))
        {
            return new TemplateMatchResult(false, 0, 0, 0);
        }

        var assetPath = _calibrationManager.ResolvePath(relativePath);
        if (!File.Exists(assetPath))
        {
            AppendLog($"에셋 없음: {assetPath}");
            return new TemplateMatchResult(false, 0, 0, 0);
        }

        using var template = new Bitmap(assetPath);
        return _visionProcessor.FindTemplate(source, template, threshold);
    }

    private IReadOnlyList<TemplateMatchResult> FindAssets(Bitmap source, string assetName, double threshold)
    {
        if (!_config.Assets.TryGetValue(assetName, out var relativePath))
        {
            return [];
        }

        var assetPath = _calibrationManager.ResolvePath(relativePath);
        if (!File.Exists(assetPath))
        {
            AppendLog($"에셋 없음: {assetPath}");
            return [];
        }

        using var template = new Bitmap(assetPath);
        return _visionProcessor.FindTemplateMatches(source, template, threshold);
    }

    private async Task<int> ReadNumberFromRegionAsync(string regionKey, int? min = null, int? max = null)
    {
        var region = _config.Regions[regionKey];
        if (region.IsEmpty)
        {
            throw new InvalidOperationException($"{regionKey} 영역이 설정되지 않았습니다.");
        }

        using var capture = _screenCapturer.CaptureRegion(region);
        var text = await _visionProcessor.RecognizeTextAsync(capture);
        AppendLog($"{regionKey} OCR 원문:{Environment.NewLine}{text}");

        var values = Regex.Matches(text, @"\d+")
            .Select(match => int.Parse(match.Value))
            .Where(value => (!min.HasValue || value >= min.Value) && (!max.HasValue || value <= max.Value))
            .ToArray();

        return values.LastOrDefault();
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
