using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.IO;
using System.Text.Json;
using UsbI2cController.ViewModels;

namespace UsbI2cController;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string SettingsFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsbI2cController",
        "window_settings.json");

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        
        LoadWindowSettings();
        
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // ウィンドウが画面外に出ていないかチェック
        EnsureWindowIsVisible();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowSettings();
    }

    private void LoadWindowSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return;
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<WindowSettings>(json);

            if (settings != null)
            {
                if (settings.Width > 0 && settings.Height > 0)
                {
                    Width = settings.Width;
                    Height = settings.Height;
                }

                if (settings.Left.HasValue && settings.Top.HasValue)
                {
                    Left = settings.Left.Value;
                    Top = settings.Top.Value;
                }

                if (settings.WindowState.HasValue)
                {
                    WindowState = settings.WindowState.Value;
                }
            }
        }
        catch
        {
            // 読み込み失敗時はデフォルト設定を使用
        }
    }

    private void SaveWindowSettings()
    {
        try
        {
            var settings = new WindowSettings
            {
                Width = RestoreBounds.Width,
                Height = RestoreBounds.Height,
                Left = RestoreBounds.Left,
                Top = RestoreBounds.Top,
                WindowState = WindowState
            };

            var directory = System.IO.Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // 保存失敗してもアプリは続行
        }
    }

    private void EnsureWindowIsVisible()
    {
        var screenWidth = SystemParameters.VirtualScreenWidth;
        var screenHeight = SystemParameters.VirtualScreenHeight;
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;

        // ウィンドウが完全に画面外にある場合は中央に配置
        if (Left + Width < screenLeft || Left > screenLeft + screenWidth ||
            Top + Height < screenTop || Top > screenTop + screenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private class WindowSettings
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public WindowState? WindowState { get; set; }
    }
}