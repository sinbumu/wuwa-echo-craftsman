using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using WutheringWavesEchoCraftsman.Models;
using WutheringWavesEchoCraftsman.Services;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class EchoAutomator
{
    private readonly AppConfig _config;
    private readonly CalibrationManager _calibrationManager;
    private readonly ScreenCapturer _screenCapturer;
    private readonly VisionProcessor _visionProcessor;
    private readonly InputController _inputController;
    private readonly DatabaseService _databaseService;
    private readonly Action<string> _log;

    public EchoAutomator(
        AppConfig config,
        CalibrationManager calibrationManager,
        ScreenCapturer screenCapturer,
        VisionProcessor visionProcessor,
        InputController inputController,
        DatabaseService databaseService,
        Action<string> log)
    {
        _config = config;
        _calibrationManager = calibrationManager;
        _screenCapturer = screenCapturer;
        _visionProcessor = visionProcessor;
        _inputController = inputController;
        _databaseService = databaseService;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var remaining = _config.RemainingCount;
        _log($"자동화 시작: remainingCount={remaining}, dryRun={_config.DryRun}");

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFailSafeNotTriggered();

            _log($"루프 시작: 남은 횟수 {remaining}");
            if (!await SearchAsync(cancellationToken))
            {
                _log("SEARCH: +0 에코를 찾지 못해 정상 종료합니다.");
                return;
            }

            await EnhanceAsync(cancellationToken);
            await OptimizeAsync(cancellationToken);
            await EvaluateAsync(cancellationToken);

            _inputController.PressKey(VirtualKeys.Escape);
            await Task.Delay(500, cancellationToken);
            remaining--;
            _config.RemainingCount = remaining;
        }

        _log("자동화 정상 완료");
    }

    private async Task<bool> SearchAsync(CancellationToken cancellationToken)
    {
        var listRegion = _config.Regions["roi_list"];
        using var listCapture = _screenCapturer.CaptureRegion(listRegion);

        var match = FindAsset(listCapture, "template_plus_zero.png");
        _log($"SEARCH: +0 매칭 confidence={match.Confidence:0.000}");
        if (!match.Success)
        {
            return false;
        }

        _inputController.Click(listRegion.X + match.CenterX, listRegion.Y + match.CenterY);
        await Task.Delay(300, cancellationToken);
        await ClickAssetOnScreenAsync("btn_enhance_tab.png", cancellationToken);

        return true;
    }

    private async Task EnhanceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFailSafeNotTriggered();

            var level = await ReadLevelAsync(cancellationToken);
            _log($"ENHANCE: 현재 레벨 판독={level}");
            if (level >= _config.TargetLevel)
            {
                return;
            }

            await ClickAssetOnScreenAsync("btn_slot_plus.png", cancellationToken);

            var materialRegion = _config.Regions["roi_material"];
            using var materialCapture = _screenCapturer.CaptureRegion(materialRegion);
            var discard = FindAsset(materialCapture, "icon_discard.png", 0.80);
            var exp = discard.Success ? discard : FindAsset(materialCapture, "icon_exp.png", 0.80);
            if (!exp.Success)
            {
                throw new InvalidOperationException("재료 소진: 폐기 에코와 음파통을 찾지 못했습니다.");
            }

            _inputController.Click(materialRegion.X + exp.CenterX, materialRegion.Y + exp.CenterY);
            await Task.Delay(200, cancellationToken);
            await ClickAssetOnScreenAsync("btn_enhance_confirm.png", cancellationToken);
            await Task.Delay(1200, cancellationToken);
        }

        throw new InvalidOperationException("강화 반복 제한을 초과했습니다.");
    }

    private async Task OptimizeAsync(CancellationToken cancellationToken)
    {
        await ClickAssetOnScreenAsync("btn_optimize_tab.png", cancellationToken);
        await Task.Delay(500, cancellationToken);

        if (!_config.Regions.TryGetValue("roi_substat", out var substatRegion) || substatRegion.IsEmpty)
        {
            _log("OPTIMIZE: 부옵션 ROI가 없어 버튼 매칭 결과만 사용합니다.");
            return;
        }

        var previousText = string.Empty;
        if (!substatRegion.IsEmpty)
        {
            using var before = _screenCapturer.CaptureRegion(substatRegion);
            previousText = await _visionProcessor.RecognizeTextAsync(before, cancellationToken);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clicked = await ClickAssetOnScreenAsync("btn_optimize_confirm.png", cancellationToken, throwOnFailure: false);
            if (!clicked)
            {
                _log("OPTIMIZE: 실행 버튼 매칭 실패를 완료 조건으로 간주합니다.");
                return;
            }

            await Task.Delay(800, cancellationToken);
            using var after = _screenCapturer.CaptureRegion(substatRegion);
            var currentText = await _visionProcessor.RecognizeTextAsync(after, cancellationToken);
            if (!string.Equals(previousText, currentText, StringComparison.Ordinal))
            {
                _log("OPTIMIZE: 부옵션 OCR 결과 갱신 감지");
                return;
            }
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var substatRegion = _config.Regions["roi_substat"];
        using var capture = _screenCapturer.CaptureRegion(substatRegion);
        using var processed = _visionProcessor.PreprocessForOcr(capture);
        var text = await _visionProcessor.RecognizeTextAsync(processed, cancellationToken);
        var parsed = SubstatInfo.ParseLines(text);

        var enabledRules = _config.SubstatRules.Where(rule => rule.Enabled).ToArray();
        var validCount = parsed.Count(stat =>
            enabledRules.Any(rule => rule.Key == stat.Key && stat.Value >= rule.MinValue));

        var decision = validCount >= _config.RequiredValidSubstatCount ? "LOCK" : "DISCARD";
        _log($"EVALUATE: 유효 {validCount}/{_config.RequiredValidSubstatCount}, 판정={decision}");

        _inputController.PressKey(decision == "LOCK" ? VirtualKeys.C : VirtualKeys.Z);
        await _databaseService.InsertResultAsync(text, decision, validCount, cancellationToken);
    }

    private async Task<int> ReadLevelAsync(CancellationToken cancellationToken)
    {
        var levelRegion = _config.Regions["roi_level"];
        using var levelCapture = _screenCapturer.CaptureRegion(levelRegion);
        var text = await _visionProcessor.RecognizeTextAsync(levelCapture, cancellationToken);
        var match = Regex.Match(text, @"\+?\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private async Task<bool> ClickAssetOnScreenAsync(string assetName, CancellationToken cancellationToken, bool throwOnFailure = true)
    {
        using var screen = _screenCapturer.CaptureVirtualScreen();
        var match = FindAsset(screen, assetName);
        _log($"{assetName}: confidence={match.Confidence:0.000}");

        if (!match.Success)
        {
            if (throwOnFailure)
            {
                throw new InvalidOperationException($"{assetName} 매칭 실패");
            }

            return false;
        }

        _inputController.Click(match.CenterX, match.CenterY);
        await Task.Delay(300, cancellationToken);
        return true;
    }

    private TemplateMatchResult FindAsset(Bitmap source, string assetName, double threshold = 0.85)
    {
        var assetPath = _calibrationManager.ResolvePath(_config.Assets[assetName]);
        if (!File.Exists(assetPath))
        {
            _log($"에셋 없음: {assetPath}");
            return new TemplateMatchResult(false, 0, 0, 0);
        }

        using var template = new Bitmap(assetPath);
        return _visionProcessor.FindTemplate(source, template, threshold);
    }

    private void EnsureFailSafeNotTriggered()
    {
        var (x, y) = _inputController.GetCursorPosition();
        if (x == 0 && y == 0)
        {
            throw new OperationCanceledException("마우스 모서리 Fail-Safe가 작동했습니다.");
        }
    }
}
