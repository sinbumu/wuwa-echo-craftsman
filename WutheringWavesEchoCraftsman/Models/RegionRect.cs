using System.Drawing;

namespace WutheringWavesEchoCraftsman.Models;

public sealed record RegionRect(int X, int Y, int Width, int Height)
{
    public static RegionRect Empty { get; } = new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public Rectangle ToRectangle() => new(X, Y, Width, Height);
}
