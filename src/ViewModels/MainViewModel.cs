using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using tiny11_ui.Services;
using tiny11_ui.Models;

namespace tiny11_ui.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private string _isoPath = string.Empty;
        public string IsoPath
        {
            get => _isoPath;
            set
            {
                _isoPath = value;
                OnPropertyChanged();
                StartBuildCommand.RaiseCanExecuteChanged();
            }
        }

        private string _scratchPath = string.Empty;
        public string ScratchPath
        {
            get => _scratchPath;
            set
            {
                _scratchPath = value;
                OnPropertyChanged();
                StartBuildCommand.RaiseCanExecuteChanged();
            }
        }

        private string _outputPath = string.Empty;
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                _outputPath = value;
                OnPropertyChanged();
                StartBuildCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _isStandardBuild = true;
        public bool IsStandardBuild
        {
            get => _isStandardBuild;
            set
            {
                _isStandardBuild = value;
                OnPropertyChanged();
            }
        }

        private bool _isCoreBuild = false;
        public bool IsCoreBuild
        {
            get => _isCoreBuild;
            set
            {
                _isCoreBuild = value;
                OnPropertyChanged();
            }
        }

        #region Advanced Options Properties

        // Preset Options
        private bool _isMinimalPreset = false;
        public bool IsMinimalPreset 
        { 
            get => _isMinimalPreset; 
            set { _isMinimalPreset = value; OnPropertyChanged(); if (value) ApplyMinimalPreset(); } 
        }

        private bool _isBalancedPreset = true;
        public bool IsBalancedPreset 
        { 
            get => _isBalancedPreset; 
            set { _isBalancedPreset = value; OnPropertyChanged(); if (value) ApplyBalancedPreset(); } 
        }

        private bool _isGamingPreset = false;
        public bool IsGamingPreset 
        { 
            get => _isGamingPreset; 
            set { _isGamingPreset = value; OnPropertyChanged(); if (value) ApplyGamingPreset(); } 
        }

        private bool _isEnterprisePreset = false;
        public bool IsEnterprisePreset 
        { 
            get => _isEnterprisePreset; 
            set { _isEnterprisePreset = value; OnPropertyChanged(); if (value) ApplyEnterprisePreset(); } 
        }

        // Application Removal
        private bool _removeEdge = true;
        public bool RemoveEdge { get => _removeEdge; set { _removeEdge = value; OnPropertyChanged(); } }

        private bool _removeOneDrive = true;
        public bool RemoveOneDrive { get => _removeOneDrive; set { _removeOneDrive = value; OnPropertyChanged(); } }

        private bool _removeCortana = true;
        public bool RemoveCortana { get => _removeCortana; set { _removeCortana = value; OnPropertyChanged(); } }

        private bool _removeChat = true;
        public bool RemoveChat { get => _removeChat; set { _removeChat = value; OnPropertyChanged(); } }

        private bool _removeTeams = true;
        public bool RemoveTeams { get => _removeTeams; set { _removeTeams = value; OnPropertyChanged(); } }

        private bool _removeXbox = false;
        public bool RemoveXbox { get => _removeXbox; set { _removeXbox = value; OnPropertyChanged(); } }

        // System Optimizations
        private bool _disableTelemetry = true;
        public bool DisableTelemetry { get => _disableTelemetry; set { _disableTelemetry = value; OnPropertyChanged(); } }

        private bool _disableWindowsUpdate = false;
        public bool DisableWindowsUpdate { get => _disableWindowsUpdate; set { _disableWindowsUpdate = value; OnPropertyChanged(); } }

        private bool _disableDefender = false;
        public bool DisableDefender { get => _disableDefender; set { _disableDefender = value; OnPropertyChanged(); } }

        private bool _disableSponsoredApps = true;
        public bool DisableSponsoredApps { get => _disableSponsoredApps; set { _disableSponsoredApps = value; OnPropertyChanged(); } }

        private bool _disableReservedStorage = true;
        public bool DisableReservedStorage { get => _disableReservedStorage; set { _disableReservedStorage = value; OnPropertyChanged(); } }

        private bool _disableBitLocker = true;
        public bool DisableBitLocker { get => _disableBitLocker; set { _disableBitLocker = value; OnPropertyChanged(); } }

        // System Requirements Bypass
        private bool _bypassTPM = true;
        public bool BypassTPM { get => _bypassTPM; set { _bypassTPM = value; OnPropertyChanged(); } }

        private bool _bypassCPU = true;
        public bool BypassCPU { get => _bypassCPU; set { _bypassCPU = value; OnPropertyChanged(); } }

        private bool _bypassRAM = true;
        public bool BypassRAM { get => _bypassRAM; set { _bypassRAM = value; OnPropertyChanged(); } }

        private bool _bypassSecureBoot = true;
        public bool BypassSecureBoot { get => _bypassSecureBoot; set { _bypassSecureBoot = value; OnPropertyChanged(); } }

        // OOBE Settings
        private bool _bypassMSAccount = true;
        public bool BypassMSAccount { get => _bypassMSAccount; set { _bypassMSAccount = value; OnPropertyChanged(); } }

        private bool _skipNetworkConnection = true;
        public bool SkipNetworkConnection { get => _skipNetworkConnection; set { _skipNetworkConnection = value; OnPropertyChanged(); } }

        private bool _skipPrivacyQuestions = true;
        public bool SkipPrivacyQuestions { get => _skipPrivacyQuestions; set { _skipPrivacyQuestions = value; OnPropertyChanged(); } }

        // Deep Cleanup / Size Reduction
        private bool _cleanupComponentStore = true;
        public bool CleanupComponentStore { get => _cleanupComponentStore; set { _cleanupComponentStore = value; OnPropertyChanged(); } }

        private bool _compressFinalImage = true;
        public bool CompressFinalImage { get => _compressFinalImage; set { _compressFinalImage = value; OnPropertyChanged(); } }

        private bool _removeHyperV = false;
        public bool RemoveHyperV { get => _removeHyperV; set { _removeHyperV = value; OnPropertyChanged(); } }

        private bool _removeRecall = true;
        public bool RemoveRecall { get => _removeRecall; set { _removeRecall = value; OnPropertyChanged(); } }

        private bool _removeWidgets = true;
        public bool RemoveWidgets { get => _removeWidgets; set { _removeWidgets = value; OnPropertyChanged(); } }

        private bool _removeCopilot = true;
        public bool RemoveCopilot { get => _removeCopilot; set { _removeCopilot = value; OnPropertyChanged(); } }

        private bool _removeInputComponents = false;
        public bool RemoveInputComponents { get => _removeInputComponents; set { _removeInputComponents = value; OnPropertyChanged(); } }

        private bool _cleanupDriverStore = false;
        public bool CleanupDriverStore { get => _cleanupDriverStore; set { _cleanupDriverStore = value; OnPropertyChanged(); } }

        #endregion

        private ObservableCollection<string> _windowsEditions = new ObservableCollection<string>();
        public ObservableCollection<string> WindowsEditions
        {
            get => _windowsEditions;
            set
            {
                _windowsEditions = value;
                OnPropertyChanged();
            }
        }

        private string? _selectedEdition;
        public string? SelectedEdition
        {
            get => _selectedEdition;
            set
            {
                _selectedEdition = value;
                OnPropertyChanged();
                StartBuildCommand.RaiseCanExecuteChanged();
            }
        }

        private bool _isEditionSelectionEnabled = false;
        public bool IsEditionSelectionEnabled
        {
            get => _isEditionSelectionEnabled;
            set
            {
                _isEditionSelectionEnabled = value;
                OnPropertyChanged();
            }
        }

        private string _logOutput = string.Empty;
        public string LogOutput
        {
            get => _logOutput;
            set
            {
                _logOutput = value;
                OnPropertyChanged();
            }
        }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        private double _progressValue = 0;
        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged();
            }
        }

        private bool _isIndeterminate = false;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set
            {
                _isIndeterminate = value;
                OnPropertyChanged();
            }
        }

        private bool _isBuildRunning = false;
        public bool IsBuildRunning
        {
            get => _isBuildRunning;
            set
            {
                _isBuildRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanClose));
                OnPropertyChanged(nameof(CanStartNewBuild));
                CancelBuildCommand?.RaiseCanExecuteChanged();
                StartBuildCommand?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Uygulama kapatılabilir mi?
        /// </summary>
        public bool CanClose => !IsBuildRunning;

        /// <summary>
        /// Uygulama sürümü (assembly'den okunur)
        /// </summary>
        public string AppVersion
        {
            get
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "v?" : $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        /// <summary>
        /// Yeni build başlatılabilir mi?
        /// </summary>
        public bool CanStartNewBuild => !IsBuildRunning;

        private readonly PowerShellService _powerShellService;
        private readonly LocalizationService _localizationService;

        public ICommand BrowseIsoCommand { get; }
        public ICommand BrowseScratchCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public RelayCommand StartBuildCommand { get; }
        public RelayCommand CancelBuildCommand { get; }

        // Localization Properties
        private ObservableCollection<LanguageInfo> _availableLanguages = new ObservableCollection<LanguageInfo>();
        public ObservableCollection<LanguageInfo> AvailableLanguages
        {
            get => _availableLanguages;
            set { _availableLanguages = value; OnPropertyChanged(); }
        }

        private LanguageInfo? _selectedLanguage;
        public LanguageInfo? SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                _selectedLanguage = value;
                OnPropertyChanged();
                if (value != null)
                {
                    _localizationService.LoadLanguage(value.Code);
                }
            }
        }

        public MainViewModel()
        {
            _localizationService = LocalizationService.Instance;
            _powerShellService = new PowerShellService(_localizationService);
            
            // PowerShell service event'lerini bağla
            _powerShellService.OutputReceived += OnOutputReceived;
            _powerShellService.ErrorReceived += OnErrorReceived;
            _powerShellService.ProcessCompleted += OnProcessCompleted;
            
            // Localization event'ini bağla
            _localizationService.LanguageChanged += OnLanguageChanged;

            BrowseIsoCommand = new RelayCommand(BrowseIso);
            BrowseScratchCommand = new RelayCommand(BrowseScratch);
            BrowseOutputCommand = new RelayCommand(BrowseOutput);
            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);
            StartBuildCommand = new RelayCommand(StartBuild, CanStartBuild);
            CancelBuildCommand = new RelayCommand(CancelBuild, () => IsBuildRunning);
            
            // Dil seçeneklerini yükle
            LoadAvailableLanguages();

            // Varsayılan çalışma dizini
            ScratchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tiny11_Temp");
            
            // Başlangıç mesajı (localize edilecek)
            UpdateStartupMessage();
        }

        private void BrowseIso()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = GetLocalizedString("IsoDialogTitle"),
                Filter = GetLocalizedString("IsoDialogFilter"),
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                IsoPath = openFileDialog.FileName;
                LogOutput += string.Format(GetLocalizedString("IsoSelected"), IsoPath) + "\n";
                
                // Windows sürümlerini yükle
                _ = LoadWindowsEditionsAsync();
            }
        }

        private void BrowseScratch()
        {
            using var folderDialog = new WinForms.FolderBrowserDialog
            {
                Description = GetLocalizedString("WorkingDirDialogTitle"),
                UseDescriptionForTitle = true,
                SelectedPath = ScratchPath
            };

            if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                ScratchPath = folderDialog.SelectedPath;
                LogOutput += string.Format(GetLocalizedString("WorkingDirSelected"), ScratchPath) + "\n";
            }
        }

        private void BrowseOutput()
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = GetLocalizedString("OutputDialogTitle"),
                Filter = GetLocalizedString("IsoDialogFilter"),
                DefaultExt = "iso",
                FileName = "tiny11.iso"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                OutputPath = saveFileDialog.FileName;
                LogOutput += string.Format(GetLocalizedString("OutputPathSelected"), OutputPath) + "\n";
            }
        }

        /// <summary>
        /// İşlemi iptal et
        /// </summary>
        private async void CancelBuild()
        {
            if (!IsBuildRunning) return;

            var result = System.Windows.MessageBox.Show(
                GetLocalizedString("CancelConfirmMessage"),
                GetLocalizedString("CancelConfirmTitle"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                StatusText = GetLocalizedString("StatusCancelling");
                await _powerShellService.CancelAsync();
                IsBuildRunning = false;
                IsIndeterminate = false;
                StatusText = GetLocalizedString("StatusCancelled");
            }
        }

        /// <summary>
        /// Pencere kapanırken çağrılır - işlem varsa engelle
        /// </summary>
        public bool HandleWindowClosing()
        {
            if (!IsBuildRunning) return true; // Kapatılabilir

            var result = System.Windows.MessageBox.Show(
                GetLocalizedString("CloseConfirmMessage"),
                GetLocalizedString("CloseConfirmTitle"),
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // İşlemi iptal et ve sonra kapat
                Task.Run(async () =>
                {
                    await _powerShellService.CancelAsync();
                    
                    // UI thread'de uygulamayı kapat
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsBuildRunning = false;
                        System.Windows.Application.Current.Shutdown();
                    });
                });
                
                return false; // Şimdilik kapatmayı engelle, async bitince kapanacak
            }

            return false; // Kapatmayı engelle
        }

        private async void StartBuild()
        {
            if (!CanStartBuild())
                return;

            IsBuildRunning = true;

            try
            {
                StatusText = GetLocalizedString("StatusStarting");
                IsIndeterminate = true;
                
                // Çalışma dizini oluştur
                if (!Directory.Exists(ScratchPath))
                {
                    Directory.CreateDirectory(ScratchPath);
                }

                // Kullanıcı seçeneklerini ComponentRemovalOptions'a dönüştür
                var options = new ComponentRemovalOptions
                {
                    // Uygulama kaldırma
                    RemoveEdge = RemoveEdge,
                    RemoveOneDrive = RemoveOneDrive,
                    RemoveCortana = RemoveCortana,
                    RemoveChat = RemoveChat,
                    RemoveTeams = RemoveTeams,
                    RemoveXbox = RemoveXbox,
                    
                    // Sistem optimizasyonları
                    DisableTelemetry = DisableTelemetry,
                    DisableWindowsUpdate = DisableWindowsUpdate,
                    DisableDefender = DisableDefender,
                    DisableSponsoredApps = DisableSponsoredApps,
                    DisableReservedStorage = DisableReservedStorage,
                    DisableBitLocker = DisableBitLocker,
                    
                    // Sistem gereksinimleri bypass
                    BypassTPM = BypassTPM,
                    BypassCPU = BypassCPU,
                    BypassRAM = BypassRAM,
                    BypassSecureBoot = BypassSecureBoot,
                    
                    // OOBE ayarları
                    BypassMSAccount = BypassMSAccount,
                    SkipNetworkConnection = SkipNetworkConnection,
                    SkipPrivacyQuestions = SkipPrivacyQuestions,

                    // Derin temizlik / boyut küçültme
                    CleanupComponentStore = CleanupComponentStore,
                    CompressFinalImage = CompressFinalImage,
                    RemoveHyperV = RemoveHyperV,
                    RemoveRecall = RemoveRecall,
                    RemoveWidgets = RemoveWidgets,
                    RemoveCopilot = RemoveCopilot,
                    RemoveInputComponents = RemoveInputComponents,
                    CleanupDriverStore = CleanupDriverStore
                };

                LogOutput += GetLocalizedString("LogBuildStarted") + "\n";
                LogOutput += string.Format(GetLocalizedString("LogIsoPath"), IsoPath) + "\n";
                LogOutput += string.Format(GetLocalizedString("LogWorkingDir"), ScratchPath) + "\n";
                LogOutput += string.Format(GetLocalizedString("LogOutputPath"), OutputPath) + "\n";
                LogOutput += string.Format(GetLocalizedString("LogEdition"), SelectedEdition) + "\n";
                LogOutput += string.Format(GetLocalizedString("LogBuildType"), IsCoreBuild ? GetLocalizedString("LogBuildTypeCore") : GetLocalizedString("LogBuildTypeStandard")) + "\n\n";
                
                // Seçilen ayarları logla
                var yes = GetLocalizedString("LogYes");
                var no = GetLocalizedString("LogNo");
                LogOutput += GetLocalizedString("LogSelectedOptions") + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogRemoveEdge"), RemoveEdge ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogRemoveOneDrive"), RemoveOneDrive ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogRemoveCortana"), RemoveCortana ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogRemoveChat"), RemoveChat ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogRemoveTeams"), RemoveTeams ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogRemoveXbox"), RemoveXbox ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogDisableTelemetry"), DisableTelemetry ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogDisableUpdate"), DisableWindowsUpdate ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogDisableDefender"), DisableDefender ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogBypassTPM"), BypassTPM ? yes : no) + "\n";
                LogOutput += "   " + string.Format(GetLocalizedString("LogBypassMSAccount"), BypassMSAccount ? yes : no) + "\n";
                LogOutput += "\n";

                // Edition index'i Windows sürümü string'inden çıkar
                var editionIndex = 1; // Varsayılan
                if (SelectedEdition!.Contains(" - "))
                {
                    var parts = SelectedEdition.Split(new[] { " - " }, StringSplitOptions.None);
                    if (int.TryParse(parts[0], out int parsedIndex))
                    {
                        editionIndex = parsedIndex;
                    }
                }

                LogOutput += string.Format(GetLocalizedString("LogEditionIndex"), editionIndex) + "\n\n";

                StatusText = GetLocalizedString("LogProcessing");
                
                // Yeni metodu kullan - kullanıcı seçenekleriyle
                var success = await _powerShellService.RunTiny11WithOptionsAsync(
                    IsoPath!,
                    ScratchPath,
                    OutputPath,
                    editionIndex,
                    options,
                    IsCoreBuild
                );
                
                // Çıktı ISO kontrolü
                if (success && File.Exists(OutputPath))
                {
                    StatusText = GetLocalizedString("BuildCompleted");
                    LogOutput += "\n" + GetLocalizedString("LogBuildSuccess") + "\n";
                    LogOutput += string.Format(GetLocalizedString("LogOutputLocation"), OutputPath) + "\n";
                    
                    // Dosya boyutunu göster
                    var fileInfo = new FileInfo(OutputPath);
                    var sizeInGB = fileInfo.Length / (1024.0 * 1024.0 * 1024.0);
                    LogOutput += string.Format(GetLocalizedString("LogFileSize"), sizeInGB.ToString("F2"), fileInfo.Length.ToString("N0")) + "\n";
                }
                else if (success)
                {
                    StatusText = GetLocalizedString("LogBuildFailedNoIso");
                    LogOutput += "\n" + GetLocalizedString("LogBuildFailedNoIso") + "\n";
                    LogOutput += string.Format(GetLocalizedString("LogExpectedLocation"), OutputPath) + "\n";
                    LogOutput += GetLocalizedString("LogCheckWorkingDir") + "\n";
                }
                else
                {
                    StatusText = GetLocalizedString("BuildFailed");
                    LogOutput += "\n" + GetLocalizedString("LogBuildFailed") + "\n";
                }
            }
            catch (Exception ex)
            {
                LogOutput += string.Format(GetLocalizedString("LogError"), ex.Message) + "\n";
                StatusText = GetLocalizedString("Error");
            }
            finally
            {
                IsBuildRunning = false;
                IsIndeterminate = false;
            }
        }

        private bool CanStartBuild()
        {
            // İşlem çalışıyorsa başlatılamaz
            if (IsBuildRunning) return false;
            
            var canStart = !string.IsNullOrEmpty(IsoPath) && 
                           !string.IsNullOrEmpty(ScratchPath) && 
                           !string.IsNullOrEmpty(OutputPath) && 
                           !string.IsNullOrEmpty(SelectedEdition);
            
            // Debug bilgisi
            if (!canStart)
            {
                var missing = new System.Collections.Generic.List<string>();
                if (string.IsNullOrEmpty(IsoPath)) missing.Add("ISO");
                if (string.IsNullOrEmpty(ScratchPath)) missing.Add(GetLocalizedString("WorkingDirectory"));
                if (string.IsNullOrEmpty(SelectedEdition)) missing.Add(GetLocalizedString("WindowsEdition"));
                
                StatusText = string.Format(GetLocalizedString("StatusMissing"), string.Join(", ", missing));
            }
            else
            {
                StatusText = GetLocalizedString("StatusReady");
            }
            
            return canStart;
        }

        private bool IsRunningAsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private async Task LoadWindowsEditionsAsync()
        {
            try
            {
                if (!IsRunningAsAdministrator())
                {
                    LogOutput += GetLocalizedString("LogAdminRightsRecommended") + "\n";
                    LogOutput += GetLocalizedString("LogAdminRightsNeededForEditions") + "\n";
                    LogOutput += GetLocalizedString("LoadingDefaultEditions") + "\n\n";
                    
                    // Yönetici yetkisi yoksa tüm varsayılan sürümler ekle
                    WindowsEditions.Clear();
                    WindowsEditions.Add("1 - Windows 11 Home");
                    WindowsEditions.Add("2 - Windows 11 Home Single Language");
                    WindowsEditions.Add("3 - Windows 11 Education");
                    WindowsEditions.Add("4 - Windows 11 Pro");
                    WindowsEditions.Add("5 - Windows 11 Pro Education");
                    WindowsEditions.Add("6 - Windows 11 Pro for Workstations");
                    
                    SelectedEdition = WindowsEditions[3]; // Windows 11 Pro'yu varsayılan yap
                    IsEditionSelectionEnabled = true;
                    StatusText = "Hazır (Varsayılan sürümler)";
                    return;
                }

                StatusText = "Windows sürümleri yükleniyor...";
                IsIndeterminate = true;
                
                var editions = await _powerShellService.GetWindowsEditionsAsync(IsoPath!);
                
                WindowsEditions.Clear();
                foreach (var edition in editions)
                {
                    WindowsEditions.Add(edition);
                }

                if (WindowsEditions.Count > 0)
                {
                    SelectedEdition = WindowsEditions[0];
                    IsEditionSelectionEnabled = true;
                    LogOutput += $"{WindowsEditions.Count} " + GetLocalizedString("EditionsLoaded") + "\n";
                }
                else
                {
                    LogOutput += GetLocalizedString("NoEditionsFound") + "\n";
                }

                StatusText = "Hazır";
            }
            catch (Exception ex)
            {
                LogOutput += string.Format(GetLocalizedString("LogError"), GetLocalizedString("EditionsLoadFailed") + ": " + ex.Message) + "\n";
                LogOutput += GetLocalizedString("LoadingDefaultEditions") + "\n";
                
                // Hata durumunda tüm varsayılan sürümler
                WindowsEditions.Clear();
                WindowsEditions.Add("1 - Windows 11 Home");
                WindowsEditions.Add("2 - Windows 11 Home Single Language");
                WindowsEditions.Add("3 - Windows 11 Education");
                WindowsEditions.Add("4 - Windows 11 Pro");
                WindowsEditions.Add("5 - Windows 11 Pro Education");
                WindowsEditions.Add("6 - Windows 11 Pro for Workstations");
                SelectedEdition = WindowsEditions[3]; // Windows 11 Pro
                IsEditionSelectionEnabled = true;
                
                StatusText = "Hazır (Varsayılan sürümler)";
            }
            finally
            {
                IsIndeterminate = false;
            }
        }

        private void OnOutputReceived(string output)
        {
            // UI thread'de çalıştır
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogOutput += output + "\n";
                
                // Progress tracking based on output messages
                if (output.Contains("Exporting image"))
                    StatusText = "Exporting image...";
                else if (output.Contains("Unmounting image"))
                    StatusText = GetLocalizedString("StatusUnmounting");
                else if (output.Contains("Cleanup complete"))
                    StatusText = GetLocalizedString("StatusCleanup");
                else if (output.Contains("Creating ISO"))
                    StatusText = GetLocalizedString("StatusCreatingIso");
                else if (output.Contains("Mounting"))
                    StatusText = GetLocalizedString("StatusMounting");
                else if (output.Contains("Copying"))
                    StatusText = GetLocalizedString("StatusCopying");
            });
        }

        private void OnErrorReceived(string error)
        {
            // UI thread'de çalıştır
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                LogOutput += string.Format(GetLocalizedString("LogError"), error) + "\n";
            });
        }

        private async void OnProcessCompleted(int exitCode)
        {
            if (exitCode == 0)
            {
                StatusText = GetLocalizedString("Completed");
                LogOutput += "\n" + GetLocalizedString("LogProcessCompletedBanner") + "\n";
            }
            else
            {
                StatusText = GetLocalizedString("Error");
                LogOutput += "\n" + string.Format(GetLocalizedString("LogProcessFailedBanner"), exitCode) + "\n";
            }
            
            // ISO'yu unmount et
            if (!string.IsNullOrEmpty(IsoPath))
            {
                LogOutput += GetLocalizedString("LogIsoUnmounting") + "\n";
                await _powerShellService.UnmountIsoAsync(IsoPath);
            }
            
            IsIndeterminate = false;
        }

        #region Preset Methods

        private void ApplyMinimalPreset()
        {
            // Minimal: Maximum temizlik, minimum özellik
            RemoveEdge = true;
            RemoveOneDrive = true;
            RemoveCortana = true;
            RemoveChat = true;
            RemoveTeams = true;
            RemoveXbox = true;
            
            DisableTelemetry = true;
            DisableWindowsUpdate = true;
            DisableDefender = true;
            DisableSponsoredApps = true;
            DisableReservedStorage = true;
            DisableBitLocker = true;
            
            BypassTPM = true;
            BypassCPU = true;
            BypassRAM = true;
            BypassSecureBoot = true;
            
            BypassMSAccount = true;
            SkipNetworkConnection = true;
            SkipPrivacyQuestions = true;

            CleanupComponentStore = true;
            CompressFinalImage = true;
            RemoveHyperV = true;
            RemoveRecall = true;
            RemoveWidgets = true;
            RemoveCopilot = true;
            RemoveInputComponents = true;
            CleanupDriverStore = true;

            LogOutput += GetLocalizedString("LogPresetMinimalApplied") + "\n";
        }

        private void ApplyBalancedPreset()
        {
            // Balanced: Dengeli yaklaşım (varsayılan)
            RemoveEdge = true;
            RemoveOneDrive = true;
            RemoveCortana = true;
            RemoveChat = true;
            RemoveTeams = true;
            RemoveXbox = false;
            
            DisableTelemetry = true;
            DisableWindowsUpdate = false;
            DisableDefender = false;
            DisableSponsoredApps = true;
            DisableReservedStorage = true;
            DisableBitLocker = true;
            
            BypassTPM = true;
            BypassCPU = true;
            BypassRAM = true;
            BypassSecureBoot = true;
            
            BypassMSAccount = true;
            SkipNetworkConnection = true;
            SkipPrivacyQuestions = true;

            CleanupComponentStore = true;
            CompressFinalImage = true;
            RemoveHyperV = false;
            RemoveRecall = true;
            RemoveWidgets = true;
            RemoveCopilot = true;
            RemoveInputComponents = false;
            CleanupDriverStore = false;

            LogOutput += GetLocalizedString("LogPresetBalancedApplied") + "\n";
        }

        private void ApplyGamingPreset()
        {
            // Gaming: Performans odaklı, Xbox apps korunur
            RemoveEdge = false; // Bazı oyunlar Edge WebView kullanır
            RemoveOneDrive = true;
            RemoveCortana = true;
            RemoveChat = true;
            RemoveTeams = true;
            RemoveXbox = false; // Gaming için Xbox apps korunur
            
            DisableTelemetry = true;
            DisableWindowsUpdate = false; // Oyun güncellemeleri için
            DisableDefender = false; // Güvenlik korunur
            DisableSponsoredApps = true;
            DisableReservedStorage = false; // Performans için
            DisableBitLocker = true;
            
            BypassTPM = true;
            BypassCPU = true;
            BypassRAM = true;
            BypassSecureBoot = true;
            
            BypassMSAccount = false; // Xbox entegrasyonu için
            SkipNetworkConnection = false;
            SkipPrivacyQuestions = true;

            CleanupComponentStore = true;
            CompressFinalImage = true;
            RemoveHyperV = false;
            RemoveRecall = true;
            RemoveWidgets = true;
            RemoveCopilot = true;
            RemoveInputComponents = false;
            CleanupDriverStore = false;

            LogOutput += GetLocalizedString("LogPresetGamingApplied") + "\n";
        }

        private void ApplyEnterprisePreset()
        {
            // Enterprise: İş ortamı için güvenlik ve stabilite odaklı
            RemoveEdge = false; // Enterprise uygulamalar için
            RemoveOneDrive = false; // İş dosyaları için
            RemoveCortana = true;
            RemoveChat = false; // İş iletişimi için
            RemoveTeams = false; // İş iletişimi için
            RemoveXbox = true;
            
            DisableTelemetry = false; // Enterprise telemetri korunabilir
            DisableWindowsUpdate = false; // Güvenlik güncellemeleri
            DisableDefender = false; // Güvenlik korunur
            DisableSponsoredApps = true;
            DisableReservedStorage = false;
            DisableBitLocker = false; // Enterprise güvenlik
            
            BypassTPM = false; // Enterprise güvenlik için TPM korunur
            BypassCPU = true;
            BypassRAM = true;
            BypassSecureBoot = false; // Enterprise güvenlik
            
            BypassMSAccount = true;
            SkipNetworkConnection = false;
            SkipPrivacyQuestions = false; // Enterprise uyumluluk

            CleanupComponentStore = true;
            CompressFinalImage = false; // Servis edilebilirlik için standart WIM korunur
            RemoveHyperV = false; // Sanallaştırma ihtiyacı olabilir
            RemoveRecall = true; // Gizlilik/uyumluluk
            RemoveWidgets = true;
            RemoveCopilot = true; // Kurumsal uyumluluk
            RemoveInputComponents = false; // Erişilebilirlik ihtiyaçları
            CleanupDriverStore = false; // Farklı donanımlar için geniş sürücü desteği

            LogOutput += GetLocalizedString("LogPresetEnterpriseApplied") + "\n";
        }

        #endregion

        #region Localization Methods

        private void LoadAvailableLanguages()
        {
            var languages = _localizationService.GetAvailableLanguages();
            AvailableLanguages.Clear();
            foreach (var language in languages)
            {
                AvailableLanguages.Add(language);
            }
            
            // Mevcut dili seç
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == _localizationService.CurrentLanguage);
        }

        private void ChangeLanguage()
        {
            if (SelectedLanguage != null)
            {
                _localizationService.LoadLanguage(SelectedLanguage.Code);
            }
        }

        private void OnLanguageChanged(object? sender, string languageCode)
        {
            // UI'daki tüm string'leri güncelle
            OnPropertyChanged(nameof(LocalizedLabels));
            OnPropertyChanged(nameof(LocalizedButtons));
            OnPropertyChanged(nameof(LocalizedHeaders));
            OnPropertyChanged(nameof(LocalizedPresets));
            OnPropertyChanged(nameof(LocalizedApps));
            OnPropertyChanged(nameof(LocalizedSystemFeatures));
            OnPropertyChanged(nameof(LocalizedSystemRequirements));
            OnPropertyChanged(nameof(LocalizedInstallationProcess));
            OnPropertyChanged(nameof(LocalizedDeepCleanup));
            OnPropertyChanged(nameof(LocalizedTooltips));
            
            // StatusText'i güncelle
            if (!CanStartBuild())
            {
                CanStartBuild(); // Bu metod StatusText'i günceller
            }
            else
            {
                StatusText = GetLocalizedString("Ready");
            }
            
            // Log'a dil değişikliği mesajı ekle
            var message = GetLocalizedString(languageCode == "tr-TR" ? "LanguageChangedTR" : "LanguageChangedEN");
            LogOutput += $"{message}\n";
            
            // Dialog'ları da güncelle
            UpdateDialogTexts();
        }

        private void UpdateStartupMessage()
        {
            LogOutput = GetLocalizedString("AppStarted") + "\n";
            if (IsRunningAsAdministrator())
            {
                LogOutput += GetLocalizedString("AdminMode") + "\n";
            }
            else
            {
                LogOutput += GetLocalizedString("UserMode") + "\n";
                LogOutput += GetLocalizedString("AdminRecommendation") + "\n";
            }
            LogOutput += GetLocalizedString("DefaultWorkingDir") + ": " + ScratchPath + "\n\n";
        }

        private void UpdateDialogTexts()
        {
            // Dialog başlıklarını güncelleme burada yapılacak
            // Şu an için boş bırakıyoruz
        }

        public string GetLocalizedString(string key) => _localizationService.GetString(key);
        public string GetLocalizedString(string key, params object[] args) => _localizationService.GetString(key, args);

        // Localized UI Properties
        public LocalizedLabels LocalizedLabels => new LocalizedLabels(_localizationService);
        public LocalizedButtons LocalizedButtons => new LocalizedButtons(_localizationService);
        public LocalizedHeaders LocalizedHeaders => new LocalizedHeaders(_localizationService);
        public LocalizedPresets LocalizedPresets => new LocalizedPresets(_localizationService);
        public LocalizedApps LocalizedApps => new LocalizedApps(_localizationService);
        public LocalizedSystemFeatures LocalizedSystemFeatures => new LocalizedSystemFeatures(_localizationService);
        public LocalizedSystemRequirements LocalizedSystemRequirements => new LocalizedSystemRequirements(_localizationService);
        public LocalizedInstallationProcess LocalizedInstallationProcess => new LocalizedInstallationProcess(_localizationService);
        public LocalizedDeepCleanup LocalizedDeepCleanup => new LocalizedDeepCleanup(_localizationService);
        public LocalizedTooltips LocalizedTooltips => new LocalizedTooltips(_localizationService);

        #endregion
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}