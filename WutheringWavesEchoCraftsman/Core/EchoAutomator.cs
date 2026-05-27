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
            await Task.Delay(ActionDelayMs, cancellationToken);
            remaining--;
            _config.RemainingCount = remaining;
        }

        _log("자동화 정상 완료");
    }

    private async Task<bool> SearchAsync(CancellationToken cancellationToken)
    {
        var listRegion = _config.Regions["roi_list"];
        using var listCapture = _screenCapturer.CaptureRegion(listRegion);

        var matches = FindAssets(listCapture, "template_plus_zero.png");
        var match = matches.FirstOrDefault(new TemplateMatchResult(false, 0, 0, 0));
        _log($"SEARCH: +0 후보 {matches.Count}개, 선택 confidence={match.Confidence:0.000}, local=({match.CenterX}, {match.CenterY})");
        if (!match.Success)
        {
            return false;
        }

        _inputController.Click(listRegion.X + match.CenterX, listRegion.Y + match.CenterY);
        await Task.Delay(ActionDelayMs, cancellationToken);
        await ClickRegionAsync("roi_enhance_tab", cancellationToken);

        return true;
    }

    private async Task EnhanceAsync(CancellationToken cancellationToken)
    {
        var previousExpectedLevel = await ReadExpectedLevelAsync(cancellationToken);
        var materialClicksWithoutLevelIncrease = 0;
        var usedDiscardMaterial = false;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFailSafeNotTriggered();

            await ClickRegionAsync("roi_slot_plus", cancellationToken);

            var materialClicksThisAttempt = 0;
            var usedDiscardThisAttempt = false;

            if (_config.UseDiscardEchoMaterials)
            {
                var materialRegion = _config.Regions["roi_material"];
                using var materialCapture = _screenCapturer.CaptureRegion(materialRegion);
                var discard = FindAsset(materialCapture, "icon_discard.png", 0.80);

                if (discard.Success)
                {
                    _inputController.Click(materialRegion.X + discard.CenterX, materialRegion.Y + discard.CenterY);
                    usedDiscardMaterial = true;
                    usedDiscardThisAttempt = true;
                    materialClicksThisAttempt = 1;
                    await Task.Delay(ActionDelayMs, cancellationToken);
                }
            }

            if (!usedDiscardThisAttempt)
            {
                materialClicksThisAttempt = await ClickExpMaterialsBatchAsync(cancellationToken);
            }

            var expectedLevel = await ReadExpectedLevelAsync(cancellationToken);
            _log($"ENHANCE: 강화 후 예상 레벨 판독={expectedLevel}");

            if (expectedLevel > previousExpectedLevel)
            {
                previousExpectedLevel = expectedLevel;
                materialClicksWithoutLevelIncrease = 0;
            }
            else
            {
                materialClicksWithoutLevelIncrease += Math.Max(1, materialClicksThisAttempt);
            }

            if (expectedLevel >= _config.TargetLevel)
            {
                _inputController.PressKey(VirtualKeys.Escape);
                _log("ENHANCE: 재료 선택장 닫기 ESC 입력");
                await Task.Delay(ActionDelayMs, cancellationToken);
                await ClickRegionAsync("roi_enhance_confirm", cancellationToken);
                if (usedDiscardMaterial)
                {
                    await ClickRegionAsync("roi_discard_material_confirm", cancellationToken);
                }

                await CloseCompletionOverlayAsync("roi_enhance_complete_close", "ENHANCE", cancellationToken);
                return;
            }

            if (materialClicksWithoutLevelIncrease >= 5)
            {
                throw new InvalidOperationException("강화 재료를 5회 클릭했지만 예상 레벨이 증가하지 않았습니다.");
            }
        }

        throw new InvalidOperationException("강화 반복 제한을 초과했습니다.");
    }

    private async Task OptimizeAsync(CancellationToken cancellationToken)
    {
        await ClickRegionAsync("roi_optimize_tab", cancellationToken);
        await Task.Delay(ActionDelayMs, cancellationToken);

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

        await SetOptimizeCountAsync(cancellationToken);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ClickRegionAsync("roi_optimize_confirm", cancellationToken);
            await CloseCompletionOverlayAsync("roi_optimize_complete_close", "OPTIMIZE", cancellationToken);

            await Task.Delay(ActionDelayMs, cancellationToken);
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
        var requiredRules = enabledRules.Where(rule => rule.Required).ToArray();
        var requiredSatisfied = requiredRules.All(rule =>
            parsed.Any(stat => stat.Key == rule.Key && stat.Value >= rule.MinValue));

        var decision = requiredSatisfied && validCount >= _config.RequiredValidSubstatCount ? "LOCK" : "DISCARD";
        _log($"EVALUATE: 필수 {requiredRules.Length}개 충족={requiredSatisfied}, 유효 {validCount}/{_config.RequiredValidSubstatCount}, 판정={decision}");

        _inputController.PressKey(decision == "LOCK" ? VirtualKeys.C : VirtualKeys.Z);
        await _databaseService.InsertResultAsync(text, decision, validCount, cancellationToken);
    }

    private async Task<int> ReadExpectedLevelAsync(CancellationToken cancellationToken)
    {
        var levelRegion = _config.Regions["roi_expected_level"];
        using var levelCapture = _screenCapturer.CaptureRegion(levelRegion);
        var text = await _visionProcessor.RecognizeTextAsync(levelCapture, cancellationToken);
        var match = Regex.Match(text, @"\+?\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private async Task SetOptimizeCountAsync(CancellationToken cancellationToken)
    {
        var targetCount = Math.Clamp(_config.TargetOptimizeCount, 1, 5);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var currentCount = await ReadOptimizeCountAsync(cancellationToken);
            _log($"OPTIMIZE: 현재 시행 횟수={currentCount}, 목표={targetCount}");

            if (currentCount == targetCount)
            {
                return;
            }

            if (currentCount == 0)
            {
                await Task.Delay(ActionDelayMs, cancellationToken);
                continue;
            }

            var regionKey = currentCount < targetCount ? "roi_optimize_plus" : "roi_optimize_minus";
            var clickCount = Math.Abs(targetCount - currentCount);
            for (var index = 0; index < clickCount; index++)
            {
                var region = _config.Regions[regionKey];
                ClickRegion(region);
                _log($"OPTIMIZE: {regionKey} 빠른 클릭 {index + 1}/{clickCount}");
                await Task.Delay(OptimizeCountClickDelayMs, cancellationToken);
            }

            await Task.Delay(ActionDelayMs, cancellationToken);
        }

        throw new InvalidOperationException($"옵티마이즈 시행 횟수를 목표값({_config.TargetOptimizeCount})으로 맞추지 못했습니다.");
    }

    private async Task<int> ReadOptimizeCountAsync(CancellationToken cancellationToken)
    {
        var countRegion = _config.Regions["roi_optimize_count"];
        using var capture = _screenCapturer.CaptureRegion(countRegion);
        var text = await _visionProcessor.RecognizeTextAsync(capture, cancellationToken);
        var matches = Regex.Matches(text, @"\d+")
            .Select(match => int.Parse(match.Value))
            .Where(value => value is >= 1 and <= 5)
            .ToArray();

        return matches.LastOrDefault();
    }

    private async Task ClickRegionAsync(string regionKey, CancellationToken cancellationToken)
    {
        var region = _config.Regions[regionKey];
        ClickRegion(region);
        _log($"{regionKey}: 중앙 클릭 ({region.X + region.Width / 2}, {region.Y + region.Height / 2})");
        await Task.Delay(ActionDelayMs, cancellationToken);
    }

    private async Task CloseCompletionOverlayAsync(string regionKey, string phase, CancellationToken cancellationToken)
    {
        await Task.Delay(CompletionOverlayDelayMs, cancellationToken);
        if (!_config.Regions.TryGetValue(regionKey, out var region) || region.IsEmpty)
        {
            _log($"{phase}: 완료 오버레이 닫기 영역이 설정되지 않아 닫기 클릭을 건너뜁니다.");
            return;
        }

        ClickRegion(region);
        _log($"{phase}: 완료 오버레이 닫기 클릭 ({region.X + region.Width / 2}, {region.Y + region.Height / 2})");
        await Task.Delay(ActionDelayMs, cancellationToken);
    }

    private void ClickRegion(RegionRect region)
    {
        if (region.IsEmpty)
        {
            throw new InvalidOperationException("클릭 영역이 설정되지 않았습니다.");
        }

        _inputController.Click(region.X + region.Width / 2, region.Y + region.Height / 2);
    }

    private IReadOnlyList<RegionRect> GetConfiguredExpMaterialRegions()
    {
        return
        [
            .. Enumerable.Range(1, 4)
                .Select(index => $"roi_exp_material_{index}")
                .Take(Math.Clamp(_config.ExpMaterialSlotsToUse, 1, 4))
                .Where(key => _config.Regions.TryGetValue(key, out var region) && !region.IsEmpty)
                .Select(key => _config.Regions[key]),
        ];
    }

    private async Task<int> ClickExpMaterialsBatchAsync(CancellationToken cancellationToken)
    {
        var expRegions = GetConfiguredExpMaterialRegions();
        if (expRegions.Count == 0)
        {
            throw new InvalidOperationException("재료 소진: 폐기 에코를 찾지 못했고 음파통 영역도 설정되지 않았습니다.");
        }

        var clickCount = GetSuggestedExpMaterialClickCount(_config.TargetLevel);
        for (var index = 0; index < clickCount; index++)
        {
            var expRegion = expRegions[index % expRegions.Count];
            ClickRegion(expRegion);
            _log($"ENHANCE: 음파통 빠른 클릭 {index + 1}/{clickCount} ({expRegion.X}, {expRegion.Y}, {expRegion.Width}, {expRegion.Height})");
            await Task.Delay(ExpMaterialClickDelayMs, cancellationToken);
        }

        return clickCount;
    }

    private static int GetSuggestedExpMaterialClickCount(int targetLevel)
    {
        if (targetLevel >= 25)
        {
            return 29;
        }

        if (targetLevel >= 20)
        {
            return 16;
        }

        if (targetLevel >= 15)
        {
            return 8;
        }

        if (targetLevel >= 10)
        {
            return 4;
        }

        return 1;
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
        await Task.Delay(ActionDelayMs, cancellationToken);
        return true;
    }

    private int ActionDelayMs => Math.Max(100, _config.ActionDelayMs);

    private int CompletionOverlayDelayMs => Math.Max(300, _config.CompletionOverlayDelayMs);

    private int ExpMaterialClickDelayMs => Math.Max(50, _config.ExpMaterialClickDelayMs);

    private int OptimizeCountClickDelayMs => Math.Max(50, _config.OptimizeCountClickDelayMs);

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

    private IReadOnlyList<TemplateMatchResult> FindAssets(Bitmap source, string assetName, double threshold = 0.85)
    {
        var assetPath = _calibrationManager.ResolvePath(_config.Assets[assetName]);
        if (!File.Exists(assetPath))
        {
            _log($"에셋 없음: {assetPath}");
            return [];
        }

        using var template = new Bitmap(assetPath);
        return _visionProcessor.FindTemplateMatches(source, template, threshold);
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
