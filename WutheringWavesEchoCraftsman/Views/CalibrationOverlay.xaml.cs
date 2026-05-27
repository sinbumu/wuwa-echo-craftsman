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

            new("roi_expected_level", "강화 후 예상 레벨 텍스트", CalibrationStepKind.Region, CalibrationScreen.Enhance, "재료 투입 후 도달할 예상 레벨(+5, +10 등) 텍스트 영역을 드래그하세요."),
            new("roi_slot_plus", "재료 투입 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 재료 슬롯 + 버튼 또는 재료 투입 영역을 드래그하세요. 자동화는 영역 중앙을 클릭해 재료 리스트를 엽니다."),
            new("roi_enhance_confirm", "강화 확인 버튼 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 실행/확인 버튼 영역을 드래그하세요. 자동화는 영역 중앙을 클릭합니다."),
            new("roi_discard_material_confirm", "폐기 에코 재료 사용 확인 수락 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "폐기 에코를 강화 재료로 사용할 때 뜨는 확인창의 수락/확인 버튼 영역을 드래그하세요."),
            new("roi_enhance_complete_close", "강화 완료 오버레이 닫기 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 완료 후 뜨는 결과 오버레이에서, 아무 곳이나 클릭해 닫을 수 있는 안전한 영역을 드래그하세요."),
            new("roi_optimize_tab", "튜닝 탭 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.Enhance, "강화 화면에서 옵티마이즈/튜닝 화면으로 이동하는 탭 버튼 영역을 드래그하세요."),

            new("roi_material", "재료 인벤토리", CalibrationStepKind.Region, CalibrationScreen.MaterialList, "재료 슬롯 + 버튼을 눌러 열린 우측 재료 리스트 영역 전체를 드래그하세요."),
            new("icon_discard.png", "폐기 휴지통 아이콘", CalibrationStepKind.Asset, CalibrationScreen.MaterialList, "우측 재료 리스트에서 폐기 에코/휴지통 아이콘만 작게 드래그하세요."),
            new("roi_exp_material_1", "음파통 1 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.MaterialList, "우측 재료 리스트의 첫 번째로 사용할 음파통 영역을 드래그하세요."),
            new("roi_exp_material_2", "음파통 2 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.MaterialList, "필요하면 두 번째 음파통 영역을 드래그하세요. 사용하지 않으면 작게 임의 지정 후 나중에 수정할 수 있습니다."),
            new("roi_exp_material_3", "음파통 3 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.MaterialList, "필요하면 세 번째 음파통 영역을 드래그하세요."),
            new("roi_exp_material_4", "음파통 4 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.MaterialList, "필요하면 네 번째 음파통 영역을 드래그하세요."),

            new("roi_substat", "부옵션 텍스트", CalibrationStepKind.Region, CalibrationScreen.Optimize, "옵티마이즈 결과로 표시되는 부옵션 텍스트 목록 영역을 드래그하세요."),
            new("roi_optimize_count", "옵티마이즈 횟수 텍스트", CalibrationStepKind.Region, CalibrationScreen.Optimize, "옵티마이즈 횟수: n 형태의 현재 시행 횟수 텍스트 영역을 드래그하세요."),
            new("roi_optimize_minus", "옵티마이즈 - 버튼 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.Optimize, "옵티마이즈 시행 횟수를 줄이는 - 버튼 영역을 드래그하세요."),
            new("roi_optimize_plus", "옵티마이즈 + 버튼 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.Optimize, "옵티마이즈 시행 횟수를 늘리는 + 버튼 영역을 드래그하세요."),
            new("roi_optimize_confirm", "옵티마이즈 실행 버튼 클릭 영역", CalibrationStepKind.Region, CalibrationScreen.Optimize, "옵티마이즈 실행/해금 버튼 영역을 드래그하세요. 자동화는 영역 중앙을 클릭합니다."),
            new("roi_optimize_complete_close", "옵티마이즈 완료 오버레이 닫기 영역", CalibrationStepKind.Region, CalibrationScreen.Optimize, "옵티마이즈 완료 후 뜨는 결과 오버레이에서, 아무 곳이나 클릭해 닫을 수 있는 안전한 영역을 드래그하세요."),
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
    MaterialList,
    Optimize,
}

public static class CalibrationGuide
{
    public static string GetScreenTitle(CalibrationScreen screen)
    {
        return screen switch
        {
            CalibrationScreen.EchoList => "에코 목록 화면",
            CalibrationScreen.Enhance => "에코 강화 기본 화면",
            CalibrationScreen.MaterialList => "에코 강화 재료 리스트 화면",
            CalibrationScreen.Optimize => "에코 옵티마이즈 화면",
            _ => "캘리브레이션 화면",
        };
    }

    public static string GetPreparationMessage(CalibrationScreen screen)
    {
        return screen switch
        {
            CalibrationScreen.EchoList =>
                "1/4 에코 목록 화면을 준비하세요." + Environment.NewLine
                + "- 캐릭터 > 에코 탭의 에코 목록 화면으로 이동하세요." + Environment.NewLine
                + "- 목표 세트/코스트 필터와 레벨 오름차순 정렬을 적용하세요." + Environment.NewLine
                + "- +0 에코가 보이고, 에코 선택 시 육성 버튼이 보이는 상태가 좋습니다.",

            CalibrationScreen.Enhance =>
                "2/4 에코 강화 기본 화면을 준비하세요." + Environment.NewLine
                + "- 목록에서 +0 에코를 선택하고 육성 버튼을 눌러 강화 화면으로 이동하세요." + Environment.NewLine
                + "- 재료 투입 후 변하는 예상 레벨 텍스트 영역이 보이게 하세요." + Environment.NewLine
                + "- 아직 재료 슬롯 + 버튼은 누르지 마세요." + Environment.NewLine
                + "- 현재 화면에서 재료 투입 영역, 강화 버튼, 폐기 에코 재료 사용 확인창의 수락 지점, 강화 완료 오버레이 닫기용 안전 클릭 지점, 옵티마이즈/튜닝 탭 버튼을 지정할 수 있게 준비하세요.",

            CalibrationScreen.MaterialList =>
                "3/4 에코 강화 재료 리스트 화면을 준비하세요." + Environment.NewLine
                + "- 에코 강화 화면에서 재료 슬롯 + 버튼 또는 재료 투입 영역을 클릭하세요." + Environment.NewLine
                + "- 우측에 강화 재료/강화된 에코 목록이 열린 상태로 준비하세요." + Environment.NewLine
                + "- 폐기 에코 아이콘과 사용할 음파통 1~4종 영역이 실제로 보이게 스크롤/필터를 맞춰두세요.",

            CalibrationScreen.Optimize =>
                "4/4 에코 옵티마이즈 화면을 준비하세요." + Environment.NewLine
                + "- 강화 화면에서 옵티마이즈/튜닝 탭으로 이동하세요." + Environment.NewLine
                + "- 부옵션 텍스트 영역, 옵티마이즈 횟수 텍스트, +/- 버튼, 실행/해금 버튼, 완료 오버레이 닫기용 안전 클릭 지점이 보이는 상태로 준비하세요.",

            _ => "캘리브레이션할 화면을 준비하세요.",
        };
    }
}
