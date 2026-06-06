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
    private readonly Action<int?>? _updateOverlayLevel;
    private readonly Action<IReadOnlyList<ParsedSubstat>>? _updateOverlaySubstats;
    private readonly Action<string>? _addOverlayHistory;
    private int _processedCount;

    public EchoAutomator(
        AppConfig config,
        CalibrationManager calibrationManager,
        ScreenCapturer screenCapturer,
        VisionProcessor visionProcessor,
        InputController inputController,
        DatabaseService databaseService,
        Action<string> log,
        Action<int?>? updateOverlayLevel = null,
        Action<IReadOnlyList<ParsedSubstat>>? updateOverlaySubstats = null,
        Action<string>? addOverlayHistory = null)
    {
        _config = config;
        _calibrationManager = calibrationManager;
        _screenCapturer = screenCapturer;
        _visionProcessor = visionProcessor;
        _inputController = inputController;
        _databaseService = databaseService;
        _log = log;
        _updateOverlayLevel = updateOverlayLevel;
        _updateOverlaySubstats = updateOverlaySubstats;
        _addOverlayHistory = addOverlayHistory;
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

            await RunStagedEnhanceLoopAsync(cancellationToken);
            remaining--;
            _config.RemainingCount = remaining;
        }

        _log("자동화 정상 완료");
    }

    private async Task<bool> SearchAsync(CancellationToken cancellationToken)
    {
        var listRegion = _config.Regions["roi_list"];

        for (var attempt = 0; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFailSafeNotTriggered();

            using var listCapture = _screenCapturer.CaptureRegion(listRegion);
            var matches = FindAssets(listCapture, "template_plus_zero.png");
            var match = matches.FirstOrDefault(new TemplateMatchResult(false, 0, 0, 0));
            _log($"SEARCH: +0 후보 {matches.Count}개, 선택 confidence={match.Confidence:0.000}, local=({match.CenterX}, {match.CenterY}), 탐색={attempt + 1}/4");
            if (match.Success)
            {
                _inputController.Click(listRegion.X + match.CenterX, listRegion.Y + match.CenterY);
                await Task.Delay(ActionDelayMs, cancellationToken);
                await ClickRegionAsync("roi_enhance_tab", cancellationToken);
                return true;
            }

            if (attempt == 3)
            {
                break;
            }

            await ScrollEchoListAsync(listRegion, attempt + 1, cancellationToken);
        }

        return false;
    }

    private async Task ScrollEchoListAsync(RegionRect listRegion, int scrollAttempt, CancellationToken cancellationToken)
    {
        var centerX = listRegion.X + listRegion.Width / 2;
        var centerY = listRegion.Y + listRegion.Height / 2;
        var direction = string.Equals(_config.EchoListScrollDirection, "Up", StringComparison.OrdinalIgnoreCase)
            ? "Up"
            : "Down";
        var scrollAmount = Math.Max(120, _config.EchoListScrollAmount);
        var wheelDelta = direction == "Up" ? scrollAmount : -scrollAmount;

        _log($"SEARCH: +0 미발견, 목록 {GetScrollDirectionText(direction)} 휠 스크롤 {scrollAttempt}/3 ({centerX},{centerY}), delta={wheelDelta}");
        _inputController.ScrollWheel(centerX, centerY, wheelDelta);
        await Task.Delay(EchoListScrollDelayMs, cancellationToken);
    }

    private async Task RunStagedEnhanceLoopAsync(CancellationToken cancellationToken)
    {
        var targetLevel = Math.Clamp(_config.TargetLevel, 5, 25);
        EvaluationResult? lastEvaluation = null;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFailSafeNotTriggered();

            _log($"STAGED_ENHANCE: 단계별 강화 {attempt}/5 시작, 목표 레벨=+{targetLevel}");
            var usedDiscardMaterial = _config.UseDiscardEchoMaterials
                && await TrySelectDiscardEchoMaterialAsync(cancellationToken);
            if (!usedDiscardMaterial)
            {
                await ClickRegionAsync("roi_staged_auto_input", cancellationToken);
            }

            await ClickRegionAsync("roi_enhance_confirm", cancellationToken);
            await CloseCompletionOverlayAsync(
                "roi_enhance_complete_close",
                usedDiscardMaterial ? "DISCARD_MATERIAL_ENHANCE" : "STAGED_ENHANCE",
                cancellationToken);

            var currentLevel = await ReadCurrentLevelAsync(cancellationToken);
            if (currentLevel is null)
            {
                throw new InvalidOperationException("현재 에코 레벨 OCR에 실패했습니다. 재료 과소비 방지를 위해 자동화를 중단합니다.");
            }

            _log($"STAGED_ENHANCE: 현재 레벨 OCR=+{currentLevel.Value}, 목표=+{targetLevel}");
            lastEvaluation = await EvaluateSubstatsAsync(cancellationToken);
            if (lastEvaluation.IsSatisfied)
            {
                await ApplyDecisionAsync(lastEvaluation with { Decision = "LOCK" }, cancellationToken);
                return;
            }

            if (currentLevel.Value >= targetLevel)
            {
                _log($"STAGED_ENHANCE: 목표 레벨 도달(+{currentLevel.Value}/+{targetLevel}), 조건 미달로 폐기");
                await ApplyDecisionAsync(lastEvaluation with { Decision = "DISCARD" }, cancellationToken);
                return;
            }

            if (TryGetEarlyDiscardReason(lastEvaluation, currentLevel.Value, targetLevel, out var reason))
            {
                _log($"STAGED_ENHANCE: 조기 폐기 - {reason}");
                await ApplyDecisionAsync(lastEvaluation with { Decision = "DISCARD" }, cancellationToken);
                return;
            }
        }

        await ApplyDecisionAsync((lastEvaluation ?? EvaluationResult.Empty) with { Decision = "DISCARD" }, cancellationToken);
    }

    private async Task<bool> TrySelectDiscardEchoMaterialAsync(CancellationToken cancellationToken)
    {
        _log("DISCARD_MATERIAL: 폐기 에코 우선 투입 시도");
        await ClickRegionAsync("roi_echo_material_input", cancellationToken);

        var materialRegion = _config.Regions["roi_echo_material_list"];
        using var materialCapture = _screenCapturer.CaptureRegion(materialRegion);
        var discards = FindAssets(materialCapture, "template_discard_echo.png", 0.80);
        var discard = discards.FirstOrDefault(new TemplateMatchResult(false, 0, 0, 0));
        _log($"DISCARD_MATERIAL: 폐기 에코 후보 {discards.Count}개");

        if (!discard.Success)
        {
            _inputController.PressKey(VirtualKeys.Escape);
            _log("DISCARD_MATERIAL: 폐기 에코 없음, 재료 목록 닫기 후 단계별 투입으로 전환");
            await Task.Delay(ActionDelayMs, cancellationToken);
            return false;
        }

        _inputController.Click(materialRegion.X + discard.CenterX, materialRegion.Y + discard.CenterY);
        _log($"DISCARD_MATERIAL: 폐기 에코 선택 confidence={discard.Confidence:0.000}, local=({discard.CenterX}, {discard.CenterY})");
        await Task.Delay(ActionDelayMs, cancellationToken);

        _inputController.PressKey(VirtualKeys.Escape);
        _log("DISCARD_MATERIAL: 재료 목록 닫기 ESC 입력");
        await Task.Delay(ActionDelayMs, cancellationToken);
        return true;
    }

    private async Task<EvaluationResult> EvaluateSubstatsAsync(CancellationToken cancellationToken)
    {
        var substatRegion = _config.Regions["roi_substat"];
        using var capture = _screenCapturer.CaptureRegion(substatRegion);
        using var processed = _visionProcessor.PreprocessForOcr(capture);
        var text = await _visionProcessor.RecognizeTextAsync(processed, cancellationToken);
        var parsed = SubstatInfo.ParseLines(text);
        _updateOverlaySubstats?.Invoke(parsed);

        var enabledRules = _config.SubstatRules.Where(rule => rule.Enabled).ToArray();
        var parsedKeys = parsed.Select(stat => stat.Key).ToHashSet(StringComparer.Ordinal);
        var validCount = enabledRules.Count(rule => parsedKeys.Contains(rule.Key));
        var requiredRules = enabledRules.Where(rule => rule.Required).ToArray();
        var requiredMatchedCount = requiredRules.Count(rule => parsedKeys.Contains(rule.Key));
        var requiredSatisfied = requiredMatchedCount == requiredRules.Length;
        var isSatisfied = requiredSatisfied && validCount >= _config.RequiredValidSubstatCount;

        _log($"EVALUATE: 필수 {requiredRules.Length}개 충족={requiredSatisfied}, 유효 {validCount}/{_config.RequiredValidSubstatCount}, 조건만족={isSatisfied}");
        return new EvaluationResult(text, parsed.Count, validCount, requiredRules.Length, requiredMatchedCount, requiredSatisfied, isSatisfied, isSatisfied ? "LOCK" : "DISCARD");
    }

    private async Task ApplyDecisionAsync(EvaluationResult evaluation, CancellationToken cancellationToken)
    {
        _log($"EVALUATE: 최종 판정={evaluation.Decision}");
        _addOverlayHistory?.Invoke(
            $"{DateTime.Now:HH:mm:ss} #{_processedCount + 1} {(evaluation.Decision == "LOCK" ? "잠금" : "폐기")} (유효 {evaluation.ValidCount}/{_config.RequiredValidSubstatCount})");
        _inputController.PressKey(evaluation.Decision == "LOCK" ? VirtualKeys.C : VirtualKeys.Z);
        await _databaseService.InsertResultAsync(evaluation.RawText, evaluation.Decision, evaluation.ValidCount, cancellationToken);
        _processedCount++;
        await Task.Delay(ActionDelayMs, cancellationToken);

        _inputController.PressKey(VirtualKeys.Escape);
        _log($"RETURN: ESC 입력으로 에코 목록 복귀, {ReturnToListDelayMs}ms 대기");
        await Task.Delay(ReturnToListDelayMs, cancellationToken);
    }

    private bool TryGetEarlyDiscardReason(EvaluationResult evaluation, int currentLevel, int targetLevel, out string reason)
    {
        var currentRevealedCount = GetRevealedSubstatCount(currentLevel);
        var targetRevealedCount = GetRevealedSubstatCount(targetLevel);
        var remainingRevealCount = Math.Max(0, targetRevealedCount - currentRevealedCount);

        if (evaluation.ObservedSubstatCount < currentRevealedCount)
        {
            reason = $"현재 +{currentLevel} 기준 공개 부옵 {currentRevealedCount}개 중 {evaluation.ObservedSubstatCount}개만 OCR 인식되어 조기 폐기를 보류";
            _log($"STAGED_ENHANCE: {reason}");
            return false;
        }

        var maxPossibleValidCount = evaluation.ValidCount + remainingRevealCount;
        if (maxPossibleValidCount < _config.RequiredValidSubstatCount)
        {
            reason = $"최대 가능 유효 부옵 {maxPossibleValidCount}/{_config.RequiredValidSubstatCount} (현재 {evaluation.ValidCount} + 남은 {remainingRevealCount})";
            return true;
        }

        var missingRequiredCount = evaluation.RequiredCount - evaluation.RequiredMatchedCount;
        if (missingRequiredCount > remainingRevealCount)
        {
            reason = $"필수 부옵 미충족 {missingRequiredCount}개가 남은 공개 가능 수 {remainingRevealCount}개보다 많음";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private async Task<int?> ReadCurrentLevelAsync(CancellationToken cancellationToken)
    {
        var levelRegion = _config.Regions["roi_current_level"];
        using var capture = _screenCapturer.CaptureRegion(levelRegion);
        var attempts = new List<string>();

        using var candidates = new DisposableBitmapList(_visionProcessor.CreateSmallTextOcrCandidates(capture));
        for (var index = 0; index < candidates.Items.Count; index++)
        {
            var text = await _visionProcessor.RecognizeTextAsync(candidates.Items[index], cancellationToken);
            attempts.Add($"#{index + 1}='{text.ReplaceLineEndings(" ")}'");

            var level = ParseEchoLevel(text);
            if (level.HasValue)
            {
                _log($"STAGED_ENHANCE: 현재 레벨 OCR 성공 후보=#{index + 1}, 원문='{text.ReplaceLineEndings(" ")}', 판독=+{level.Value}");
                _updateOverlayLevel?.Invoke(level);
                return level;
            }
        }

        _log($"STAGED_ENHANCE: 현재 레벨 OCR 실패, ROI=({levelRegion.X},{levelRegion.Y},{levelRegion.Width},{levelRegion.Height}), 후보 원문={string.Join(", ", attempts)}");
        _updateOverlayLevel?.Invoke(null);
        return null;
    }

    private static int? ParseEchoLevel(string text)
    {
        var plusMatch = Regex.Match(text, @"[+＋]\s*(\d{1,2})");
        if (plusMatch.Success && int.TryParse(plusMatch.Groups[1].Value, out var plusLevel) && plusLevel is >= 0 and <= 25)
        {
            return plusLevel;
        }

        foreach (Match match in Regex.Matches(text, @"\d{1,2}"))
        {
            if (int.TryParse(match.Value, out var level) && level is >= 0 and <= 25)
            {
                return level;
            }
        }

        return null;
    }

    private static int GetRevealedSubstatCount(int level)
    {
        return Math.Clamp(level / 5, 0, 5);
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
            var expectedLevel = previousExpectedLevel;

            if (_config.UseDiscardEchoMaterials)
            {
                var materialRegion = _config.Regions["roi_material"];
                using var materialCapture = _screenCapturer.CaptureRegion(materialRegion);
                var discards = FindAssets(materialCapture, "icon_discard.png", 0.80);
                _log($"ENHANCE: 폐기 에코 후보 {discards.Count}개");

                foreach (var discard in discards)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _inputController.Click(materialRegion.X + discard.CenterX, materialRegion.Y + discard.CenterY);
                    usedDiscardMaterial = true;
                    usedDiscardThisAttempt = true;
                    materialClicksThisAttempt++;
                    await Task.Delay(ActionDelayMs, cancellationToken);

                    expectedLevel = await ReadExpectedLevelAsync(cancellationToken);
                    _log($"ENHANCE: 폐기 에코 {materialClicksThisAttempt}/{discards.Count} 선택 후 예상 레벨 판독={expectedLevel}");
                    if (expectedLevel >= _config.TargetLevel)
                    {
                        break;
                    }
                }
            }

            if (expectedLevel < _config.TargetLevel)
            {
                if (usedDiscardThisAttempt)
                {
                    var expResult = await ClickExpMaterialsUntilTargetAsync(cancellationToken);
                    materialClicksThisAttempt += expResult.ClickCount;
                    expectedLevel = expResult.ExpectedLevel;
                }
                else
                {
                    materialClicksThisAttempt += await ClickExpMaterialsBatchAsync(cancellationToken);
                    expectedLevel = await ReadExpectedLevelAsync(cancellationToken);
                    _log($"ENHANCE: 음파통 투입 후 예상 레벨 판독={expectedLevel}");
                }
            }

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
        var parsedKeys = parsed.Select(stat => stat.Key).ToHashSet(StringComparer.Ordinal);
        var validCount = enabledRules.Count(rule => parsedKeys.Contains(rule.Key));
        var requiredRules = enabledRules.Where(rule => rule.Required).ToArray();
        var requiredMatchedCount = requiredRules.Count(rule => parsedKeys.Contains(rule.Key));
        var requiredSatisfied = requiredMatchedCount == requiredRules.Length;

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

    private async Task<(int ClickCount, int ExpectedLevel)> ClickExpMaterialsUntilTargetAsync(CancellationToken cancellationToken)
    {
        var expRegions = GetConfiguredExpMaterialRegions();
        if (expRegions.Count == 0)
        {
            throw new InvalidOperationException("재료 소진: 폐기 에코를 사용했지만 목표 레벨에 도달하지 못했고 음파통 영역도 설정되지 않았습니다.");
        }

        var clickLimit = GetSuggestedExpMaterialClickCount(_config.TargetLevel);
        var expectedLevel = 0;
        for (var index = 0; index < clickLimit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var expRegion = expRegions[index % expRegions.Count];
            ClickRegion(expRegion);
            await Task.Delay(ActionDelayMs, cancellationToken);

            expectedLevel = await ReadExpectedLevelAsync(cancellationToken);
            _log($"ENHANCE: 폐기 에코 후 음파통 {index + 1}/{clickLimit} 클릭, 예상 레벨 판독={expectedLevel}");
            if (expectedLevel >= _config.TargetLevel)
            {
                return (index + 1, expectedLevel);
            }
        }

        return (clickLimit, expectedLevel);
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

    private int ReturnToListDelayMs => Math.Max(300, _config.ReturnToListDelayMs);

    private int EchoListScrollDelayMs => Math.Max(300, _config.EchoListScrollDelayMs);

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

    private static string GetScrollDirectionText(string direction)
    {
        return direction == "Up" ? "위로" : "아래로";
    }

    private sealed record EvaluationResult(
        string RawText,
        int ObservedSubstatCount,
        int ValidCount,
        int RequiredCount,
        int RequiredMatchedCount,
        bool RequiredSatisfied,
        bool IsSatisfied,
        string Decision)
    {
        public static EvaluationResult Empty { get; } = new(string.Empty, 0, 0, 0, 0, false, false, "DISCARD");
    }

    private sealed class DisposableBitmapList(IReadOnlyList<Bitmap> items) : IDisposable
    {
        public IReadOnlyList<Bitmap> Items { get; } = items;

        public void Dispose()
        {
            foreach (var item in Items)
            {
                item.Dispose();
            }
        }
    }
}
