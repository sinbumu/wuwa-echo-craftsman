using System.Drawing;
using System.Windows;
using WutheringWavesEchoCraftsman.Views;
using Forms = System.Windows.Forms;

namespace WutheringWavesEchoCraftsman;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        ConfigureTrayIcon(mainWindow);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }

    private void ConfigureTrayIcon(Window mainWindow)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("설정 열기", null, (_, _) =>
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            });
        });
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(Shutdown));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Wuwa Echo Craftsman",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) =>
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            });
        };
    }
}

