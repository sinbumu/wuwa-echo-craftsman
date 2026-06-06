using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WutheringWavesEchoCraftsman.Models;

namespace WutheringWavesEchoCraftsman.Views;

public partial class AutomationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly ObservableCollection<OverlaySubstatRow> _substats = [];
    private readonly ObservableCollection<string> _history = [];

    public AutomationOverlayWindow()
    {
        InitializeComponent();
        SubstatItemsControl.ItemsSource = _substats;
        HistoryItemsControl.ItemsSource = _history;
        Loaded += (_, _) => PositionOnRightMiddle();
    }

    public void UpdateSubstats(IReadOnlyList<ParsedSubstat> substats)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _substats.Clear();
            foreach (var substat in substats.Take(5))
            {
                _substats.Add(new OverlaySubstatRow(
                    substat.DisplayName,
                    substat.Value.ToString("0.##", CultureInfo.InvariantCulture)));
            }

            EmptySubstatTextBlock.Visibility = _substats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    public void AddHistory(string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _history.Insert(0, text);
            while (_history.Count > 5)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            EmptyHistoryTextBlock.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void PositionOnRightMiddle()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 48;
        Top = workArea.Top + workArea.Height * 0.35;
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, exStyle | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

public sealed record OverlaySubstatRow(string DisplayName, string ValueText);
