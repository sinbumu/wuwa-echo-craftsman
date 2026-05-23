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
        using var gray = new Mat();
        using var threshold = new Mat();

        Cv2.CvtColor(sourceMat, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(gray, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        return BitmapConverter.ToBitmap(threshold);
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
