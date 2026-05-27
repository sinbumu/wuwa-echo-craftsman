using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using WutheringWavesEchoCraftsman.Views;
using Forms = System.Windows.Forms;

namespace WutheringWavesEchoCraftsman;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string AppIconResourcePath = "asset/app_icon.png";
    private const string SplashResourcePath = "asset/splash_image.png";

    private Forms.NotifyIcon? _notifyIcon;
    private Icon? _trayIcon;

    public static bool IsExplicitShutdownRequested { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var splashStartedAt = DateTimeOffset.UtcNow;
        var splashScreen = new SplashScreen(SplashResourcePath);
        splashScreen.Show(false);

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        ConfigureTrayIcon(mainWindow);

        var remainingSplashTime = TimeSpan.FromSeconds(1) - (DateTimeOffset.UtcNow - splashStartedAt);
        if (remainingSplashTime > TimeSpan.Zero)
        {
            await Task.Delay(remainingSplashTime);
        }

        mainWindow.Show();
        splashScreen.Close(TimeSpan.FromMilliseconds(200));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _trayIcon?.Dispose();
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
        menu.Items.Add("종료", null, (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                IsExplicitShutdownRequested = true;
                Shutdown();
            });
        });

        _trayIcon = CreateTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon ?? SystemIcons.Application,
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

    private static Icon? CreateTrayIcon()
    {
        var resource = GetResourceStream(new Uri($"pack://application:,,,/{AppIconResourcePath}", UriKind.Absolute));
        if (resource is null)
        {
            return null;
        }

        using var source = new Bitmap(resource.Stream);
        using var resized = new Bitmap(source, new System.Drawing.Size(32, 32));
        var iconHandle = resized.GetHicon();

        try
        {
            return (Icon)Icon.FromHandle(iconHandle).Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

