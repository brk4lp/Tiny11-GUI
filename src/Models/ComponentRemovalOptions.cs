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
        public bool RemoveChat { get; set; } = true;
        public bool RemoveTeams { get; set; } = true;
        public bool RemoveXbox { get; set; } = false;

        // Sistem Optimizasyonları
        public bool DisableTelemetry { get; set; } = true;
        public bool DisableWindowsUpdate { get; set; } = false;
        public bool DisableDefender { get; set; } = false;
        public bool DisableSponsoredApps { get; set; } = true;
        public bool DisableReservedStorage { get; set; } = true;
        public bool DisableBitLocker { get; set; } = true;

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

            // Teams / Chat
            if (RemoveTeams || RemoveChat)
            {
                packages.Add("MicrosoftTeams");
                packages.Add("Microsoft.MicrosoftTeams");
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

            return packages.ToArray();
        }

        /// <summary>
        /// Registry düzenlemelerini PowerShell script'i olarak döndürür
        /// </summary>
        public string GenerateRegistryScript()
        {
            var script = new System.Text.StringBuilder();
            script.AppendLine("# Registry Modifications for Tiny11");

            // Telemetri devre dışı bırakma
            if (DisableTelemetry)
            {
                script.AppendLine(@"
# Disable Telemetry
reg add 'HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection' /v AllowTelemetry /t REG_DWORD /d 0 /f
reg add 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection' /v AllowTelemetry /t REG_DWORD /d 0 /f
");
            }

            // Windows Update devre dışı bırakma
            if (DisableWindowsUpdate)
            {
                script.AppendLine(@"
# Disable Windows Update
reg add 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' /v NoAutoUpdate /t REG_DWORD /d 1 /f
reg add 'HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' /v AUOptions /t REG_DWORD /d 2 /f
");
            }

            // Windows Defender devre dışı bırakma
            if (DisableDefender)
            {
                script.AppendLine(@"
# Disable Windows Defender (Not Recommended)
reg add 'HKLM\SOFTWARE\Policies\Microsoft\Windows Defender' /v DisableAntiSpyware /t REG_DWORD /d 1 /f
");
            }

            // Sponsored Apps (Önerilen uygulamalar)
            if (DisableSponsoredApps)
            {
                script.AppendLine(@"
# Disable Sponsored Apps / Suggestions
reg add 'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' /v SilentInstalledAppsEnabled /t REG_DWORD /d 0 /f
reg add 'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' /v SystemPaneSuggestionsEnabled /t REG_DWORD /d 0 /f
reg add 'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' /v SoftLandingEnabled /t REG_DWORD /d 0 /f
reg add 'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' /v SubscribedContent-338393Enabled /t REG_DWORD /d 0 /f
reg add 'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' /v SubscribedContent-353694Enabled /t REG_DWORD /d 0 /f
reg add 'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' /v SubscribedContent-353696Enabled /t REG_DWORD /d 0 /f
");
            }

            // Reserved Storage
            if (DisableReservedStorage)
            {
                script.AppendLine(@"
# Disable Reserved Storage
reg add 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager' /v ShippedWithReserves /t REG_DWORD /d 0 /f
");
            }

            // BitLocker
            if (DisableBitLocker)
            {
                script.AppendLine(@"
# Disable BitLocker Auto-Encryption
reg add 'HKLM\SYSTEM\CurrentControlSet\Control\BitLocker' /v PreventDeviceEncryption /t REG_DWORD /d 1 /f
");
            }

            return script.ToString();
        }

        /// <summary>
        /// Sistem gereksinimleri bypass için registry script'i oluşturur
        /// </summary>
        public string GenerateBypassScript()
        {
            var script = new System.Text.StringBuilder();
            script.AppendLine("# System Requirements Bypass for Tiny11");

            if (BypassTPM || BypassCPU || BypassRAM || BypassSecureBoot)
            {
                script.AppendLine(@"
# Create Setup bypass registry keys
reg add 'HKLM\SYSTEM\Setup\LabConfig' /v BypassTPMCheck /t REG_DWORD /d 1 /f
reg add 'HKLM\SYSTEM\Setup\LabConfig' /v BypassSecureBootCheck /t REG_DWORD /d 1 /f
reg add 'HKLM\SYSTEM\Setup\LabConfig' /v BypassRAMCheck /t REG_DWORD /d 1 /f
reg add 'HKLM\SYSTEM\Setup\LabConfig' /v BypassCPUCheck /t REG_DWORD /d 1 /f
reg add 'HKLM\SYSTEM\Setup\LabConfig' /v BypassStorageCheck /t REG_DWORD /d 1 /f
");
            }

            // OOBE Bypass
            if (BypassMSAccount)
            {
                script.AppendLine(@"
# Bypass Microsoft Account requirement
reg add 'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE' /v BypassNRO /t REG_DWORD /d 1 /f
");
            }

            return script.ToString();
        }

        /// <summary>
        /// autounattend.xml için OOBE ayarlarını döndürür
        /// </summary>
        public string GenerateOOBESettings()
        {
            var settings = new System.Text.StringBuilder();

            if (SkipNetworkConnection)
            {
                settings.AppendLine("SkipMachineOOBE=true");
            }

            if (SkipPrivacyQuestions)
            {
                settings.AppendLine("SkipUserOOBE=true");
            }

            if (BypassMSAccount)
            {
                settings.AppendLine("BypassNRO=1");
            }

            return settings.ToString();
        }
    }
}
