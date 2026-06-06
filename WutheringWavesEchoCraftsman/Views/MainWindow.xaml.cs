using System.Drawing;
using System.IO;
using System.IO.Compression;
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
    private AutomationOverlayWindow? _automationOverlay;

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
        SyncTargetLevelFromOptimizeCount();
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
        _config.RemainingCount = ParseInt(RemainingCountTextBox.Text, 1);
        _config.TargetOptimizeCount = Math.Clamp(ParseInt(OptimizeCountTextBox.Text, 1), 1, 5);
        _config.TargetLevel = _config.TargetOptimizeCount * 5;
        TargetLevelTextBox.Text = _config.TargetLevel.ToString();
        OptimizeCountTextBox.Text = _config.TargetOptimizeCount.ToString();
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

    private void SyncTargetLevelFromOptimizeCount()
    {
        _config.TargetOptimizeCount = Math.Clamp(_config.TargetOptimizeCount, 1, 5);
        _config.TargetLevel = _config.TargetOptimizeCount * 5;
    }

    private void OptimizeCountTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TargetLevelTextBox is null)
        {
            return;
        }

        var optimizeCount = Math.Clamp(ParseInt(OptimizeCountTextBox.Text, 1), 1, 5);
        var targetLevel = optimizeCount * 5;
        var targetLevelText = targetLevel.ToString();
        if (!string.Equals(TargetLevelTextBox.Text, targetLevelText, StringComparison.Ordinal))
        {
            TargetLevelTextBox.Text = targetLevelText;
        }
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

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "설정 내보내기",
            FileName = GetDefaultProfileFileName(),
            Filter = "Zip archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            AppendLog("설정 내보내기 시작");
            await Task.Run(() => ExportProfile(dialog.FileName));
            AppendLog($"설정 내보내기 완료: {dialog.FileName}");
            System.Windows.MessageBox.Show(this, "설정 내보내기가 완료되었습니다.", "설정 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"설정 내보내기 실패: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "설정 내보내기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "설정 불러오기",
            Filter = "Zip archive (*.zip)|*.zip",
            DefaultExt = ".zip",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            "현재 캘리브레이션 설정과 에셋을 선택한 파일의 내용으로 덮어씁니다. 계속할까요?",
            "설정 불러오기",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            AppendLog("설정 불러오기 시작");
            await Task.Run(() => ImportProfile(dialog.FileName));
            _config = _calibrationManager.LoadOrCreate();
            LoadConfigToUi();
            ApplyTheme(_config.DarkMode);
            AppendLog("설정 불러오기 완료");
            System.Windows.MessageBox.Show(this, "설정 불러오기가 완료되었습니다.", "설정 불러오기", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"설정 불러오기 실패: {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "설정 불러오기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportProfile(string destinationPath)
    {
        if (!File.Exists(_calibrationManager.ConfigPath))
        {
            throw new FileNotFoundException("내보낼 config.json 파일을 찾을 수 없습니다.", _calibrationManager.ConfigPath);
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(_calibrationManager.ConfigPath, "config.json", CompressionLevel.Optimal);

        if (!Directory.Exists(_calibrationManager.AssetsDirectory))
        {
            return;
        }

        foreach (var assetPath in Directory.EnumerateFiles(_calibrationManager.AssetsDirectory, "*.png", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_calibrationManager.AssetsDirectory, assetPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(assetPath, $"assets/{relativePath}", CompressionLevel.Optimal);
        }
    }

    private void ImportProfile(string sourcePath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "wuwa-echo-craftsman-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            ValidateZipEntryPaths(sourcePath);
            ZipFile.ExtractToDirectory(sourcePath, tempDirectory);

            var importedConfigPath = Path.Combine(tempDirectory, "config.json");
            var importedAssetsDirectory = Path.Combine(tempDirectory, "assets");
            if (!File.Exists(importedConfigPath))
            {
                throw new InvalidOperationException("압축 파일에 config.json이 없습니다.");
            }

            if (!Directory.Exists(importedAssetsDirectory))
            {
                throw new InvalidOperationException("압축 파일에 assets 폴더가 없습니다.");
            }

            var hasRequiredPng = CalibrationTargets.RequiredAssetKeys
                .Any(assetName => File.Exists(Path.Combine(importedAssetsDirectory, assetName)));
            if (!hasRequiredPng)
            {
                throw new InvalidOperationException("압축 파일에 필수 템플릿 PNG가 없습니다.");
            }

            Directory.CreateDirectory(_calibrationManager.DataDirectory);
            File.Copy(importedConfigPath, _calibrationManager.ConfigPath, overwrite: true);

            if (Directory.Exists(_calibrationManager.AssetsDirectory))
            {
                Directory.Delete(_calibrationManager.AssetsDirectory, recursive: true);
            }

            Directory.CreateDirectory(_calibrationManager.AssetsDirectory);
            foreach (var importedAssetPath in Directory.EnumerateFiles(importedAssetsDirectory, "*.png", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(importedAssetsDirectory, importedAssetPath);
                var targetPath = Path.Combine(_calibrationManager.AssetsDirectory, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(importedAssetPath, targetPath, overwrite: true);
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static void ValidateZipEntryPaths(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        foreach (var entry in archive.Entries)
        {
            if (Path.IsPathRooted(entry.FullName)
                || entry.FullName.Contains("..", StringComparison.Ordinal)
                || entry.FullName.Contains('\\', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("압축 파일에 허용되지 않는 경로가 포함되어 있습니다.");
            }
        }
    }

    private static string GetDefaultProfileFileName()
    {
        var width = (int)SystemParameters.PrimaryScreenWidth;
        var height = (int)SystemParameters.PrimaryScreenHeight;
        return $"WuWa_{width}x{height}_Profile.zip";
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
            using var capture = _config.Regions.TryGetValue("roi_substat", out var region) && !region.IsEmpty
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
                CalibrationScreen.Enhance,
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
        await RunStepTestAsync("단계별 투입 1회", async input =>
        {
            ClickRegion(input, "roi_staged_auto_input");
            await Task.Delay(_config.ActionDelayMs);
            AppendLog("TEST STAGED: 단계별 투입 영역 클릭 완료");
        });
    }

    private async void TestExpectedLevel_Click(object sender, RoutedEventArgs e)
    {
        await RunStepTestAsync("부옵션 OCR", async _ =>
        {
            var substatRegion = _config.Regions["roi_substat"];
            using var capture = _screenCapturer.CaptureRegion(substatRegion);
            using var processed = _visionProcessor.PreprocessForOcr(capture);
            var text = await _visionProcessor.RecognizeTextAsync(processed);
            var parsed = SubstatInfo.ParseLines(text);
            AppendLog($"TEST OCR: 부옵션 {parsed.Count}개 인식{Environment.NewLine}{text}");
        });
    }

    private async void Calibration_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        var window = new CalibrationWindow(_config, _calibrationManager, _screenCapturer, AppendLog)
        {
            Owner = this,
        };
        window.ShowDialog();
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
            _ => "캘리브레이션 화면",
        };
    }

    private static string GetCalibrationPreparationMessage(CalibrationScreen screen)
    {
        return screen switch
        {
            CalibrationScreen.EchoList =>
                "1/2 에코 목록 화면을 준비하세요." + Environment.NewLine
                + "- 캐릭터 > 에코 탭의 에코 목록 화면으로 이동하세요." + Environment.NewLine
                + "- 목표 세트/코스트 필터와 레벨 오름차순 정렬을 적용하세요." + Environment.NewLine
                + "- +0 에코가 보이고, 에코 선택 시 육성 버튼이 보이는 상태가 좋습니다.",

            CalibrationScreen.Enhance =>
                "2/2 에코 강화 화면을 준비하세요." + Environment.NewLine
                + "- 목록에서 +0 에코를 선택하고 육성 버튼을 눌러 강화 화면으로 이동하세요." + Environment.NewLine
                + "- 인게임 자동 투입 설정을 단계별 투입 + 옵티마이즈 동기화 켜기 + 강화 재료 및 에코로 맞춰두세요." + Environment.NewLine
                + "- 단계별 투입 버튼, 강화 버튼, 완료 오버레이 닫기 영역, 부옵션 텍스트 영역이 보이는 상태로 준비하세요.",

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

        try
        {
            await PrepareForGameInputAsync("자동화 시작");
            if (_automationOverlay is null)
            {
                _automationOverlay = new AutomationOverlayWindow();
                _automationOverlay.Closed += (_, _) => _automationOverlay = null;
            }

            _automationOverlay.ResetForNewRun();
            _automationOverlay.Show();

            var automator = new EchoAutomator(
                _config,
                _calibrationManager,
                _screenCapturer,
                _visionProcessor,
                input,
                _databaseService,
                AppendLog,
                _automationOverlay.UpdateSubstats,
                _automationOverlay.AddHistory);

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
