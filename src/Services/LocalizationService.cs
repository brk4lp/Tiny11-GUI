using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace tiny11_ui.Services
{
    public class LocalizationService
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private Dictionary<string, string> _strings = new Dictionary<string, string>();
        private string _currentLanguage = "tr-TR"; // Varsayılan dil

        public event EventHandler<string>? LanguageChanged;

            public string CurrentLanguage { get; private set; } = "en-US";

        public LocalizationService()
        {
            LoadLanguage(_currentLanguage);
        }

        public void LoadLanguage(string languageCode)
        {
            try
            {
                _currentLanguage = languageCode;
                var resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", $"Strings.{languageCode}.txt");
                
                if (!File.Exists(resourcePath))
                {
                    // Fallback to Turkish if file doesn't exist
                    resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Strings.tr-TR.txt");
                }

                _strings.Clear();
                
                if (File.Exists(resourcePath))
                {
                    var lines = File.ReadAllLines(resourcePath);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                            continue;

                        var parts = line.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            _strings[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }

                LanguageChanged?.Invoke(this, _currentLanguage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Language loading error: {ex.Message}");
            }
        }

        public string GetString(string key)
        {
            return _strings.TryGetValue(key, out var value) ? value : $"[{key}]";
        }

        public string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        public List<LanguageInfo> GetAvailableLanguages()
        {
            return new List<LanguageInfo>
            {
                new LanguageInfo("tr-TR", "Türkçe"),
                new LanguageInfo("en-US", "English")
            };
        }
    }

    public class LanguageInfo
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }

        public LanguageInfo(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }
}
