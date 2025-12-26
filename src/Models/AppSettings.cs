using System;

namespace Tiny11UI.Models
{
    public class AppSettings
    {
        public string IsoFilePath { get; set; }
        public string ScratchDirectory { get; set; }
        public bool EnableLogging { get; set; }
        public string LogFilePath { get; set; }
        public string UserPreference { get; set; }

        public AppSettings()
        {
            // Default values
            IsoFilePath = string.Empty;
            ScratchDirectory = string.Empty;
            EnableLogging = true;
            LogFilePath = "logs/app.log";
            UserPreference = "default";
        }
    }
}