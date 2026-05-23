using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WutheringWavesEchoCraftsman.Models;
using Forms = System.Windows.Forms;

namespace WutheringWavesEchoCraftsman.Core;

public sealed class ScreenCapturer
{
    public Bitmap CaptureVirtualScreen()
    {
        var bounds = Forms.SystemInformation.VirtualScreen;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

        return bitmap;
    }

    public Bitmap CaptureRegion(RegionRect region)
    {
        if (region.IsEmpty)
        {
            throw new ArgumentException("Capture region must have positive width and height.", nameof(region));
        }

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.X, region.Y, 0, 0, new Size(region.Width, region.Height), CopyPixelOperation.SourceCopy);

        return bitmap;
    }

    public void SavePng(Bitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        bitmap.Save(path, ImageFormat.Png);
    }

    public double GetPrimaryDpiScale()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return graphics.DpiX / 96.0;
    }
}
