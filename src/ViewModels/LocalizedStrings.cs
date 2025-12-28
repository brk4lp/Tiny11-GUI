using tiny11_ui.Services;

namespace tiny11_ui.ViewModels
{
    public class LocalizedLabels
    {
        private readonly LocalizationService _localization;
        
        public LocalizedLabels(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string IsoSelection => _localization.GetString("IsoSelection");
        public string WorkingDirectory => _localization.GetString("WorkingDirectory");
        public string OutputPath => _localization.GetString("OutputPath");
        public string ImageType => _localization.GetString("ImageType");
        public string WindowsEdition => _localization.GetString("WindowsEdition");
        public string Language => _localization.GetString("Language");
        public string StandardType => _localization.GetString("StandardType");
        public string CoreType => _localization.GetString("CoreType");
    }

    public class LocalizedButtons
    {
        private readonly LocalizationService _localization;
        
        public LocalizedButtons(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string BrowseButton => _localization.GetString("BrowseButton");
        public string SelectButton => _localization.GetString("SelectButton");
        public string SaveButton => _localization.GetString("SaveButton");
        public string StartBuildButton => _localization.GetString("StartBuildButton");
        public string CancelButton => _localization.GetString("CancelButton");
    }

    public class LocalizedHeaders
    {
        private readonly LocalizationService _localization;
        
        public LocalizedHeaders(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string AdvancedOptions => _localization.GetString("AdvancedOptions");
        public string PresetSelection => _localization.GetString("PresetSelection");
        public string AppsToRemove => _localization.GetString("AppsToRemove");
        public string SystemFeaturesToDisable => _localization.GetString("SystemOptimizations");
        public string SystemRequirementsBypass => _localization.GetString("SystemRequirements");
        public string InstallationProcessBypass => _localization.GetString("InstallationProcess");
    }

    public class LocalizedPresets
    {
        private readonly LocalizationService _localization;
        
        public LocalizedPresets(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string Minimal => _localization.GetString("PresetMinimal");
        public string Balanced => _localization.GetString("PresetBalanced");
        public string Gaming => _localization.GetString("PresetGaming");
        public string Enterprise => _localization.GetString("PresetEnterprise");
    }

    public class LocalizedApps
    {
        private readonly LocalizationService _localization;
        
        public LocalizedApps(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string MicrosoftEdge => _localization.GetString("MicrosoftEdge");
        public string OneDrive => _localization.GetString("OneDrive");
        public string Cortana => _localization.GetString("Cortana");
        public string WindowsChat => _localization.GetString("WindowsChat");
        public string MicrosoftTeams => _localization.GetString("MicrosoftTeams");
        public string XboxApps => _localization.GetString("XboxApps");
    }

    public class LocalizedSystemFeatures
    {
        private readonly LocalizationService _localization;
        
        public LocalizedSystemFeatures(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string Telemetry => _localization.GetString("Telemetry");
        public string WindowsUpdate => _localization.GetString("AutomaticUpdates");
        public string WindowsDefender => _localization.GetString("WindowsDefender");
        public string SponsoredApps => _localization.GetString("SponsoredApps");
        public string ReservedStorage => _localization.GetString("ReservedStorage");
        public string BitLocker => _localization.GetString("BitLockerEncryption");
    }

    public class LocalizedSystemRequirements
    {
        private readonly LocalizationService _localization;
        
        public LocalizedSystemRequirements(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string TPMRequirement => _localization.GetString("TPMRequirement");
        public string CPUCompatibility => _localization.GetString("CPUCompatibilityCheck");
        public string RAMRequirement => _localization.GetString("RAMMinimumRequirement");
        public string SecureBootRequirement => _localization.GetString("SecureBootRequirement");
    }

    public class LocalizedInstallationProcess
    {
        private readonly LocalizationService _localization;
        
        public LocalizedInstallationProcess(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string MicrosoftAccountRequirement => _localization.GetString("MicrosoftAccountRequirement");
        public string NetworkConnectionCheck => _localization.GetString("InternetConnectionCheck");
        public string PrivacySettings => _localization.GetString("PrivacySettingsSetup");
    }

    public class LocalizedTooltips
    {
        private readonly LocalizationService _localization;
        
        public LocalizedTooltips(LocalizationService localization)
        {
            _localization = localization;
        }
        
        public string TPMBypass => _localization.GetString("TPMTooltip");
        public string CPUBypass => _localization.GetString("CPUTooltip");
        public string RAMBypass => _localization.GetString("RAMTooltip");
        public string SecureBootBypass => _localization.GetString("SecureBootTooltip");
        public string MSAccountBypass => _localization.GetString("MSAccountTooltip");
        public string NetworkBypass => _localization.GetString("NetworkTooltip");
        public string PrivacyBypass => _localization.GetString("PrivacyTooltip");
        public string MinimalPreset => _localization.GetString("PresetMinimalTooltip");
        public string BalancedPreset => _localization.GetString("PresetBalancedTooltip");
        public string GamingPreset => _localization.GetString("PresetGamingTooltip");
        public string EnterprisePreset => _localization.GetString("PresetEnterpriseTooltip");
        public string LanguageSelector => _localization.GetString("Language");
    }
}
