namespace tiny11_ui.Models
{
    /// <summary>
    /// Tiny11 oluşturulurken hangi bileşenlerin kaldırılacağını/devre dışı bırakılacağını belirten seçenekler
    /// </summary>
    public class ComponentRemovalOptions
    {
        // Uygulama Kaldırma Seçenekleri
        public bool RemoveEdge { get; set; } = true;
        public bool RemoveOneDrive { get; set; } = true;
        public bool RemoveCortana { get; set; } = true;
        public bool RemoveTeams { get; set; } = true;
        public bool RemoveXbox { get; set; } = false;

        // Sistem Optimizasyonları
        public bool DisableTelemetry { get; set; } = true;
        public bool DisableWindowsUpdate { get; set; } = false;
        public bool DisableSponsoredApps { get; set; } = true;
        public bool DisableReservedStorage { get; set; } = true;
        public bool DisableBitLocker { get; set; } = true;

        /// <summary>
        /// Windows Defender antivirüs servislerini imajda devre dışı bırakır ve Defender paketini
        /// kaldırmayı dener (CORE derlemesindeki Defender işlemleriyle aynı).
        /// </summary>
        public bool DisableDefender { get; set; } = false;

        // Sistem Gereksinimleri Bypass
        public bool BypassTPM { get; set; } = true;
        public bool BypassCPU { get; set; } = true;
        public bool BypassRAM { get; set; } = true;
        public bool BypassSecureBoot { get; set; } = true;

        // OOBE Ayarları
        public bool BypassMSAccount { get; set; } = true;
        public bool SkipNetworkConnection { get; set; } = true;
        public bool SkipPrivacyQuestions { get; set; } = true;

        /// <summary>
        /// Kullanıcının sağladığı özel autounattend.xml dosyasının yolu (opsiyonel).
        /// Belirtilmişse, oluşturulan ISO'nun kök dizinine kopyalanır.
        /// </summary>
        public string? CustomAutounattendPath { get; set; }

        // Derin Temizlik / Boyut Küçültme
        public bool CleanupComponentStore { get; set; } = true;
        public bool CompressFinalImage { get; set; } = true;
        public bool RemoveHyperV { get; set; } = false;
        public bool RemoveRecall { get; set; } = true;
        public bool RemoveWidgets { get; set; } = true;
        public bool RemoveCopilot { get; set; } = true;
        public bool RemoveInputComponents { get; set; } = false;
        public bool CleanupDriverStore { get; set; } = false;

        /// <summary>
        /// Kaldırılacak AppX paketlerinin listesini döndürür
        /// </summary>
        public string[] GetPackagesToRemove()
        {
            var packages = new System.Collections.Generic.List<string>
            {
                // Her zaman kaldırılacaklar (bloatware)
                "Microsoft.BingNews",
                "Microsoft.BingWeather",
                "Microsoft.GetHelp",
                "Microsoft.Getstarted",
                "Microsoft.MicrosoftOfficeHub",
                "Microsoft.MicrosoftSolitaireCollection",
                "Microsoft.People",
                "Microsoft.PowerAutomateDesktop",
                "Microsoft.Todos",
                "Microsoft.WindowsAlarms",
                "Microsoft.WindowsCommunicationsApps",
                "Microsoft.WindowsFeedbackHub",
                "Microsoft.WindowsMaps",
                "Microsoft.WindowsSoundRecorder",
                "Microsoft.YourPhone",
                "Microsoft.ZuneMusic",
                "Microsoft.ZuneVideo",
                "MicrosoftCorporationII.MicrosoftFamily",
                "MicrosoftCorporationII.QuickAssist",
                "Clipchamp.Clipchamp",
                "Microsoft.Windows.DevHome"
            };

            // Edge ve ilişkili bileşenler
            if (RemoveEdge)
            {
                packages.Add("Microsoft.MicrosoftEdge");
                packages.Add("Microsoft.MicrosoftEdge.Stable");
                packages.Add("Microsoft.MicrosoftEdgeDevToolsClient");
            }

            // OneDrive
            if (RemoveOneDrive)
            {
                packages.Add("Microsoft.OneDrive");
                packages.Add("Microsoft.OneDriveSync");
            }

            // Cortana
            if (RemoveCortana)
            {
                if (!packages.Contains("Microsoft.549981C3F5F10"))
                    packages.Add("Microsoft.549981C3F5F10");
            }

            // Teams / Chat aynı paket ailesidir; tek seçenek olarak yönetilir.
            if (RemoveTeams)
            {
                packages.Add("MicrosoftTeams");
                packages.Add("Microsoft.MicrosoftTeams");
                packages.Add("MSTeams");
                packages.Add("Microsoft.Windows.Teams");
            }

            // Xbox uygulamaları
            if (RemoveXbox)
            {
                packages.Add("Microsoft.GamingApp");
                packages.Add("Microsoft.Xbox.TCUI");
                packages.Add("Microsoft.XboxGamingOverlay");
                packages.Add("Microsoft.XboxGameOverlay");
                packages.Add("Microsoft.XboxSpeechToTextOverlay");
                packages.Add("Microsoft.XboxIdentityProvider");
                packages.Add("Microsoft.XboxApp");
            }

            // Widgets (kısa substring - paket adı build'e göre değişebilir)
            if (RemoveWidgets)
            {
                packages.Add("WebExperience");
            }

            // Copilot (kısa substring - paket adı build'e göre değişebilir)
            if (RemoveCopilot)
            {
                packages.Add("Copilot");
            }

            return packages.ToArray();
        }

    }
}
