using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WutheringWavesEchoCraftsman.Models;
using Point = System.Windows.Point;

namespace WutheringWavesEchoCraftsman.Views;

public partial class CalibrationOverlay : Window
{
    private readonly Bitmap _screenshot;
    private readonly CalibrationStep _step;
    private readonly TaskCompletionSource<CalibrationResult?> _completionSource = new();
    private Point? _startPoint;

    private CalibrationOverlay(Bitmap screenshot, CalibrationStep step)
    {
        InitializeComponent();
        _screenshot = screenshot;
        _step = step;
        ScreenshotImage.Source = ToBitmapImage(screenshot);
        InstructionTextBlock.Text = $"{step.ScreenTitle} - {step.Title}\n{step.Detail}\nESC: 취소";
    }

    public static Task<CalibrationResult?> CaptureAsync(Bitmap screenshot, CalibrationStep step)
    {
        var overlay = new CalibrationOverlay(screenshot, step);
        overlay.Show();
        return overlay._completionSource.Task;
    }

    private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(SelectionCanvas);
        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, _startPoint.Value.X);
        Canvas.SetTop(SelectionBorder, _startPoint.Value.Y);
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;
        SelectionCanvas.CaptureMouse();
    }

    private void SelectionCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_startPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(SelectionCanvas);
        var x = Math.Min(current.X, _startPoint.Value.X);
        var y = Math.Min(current.Y, _startPoint.Value.Y);
        var width = Math.Abs(current.X - _startPoint.Value.X);
        var height = Math.Abs(current.Y - _startPoint.Value.Y);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_startPoint is null)
        {
            return;
        }

        SelectionCanvas.ReleaseMouseCapture();
        var current = e.GetPosition(SelectionCanvas);
        var region = ToBitmapRegion(_startPoint.Value, current);
        _completionSource.TrySetResult(new CalibrationResult(_step, region));
        Close();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _completionSource.TrySetResult(null);
            Close();
        }
    }

    private RegionRect ToBitmapRegion(Point start, Point end)
    {
        var scaleX = _screenshot.Width / Math.Max(1, ScreenshotImage.ActualWidth);
        var scaleY = _screenshot.Height / Math.Max(1, ScreenshotImage.ActualHeight);
        var x = Math.Min(start.X, end.X) * scaleX;
        var y = Math.Min(start.Y, end.Y) * scaleY;
        var width = Math.Abs(end.X - start.X) * scaleX;
        var height = Math.Abs(end.Y - start.Y) * scaleY;

        return new RegionRect(
            Math.Max(0, (int)Math.Round(x)),
            Math.Max(0, (int)Math.Round(y)),
            Math.Max(1, (int)Math.Round(width)),
            Math.Max(1, (int)Math.Round(height)));
    }

    private static BitmapImage ToBitmapImage(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public sealed record CalibrationResult(CalibrationStep Step, RegionRect Region);

public sealed record CalibrationStep(
    string Key,
    string Title,
    CalibrationStepKind Kind,
    CalibrationScreen Screen,
    string Detail)
{
    public static IReadOnlyList<CalibrationStep> CreateDefaultSteps()
    {
        return
        [
            new("roi_list", "에코 목록", CalibrationStepKind.Region, CalibrationScreen.EchoList, "좌측 에코 썸네일 그리드 전체를 드래그하세요."),
            new("template_plus_zero.png", "+0 표시", CalibrationStepKind.Asset, CalibrationScreen.EchoList, "미강화 에코 썸네일의 +0 표시만 작게 드래그하세요."),
            new("btn_enhance_tab.png", "육성 버튼", CalibrationStepKind.Asset, CalibrationScreen.EchoList, "에코를 선택했을 때 보이는 육성 버튼 이미지만 드래그하세요."),

            new("roi_level", "현재 레벨 텍스트", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 화면의 현재 레벨(+0, +5 등) 텍스트 영역을 드래그하세요."),
            new("btn_slot_plus.png", "재료 슬롯 + 버튼", CalibrationStepKind.Asset, CalibrationScreen.Enhance, "강화 재료 슬롯의 + 버튼 이미지만 드래그하세요."),
            new("btn_enhance_confirm.png", "강화 확인 버튼", CalibrationStepKind.Asset, CalibrationScreen.Enhance, "강화 실행/확인 버튼 이미지만 드래그하세요."),
            new("btn_optimize_tab.png", "튜닝 탭", CalibrationStepKind.Asset, CalibrationScreen.Enhance, "강화 화면에서 옵티마이즈/튜닝 화면으로 이동하는 탭 버튼을 드래그하세요."),

            new("roi_material", "재료 인벤토리", CalibrationStepKind.Region, CalibrationScreen.MaterialList, "재료 슬롯 + 버튼을 눌러 열린 우측 재료 리스트 영역 전체를 드래그하세요."),
            new("icon_discard.png", "폐기 휴지통 아이콘", CalibrationStepKind.Asset, CalibrationScreen.MaterialList, "우측 재료 리스트에서 폐기 에코/휴지통 아이콘만 작게 드래그하세요."),
            new("icon_exp.png", "음파통 아이콘", CalibrationStepKind.Asset, CalibrationScreen.MaterialList, "우측 재료 리스트에서 음파통 아이콘만 작게 드래그하세요."),

            new("roi_substat", "부옵션 텍스트", CalibrationStepKind.Region, CalibrationScreen.Optimize, "옵티마이즈 결과로 표시되는 부옵션 텍스트 목록 영역을 드래그하세요."),
            new("btn_optimize_confirm.png", "옵티마이즈 실행 버튼", CalibrationStepKind.Asset, CalibrationScreen.Optimize, "옵티마이즈 실행/해금 버튼 이미지만 드래그하세요."),
        ];
    }

    public string ScreenTitle => Screen switch
    {
        CalibrationScreen.EchoList => "에코 목록 화면",
        CalibrationScreen.Enhance => "에코 강화 화면",
        CalibrationScreen.MaterialList => "에코 강화 재료 리스트 화면",
        CalibrationScreen.Optimize => "에코 옵티마이즈 화면",
        _ => "캘리브레이션",
    };
}

public enum CalibrationStepKind
{
    Region,
    Asset,
}

public enum CalibrationScreen
{
    EchoList,
    Enhance,
    MaterialList,
    Optimize,
}
