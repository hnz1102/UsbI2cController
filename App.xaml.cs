using System.Configuration;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows;
using Application = System.Windows.Application;

namespace UsbI2cController;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // 保存された言語設定を読み込んで適用
        LoadAndApplyLanguageSettings();
    }

    private void LoadAndApplyLanguageSettings()
    {
        try
        {
            string languageSettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UsbI2cController",
                "language_settings.json");

            string languageCode = "ja"; // デフォルト

            if (File.Exists(languageSettingsPath))
            {
                string json = File.ReadAllText(languageSettingsPath);
                var settings = JsonSerializer.Deserialize<LanguageSettings>(json);
                if (settings != null && !string.IsNullOrEmpty(settings.Language))
                {
                    languageCode = settings.Language;
                }
            }

            // 言語リソースを読み込み
            var dict = new ResourceDictionary();
            dict.Source = new Uri($"pack://application:,,,/Resources/Strings.{languageCode}.xaml", UriKind.Absolute);
            Current.Resources.MergedDictionaries.Add(dict);
        }
        catch
        {
            // エラー時はデフォルト（日本語）を読み込み
            var dict = new ResourceDictionary();
            dict.Source = new Uri("pack://application:,,,/Resources/Strings.ja.xaml", UriKind.Absolute);
            Current.Resources.MergedDictionaries.Add(dict);
        }
    }

    private class LanguageSettings
    {
        public string Language { get; set; } = "ja";
    }
}

