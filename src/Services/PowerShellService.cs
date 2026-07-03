using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tiny11_ui.Models;

namespace tiny11_ui.Services
{
    public class PowerShellService
    {
        public event Action<string>? OutputReceived;
        public event Action<string>? ErrorReceived;
        public event Action<int>? ProcessCompleted;

        private Process? _currentProcess;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning;
        private string? _currentMountDir;
        private string? _currentIsoPath;
        private string? _currentScratchPath;
        private readonly LocalizationService _localizationService;

        public PowerShellService(LocalizationService localizationService)
        {
            _localizationService = localizationService;
        }

        private string GetLocalizedString(string key) => _localizationService.GetString(key);

        /// <summary>
        /// İşlem çalışıyor mu?
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Çalışan işlemi iptal eder ve temizlik yapar
        /// </summary>
        public async Task CancelAsync()
        {
            if (!_isRunning) return;

            OutputReceived?.Invoke("\n" + GetLocalizedString("LogCancelRequested") + "\n");

            try
            {
                // CancellationToken'ı tetikle
                _cancellationTokenSource?.Cancel();

                // Çalışan process'i sonlandır
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    try
                    {
                        _currentProcess.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        try { _currentProcess.Kill(); } catch { }
                    }
                    await Task.Delay(1000);
                }

                // Temizlik yap
                await CleanupAfterCancelAsync();

                OutputReceived?.Invoke(GetLocalizedString("LogCancelComplete") + "\n");
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCancelError"), ex.Message) + "\n");
            }
            finally
            {
                _isRunning = false;
                _currentProcess = null;
            }
        }

        /// <summary>
        /// İptal sonrası temizlik işlemleri
        /// </summary>
        private async Task CleanupAfterCancelAsync()
        {
            OutputReceived?.Invoke(GetLocalizedString("LogCancelCleanup") + "\n");

            // Mount edilmiş WIM'i unmount et (discard - değişiklikleri atarak)
            if (!string.IsNullOrEmpty(_currentMountDir) && Directory.Exists(_currentMountDir))
            {
                OutputReceived?.Invoke("   " + GetLocalizedString("LogWimUnmounting") + "\n");
                await RunCleanupCommandAsync($"dism /unmount-wim /mountdir:\"{_currentMountDir}\" /discard");
            }

            // Mount edilmiş ISO'yu unmount et
            if (!string.IsNullOrEmpty(_currentIsoPath))
            {
                OutputReceived?.Invoke("   " + GetLocalizedString("LogIsoUnmounting") + "\n");
                await RunCleanupCommandAsync($"powershell -Command \"Dismount-DiskImage -ImagePath '{_currentIsoPath}' -ErrorAction SilentlyContinue\"");
            }

            // Registry hive'larını unload et
            OutputReceived?.Invoke("   " + GetLocalizedString("LogRegistryUnloading") + "\n");
            await RunCleanupCommandAsync("reg unload HKLM\\OFFLINE_SOFTWARE 2>nul");
            await RunCleanupCommandAsync("reg unload HKLM\\OFFLINE_SYSTEM 2>nul");
            await RunCleanupCommandAsync("reg unload HKU\\OFFLINE_NTUSER 2>nul");

            // Geçici dosyaları temizle
            if (!string.IsNullOrEmpty(_currentScratchPath))
            {
                OutputReceived?.Invoke("   " + GetLocalizedString("LogTempFilesDeleting") + "\n");
                try
                {
                    var workDir = Path.Combine(_currentScratchPath, "tiny11_work");
                    var mountDir = Path.Combine(_currentScratchPath, "tiny11_mount");
                    var isoDir = Path.Combine(_currentScratchPath, "tiny11_iso");

                    if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
                    if (Directory.Exists(mountDir)) Directory.Delete(mountDir, true);
                    if (Directory.Exists(isoDir)) Directory.Delete(isoDir, true);
                }
                catch (Exception ex)
                {
                    OutputReceived?.Invoke("   " + string.Format(GetLocalizedString("LogTempFilesError"), ex.Message) + "\n");
                }
            }
        }

        /// <summary>
        /// Temizlik komutu çalıştırır
        /// </summary>
        private async Task RunCleanupCommandAsync(string command)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {command}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                
                // 30 saniye timeout
                var completed = await Task.Run(() => process.WaitForExit(30000));
                if (!completed)
                {
                    try { process.Kill(); } catch { }
                }
            }
            catch { /* Temizlik hatalarını yoksay */ }
        }

        /// <summary>
        /// Kullanıcı seçeneklerine göre özelleştirilmiş Tiny11 oluşturma işlemi
        /// </summary>
        public async Task<bool> RunTiny11WithOptionsAsync(
            string isoPath, 
            string scratchPath, 
            string outputPath, 
            int editionIndex, 
            ComponentRemovalOptions options,
            bool isCoreVersion = false)
        {
            if (_isRunning)
            {
                OutputReceived?.Invoke(GetLocalizedString("LogAlreadyRunning") + "\n");
                return false;
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _currentIsoPath = isoPath;
            _currentScratchPath = scratchPath;
            _currentMountDir = Path.Combine(scratchPath, "tiny11_mount");

            try
            {
                // Önce kapsamlı cleanup yap
                await ComprehensiveCleanupAsync();

                OutputReceived?.Invoke(GetLocalizedString("LogTiny11Starting"));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogIsoPath"), isoPath));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogWorkingDir"), scratchPath));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogEditionIndex"), editionIndex));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogOutputPath"), outputPath));
                OutputReceived?.Invoke("");

                // Seçeneklere göre dinamik PowerShell scripti oluştur
                var script = GenerateTiny11Script(isoPath, scratchPath, outputPath, editionIndex, options);

                // Scripti geçici dosyaya yaz
                var tempScriptPath = Path.Combine(Path.GetTempPath(), $"tiny11_custom_{DateTime.Now:yyyyMMddHHmmss}.ps1");
                await File.WriteAllTextAsync(tempScriptPath, script, Encoding.UTF8);

                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogScriptCreated"), tempScriptPath));
                OutputReceived?.Invoke("");

                // Scripti çalıştır (iptal kontrolü ile)
                var success = await RunPowerShellScriptFileAsync(tempScriptPath, _cancellationTokenSource.Token);

                // Geçici scripti temizle
                try
                {
                    if (File.Exists(tempScriptPath))
                        File.Delete(tempScriptPath);
                }
                catch { /* Ignore cleanup errors */ }

                return success;
            }
            catch (OperationCanceledException)
            {
                OutputReceived?.Invoke("\n" + GetLocalizedString("LogCancelByUser") + "\n");
                return false;
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogError"), ex.Message));
                await CleanupEnvironmentAsync();
                return false;
            }
            finally
            {
                _isRunning = false;
                _currentProcess = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// Oluşturulacak script'i önizleme için döndürür (çalıştırmadan)
        /// </summary>
        public string PreviewScript(string isoPath, string scratchPath, string outputPath, 
            int editionIndex, ComponentRemovalOptions options, bool isCoreBuild = false)
        {
            return GenerateTiny11Script(isoPath, scratchPath, outputPath, editionIndex, options);
        }

        /// <summary>
        /// Kullanıcı seçeneklerine göre dinamik PowerShell scripti oluşturur
        /// </summary>
        private string GenerateTiny11Script(string isoPath, string scratchPath, string outputPath, int editionIndex, ComponentRemovalOptions options)
        {
            var sb = new StringBuilder();

            // Script başlangıcı
            sb.AppendLine(@"# Tiny11 Builder - Custom Script");
            sb.AppendLine(@"# Generated by tiny11-ui");
            sb.AppendLine(@"# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine(@"$ErrorActionPreference = 'Continue'");
            sb.AppendLine();

            // Değişkenler
            sb.AppendLine($@"$isoPath = '{isoPath.Replace("'", "''")}'");
            sb.AppendLine($@"$scratchPath = '{scratchPath.Replace("'", "''")}'");
            sb.AppendLine($@"$outputPath = '{outputPath.Replace("'", "''")}'");
            sb.AppendLine($@"$editionIndex = {editionIndex}");
            sb.AppendLine();

            // Dizin hazırlama - Her zaman benzersiz dizin kullan
            sb.AppendLine(@"# Benzersiz çalışma dizinleri oluştur (önceki mount sorunlarını önlemek için)");
            sb.AppendLine(@"$timestamp = Get-Date -Format 'yyyyMMddHHmmss'");
            sb.AppendLine(@"$workDir = Join-Path $scratchPath ""tiny11_work_$timestamp""");
            sb.AppendLine(@"$mountDir = Join-Path $scratchPath ""tiny11_mount_$timestamp""");
            sb.AppendLine(@"$isoDir = Join-Path $scratchPath ""tiny11_iso_$timestamp""");
            sb.AppendLine();

            // Eski mount'ları temizlemeye çalış (arka planda, hata olursa devam et)
            sb.AppendLine(@"# Eski mount'ları temizlemeye çalış (başarısız olursa sorun değil - yeni dizin kullanıyoruz)");
            sb.AppendLine(@"Write-Host 'Checking for stale mounts...' -ForegroundColor Yellow");
            sb.AppendLine();
            sb.AppendLine(@"# Registry hive'larını unload et");
            sb.AppendLine(@"reg unload 'HKLM\OFFLINE_SOFTWARE' 2>$null");
            sb.AppendLine(@"reg unload 'HKLM\OFFLINE_SYSTEM' 2>$null");
            sb.AppendLine(@"reg unload 'HKU\OFFLINE_NTUSER' 2>$null");
            sb.AppendLine(@"[gc]::Collect()");
            sb.AppendLine();
            sb.AppendLine(@"# DISM cleanup (arka planda)");
            sb.AppendLine(@"$dismCleanup = Start-Job -ScriptBlock { dism /cleanup-wim 2>$null }");
            sb.AppendLine(@"Wait-Job $dismCleanup -Timeout 30 | Out-Null");
            sb.AppendLine(@"Remove-Job $dismCleanup -Force -ErrorAction SilentlyContinue");
            sb.AppendLine();
            
            sb.AppendLine(@"# ISO'yu unmount et (önceden mount edilmişse)");
            sb.AppendLine(@"try {");
            sb.AppendLine(@"    Dismount-DiskImage -ImagePath $isoPath -ErrorAction SilentlyContinue");
            sb.AppendLine(@"} catch { }");
            sb.AppendLine();

            // Yeni dizinleri oluştur
            sb.AppendLine(@"Write-Host 'Preparing directories...' -ForegroundColor Cyan");
            sb.AppendLine(@"Write-Host ""   Work: $workDir"" -ForegroundColor Gray");
            sb.AppendLine(@"Write-Host ""   Mount: $mountDir"" -ForegroundColor Gray");
            sb.AppendLine(@"Write-Host ""   ISO: $isoDir"" -ForegroundColor Gray");
            sb.AppendLine(@"New-Item -ItemType Directory -Force -Path $workDir | Out-Null");
            sb.AppendLine(@"New-Item -ItemType Directory -Force -Path $mountDir | Out-Null");
            sb.AppendLine(@"New-Item -ItemType Directory -Force -Path $isoDir | Out-Null");
            sb.AppendLine();

            // ISO mount
            sb.AppendLine(@"# ISO'yu mount et");
            sb.AppendLine(@"Write-Host 'Mounting ISO...' -ForegroundColor Cyan");
            sb.AppendLine(@"$mountResult = Mount-DiskImage -ImagePath $isoPath -PassThru");
            sb.AppendLine(@"$driveLetter = ($mountResult | Get-Volume).DriveLetter + ':'");
            sb.AppendLine(@"Write-Host ""Mounted drive: $driveLetter"" -ForegroundColor Green");
            sb.AppendLine();

            // ISO içeriğini kopyala
            sb.AppendLine(@"# ISO içeriğini kopyala");
            sb.AppendLine(@"Write-Host 'Copying ISO content...' -ForegroundColor Cyan");
            sb.AppendLine(@"Copy-Item -Path ""$driveLetter\*"" -Destination $isoDir -Recurse -Force");
            sb.AppendLine();

            // WIM/ESD dosyasını bul
            sb.AppendLine(@"# WIM dosyasını bul");
            sb.AppendLine(@"$wimPath = Join-Path $isoDir 'sources\install.wim'");
            sb.AppendLine(@"$esdPath = Join-Path $isoDir 'sources\install.esd'");
            sb.AppendLine(@"if (Test-Path $esdPath) {");
            sb.AppendLine(@"    Write-Host 'Converting ESD -> WIM...' -ForegroundColor Cyan");
            sb.AppendLine(@"    dism /export-image /sourceimagefile:$esdPath /sourceindex:$editionIndex /destinationimagefile:$wimPath /compress:max");
            sb.AppendLine(@"    Remove-Item $esdPath -Force");
            sb.AppendLine(@"} elseif (!(Test-Path $wimPath)) {");
            sb.AppendLine(@"    throw 'install.wim or install.esd not found!'");
            sb.AppendLine(@"}");
            sb.AppendLine();

            // WIM dosyasını mount et - daha güvenli
            sb.AppendLine(@"# WIM dosyasını mount et");
            sb.AppendLine(@"Write-Host 'Mounting Windows image...' -ForegroundColor Cyan");
            sb.AppendLine(@"Set-ItemProperty -Path $wimPath -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue");
            sb.AppendLine();
            
            // Mount işlemi - basit ve direkt
            sb.AppendLine(@"# Mount işlemi");
            sb.AppendLine(@"Write-Host ""   Mounting to: $mountDir"" -ForegroundColor Gray");
            sb.AppendLine(@"$mountResult = dism /mount-wim /wimfile:$wimPath /index:$editionIndex /mountdir:$mountDir 2>&1");
            sb.AppendLine();
            sb.AppendLine(@"if ($LASTEXITCODE -ne 0) {");
            sb.AppendLine(@"    Write-Host 'Mount failed, error details:' -ForegroundColor Red");
            sb.AppendLine(@"    Write-Host $mountResult -ForegroundColor Yellow");
            sb.AppendLine(@"    Write-Host ''");
            sb.AppendLine(@"    Write-Host 'Attempting recovery...' -ForegroundColor Yellow");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    # Cleanup-wim dene");
            sb.AppendLine(@"    dism /cleanup-wim 2>$null");
            sb.AppendLine(@"    Start-Sleep -Seconds 3");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    # Yeni dizin ile tekrar dene");
            sb.AppendLine(@"    $retryTimestamp = Get-Date -Format 'yyyyMMddHHmmss'");
            sb.AppendLine(@"    $mountDir = Join-Path $scratchPath ""tiny11_mount_retry_$retryTimestamp""");
            sb.AppendLine(@"    New-Item -ItemType Directory -Force -Path $mountDir | Out-Null");
            sb.AppendLine(@"    Write-Host ""   Retrying with: $mountDir"" -ForegroundColor Gray");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    $mountResult = dism /mount-wim /wimfile:$wimPath /index:$editionIndex /mountdir:$mountDir 2>&1");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    if ($LASTEXITCODE -ne 0) {");
            sb.AppendLine(@"        Write-Host 'ERROR: Failed to mount WIM image!' -ForegroundColor Red");
            sb.AppendLine(@"        Write-Host 'Please restart your computer and try again.' -ForegroundColor Yellow");
            sb.AppendLine(@"        throw 'Failed to mount WIM image'");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"}");
            sb.AppendLine(@"Write-Host 'Image mounted successfully' -ForegroundColor Green");
            sb.AppendLine();

            // Paket kaldırma listesi
            var packagesToRemove = options.GetPackagesToRemove();
            if (packagesToRemove.Length > 0)
            {
                sb.AppendLine(@"# Gereksiz uygulamaları kaldır");
                sb.AppendLine(@"Write-Host 'Removing unnecessary applications...' -ForegroundColor Cyan");
                sb.AppendLine(@"$packagesToRemove = @(");
                foreach (var package in packagesToRemove)
                {
                    sb.AppendLine($@"    '{package}'");
                }
                sb.AppendLine(@")");
                sb.AppendLine();

                sb.AppendLine(@"$installedPackages = Get-AppxProvisionedPackage -Path $mountDir | Select-Object -ExpandProperty PackageName");
                sb.AppendLine(@"foreach ($package in $packagesToRemove) {");
                sb.AppendLine(@"    $matchingPackages = $installedPackages | Where-Object { $_ -like ""*$package*"" }");
                sb.AppendLine(@"    foreach ($match in $matchingPackages) {");
                sb.AppendLine(@"        try {");
                sb.AppendLine(@"            Write-Host ""   Removing: $match"" -ForegroundColor Yellow");
                sb.AppendLine(@"            Remove-AppxProvisionedPackage -Path $mountDir -PackageName $match -ErrorAction SilentlyContinue | Out-Null");
                sb.AppendLine(@"        } catch {");
                sb.AppendLine(@"            Write-Host ""   Skipped: $match"" -ForegroundColor Gray");;
                sb.AppendLine(@"        }");
                sb.AppendLine(@"    }");
                sb.AppendLine(@"}");
                sb.AppendLine();
            }

            // Edge kaldırma (özel işlem gerektirir)
            if (options.RemoveEdge)
            {
                sb.AppendLine(@"# Microsoft Edge'i kaldır");
                sb.AppendLine(@"Write-Host 'Removing Microsoft Edge...' -ForegroundColor Cyan");
                sb.AppendLine(@"$edgePaths = @(");
                sb.AppendLine(@"    ""$mountDir\Program Files (x86)\Microsoft\Edge""");
                sb.AppendLine(@"    ""$mountDir\Program Files (x86)\Microsoft\EdgeUpdate""");
                sb.AppendLine(@"    ""$mountDir\Program Files (x86)\Microsoft\EdgeCore""");
                sb.AppendLine(@")");
                sb.AppendLine(@"foreach ($path in $edgePaths) {");
                sb.AppendLine(@"    if (Test-Path $path) {");
                sb.AppendLine(@"        Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue");
                sb.AppendLine(@"        Write-Host ""   Deleted: $path"" -ForegroundColor Yellow");;
                sb.AppendLine(@"    }");
                sb.AppendLine(@"}");
                sb.AppendLine();
            }

            // OneDrive kaldırma
            if (options.RemoveOneDrive)
            {
                sb.AppendLine(@"# OneDrive setup dosyalarını kaldır");
                sb.AppendLine(@"Write-Host 'Removing OneDrive...' -ForegroundColor Cyan");
                sb.AppendLine(@"$onedrivePaths = @(");
                sb.AppendLine(@"    ""$mountDir\Windows\System32\OneDriveSetup.exe""");
                sb.AppendLine(@"    ""$mountDir\Windows\SysWOW64\OneDriveSetup.exe""");
                sb.AppendLine(@")");
                sb.AppendLine(@"foreach ($path in $onedrivePaths) {");
                sb.AppendLine(@"    if (Test-Path $path) {");
                sb.AppendLine(@"        Remove-Item -Path $path -Force -ErrorAction SilentlyContinue");
                sb.AppendLine(@"        Write-Host ""   Deleted: $path"" -ForegroundColor Yellow");
                sb.AppendLine(@"    }");
                sb.AppendLine(@"}");
                sb.AppendLine();
            }

            // Registry ayarları - Offline registry editing
            sb.AppendLine(@"# Registry ayarlarını uygula");
            sb.AppendLine(@"Write-Host 'Applying registry settings...' -ForegroundColor Cyan");
            sb.AppendLine();

            // Load registry hives - doğrudan path ile
            sb.AppendLine(@"$softwareHive = Join-Path $mountDir 'Windows\System32\config\SOFTWARE'");
            sb.AppendLine(@"$systemHive = Join-Path $mountDir 'Windows\System32\config\SYSTEM'");
            sb.AppendLine(@"$ntuserHive = Join-Path $mountDir 'Users\Default\NTUSER.DAT'");
            sb.AppendLine();
            sb.AppendLine(@"# Hive'ları yükle");
            sb.AppendLine(@"Write-Host '   Loading registry hives...' -ForegroundColor Gray");
            sb.AppendLine(@"$regLoadSw = Start-Process -FilePath 'reg.exe' -ArgumentList ""load `""HKLM\OFFLINE_SOFTWARE`"" `""$softwareHive`"""" -NoNewWindow -Wait -PassThru");
            sb.AppendLine(@"$regLoadSys = Start-Process -FilePath 'reg.exe' -ArgumentList ""load `""HKLM\OFFLINE_SYSTEM`"" `""$systemHive`"""" -NoNewWindow -Wait -PassThru");
            sb.AppendLine(@"$regLoadNt = Start-Process -FilePath 'reg.exe' -ArgumentList ""load `""HKU\OFFLINE_NTUSER`"" `""$ntuserHive`"""" -NoNewWindow -Wait -PassThru");
            sb.AppendLine(@"Start-Sleep -Seconds 1");
            sb.AppendLine();

            // Sistem gereksinimleri bypass
            if (options.BypassTPM || options.BypassCPU || options.BypassRAM || options.BypassSecureBoot)
            {
                sb.AppendLine(@"# Sistem gereksinimleri bypass");
                sb.AppendLine(@"Write-Host 'Bypassing system requirements...' -ForegroundColor Cyan");
                
                if (options.BypassTPM)
                    sb.AppendLine(@"reg add ""HKLM\OFFLINE_SYSTEM\Setup\LabConfig"" /v BypassTPMCheck /t REG_DWORD /d 1 /f 2>$null");
                
                if (options.BypassSecureBoot)
                    sb.AppendLine(@"reg add ""HKLM\OFFLINE_SYSTEM\Setup\LabConfig"" /v BypassSecureBootCheck /t REG_DWORD /d 1 /f 2>$null");
                
                if (options.BypassRAM)
                    sb.AppendLine(@"reg add ""HKLM\OFFLINE_SYSTEM\Setup\LabConfig"" /v BypassRAMCheck /t REG_DWORD /d 1 /f 2>$null");
                
                if (options.BypassCPU)
                    sb.AppendLine(@"reg add ""HKLM\OFFLINE_SYSTEM\Setup\LabConfig"" /v BypassCPUCheck /t REG_DWORD /d 1 /f 2>$null");
                
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SYSTEM\Setup\LabConfig"" /v BypassStorageCheck /t REG_DWORD /d 1 /f 2>$null");
                sb.AppendLine();
            }

            // Telemetri
            if (options.DisableTelemetry)
            {
                sb.AppendLine(@"# Telemetri devre dışı");
                sb.AppendLine(@"Write-Host 'Disabling telemetry...' -ForegroundColor Cyan");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SOFTWARE\Policies\Microsoft\Windows\DataCollection"" /v AllowTelemetry /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection"" /v AllowTelemetry /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine();
            }

            // Sponsored Apps
            if (options.DisableSponsoredApps)
            {
                sb.AppendLine(@"# Önerilen uygulamalar devre dışı");
                sb.AppendLine(@"Write-Host 'Disabling sponsored apps...' -ForegroundColor Cyan");
                sb.AppendLine(@"reg add ""HKU\OFFLINE_NTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SilentInstalledAppsEnabled /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine(@"reg add ""HKU\OFFLINE_NTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SystemPaneSuggestionsEnabled /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine(@"reg add ""HKU\OFFLINE_NTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SoftLandingEnabled /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine(@"reg add ""HKU\OFFLINE_NTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SubscribedContent-338388Enabled /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine(@"reg add ""HKU\OFFLINE_NTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SubscribedContent-338389Enabled /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine(@"reg add ""HKU\OFFLINE_NTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SubscribedContent-353694Enabled /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine(@"reg add ""HKU\OFFLINE_NTUSER\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SubscribedContent-353696Enabled /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine();
            }

            // Reserved Storage
            if (options.DisableReservedStorage)
            {
                sb.AppendLine(@"# Reserved Storage devre dışı");
                sb.AppendLine(@"Write-Host 'Disabling Reserved Storage...' -ForegroundColor Cyan");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager"" /v ShippedWithReserves /t REG_DWORD /d 0 /f 2>$null");
                sb.AppendLine();
            }

            // BitLocker
            if (options.DisableBitLocker)
            {
                sb.AppendLine(@"# BitLocker otomatik şifreleme devre dışı");
                sb.AppendLine(@"Write-Host 'Disabling BitLocker auto-encryption...' -ForegroundColor Cyan");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SYSTEM\CurrentControlSet\Control\BitLocker"" /v PreventDeviceEncryption /t REG_DWORD /d 1 /f 2>$null");
                sb.AppendLine();
            }

            // MS Account Bypass
            if (options.BypassMSAccount)
            {
                sb.AppendLine(@"# Microsoft hesabı bypass");
                sb.AppendLine(@"Write-Host 'Bypassing Microsoft account requirement...' -ForegroundColor Cyan");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE"" /v BypassNRO /t REG_DWORD /d 1 /f 2>$null");
                sb.AppendLine();
            }

            // Windows Update
            if (options.DisableWindowsUpdate)
            {
                sb.AppendLine(@"# Windows Update devre dışı");
                sb.AppendLine(@"Write-Host 'Disabling Windows Update...' -ForegroundColor Cyan");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"" /v NoAutoUpdate /t REG_DWORD /d 1 /f 2>$null");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU"" /v AUOptions /t REG_DWORD /d 2 /f 2>$null");
                sb.AppendLine();
            }

            // Windows Defender
            if (options.DisableDefender)
            {
                sb.AppendLine(@"# Windows Defender devre dışı (Önerilmez!)");
                sb.AppendLine(@"Write-Host 'Disabling Windows Defender (Security risk!)...' -ForegroundColor Red");
                sb.AppendLine(@"reg add ""HKLM\OFFLINE_SOFTWARE\Policies\Microsoft\Windows Defender"" /v DisableAntiSpyware /t REG_DWORD /d 1 /f 2>$null");
                sb.AppendLine();
            }

            // Derin temizlik / boyut küçültme (WIM hâlâ mount'lu iken yapılmalı)
            if (options.RemoveHyperV || options.RemoveRecall || options.RemoveInputComponents || options.CleanupDriverStore || options.CleanupComponentStore)
            {
                sb.AppendLine(@"# Derin temizlik (boyut küçültme)");
                sb.AppendLine(@"Write-Host 'Running deep cleanup...' -ForegroundColor Cyan");
                sb.AppendLine();
            }

            if (options.RemoveHyperV)
            {
                sb.AppendLine(@"Write-Host '   Removing Hyper-V...' -ForegroundColor Yellow");
                sb.AppendLine(@"dism /image:$mountDir /Disable-Feature /FeatureName:Microsoft-Hyper-V-All /Remove /NoRestart 2>$null | Out-Null");
                sb.AppendLine();
            }

            if (options.RemoveRecall)
            {
                sb.AppendLine(@"Write-Host '   Removing Windows Recall...' -ForegroundColor Yellow");
                sb.AppendLine(@"try {");
                sb.AppendLine(@"    Get-WindowsCapability -Path $mountDir | Where-Object { $_.Name -like 'Recall*' } | ForEach-Object {");
                sb.AppendLine(@"        Remove-WindowsCapability -Path $mountDir -Name $_.Name -ErrorAction SilentlyContinue | Out-Null");
                sb.AppendLine(@"    }");
                sb.AppendLine(@"} catch { }");
                sb.AppendLine();
            }

            if (options.RemoveInputComponents)
            {
                sb.AppendLine(@"Write-Host '   Removing Speech/OCR/Handwriting components...' -ForegroundColor Yellow");
                sb.AppendLine(@"try {");
                sb.AppendLine(@"    Get-WindowsCapability -Path $mountDir | Where-Object { $_.Name -like 'Language.Speech*' -or $_.Name -like 'Language.OCR*' -or $_.Name -like 'Language.Handwriting*' -or $_.Name -like 'Language.TextToSpeech*' } | ForEach-Object {");
                sb.AppendLine(@"        Remove-WindowsCapability -Path $mountDir -Name $_.Name -ErrorAction SilentlyContinue | Out-Null");
                sb.AppendLine(@"    }");
                sb.AppendLine(@"} catch { }");
                sb.AppendLine();
            }

            if (options.CleanupDriverStore)
            {
                sb.AppendLine(@"Write-Host '   Removing unused driver packages (printer/scanner/modem/Xbox)...' -ForegroundColor Yellow");
                sb.AppendLine(@"try {");
                sb.AppendLine(@"    Get-WindowsDriver -Path $mountDir | Where-Object { $_.ClassName -in @('Printer','PrinterQueue','Image','Modem','XboxComposite') } | ForEach-Object {");
                sb.AppendLine(@"        Remove-WindowsDriver -Path $mountDir -Driver $_.Driver -ErrorAction SilentlyContinue | Out-Null");
                sb.AppendLine(@"    }");
                sb.AppendLine(@"} catch { }");
                sb.AppendLine();
            }

            if (options.CleanupComponentStore)
            {
                sb.AppendLine(@"Write-Host '   Cleaning up component store (WinSxS, this can take several minutes)...' -ForegroundColor Yellow");
                sb.AppendLine(@"dism /image:$mountDir /Cleanup-Image /StartComponentCleanup /ResetBase");
                sb.AppendLine();
            }

            // Unload registry hives
            sb.AppendLine(@"# Registry hive'larını kaldır");
            sb.AppendLine(@"Write-Host '   Unloading registry hives...' -ForegroundColor Gray");
            sb.AppendLine(@"[gc]::Collect()");
            sb.AppendLine(@"Start-Sleep -Seconds 2");
            sb.AppendLine(@"reg unload ""HKLM\OFFLINE_SOFTWARE"" 2>$null");
            sb.AppendLine(@"reg unload ""HKLM\OFFLINE_SYSTEM"" 2>$null");
            sb.AppendLine(@"reg unload ""HKU\OFFLINE_NTUSER"" 2>$null");
            sb.AppendLine();

            // BypassNRO script oluştur (MS hesabı bypass için autounattend)
            if (options.BypassMSAccount || options.SkipNetworkConnection)
            {
                sb.AppendLine(@"# OOBE bypass scripti oluştur");
                sb.AppendLine(@"Write-Host 'Creating OOBE bypass script...' -ForegroundColor Cyan");
                sb.AppendLine(@"$bypassScript = @'");
                sb.AppendLine(@"@echo off");
                sb.AppendLine(@"reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE /v BypassNRO /t REG_DWORD /d 1 /f");
                sb.AppendLine(@"shutdown /r /t 0");
                sb.AppendLine(@"'@");
                sb.AppendLine(@"$bypassScriptPath = ""$mountDir\Windows\System32\bypassnro.cmd""");
                sb.AppendLine(@"Set-Content -Path $bypassScriptPath -Value $bypassScript -Encoding ASCII");
                sb.AppendLine();
            }

            // WIM unmount
            sb.AppendLine(@"# Image'ı kaydet ve unmount et");
            sb.AppendLine(@"Write-Host 'Saving changes...' -ForegroundColor Cyan");
            sb.AppendLine(@"dism /unmount-wim /mountdir:$mountDir /commit");
            sb.AppendLine();

            // ISO unmount
            sb.AppendLine(@"# Kaynak ISO'yu unmount et");
            sb.AppendLine(@"Write-Host 'Unmounting source ISO...' -ForegroundColor Cyan");
            sb.AppendLine(@"Dismount-DiskImage -ImagePath $isoPath -ErrorAction SilentlyContinue");
            sb.AppendLine();

            // Görüntüyü sıkıştır (Recovery compression) - boyutu belirgin şekilde azaltır
            if (options.CompressFinalImage)
            {
                sb.AppendLine(@"# Görüntüyü sıkıştır (Recovery compression)");
                sb.AppendLine(@"Write-Host 'Compressing final image (recovery compression)...' -ForegroundColor Cyan");
                sb.AppendLine(@"try {");
                sb.AppendLine(@"    $compressedWimPath = Join-Path $isoDir 'sources\install_compressed.wim'");
                sb.AppendLine(@"    Export-WindowsImage -SourceImagePath $wimPath -SourceIndex $editionIndex -DestinationImagePath $compressedWimPath -CompressionType Recovery -ErrorAction Stop");
                sb.AppendLine(@"    Remove-Item $wimPath -Force");
                sb.AppendLine(@"    Rename-Item $compressedWimPath 'install.wim'");
                sb.AppendLine(@"    Write-Host '   Image compressed successfully' -ForegroundColor Green");
                sb.AppendLine(@"} catch {");
                sb.AppendLine(@"    Write-Host ""   Compression failed, keeping uncompressed image: $($_.Exception.Message)"" -ForegroundColor Yellow");
                sb.AppendLine(@"}");
                sb.AppendLine();
            }

            // Oscdimg ile ISO oluştur
            sb.AppendLine(@"# Yeni ISO oluştur");
            sb.AppendLine(@"Write-Host 'Creating Tiny11 ISO...' -ForegroundColor Cyan");
            sb.AppendLine();
            sb.AppendLine(@"# oscdimg yolunu bul");
            sb.AppendLine(@"$oscdimgPath = ''");
            sb.AppendLine();
            sb.AppendLine(@"# First, check if oscdimg.exe is available in PATH");
            sb.AppendLine(@"$oscdimgInPath = Get-Command 'oscdimg.exe' -ErrorAction SilentlyContinue");
            sb.AppendLine(@"if ($oscdimgInPath) {");
            sb.AppendLine(@"    $oscdimgPath = $oscdimgInPath.Source");
            sb.AppendLine(@"    Write-Host ""Found oscdimg.exe in PATH: $oscdimgPath"" -ForegroundColor Green");
            sb.AppendLine(@"}");
            sb.AppendLine();
            sb.AppendLine(@"# If not in PATH, check common ADK installation paths");
            sb.AppendLine(@"if ($oscdimgPath -eq '') {");
            sb.AppendLine(@"    $possiblePaths = @(");
            sb.AppendLine(@"        'C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\oscdimg.exe'");
            sb.AppendLine(@"        'C:\Program Files\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\oscdimg.exe'");
            sb.AppendLine(@"        Join-Path $PSScriptRoot 'oscdimg.exe'");
            sb.AppendLine(@"    )");
            sb.AppendLine(@"    foreach ($path in $possiblePaths) {");
            sb.AppendLine(@"        if (Test-Path $path) {");
            sb.AppendLine(@"            $oscdimgPath = $path");
            sb.AppendLine(@"            Write-Host ""Found oscdimg.exe at: $oscdimgPath"" -ForegroundColor Green");
            sb.AppendLine(@"            break");
            sb.AppendLine(@"        }");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"}");
            sb.AppendLine();
            sb.AppendLine(@"if ($oscdimgPath -eq '') {");
            sb.AppendLine(@"    Write-Host 'oscdimg.exe not found! Please install Windows ADK or add oscdimg.exe to your PATH.' -ForegroundColor Red");
            sb.AppendLine(@"    Write-Host '   https://docs.microsoft.com/en-us/windows-hardware/get-started/adk-install' -ForegroundColor Yellow");
            sb.AppendLine(@"    exit 1");
            sb.AppendLine(@"}");
            sb.AppendLine();
            sb.AppendLine(@"Write-Host ""oscdimg found: $oscdimgPath"" -ForegroundColor Green");
            sb.AppendLine();
            sb.AppendLine(@"$bootData = '2#p0,e,b""' + $isoDir + '\boot\etfsboot.com""#pEF,e,b""' + $isoDir + '\efi\microsoft\boot\efisys.bin""'");
            sb.AppendLine(@"& $oscdimgPath -m -o -u2 -udfver102 -bootdata:$bootData -l""Tiny11"" $isoDir $outputPath");
            sb.AppendLine();

            // Temizlik
            sb.AppendLine(@"# Temizlik");
            sb.AppendLine(@"Write-Host 'Cleaning up...' -ForegroundColor Cyan");
            sb.AppendLine(@"Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"Remove-Item -Path $mountDir -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"Remove-Item -Path $isoDir -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine();

            // Sonuç
            sb.AppendLine(@"if (Test-Path $outputPath) {");
            sb.AppendLine(@"    $fileSize = (Get-Item $outputPath).Length / 1GB");
            sb.AppendLine(@"    Write-Host ""Tiny11 ISO created successfully!"" -ForegroundColor Green");
            sb.AppendLine(@"    Write-Host ""Location: $outputPath"" -ForegroundColor Green");
            sb.AppendLine(@"    Write-Host ""Size: $([math]::Round($fileSize, 2)) GB"" -ForegroundColor Green");
            sb.AppendLine(@"} else {");
            sb.AppendLine(@"    Write-Host ""ISO creation failed!"" -ForegroundColor Red");
            sb.AppendLine(@"    exit 1");
            sb.AppendLine(@"}");

            return sb.ToString();
        }

        /// <summary>
        /// PowerShell script dosyasını çalıştırır (iptal desteği ile)
        /// </summary>
        private async Task<bool> RunPowerShellScriptFileAsync(string scriptPath, CancellationToken cancellationToken = default)
        {
            try
            {
                var arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\"";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };

                _currentProcess = new Process { StartInfo = processInfo };

                _currentProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        OutputReceived?.Invoke(e.Data);
                };

                _currentProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // oscdimg progress mesajları stderr'e yazılıyor - bunları filtrele
                        var data = e.Data.Trim();
                        
                        // Progress mesajlarını (% complete, Scanning, Computing, Writing, Done, etc.) normal output olarak göster
                        if (data.Contains("% complete") || 
                            data.StartsWith("Scanning") || 
                            data.StartsWith("Computing") || 
                            data.StartsWith("Writing") ||
                            data.StartsWith("Image file is") ||
                            data.StartsWith("Storage optimization") ||
                            data.StartsWith("After optimization") ||
                            data.StartsWith("Space saved") ||
                            data == "Done." ||
                            string.IsNullOrWhiteSpace(data))
                        {
                            // Bu mesajları gösterme veya normal output olarak göster
                            // (çok fazla % complete mesajı olduğu için atlıyoruz)
                            if (!data.Contains("% complete"))
                            {
                                OutputReceived?.Invoke(data);
                            }
                        }
                        else
                        {
                            // Gerçek hatalar
                            ErrorReceived?.Invoke($"ERROR: {data}");
                        }
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                // İptal kontrolü ile bekle
                while (!_currentProcess.HasExited)
                {
                    // İptal istendi mi kontrol et
                    if (cancellationToken.IsCancellationRequested)
                    {
                        OutputReceived?.Invoke("⏹️ İptal isteği alındı...");
                        throw new OperationCanceledException(cancellationToken);
                    }
                    
                    await Task.Delay(100, CancellationToken.None);
                }

                return _currentProcess?.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                throw; // İptal exception'ını yukarı ilet
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogError"), ex.Message));
                return false;
            }
        }

        public async Task<bool> RunTiny11ScriptAsync(string scriptPath, string isoPath, string scratchPath, string outputPath, int editionIndex, bool isCoreVersion = false)
        {
            try
            {
                // Önce kapsamlı cleanup yap
                await ComprehensiveCleanupAsync();
                
                var scriptName = isCoreVersion ? "tiny11Coremaker.ps1" : "tiny11maker.ps1";
                var fullScriptPath = Path.Combine(scriptPath, scriptName);
                
                if (!File.Exists(fullScriptPath))
                {
                    ErrorReceived?.Invoke($"Script dosyası bulunamadı: {fullScriptPath}");
                    return false;
                }

                var arguments = $"-ExecutionPolicy Bypass -File \"{fullScriptPath}\"";
                
                OutputReceived?.Invoke("PowerShell betiği başlatılıyor...");
                OutputReceived?.Invoke($"Komut: powershell.exe {arguments}");
                OutputReceived?.Invoke($"Otomatik input: ISO={isoPath}, Scratch={scratchPath}, Index={editionIndex}");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = scriptPath
                };

                _currentProcess = new Process { StartInfo = processInfo };

                // Output ve Error event'lerini bağla
                _currentProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        OutputReceived?.Invoke(e.Data);
                    }
                };

                _currentProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        var data = e.Data.Trim();
                        
                        // oscdimg ve DISM progress mesajlarını filtrele
                        if (data.Contains("% complete") || 
                            data.StartsWith("Scanning") || 
                            data.StartsWith("Computing") || 
                            data.StartsWith("Writing") ||
                            data.StartsWith("Image file is") ||
                            data.StartsWith("Storage optimization") ||
                            data.StartsWith("After optimization") ||
                            data.StartsWith("Space saved") ||
                            data == "Done." ||
                            string.IsNullOrWhiteSpace(data))
                        {
                            // Progress mesajlarını atlıyoruz
                        }
                        else
                        {
                            ErrorReceived?.Invoke($"ERROR: {data}");
                        }
                    }
                };

                _currentProcess.EnableRaisingEvents = true;
                _currentProcess.Exited += (sender, e) =>
                {
                    var exitCode = _currentProcess?.ExitCode ?? -1;
                    ProcessCompleted?.Invoke(exitCode);
                };

                _currentProcess.Start();

                // Async output reading başlat
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();
                
                OutputReceived?.Invoke("📝 PowerShell process başlatıldı, input gönderiliyor...");
                
                // Input stream'e otomatik değerleri gönder
                var inputWriter = _currentProcess.StandardInput;
                
                // Drive letter input için
                OutputReceived?.Invoke($"📝 ISO path gönderiliyor: {isoPath}");
                await inputWriter.WriteLineAsync(isoPath);
                await inputWriter.FlushAsync();
                
                // Image index input için  
                OutputReceived?.Invoke($"📝 Edition index gönderiliyor: {editionIndex}");
                await inputWriter.WriteLineAsync(editionIndex.ToString());
                await inputWriter.FlushAsync();
                
                // Scratch disk input için
                OutputReceived?.Invoke($"📝 Scratch path gönderiliyor: {scratchPath}");
                await inputWriter.WriteLineAsync(scratchPath);
                await inputWriter.FlushAsync();
                
                OutputReceived?.Invoke("📝 Tüm input'lar gönderildi, script çalışıyor...");
                
                // Input stream'i kapat
                inputWriter.Close();

                // Timeout ile bekle (60 dakika - ISO oluşturma uzun sürer)
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(60));
                var processTask = Task.Run(() => _currentProcess.WaitForExit());
                
                var completedTask = await Task.WhenAny(processTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    OutputReceived?.Invoke("⚠️ İşlem zaman aşımına uğradı, temizlik yapılıyor...");
                    _currentProcess?.Kill();
                    await CleanupEnvironmentAsync();
                    return false;
                }
                else
                {
                    var exitCode = _currentProcess?.ExitCode ?? -1;
                    if (exitCode != 0)
                    {
                        OutputReceived?.Invoke(string.Format(GetLocalizedString("LogProcessFailed"), exitCode));
                        await CleanupEnvironmentAsync();
                        return false;
                    }
                    else
                    {
                        OutputReceived?.Invoke(GetLocalizedString("LogProcessSuccess"));
                        
                        // Varsayılan tiny11.iso dosyasını kullanıcının istediği yere kopyala
                        var defaultOutputPath = Path.Combine(scriptPath, "tiny11.iso");
                        if (File.Exists(defaultOutputPath) && !string.IsNullOrEmpty(outputPath))
                        {
                            try
                            {
                                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogIsoCopying"), outputPath));
                                
                                // Hedef dizini oluştur
                                var outputDir = Path.GetDirectoryName(outputPath);
                                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                                {
                                    Directory.CreateDirectory(outputDir);
                                }
                                
                                File.Copy(defaultOutputPath, outputPath, true);
                                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogIsoSaved"), outputPath));
                            }
                            catch (Exception copyEx)
                            {
                                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogIsoCopyError"), copyEx.Message));
                            }
                        }
                        
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke($"PowerShell çalıştırma hatası: {ex.Message}");
                await CleanupEnvironmentAsync();
                return false;
            }
            finally
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
            }
        }



        public void StopProcess()
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill();
                    OutputReceived?.Invoke("İşlem kullanıcı tarafından durduruldu.");
                }
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke($"İşlem durdurma hatası: {ex.Message}");
            }
        }

        public async Task<string[]> GetWindowsEditionsAsync(string isoPath)
        {
            try
            {
                // ISO yolunu Base64 encode et - Türkçe karakter sorununu çözmek için
                var isoPathBytes = System.Text.Encoding.UTF8.GetBytes(isoPath);
                var base64IsoPath = Convert.ToBase64String(isoPathBytes);
                
                var script = @"
                    $ErrorActionPreference = 'Stop'
                    $base64Path = '" + base64IsoPath + @"'
                    try {
                        # Base64'ten ISO yolunu decode et
                        $isoPathBytes = [System.Convert]::FromBase64String($base64Path)
                        $isoPath = [System.Text.Encoding]::UTF8.GetString($isoPathBytes)
                        
                        Write-Host ""ISO mount ediliyor: $isoPath""
                        $mountResult = Mount-DiskImage -ImagePath $isoPath -PassThru
                        $driveLetter = ($mountResult | Get-Volume).DriveLetter + ':'
                        Write-Host ""Mount edilen surucu: $driveLetter""
                        
                        $wimPath = $null
                        $wimTestPath = $driveLetter + '\sources\install.wim'
                        $esdTestPath = $driveLetter + '\sources\install.esd'
                        
                        if (Test-Path $wimTestPath) {
                            $wimPath = $wimTestPath
                            Write-Host ""install.wim bulundu: $wimPath""
                        } elseif (Test-Path $esdTestPath) {
                            $wimPath = $esdTestPath
                            Write-Host ""install.esd bulundu: $wimPath""
                        } else {
                            throw 'Windows imaj dosyasi bulunamadi'
                        }
                        
                        # DISM kullanarak Windows sürümlerini listele
                        $images = Get-WindowsImage -ImagePath $wimPath
                        foreach ($image in $images) {
                            Write-Output ""INDEX:$($image.ImageIndex):NAME:$($image.ImageName):DRIVE:$driveLetter""
                        }
                        
                    } catch {
                        Write-Error $_.Exception.Message
                    } finally {
                        # ISO'yu unmount et
                        try {
                            $isoPathBytes = [System.Convert]::FromBase64String($base64Path)
                            $isoPath = [System.Text.Encoding]::UTF8.GetString($isoPathBytes)
                            Write-Host 'ISO unmount ediliyor...'
                            Dismount-DiskImage -ImagePath $isoPath -ErrorAction SilentlyContinue
                        } catch { }
                    }
                ";

                var result = await RunPowerShellCommandAsync(script);
                
                // Output'u parse et
                var editions = new List<string>();
                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                OutputReceived?.Invoke($"PowerShell output debug: {lines.Length} satır alındı");
                
                foreach (var line in lines)
                {
                    var cleanLine = line.Trim();
                    OutputReceived?.Invoke($"Parsing line: {cleanLine}");
                    
                    if (cleanLine.StartsWith("INDEX:") && cleanLine.Contains("NAME:") && cleanLine.Contains("DRIVE:"))
                    {
                        try
                        {
                            // FORMAT: INDEX:1:NAME:Windows 11 Home:DRIVE:D:
                            var parts = cleanLine.Split(':');
                            if (parts.Length >= 4)
                            {
                                var index = parts[1];
                                var nameStartIndex = cleanLine.IndexOf(":NAME:") + 6;
                                var driveStartIndex = cleanLine.IndexOf(":DRIVE:");
                                
                                if (nameStartIndex > 5 && driveStartIndex > nameStartIndex)
                                {
                                    var name = cleanLine.Substring(nameStartIndex, driveStartIndex - nameStartIndex);
                                    editions.Add($"{index} - {name}");
                                    OutputReceived?.Invoke($"Edition eklendi: {index} - {name}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            OutputReceived?.Invoke($"Parse hatası: {ex.Message} - Line: {cleanLine}");
                        }
                    }
                }

                if (editions.Count == 0)
                {
                    OutputReceived?.Invoke("Parse başarısız, tüm varsayılan sürümler ekleniyor...");
                    // Fallback: Tüm sürümleri ekle
                    editions.Add("1 - Windows 11 Home");
                    editions.Add("2 - Windows 11 Home Single Language");
                    editions.Add("3 - Windows 11 Education");
                    editions.Add("4 - Windows 11 Pro");
                    editions.Add("5 - Windows 11 Pro Education");
                    editions.Add("6 - Windows 11 Pro for Workstations");
                    
                    OutputReceived?.Invoke("Varsayılan Windows sürümleri yüklendi.");
                }
                else
                {
                    OutputReceived?.Invoke($"{editions.Count} Windows sürümü başarıyla parse edildi.");
                }

                return editions.ToArray();
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke($"Windows sürümleri alınamadı: {ex.Message}");
                
                // Hata durumunda tüm varsayılan sürümler döndür
                return new[] { 
                    "1 - Windows 11 Home",
                    "2 - Windows 11 Home Single Language", 
                    "3 - Windows 11 Education",
                    "4 - Windows 11 Pro",
                    "5 - Windows 11 Pro Education",
                    "6 - Windows 11 Pro for Workstations"
                };
            }
        }

        public async Task<string> MountIsoAndGetDriveLetterAsync(string isoPath)
        {
            try
            {
                var escapedIsoPath = isoPath.Replace("'", "''");
                var script = $@"
                    $mountResult = Mount-DiskImage -ImagePath '{escapedIsoPath}' -PassThru
                    $driveLetter = ($mountResult | Get-Volume).DriveLetter
                    Write-Output $driveLetter
                ";

                var result = await RunPowerShellCommandAsync(script);
                return result.Trim();
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke($"ISO mount hatası: {ex.Message}");
                return string.Empty;
            }
        }

        public async Task UnmountIsoAsync(string isoPath)
        {
            try
            {
                var escapedIsoPath = isoPath.Replace("'", "''");
                var script = $"Dismount-DiskImage -ImagePath '{escapedIsoPath}' -ErrorAction SilentlyContinue";
                await RunPowerShellCommandAsync(script);
                OutputReceived?.Invoke("ISO unmount edildi.");
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke($"ISO unmount hatası: {ex.Message}");
            }
        }

        private async Task<string> RunPowerShellCommandAsync(string command)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
                return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await Task.Run(() => process.WaitForExit());

            if (!string.IsNullOrEmpty(error))
                throw new Exception(error);

            return output;
        }

        private async Task CleanupEnvironmentAsync()
        {
            try
            {
                OutputReceived?.Invoke("🧹 Sistem temizliği yapılıyor...");
                
                // DISM cleanup
                var dismProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dism",
                        Arguments = "/cleanup-wim",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                dismProcess.Start();
                await dismProcess.WaitForExitAsync();
                
                // Mount edilmiş image'ları temizle
                var mountProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-Command \"Get-WindowsImage -Mounted | ForEach-Object { Dismount-WindowsImage -Path $_.Path -Discard }\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                mountProcess.Start();
                await mountProcess.WaitForExitAsync();
                
                OutputReceived?.Invoke(GetLocalizedString("LogSystemCleanupComplete"));
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupError"), ex.Message));
            }
        }

        private async Task ComprehensiveCleanupAsync()
        {
            try
            {
                OutputReceived?.Invoke(GetLocalizedString("LogComprehensiveCleanup"));
                
                // 1. Mevcut PowerShell processlerini sonlandır
                var processes = Process.GetProcessesByName("powershell");
                foreach (var proc in processes.Where(p => p.Id != Environment.ProcessId))
                {
                    try
                    {
                        if (proc.MainWindowTitle.Contains("tiny11") || 
                            proc.ProcessName.Contains("powershell"))
                        {
                            proc.Kill();
                            await proc.WaitForExitAsync();
                        }
                    }
                    catch { /* Ignore */ }
                }
                
                // 2. Mount edilmiş image'ları temizle
                await CleanupEnvironmentAsync();
                
                // 3. Dosya kilitleri için bekle
                await Task.Delay(2000);
                
                // 4. Çalışma dizinini temizle
                var workspacePath = @"C:\Users\berke\Documents\tiny11builder-workspace";
                var pathsToClean = new[]
                {
                    Path.Combine(workspacePath, "tiny11"),
                    Path.Combine(workspacePath, "scratchdir"),
                    Path.Combine(workspacePath, "tiny11.iso")
                };
                
                foreach (var path in pathsToClean)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                        else if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"), ex.Message));
                    }
                }
                
                OutputReceived?.Invoke(GetLocalizedString("LogComprehensiveCleanupComplete"));
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupError"), ex.Message));
            }
        }
    }
}