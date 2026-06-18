using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using PaddleOCRSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class VisionProcessor
{
    private static readonly object EngineLock = new();
    private static PaddleOCREngine? _ocrEngine;
    private static bool _engineDisposed;

    private static readonly string PortableModelDirectory =
        Path.Combine(AppContext.BaseDirectory, "data", "models");

    /// <summary>
    /// 한국어 rec 모델 + korean_dict.txt 가 준비된 경우 true.
    /// </summary>
    public static bool IsKoreanPaddleModelConfigured => TryCreateKoreanModelConfig(out _);

    public TemplateMatchResult FindTemplate(Bitmap source, Bitmap template, double threshold = 0.85)
    {
        using var sourceMat = BitmapConverter.ToMat(source);
        using var templateMat = BitmapConverter.ToMat(template);
        using var result = new Mat();

        Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLocation);

        var center = new System.Drawing.Point(
            maxLocation.X + template.Width / 2,
            maxLocation.Y + template.Height / 2);

        return new TemplateMatchResult(maxValue >= threshold, maxValue, center.X, center.Y);
    }

    public IReadOnlyList<TemplateMatchResult> FindTemplateMatches(Bitmap source, Bitmap template, double threshold = 0.85)
    {
        using var sourceMat = BitmapConverter.ToMat(source);
        using var templateMat = BitmapConverter.ToMat(template);
        using var result = new Mat();

        Cv2.MatchTemplate(sourceMat, templateMat, result, TemplateMatchModes.CCoeffNormed);

        var matches = new List<TemplateMatchResult>();
        for (var y = 0; y < result.Rows; y++)
        {
            for (var x = 0; x < result.Cols; x++)
            {
                var confidence = result.At<float>(y, x);
                if (confidence < threshold)
                {
                    continue;
                }

                var centerX = x + template.Width / 2;
                var centerY = y + template.Height / 2;
                if (matches.Any(match =>
                        Math.Abs(match.CenterX - centerX) < template.Width / 2
                        && Math.Abs(match.CenterY - centerY) < template.Height / 2))
                {
                    continue;
                }

                matches.Add(new TemplateMatchResult(true, confidence, centerX, centerY));
            }
        }

        return matches
            .OrderBy(match => match.CenterY)
            .ThenBy(match => match.CenterX)
            .ToArray();
    }

    public Task<string> RecognizeTextWithPaddleAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var mat = BitmapConverter.ToMat(bitmap);
            return RecognizeTextFromMat(mat, cancellationToken);
        }, cancellationToken);
    }

    public Task<string> RecognizeTextAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
        => RecognizeTextWithPaddleAsync(bitmap, cancellationToken);

    public async Task<string> RecognizeTextWithWindowsAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var softwareBitmap = await ToSoftwareBitmapAsync(bitmap, cancellationToken);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko-KR"));

        if (engine is null)
        {
            return string.Empty;
        }

        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
    }

    public static void DisposeSharedEngine()
    {
        lock (EngineLock)
        {
            _ocrEngine?.Dispose();
            _ocrEngine = null;
            _engineDisposed = true;
        }
    }

    public Bitmap PreprocessForOcr(Bitmap bitmap)
    {
        using var sourceMat = BitmapConverter.ToMat(bitmap);
        using var gray = ToGray(sourceMat);
        using var threshold = new Mat();

        Cv2.Threshold(gray, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        return BitmapConverter.ToBitmap(threshold);
    }

    public IReadOnlyList<Bitmap> CreateSubstatOcrCandidates(Bitmap bitmap)
    {
        using var sourceMat = BitmapConverter.ToMat(bitmap);
        using var gray = ToGray(sourceMat);
        using var upscaledColor = ResizeScale(sourceMat, 2);
        using var upscaledGray = ResizeScale(gray, 2);

        return
        [
            (Bitmap)bitmap.Clone(),
            BitmapConverter.ToBitmap(upscaledColor),
            BitmapConverter.ToBitmap(upscaledGray),
            BitmapConverter.ToBitmap(gray),
        ];
    }

    public IReadOnlyList<Bitmap> CreateSmallTextOcrCandidates(Bitmap bitmap)
    {
        using var sourceMat = BitmapConverter.ToMat(bitmap);
        using var gray = ToGray(sourceMat);
        using var resizedGray = ResizeAndPad(gray, 5, 28, Scalar.Black);
        using var threshold = new Mat();
        using var inverted = new Mat();

        Cv2.Threshold(gray, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        Cv2.BitwiseNot(threshold, inverted);
        using var resizedThreshold = ResizeAndPad(threshold, 5, 28, Scalar.Black);
        using var resizedInverted = ResizeAndPad(inverted, 5, 28, Scalar.White);

        return
        [
            BitmapConverter.ToBitmap(resizedInverted),
            BitmapConverter.ToBitmap(resizedThreshold),
            BitmapConverter.ToBitmap(resizedGray),
            (Bitmap)bitmap.Clone(),
        ];
    }

    private static string RecognizeTextFromMat(Mat mat, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var encodedMat = EnsureThreeChannelMat(mat);
        var imageBytes = encodedMat.ToBytes(".png");

        OCRResult result;
        lock (EngineLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = GetOrCreateEngine();
            result = engine.DetectText(imageBytes);
        }

        return FormatOcrResult(result);
    }

    private static PaddleOCREngine GetOrCreateEngine()
    {
        if (_engineDisposed)
        {
            throw new ObjectDisposedException(nameof(VisionProcessor), "PaddleOCR 엔진이 이미 해제되었습니다.");
        }

        if (_ocrEngine is not null)
        {
            return _ocrEngine;
        }

        EnsureNativeLibraryPath();

        var config = CreateModelConfig();
        var parameter = new OCRParameter
        {
            cpu_math_library_num_threads = Math.Clamp(Environment.ProcessorCount, 1, 8),
            enable_mkldnn = true,
            det = true,
            rec = true,
            cls = false,
            max_side_len = 1920,
            det_db_thresh = 0.2f,
            det_db_box_thresh = 0.4f,
            rec_img_h = 48,
        };

        _ocrEngine = new PaddleOCREngine(config, parameter);
        return _ocrEngine;
    }

    private static OCRModelConfig CreateModelConfig()
    {
        if (TryCreateKoreanModelConfig(out var koreanConfig))
        {
            return koreanConfig;
        }

        var detPath = Path.Combine(PortableModelDirectory, "det");
        var clsPath = Path.Combine(PortableModelDirectory, "cls");
        var recPath = Path.Combine(PortableModelDirectory, "rec");
        var keysPath = Path.Combine(PortableModelDirectory, "ppocr_keys.txt");

        if (Directory.Exists(detPath)
            && Directory.Exists(recPath)
            && File.Exists(keysPath)
            && File.Exists(Path.Combine(recPath, "inference.pdiparams")))
        {
            var customClsPath = Directory.Exists(clsPath) ? clsPath : detPath;
            return new OCRModelConfig(detPath, customClsPath, recPath, keysPath);
        }

        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "inference");
        var bundledDet = Path.Combine(bundledRoot, "PP-OCRv6_small_det_infer");
        var bundledCls = Path.Combine(bundledRoot, "PP-OCRv5_mobile_cls_infer");
        var bundledRec = Path.Combine(bundledRoot, "PP-OCRv6_small_rec_infer");
        var bundledKeys = Path.Combine(bundledRoot, "ppocr_keys.txt");

        if (Directory.Exists(bundledDet)
            && Directory.Exists(bundledRec)
            && File.Exists(bundledKeys)
            && File.Exists(Path.Combine(bundledDet, "inference.pdiparams"))
            && File.Exists(Path.Combine(bundledRec, "inference.pdiparams")))
        {
            var bundledClsPath = Directory.Exists(bundledCls) ? bundledCls : bundledDet;
            return new OCRModelConfig(bundledDet, bundledClsPath, bundledRec, bundledKeys);
        }

        return OCRModelConfig.Default;
    }

    private static bool TryCreateKoreanModelConfig(out OCRModelConfig config)
    {
        config = null!;

        var modelRoot = PortableModelDirectory;
        var dictPath = ResolveKoreanDictionaryPath(modelRoot);
        var recPath = ResolveKoreanRecModelPath(modelRoot);
        if (dictPath is null || recPath is null)
        {
            return false;
        }

        var detPath = ResolveDetModelPath(modelRoot);
        if (detPath is null)
        {
            return false;
        }

        var clsPath = ResolveClsModelPath(modelRoot, detPath);
        config = new OCRModelConfig(detPath, clsPath, recPath, dictPath);
        return true;
    }

    private static string? ResolveKoreanDictionaryPath(string modelRoot)
    {
        var koreanDictPath = Path.Combine(modelRoot, "korean_dict.txt");
        if (File.Exists(koreanDictPath))
        {
            return koreanDictPath;
        }

        var keysPath = Path.Combine(modelRoot, "ppocr_keys.txt");
        if (File.Exists(keysPath) && ContainsHangulDictionary(keysPath))
        {
            return keysPath;
        }

        return null;
    }

    private static bool ContainsHangulDictionary(string dictPath)
    {
        foreach (var line in File.ReadLines(dictPath).Take(2000))
        {
            if (line.Any(ch => ch is >= '\uAC00' and <= '\uD7A3'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsHangul(string? line)
    {
        return !string.IsNullOrEmpty(line) && line.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');
    }

    private static string? ResolveKoreanRecModelPath(string modelRoot)
    {
        foreach (var directoryName in new[]
        {
            "korean_PP-OCRv5_mobile_rec_infer",
            "korean_PP-OCRv4_rec_infer",
            "korean_PP-OCRv3_mobile_rec_infer",
            "rec",
        })
        {
            var path = Path.Combine(modelRoot, directoryName);
            if (IsValidInferenceModelDirectory(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? ResolveDetModelPath(string modelRoot)
    {
        foreach (var directoryName in new[]
        {
            "PP-OCRv5_mobile_det_infer",
            "PP-OCRv6_small_det_infer",
            "det",
        })
        {
            var path = Path.Combine(modelRoot, directoryName);
            if (IsValidInferenceModelDirectory(path))
            {
                return path;
            }
        }

        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "inference");
        foreach (var directoryName in new[] { "PP-OCRv5_mobile_det_infer", "PP-OCRv6_small_det_infer" })
        {
            var path = Path.Combine(bundledRoot, directoryName);
            if (IsValidInferenceModelDirectory(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string ResolveClsModelPath(string modelRoot, string detPath)
    {
        foreach (var directoryName in new[] { "PP-OCRv5_mobile_cls_infer", "cls" })
        {
            var path = Path.Combine(modelRoot, directoryName);
            if (IsValidInferenceModelDirectory(path))
            {
                return path;
            }
        }

        var bundledCls = Path.Combine(AppContext.BaseDirectory, "inference", "PP-OCRv5_mobile_cls_infer");
        if (IsValidInferenceModelDirectory(bundledCls))
        {
            return bundledCls;
        }

        return detPath;
    }

    private static bool IsValidInferenceModelDirectory(string path)
    {
        return Directory.Exists(path) && File.Exists(Path.Combine(path, "inference.pdiparams"));
    }

    private static void EnsureNativeLibraryPath()
    {
        var baseDirectory = AppContext.BaseDirectory;

        var requiredNativeLibraries = new[]
        {
            "PaddleOCR.dll",
            "paddle_inference.dll",
            "opencv_world470.dll",
            "mkldnn.dll",
        };

        var missing = requiredNativeLibraries
            .Where(name => !File.Exists(Path.Combine(baseDirectory, name)))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new FileNotFoundException(
                $"PaddleOCR 네이티브 실행 파일이 누락되었습니다: {string.Join(", ", missing)}. `dotnet restore` 후 다시 빌드해 주세요.",
                Path.Combine(baseDirectory, missing[0]));
        }

        var bundledDetModel = Path.Combine(baseDirectory, "inference", "PP-OCRv6_small_det_infer", "inference.pdiparams");
        if (!File.Exists(bundledDetModel))
        {
            throw new FileNotFoundException(
                $"PaddleOCR 추론 모델이 누락되었습니다: {bundledDetModel}. `dotnet restore` 후 다시 빌드해 주세요.",
                bundledDetModel);
        }
    }

    private static Mat EnsureThreeChannelMat(Mat source)
    {
        return source.Channels() switch
        {
            1 => ConvertGrayToBgr(source),
            3 => source.Clone(),
            4 => ConvertBgraToBgr(source),
            _ => source.Clone(),
        };
    }

    private static Mat ConvertGrayToBgr(Mat source)
    {
        var bgr = new Mat();
        Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);
        return bgr;
    }

    private static Mat ConvertBgraToBgr(Mat source)
    {
        var bgr = new Mat();
        Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }

    private static string FormatOcrResult(OCRResult? result)
    {
        if (result is null)
        {
            return string.Empty;
        }

        if (result.TextBlocks is { Count: > 0 })
        {
            var orderedBlocks = result.TextBlocks
                .OrderBy(block => block.BoxPoints?.FirstOrDefault()?.Y ?? 0)
                .ThenBy(block => block.BoxPoints?.FirstOrDefault()?.X ?? 0)
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text));

            return NormalizeOcrText(string.Join(Environment.NewLine, orderedBlocks));
        }

        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            return NormalizeOcrText(result.Text);
        }

        return string.Empty;
    }

    private static string NormalizeOcrText(string text)
    {
        return text
            .Replace("％", "%", StringComparison.Ordinal)
            .Replace("﹪", "%", StringComparison.Ordinal)
            .Replace("．", ".", StringComparison.Ordinal)
            .Replace("＋", "+", StringComparison.Ordinal);
    }

    private static Mat ResizeScale(Mat source, double scale)
    {
        var resized = new Mat();
        Cv2.Resize(
            source,
            resized,
            new OpenCvSharp.Size(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale))),
            0,
            0,
            InterpolationFlags.Cubic);
        return resized;
    }

    private static Mat ToGray(Mat source)
    {
        var gray = new Mat();
        if (source.Channels() == 4)
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
            return gray;
        }

        if (source.Channels() == 3)
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

        return source.Clone();
    }

    private static Mat ResizeAndPad(Mat source, int scale, int padding, Scalar borderColor)
    {
        using var resized = new Mat();
        var padded = new Mat();
        Cv2.Resize(
            source,
            resized,
            new OpenCvSharp.Size(Math.Max(1, source.Width * scale), Math.Max(1, source.Height * scale)),
            0,
            0,
            InterpolationFlags.Cubic);
        Cv2.CopyMakeBorder(resized, padded, padding, padding, padding, padding, BorderTypes.Constant, borderColor);
        return padded;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap, CancellationToken cancellationToken)
    {
        await using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        var bytes = memoryStream.ToArray();

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(randomAccessStream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
            writer.DetachStream();
        }

        randomAccessStream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream).AsTask(cancellationToken);
        using var decoded = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(cancellationToken);

        return SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Gray8);
    }
}

public sealed record TemplateMatchResult(bool Success, double Confidence, int CenterX, int CenterY);
