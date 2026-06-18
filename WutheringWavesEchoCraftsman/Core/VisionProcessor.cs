using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using PaddleOCRSharp;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class VisionProcessor
{
    private static readonly object EngineLock = new();
    private static PaddleOCREngine? _ocrEngine;
    private static bool _engineDisposed;

    private static readonly string PortableModelDirectory =
        Path.Combine(AppContext.BaseDirectory, "data", "models");

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

    public Task<string> RecognizeTextAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var mat = BitmapConverter.ToMat(bitmap);
            return RecognizeTextFromMat(mat, cancellationToken);
        }, cancellationToken);
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
        };

        _ocrEngine = new PaddleOCREngine(config, parameter);
        return _ocrEngine;
    }

    private static OCRModelConfig CreateModelConfig()
    {
        var detPath = Path.Combine(PortableModelDirectory, "det");
        var clsPath = Path.Combine(PortableModelDirectory, "cls");
        var recPath = Path.Combine(PortableModelDirectory, "rec");
        var keysPath = Path.Combine(PortableModelDirectory, "ppocr_keys.txt");

        if (Directory.Exists(detPath)
            && Directory.Exists(recPath)
            && File.Exists(keysPath))
        {
            return new OCRModelConfig(detPath, clsPath, recPath, keysPath);
        }

        return OCRModelConfig.Default;
    }

    private static void EnsureNativeLibraryPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var paddleOcrDllPath = Path.Combine(baseDirectory, "PaddleOCR.dll");
        if (File.Exists(paddleOcrDllPath))
        {
            EngineBase.PaddleOCRdllPath = paddleOcrDllPath;
        }

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

        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            return NormalizeOcrText(result.Text);
        }

        if (result.TextBlocks is null || result.TextBlocks.Count == 0)
        {
            return string.Empty;
        }

        var orderedBlocks = result.TextBlocks
            .OrderBy(block => block.BoxPoints?.FirstOrDefault()?.Y ?? 0)
            .ThenBy(block => block.BoxPoints?.FirstOrDefault()?.X ?? 0)
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return NormalizeOcrText(string.Join(Environment.NewLine, orderedBlocks));
    }

    private static string NormalizeOcrText(string text)
    {
        var normalized = text
            .Replace("％", "%", StringComparison.Ordinal)
            .Replace("﹪", "%", StringComparison.Ordinal)
            .Replace("．", ".", StringComparison.Ordinal)
            .Replace("＋", "+", StringComparison.Ordinal);

        var lines = normalized
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeOcrLine)
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeOcrLine(string line)
    {
        return Regex.Replace(line, @"[^\p{L}\p{N}%+\-.]+", string.Empty);
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
}

public sealed record TemplateMatchResult(bool Success, double Confidence, int CenterX, int CenterY);
