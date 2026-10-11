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

        private static readonly string[] SupportedLanguages =
        {
            "tr-TR", "en-US", "ru-RU", "ja-JP", "de-DE", "fr-FR", "es-ES", "zh-CN"
        };

        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        private string _currentLanguage = "en-US";

        public event EventHandler<string>? LanguageChanged;

        public string CurrentLanguage { get; private set; } = "en-US";

        public LocalizationService()
        {
            LoadLanguage(DetectDefaultLanguage());
        }

        /// <summary>
        /// İşletim sisteminin UI diline göre başlangıç dilini seçer.
        /// Tam eşleşme yoksa dil kodu (ör. de-AT → de-DE), o da yoksa en-US kullanılır.
        /// </summary>
        private static string DetectDefaultLanguage()
        {
            try
            {
                var uiCulture = CultureInfo.CurrentUICulture;
                if (!string.IsNullOrEmpty(uiCulture?.Name))
                {
                    var exact = SupportedLanguages.FirstOrDefault(code =>
                        string.Equals(code, uiCulture.Name, StringComparison.OrdinalIgnoreCase));
                    if (exact != null) return exact;

                    var languageTag = uiCulture.TwoLetterISOLanguageName;
                    if (!string.IsNullOrEmpty(languageTag))
                    {
                        var neutral = SupportedLanguages.FirstOrDefault(code =>
                            code.StartsWith(languageTag + "-", StringComparison.OrdinalIgnoreCase));
                        if (neutral != null) return neutral;
                    }
                }
            }
            catch
            {
                // Culture API'leri her ortanda çalışmayabilir; sessizce İngilizce'ye dön.
            }

            return "en-US";
        }

        public void LoadLanguage(string languageCode)
        {
            try
            {
                _strings.Clear();

                // İngilizce kaynak tüm diller için eksiksiz fallback görevi görür. Dil dosyaları
                // yalnızca farklı değerleri override edebilir; eksik anahtarlar UI'da [Key]
                // olarak görünmek yerine İngilizce kalır.
                var resourcesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
                LoadResourceFile(Path.Combine(resourcesDirectory, "Strings.en-US.txt"));

                var requestedResourcePath = Path.Combine(resourcesDirectory, $"Strings.{languageCode}.txt");
                if (!File.Exists(requestedResourcePath))
                {
                    languageCode = "en-US";
                    requestedResourcePath = Path.Combine(resourcesDirectory, "Strings.en-US.txt");
                }

                if (!languageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                {
                    LoadResourceFile(requestedResourcePath);
                }

                _currentLanguage = languageCode;
                CurrentLanguage = languageCode;
                LanguageChanged?.Invoke(this, languageCode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Language loading error: {ex.Message}");
            }
        }

        private void LoadResourceFile(string resourcePath)
        {
            if (!File.Exists(resourcePath)) return;

            foreach (var line in File.ReadAllLines(resourcePath))
            {
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    _strings[parts[0].Trim()] = parts[1].Trim().Replace("\\n", Environment.NewLine);
                }
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
                new LanguageInfo("en-US", "English"),
                new LanguageInfo("ru-RU", "Русский"),
                new LanguageInfo("ja-JP", "日本語"),
                new LanguageInfo("de-DE", "Deutsch"),
                new LanguageInfo("fr-FR", "Français"),
                new LanguageInfo("es-ES", "Español"),
                new LanguageInfo("zh-CN", "简体中文")
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
