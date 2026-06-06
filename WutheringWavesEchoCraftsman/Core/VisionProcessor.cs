using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class VisionProcessor
{
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

    public async Task<string> RecognizeTextAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
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
        var resized = new Mat();
        var padded = new Mat();
        Cv2.Resize(
            source,
            resized,
            new OpenCvSharp.Size(Math.Max(1, source.Width * scale), Math.Max(1, source.Height * scale)),
            0,
            0,
            InterpolationFlags.Cubic);
        Cv2.CopyMakeBorder(resized, padded, padding, padding, padding, padding, BorderTypes.Constant, borderColor);
        resized.Dispose();
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
