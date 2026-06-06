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
            new("roi_enhance_tab", "육성 버튼 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.EchoList, "에코를 선택했을 때 보이는 육성 버튼 영역을 드래그하세요. 자동화는 영역 중앙을 클릭합니다."),

            new("roi_staged_auto_input", "단계별 투입 버튼", CalibrationStepKind.Region, CalibrationScreen.Enhance, "인게임 자동 투입 설정을 단계별 투입 + 옵티마이즈 동기화 켜기 + 강화 재료 및 에코로 맞춘 뒤, 강화 화면의 단계별 투입 버튼 영역을 드래그하세요."),
            new("roi_echo_material_input", "에코 재료 투입 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 화면에서 에코 재료 선택 목록을 여는 재료 투입 슬롯/버튼 영역을 드래그하세요."),
            new("roi_enhance_confirm", "강화 확인 버튼 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 실행/확인 버튼 영역을 드래그하세요. 자동화는 영역 중앙을 클릭합니다."),
            new("roi_enhance_complete_close", "강화 완료 오버레이 닫기 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 완료 후 뜨는 결과 오버레이에서, 아무 곳이나 클릭해 닫을 수 있는 안전한 영역을 드래그하세요."),
            new("roi_current_level", "현재 에코 레벨 텍스트", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 화면에서 현재 에코 레벨(+0, +5 등)이 표시되는 작은 텍스트 영역을 드래그하세요."),
            new("roi_substat", "부옵션 텍스트", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 화면에서 새 부옵션이 표시되는 텍스트 목록 영역을 드래그하세요."),

            new("roi_echo_material_list", "에코 재료 목록 영역", CalibrationStepKind.Region, CalibrationScreen.EnhanceMaterialList, "에코 재료 목록이 열린 상태에서, 폐기 에코 아이콘을 수색할 우측 재료 목록 영역 전체를 드래그하세요."),
            new("template_discard_echo.png", "폐기 에코 아이콘", CalibrationStepKind.Asset, CalibrationScreen.EnhanceMaterialList, "에코 재료 목록 안의 폐기 표시 아이콘만 작게 드래그하세요."),
        ];
    }

    public string ScreenTitle => Screen switch
    {
        _ => CalibrationGuide.GetScreenTitle(Screen),
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
    EnhanceMaterialList,
}

public static class CalibrationGuide
{
    public static string GetScreenTitle(CalibrationScreen screen)
    {
        return screen switch
        {
            CalibrationScreen.EchoList => "에코 목록 화면",
            CalibrationScreen.Enhance => "에코 강화 화면",
            CalibrationScreen.EnhanceMaterialList => "에코 재료 목록 화면",
            _ => "캘리브레이션 화면",
        };
    }

    public static string GetPreparationMessage(CalibrationScreen screen)
    {
        return screen switch
        {
            CalibrationScreen.EchoList =>
                "1/3 에코 목록 화면을 준비하세요." + Environment.NewLine
                + "- 캐릭터 > 에코 탭의 에코 목록 화면으로 이동하세요." + Environment.NewLine
                + "- 목표 세트/코스트 필터와 레벨 오름차순 정렬을 적용하세요." + Environment.NewLine
                + "- +0 에코가 보이고, 에코 선택 시 육성 버튼이 보이는 상태가 좋습니다.",

            CalibrationScreen.Enhance =>
                "2/3 에코 강화 화면을 준비하세요." + Environment.NewLine
                + "- 목록에서 +0 에코를 선택하고 육성 버튼을 눌러 강화 화면으로 이동하세요." + Environment.NewLine
                + "- 인게임 자동 투입 설정을 단계별 투입 + 옵티마이즈 동기화 켜기 + 강화 재료 및 에코로 맞춰두세요." + Environment.NewLine
                + "- 단계별 투입 버튼, 에코 재료 투입 영역, 강화 버튼, 완료 오버레이 닫기 영역, 현재 레벨 텍스트, 부옵션 텍스트 영역이 보이게 준비하세요.",

            CalibrationScreen.EnhanceMaterialList =>
                "3/3 에코 재료 목록 화면을 준비하세요." + Environment.NewLine
                + "- 에코 강화 화면에서 에코 재료 투입 영역을 눌러 우측 재료 목록을 열어두세요." + Environment.NewLine
                + "- 폐기 에코 아이콘이 보이는 상태로 준비하세요.",

            _ => "캘리브레이션할 화면을 준비하세요.",
        };
    }
}
