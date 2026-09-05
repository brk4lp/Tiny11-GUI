using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        private string? _currentWorkDir;
        private string? _currentMountDir;
        private string? _currentMountDirRetry;
        private string? _currentIsoDir;
        private string? _currentIsoPath;
        private string? _currentScratchPath;
        private string? _currentStatePath;
        private string? _currentRunId;
        private readonly LocalizationService _localizationService;

        private sealed class BuildRunState
        {
            public int SchemaVersion { get; set; } = 1;
            public string RunId { get; set; } = string.Empty;
            public string ScratchPath { get; set; } = string.Empty;
            public string? WorkDirectory { get; set; }
            public string? MountDirectory { get; set; }
            public string? RetryMountDirectory { get; set; }
            public string? IsoDirectory { get; set; }
            public string? IsoPath { get; set; }
            public int OwnerProcessId { get; set; }
            public DateTime OwnerProcessStartTimeUtc { get; set; }
            public int? PowerShellProcessId { get; set; }
            public DateTime? PowerShellProcessStartTimeUtc { get; set; }
        }

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
            // Mount, başarısız ilk denemeden sonra script içinde retry dizinine geçmiş olabilir,
            // bu yüzden ikisi de kontrol edilir.
            foreach (var mountDir in new[] { _currentMountDir, _currentMountDirRetry })
            {
                if (!string.IsNullOrEmpty(_currentScratchPath) &&
                    IsOwnedScratchDirectory(mountDir, _currentScratchPath, "tiny11_mount_") &&
                    Directory.Exists(mountDir))
                {
                    OutputReceived?.Invoke("   " + GetLocalizedString("LogWimUnmounting") + "\n");
                    await RunCleanupCommandAsync($"dism /unmount-wim /mountdir:\"{mountDir}\" /discard");
                }
            }

            // Mount edilmiş ISO'yu unmount et
            if (!string.IsNullOrEmpty(_currentIsoPath))
            {
                OutputReceived?.Invoke("   " + GetLocalizedString("LogIsoUnmounting") + "\n");
                await DismountTrackedIsoAsync(_currentIsoPath);
            }

            // Registry hive'larını unload et
            OutputReceived?.Invoke("   " + GetLocalizedString("LogRegistryUnloading") + "\n");
            await RunCleanupCommandAsync("reg unload HKLM\\OFFLINE_SOFTWARE 2>nul");
            await RunCleanupCommandAsync("reg unload HKLM\\OFFLINE_SYSTEM 2>nul");
            await RunCleanupCommandAsync("reg unload HKU\\OFFLINE_NTUSER 2>nul");

            // Geçici dosyaları temizle
            OutputReceived?.Invoke("   " + GetLocalizedString("LogTempFilesDeleting") + "\n");
            foreach (var directory in new[] { _currentWorkDir, _currentMountDir, _currentMountDirRetry, _currentIsoDir })
            {
                if (!string.IsNullOrEmpty(_currentScratchPath) &&
                    IsOwnedScratchDirectory(directory, _currentScratchPath,
                        "tiny11_work_", "tiny11_mount_", "tiny11_iso_"))
                {
                    TryDeleteOwnedDirectory(directory!);
                }
            }

            DeleteCurrentRunStateIfResourcesReleased();
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
            _currentScratchPath = Path.GetFullPath(scratchPath);

            // Timestamp burada üretilip script'e sabit değer olarak geçiriliyor; script kendi
            // $timestamp'ini üretseydi, bu sınıftaki dizin referansları gerçek dizinlerden sapardı.
            _currentRunId = Guid.NewGuid().ToString("N");
            var buildTimestamp = $"{DateTime.Now:yyyyMMddHHmmssfff}_{_currentRunId[..8]}";
            _currentWorkDir = Path.Combine(_currentScratchPath, $"tiny11_work_{buildTimestamp}");
            _currentMountDir = Path.Combine(_currentScratchPath, $"tiny11_mount_{buildTimestamp}");
            _currentMountDirRetry = Path.Combine(_currentScratchPath, $"tiny11_mount_retry_{buildTimestamp}");
            _currentIsoDir = Path.Combine(_currentScratchPath, $"tiny11_iso_{buildTimestamp}");
            _currentStatePath = Path.Combine(_currentScratchPath, $".tiny11-run-{_currentRunId}.json");
            string? tempScriptPath = null;

            try
            {
                // Yalnızca bu scratch dizininde önceki Tiny11 çalıştırmalarından kalan
                // sahipliği doğrulanmış kaynakları temizle.
                await ComprehensiveCleanupAsync(_currentScratchPath);
                await SaveCurrentRunStateAsync();

                OutputReceived?.Invoke(GetLocalizedString("LogTiny11Starting"));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogIsoPath"), isoPath));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogWorkingDir"), scratchPath));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogEditionIndex"), editionIndex));
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogOutputPath"), outputPath));
                OutputReceived?.Invoke("");

                // Seçeneklere göre dinamik PowerShell scripti oluştur
                var script = GenerateTiny11Script(isoPath, scratchPath, outputPath, editionIndex, options, buildTimestamp);

                // Scripti geçici dosyaya yaz
                tempScriptPath = Path.Combine(Path.GetTempPath(), $"tiny11_custom_{DateTime.Now:yyyyMMddHHmmss}_{_currentRunId}.ps1");
                await File.WriteAllTextAsync(tempScriptPath, script, Encoding.UTF8);

                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogScriptCreated"), tempScriptPath));
                OutputReceived?.Invoke("");

                // Scripti çalıştır (iptal kontrolü ile)
                var success = await RunPowerShellScriptFileAsync(tempScriptPath, _cancellationTokenSource.Token);

                if (success)
                {
                    DeleteCurrentRunState();
                }
                else
                {
                    await CleanupAfterCancelAsync();
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                OutputReceived?.Invoke("\n" + GetLocalizedString("LogCancelByUser") + "\n");
                await CleanupAfterCancelAsync();
                return false;
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogError"), ex.Message));
                await CleanupAfterCancelAsync();
                return false;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrEmpty(tempScriptPath) && File.Exists(tempScriptPath))
                        File.Delete(tempScriptPath);
                }
                catch { /* Ignore cleanup errors */ }

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
            return GenerateTiny11Script(isoPath, scratchPath, outputPath, editionIndex, options, DateTime.Now.ToString("yyyyMMddHHmmss"));
        }

        /// <summary>
        /// Kullanıcı seçeneklerine göre dinamik PowerShell scripti oluşturur
        /// </summary>
        private string GenerateTiny11Script(string isoPath, string scratchPath, string outputPath, int editionIndex, ComponentRemovalOptions options, string buildTimestamp)
        {
            var sb = new StringBuilder();

            // Script başlangıcı
            sb.AppendLine(@"# Tiny11 Builder - Custom Script");
            sb.AppendLine(@"# Generated by tiny11-ui");
            sb.AppendLine(@"# " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine(@"$ErrorActionPreference = 'Stop'");
            sb.AppendLine();

            // Değişkenler
            sb.AppendLine($@"$isoPath = '{isoPath.Replace("'", "''")}'");
            sb.AppendLine($@"$scratchPath = '{scratchPath.Replace("'", "''")}'");
            sb.AppendLine($@"$outputPath = '{outputPath.Replace("'", "''")}'");
            sb.AppendLine($@"$editionIndex = {editionIndex}");
            sb.AppendLine($@"$temporaryOutputPath = $outputPath + '.partial.{buildTimestamp}'");
            sb.AppendLine(@"$backupOutputPath = $outputPath + '.backup'");
            sb.AppendLine(@"$scriptExitCode = 0");
            sb.AppendLine(@"$buildSucceeded = $false");
            sb.AppendLine(@"$isoMounted = $false");
            sb.AppendLine(@"$wimMounted = $false");
            sb.AppendLine(@"$softwareHiveLoaded = $false");
            sb.AppendLine(@"$systemHiveLoaded = $false");
            sb.AppendLine(@"$ntuserHiveLoaded = $false");
            sb.AppendLine();
            sb.AppendLine(@"function Assert-NativeSuccess([string]$step) {");
            sb.AppendLine(@"    if ($LASTEXITCODE -ne 0) { throw ""$step failed with exit code $LASTEXITCODE"" }");
            sb.AppendLine(@"}");
            sb.AppendLine();

            // En güncel DISM'i bul - ADK'daki DISM, host işletim sisteminden daha yeni olabilir.
            // Eski bir DISM ile yeni bir Windows imajını servislemeye çalışmak (StartComponentCleanup,
            // Export-Image /Compress:recovery gibi işlemlerde) sessizce/anlamsız hatalarla başarısız olur.
            sb.AppendLine(@"# En güncel DISM'i bul (ADK varsa host'takinden daha yeni olabilir)");
            sb.AppendLine(@"$dismPath = 'dism'");
            sb.AppendLine(@"try {");
            sb.AppendLine(@"    $systemDismPath = Join-Path $env:SystemRoot 'System32\dism.exe'");
            sb.AppendLine(@"    $systemDismVersion = (Get-Item $systemDismPath).VersionInfo.FileVersionRaw");
            sb.AppendLine(@"    $adkDismCandidates = @(");
            sb.AppendLine(@"        'C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\DISM\dism.exe'");
            sb.AppendLine(@"        'C:\Program Files\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\DISM\dism.exe'");
            sb.AppendLine(@"    )");
            sb.AppendLine(@"    foreach ($candidate in $adkDismCandidates) {");
            sb.AppendLine(@"        if (Test-Path $candidate) {");
            sb.AppendLine(@"            $adkDismVersion = (Get-Item $candidate).VersionInfo.FileVersionRaw");
            sb.AppendLine(@"            if ($adkDismVersion -gt $systemDismVersion) {");
            sb.AppendLine(@"                $dismPath = $candidate");
            sb.AppendLine(@"                Write-Host ""Using newer DISM from ADK: $candidate ($adkDismVersion)"" -ForegroundColor Green");
            sb.AppendLine(@"            }");
            sb.AppendLine(@"            break");
            sb.AppendLine(@"        }");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"} catch { }");
            sb.AppendLine();

            // Dizin hazırlama - Her zaman benzersiz dizin kullan
            sb.AppendLine(@"# Benzersiz çalışma dizinleri oluştur (önceki mount sorunlarını önlemek için)");
            sb.AppendLine($@"$timestamp = '{buildTimestamp}'");
            sb.AppendLine(@"$workDir = Join-Path $scratchPath ""tiny11_work_$timestamp""");
            sb.AppendLine(@"$mountDir = Join-Path $scratchPath ""tiny11_mount_$timestamp""");
            sb.AppendLine(@"$isoDir = Join-Path $scratchPath ""tiny11_iso_$timestamp""");
            sb.AppendLine();
            sb.AppendLine(@"try {");

            // Bu çalışmanın kullanacağı adlandırılmış kaynakları hazırla. Global DISM cleanup
            // burada çalıştırılmaz; eski run'lar C# tarafındaki sahiplik tabanlı recovery ile temizlenir.
            sb.AppendLine(@"# Prepare Tiny11-owned resources");
            sb.AppendLine();
            sb.AppendLine(@"# Registry hive'larını unload et");
            sb.AppendLine(@"reg unload 'HKLM\OFFLINE_SOFTWARE' 2>$null");
            sb.AppendLine(@"reg unload 'HKLM\OFFLINE_SYSTEM' 2>$null");
            sb.AppendLine(@"reg unload 'HKU\OFFLINE_NTUSER' 2>$null");
            sb.AppendLine(@"[gc]::Collect()");
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
            sb.AppendLine(@"$driveBefore = [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'CDRom' } | ForEach-Object { $_.Name }");
            sb.AppendLine(@"$mountResult = Mount-DiskImage -ImagePath $isoPath -PassThru");
            sb.AppendLine(@"$isoMounted = $true");
            sb.AppendLine(@"$driveLetter = $null");
            sb.AppendLine(@"for ($i = 0; $i -lt 20; $i++) {");
            sb.AppendLine(@"    $candidate = [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'CDRom' -and $_.IsReady -and $driveBefore -notcontains $_.Name } | Select-Object -First 1");
            sb.AppendLine(@"    if ($candidate) { $driveLetter = $candidate.Name.TrimEnd('\'); break }");
            sb.AppendLine(@"    Start-Sleep -Milliseconds 500");
            sb.AppendLine(@"}");
            sb.AppendLine(@"if (-not $driveLetter) { throw 'Could not determine the drive letter of the mounted ISO' }");
            sb.AppendLine(@"Write-Host ""Mounted drive: $driveLetter"" -ForegroundColor Green");
            sb.AppendLine();

            // ISO içeriğini kopyala
            sb.AppendLine(@"# ISO içeriğini kopyala");
            sb.AppendLine(@"Write-Host 'Copying ISO content...' -ForegroundColor Cyan");
            sb.AppendLine(@"Copy-Item -Path ""$driveLetter\*"" -Destination $isoDir -Recurse -Force");
            sb.AppendLine();

            // Kullanıcının sağladığı özel autounattend.xml dosyasını ISO köküne kopyala
            if (!string.IsNullOrWhiteSpace(options.CustomAutounattendPath))
            {
                sb.AppendLine(@"# Özel autounattend.xml dosyasını kopyala");
                sb.AppendLine(@"Write-Host 'Copying custom autounattend.xml...' -ForegroundColor Cyan");
                sb.AppendLine($@"Copy-Item -Path '{options.CustomAutounattendPath.Replace("'", "''")}' -Destination (Join-Path $isoDir 'autounattend.xml') -Force");
                sb.AppendLine();
            }

            // WIM/ESD dosyasını bul
            sb.AppendLine(@"# WIM dosyasını bul");
            sb.AppendLine(@"$wimPath = Join-Path $isoDir 'sources\install.wim'");
            sb.AppendLine(@"$esdPath = Join-Path $isoDir 'sources\install.esd'");
            sb.AppendLine(@"if (Test-Path $esdPath) {");
            sb.AppendLine(@"    Write-Host 'Converting ESD -> WIM...' -ForegroundColor Cyan");
            sb.AppendLine(@"    & $dismPath /export-image /sourceimagefile:$esdPath /sourceindex:$editionIndex /destinationimagefile:$wimPath /compress:max");
            sb.AppendLine(@"    Assert-NativeSuccess 'ESD to WIM export'");
            sb.AppendLine(@"    if (!(Test-Path $wimPath)) { throw 'ESD export did not create install.wim' }");
            sb.AppendLine(@"    Remove-Item $esdPath -Force");
            sb.AppendLine(@"    # Export creates a single-image WIM, so its index is always 1.");
            sb.AppendLine(@"    $editionIndex = 1");
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
            sb.AppendLine(@"$mountResult = & $dismPath /mount-wim /wimfile:$wimPath /index:$editionIndex /mountdir:$mountDir 2>&1");
            sb.AppendLine();
            sb.AppendLine(@"if ($LASTEXITCODE -ne 0) {");
            sb.AppendLine(@"    Write-Host 'Mount failed, error details:' -ForegroundColor Red");
            sb.AppendLine(@"    Write-Host $mountResult -ForegroundColor Yellow");
            sb.AppendLine(@"    Write-Host ''");
            sb.AppendLine(@"    Write-Host 'Attempting recovery...' -ForegroundColor Yellow");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    # Yalnızca bu çalışmanın mount dizinini discard etmeyi dene");
            sb.AppendLine(@"    & $dismPath /unmount-wim /mountdir:$mountDir /discard 2>$null | Out-Null");
            sb.AppendLine(@"    Start-Sleep -Seconds 3");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    # Yeni dizin ile tekrar dene");
            sb.AppendLine($@"    $mountDir = Join-Path $scratchPath ""tiny11_mount_retry_{buildTimestamp}""");
            sb.AppendLine(@"    New-Item -ItemType Directory -Force -Path $mountDir | Out-Null");
            sb.AppendLine(@"    Write-Host ""   Retrying with: $mountDir"" -ForegroundColor Gray");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    $mountResult = & $dismPath /mount-wim /wimfile:$wimPath /index:$editionIndex /mountdir:$mountDir 2>&1");
            sb.AppendLine(@"    ");
            sb.AppendLine(@"    if ($LASTEXITCODE -ne 0) {");
            sb.AppendLine(@"        Write-Host 'ERROR: Failed to mount WIM image!' -ForegroundColor Red");
            sb.AppendLine(@"        Write-Host 'Please restart your computer and try again.' -ForegroundColor Yellow");
            sb.AppendLine(@"        throw 'Failed to mount WIM image'");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"}");
            sb.AppendLine(@"$wimMounted = $true");
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
            sb.AppendLine(@"if ($regLoadSw.ExitCode -ne 0) { throw ""Failed to load SOFTWARE registry hive (exit $($regLoadSw.ExitCode))"" }");
            sb.AppendLine(@"$softwareHiveLoaded = $true");
            sb.AppendLine(@"$regLoadSys = Start-Process -FilePath 'reg.exe' -ArgumentList ""load `""HKLM\OFFLINE_SYSTEM`"" `""$systemHive`"""" -NoNewWindow -Wait -PassThru");
            sb.AppendLine(@"if ($regLoadSys.ExitCode -ne 0) { throw ""Failed to load SYSTEM registry hive (exit $($regLoadSys.ExitCode))"" }");
            sb.AppendLine(@"$systemHiveLoaded = $true");
            sb.AppendLine(@"$regLoadNt = Start-Process -FilePath 'reg.exe' -ArgumentList ""load `""HKU\OFFLINE_NTUSER`"" `""$ntuserHive`"""" -NoNewWindow -Wait -PassThru");
            sb.AppendLine(@"if ($regLoadNt.ExitCode -ne 0) { throw ""Failed to load NTUSER registry hive (exit $($regLoadNt.ExitCode))"" }");
            sb.AppendLine(@"$ntuserHiveLoaded = $true");
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
                sb.AppendLine(@"& $dismPath /image:$mountDir /Disable-Feature /FeatureName:Microsoft-Hyper-V-All /Remove /NoRestart 2>$null | Out-Null");
                sb.AppendLine(@"Assert-NativeSuccess 'Hyper-V removal'");
                sb.AppendLine();
            }

            if (options.RemoveRecall || options.RemoveInputComponents || options.CleanupDriverStore)
            {
                // Get-WindowsCapability/Get-WindowsDriver PowerShell cmdlet'leri host işletim sisteminin
                // eski Dism modülüne bağımlı ve yeni Windows imajlarıyla sessizce başarısız olabiliyor.
                // Bunun yerine dism.exe konsol çıktısını parse eden fonksiyonlar kullanılır.
                sb.AppendLine(@"function Remove-MatchingCapabilities($Patterns) {");
                sb.AppendLine(@"    $capOutput = & $dismPath /Image:$mountDir /Get-Capabilities");
                sb.AppendLine(@"    Assert-NativeSuccess 'Capability inventory'");
                sb.AppendLine(@"    $names = $capOutput | Select-String 'Capability Identity\s*:\s*(.+)' | ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() }");
                sb.AppendLine(@"    foreach ($name in $names) {");
                sb.AppendLine(@"        foreach ($pattern in $Patterns) {");
                sb.AppendLine(@"            if ($name -like $pattern) {");
                sb.AppendLine(@"                & $dismPath /Image:$mountDir /Remove-Capability /CapabilityName:$name 2>$null | Out-Null");
                sb.AppendLine(@"                Assert-NativeSuccess ""Capability removal: $name""");
                sb.AppendLine(@"                break");
                sb.AppendLine(@"            }");
                sb.AppendLine(@"        }");
                sb.AppendLine(@"    }");
                sb.AppendLine(@"}");
                sb.AppendLine(@"function Remove-MatchingDrivers($ClassNames) {");
                sb.AppendLine(@"    $driverOutput = & $dismPath /Image:$mountDir /Get-Drivers");
                sb.AppendLine(@"    Assert-NativeSuccess 'Driver inventory'");
                sb.AppendLine(@"    $driverText = $driverOutput -join ""`n""");
                sb.AppendLine(@"    $blocks = $driverText -split '(?=Published Name)'");
                sb.AppendLine(@"    foreach ($block in $blocks) {");
                sb.AppendLine(@"        if ($block -match 'Published Name\s*:\s*(\S+)') {");
                sb.AppendLine(@"            $pubName = $Matches[1]");
                sb.AppendLine(@"            if ($block -match 'Class Name\s*:\s*(\S+)' -and $ClassNames -contains $Matches[1]) {");
                sb.AppendLine(@"                & $dismPath /Image:$mountDir /Remove-Driver /Driver:$pubName 2>$null | Out-Null");
                sb.AppendLine(@"                Assert-NativeSuccess ""Driver removal: $pubName""");
                sb.AppendLine(@"            }");
                sb.AppendLine(@"        }");
                sb.AppendLine(@"    }");
                sb.AppendLine(@"}");
                sb.AppendLine();
            }

            if (options.RemoveRecall)
            {
                sb.AppendLine(@"Write-Host '   Removing Windows Recall...' -ForegroundColor Yellow");
                sb.AppendLine(@"Remove-MatchingCapabilities -Patterns @('Recall*')");
                sb.AppendLine();
            }

            if (options.RemoveInputComponents)
            {
                sb.AppendLine(@"Write-Host '   Removing Speech/OCR/Handwriting components...' -ForegroundColor Yellow");
                sb.AppendLine(@"Remove-MatchingCapabilities -Patterns @('Language.Speech*', 'Language.OCR*', 'Language.Handwriting*', 'Language.TextToSpeech*')");
                sb.AppendLine();
            }

            if (options.CleanupDriverStore)
            {
                sb.AppendLine(@"Write-Host '   Removing unused driver packages (printer/scanner/modem/Xbox)...' -ForegroundColor Yellow");
                sb.AppendLine(@"Remove-MatchingDrivers -ClassNames @('Printer', 'PrinterQueue', 'Image', 'Modem', 'XboxComposite')");
                sb.AppendLine();
            }

            if (options.CleanupComponentStore)
            {
                sb.AppendLine(@"Write-Host '   Cleaning up component store (WinSxS, this can take several minutes)...' -ForegroundColor Yellow");
                sb.AppendLine(@"& $dismPath /image:$mountDir /Cleanup-Image /StartComponentCleanup /ResetBase");
                sb.AppendLine(@"Assert-NativeSuccess 'Component store cleanup'");
                sb.AppendLine();
            }

            // Unload registry hives
            sb.AppendLine(@"# Registry hive'larını kaldır");
            sb.AppendLine(@"Write-Host '   Unloading registry hives...' -ForegroundColor Gray");
            sb.AppendLine(@"[gc]::Collect()");
            sb.AppendLine(@"Start-Sleep -Seconds 2");
            sb.AppendLine(@"reg unload ""HKLM\OFFLINE_SOFTWARE"" 2>$null");
            sb.AppendLine(@"Assert-NativeSuccess 'SOFTWARE registry hive unload'");
            sb.AppendLine(@"$softwareHiveLoaded = $false");
            sb.AppendLine(@"reg unload ""HKLM\OFFLINE_SYSTEM"" 2>$null");
            sb.AppendLine(@"Assert-NativeSuccess 'SYSTEM registry hive unload'");
            sb.AppendLine(@"$systemHiveLoaded = $false");
            sb.AppendLine(@"reg unload ""HKU\OFFLINE_NTUSER"" 2>$null");
            sb.AppendLine(@"Assert-NativeSuccess 'NTUSER registry hive unload'");
            sb.AppendLine(@"$ntuserHiveLoaded = $false");
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
            sb.AppendLine(@"& $dismPath /unmount-wim /mountdir:$mountDir /commit");
            sb.AppendLine(@"Assert-NativeSuccess 'WIM commit'");
            sb.AppendLine(@"$wimMounted = $false");
            sb.AppendLine();

            // ISO unmount
            sb.AppendLine(@"# Kaynak ISO'yu unmount et");
            sb.AppendLine(@"Write-Host 'Unmounting source ISO...' -ForegroundColor Cyan");
            sb.AppendLine(@"Dismount-DiskImage -ImagePath $isoPath -ErrorAction Stop");
            sb.AppendLine(@"$isoMounted = $false");
            sb.AppendLine();

            // Görüntüyü sıkıştır (Recovery compression) - boyutu belirgin şekilde azaltır
            if (options.CompressFinalImage)
            {
                sb.AppendLine(@"# Görüntüyü sıkıştır (Recovery compression) - dism.exe /Export-Image kullanılır, PowerShell'in");
                sb.AppendLine(@"# Export-WindowsImage cmdlet'i host işletim sisteminin eski Dism modülüne bağımlı olduğu için atlanır.");
                sb.AppendLine(@"Write-Host 'Compressing final image (recovery compression)...' -ForegroundColor Cyan");
                sb.AppendLine(@"$compressedWimPath = Join-Path $isoDir 'sources\install_compressed.wim'");
                sb.AppendLine(@"& $dismPath /Export-Image /SourceImageFile:$wimPath /SourceIndex:$editionIndex /DestinationImageFile:$compressedWimPath /Compress:recovery");
                sb.AppendLine(@"Assert-NativeSuccess 'Final WIM compression'");
                sb.AppendLine(@"if (!(Test-Path $compressedWimPath) -or (Get-Item $compressedWimPath).Length -le 0) { throw 'Final WIM compression produced no usable output' }");
                sb.AppendLine(@"Remove-Item $wimPath -Force");
                sb.AppendLine(@"Rename-Item $compressedWimPath 'install.wim'");
                sb.AppendLine(@"$editionIndex = 1");
                sb.AppendLine(@"Write-Host '   Image compressed successfully' -ForegroundColor Green");
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
            sb.AppendLine(@"    throw 'oscdimg.exe was not found'");
            sb.AppendLine(@"}");
            sb.AppendLine();
            sb.AppendLine(@"Write-Host ""oscdimg found: $oscdimgPath"" -ForegroundColor Green");
            sb.AppendLine();
            sb.AppendLine(@"$bootData = '2#p0,e,b""' + $isoDir + '\boot\etfsboot.com""#pEF,e,b""' + $isoDir + '\efi\microsoft\boot\efisys.bin""'");
            sb.AppendLine(@"Remove-Item $temporaryOutputPath -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"& $oscdimgPath -m -o -u2 -udfver102 -bootdata:$bootData -l""Tiny11"" $isoDir $temporaryOutputPath");
            sb.AppendLine(@"Assert-NativeSuccess 'ISO creation'");
            sb.AppendLine(@"if (!(Test-Path $temporaryOutputPath) -or (Get-Item $temporaryOutputPath).Length -le 0) { throw 'ISO creation produced no usable output' }");
            sb.AppendLine();

            // Başarılı çıktı aynı klasörde geçici dosyaya yazılır ve atomik olarak hedefe alınır.
            // Böylece önceki bir ISO, başarısız yeni çalışmayı başarılı gibi gösteremez.
            sb.AppendLine(@"if (Test-Path $outputPath) {");
            sb.AppendLine(@"    Remove-Item $backupOutputPath -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"    [System.IO.File]::Replace($temporaryOutputPath, $outputPath, $backupOutputPath, $true)");
            sb.AppendLine(@"    Remove-Item $backupOutputPath -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"} else {");
            sb.AppendLine(@"    [System.IO.File]::Move($temporaryOutputPath, $outputPath)");
            sb.AppendLine(@"}");
            sb.AppendLine(@"$buildSucceeded = $true");
            sb.AppendLine();

            // Sonuç
            sb.AppendLine(@"$fileSize = (Get-Item $outputPath).Length / 1GB");
            sb.AppendLine(@"Write-Host ""Tiny11 ISO created successfully!"" -ForegroundColor Green");
            sb.AppendLine(@"Write-Host ""Location: $outputPath"" -ForegroundColor Green");
            sb.AppendLine(@"Write-Host ""Size: $([math]::Round($fileSize, 2)) GB"" -ForegroundColor Green");
            sb.AppendLine(@"} catch {");
            sb.AppendLine(@"    [Console]::Error.WriteLine(""Tiny11 build failed: $($_.Exception.Message)"")");
            sb.AppendLine(@"    $scriptExitCode = 1");
            sb.AppendLine(@"} finally {");
            sb.AppendLine(@"    Write-Host 'Cleaning up this build...' -ForegroundColor Cyan");
            sb.AppendLine(@"    if ($ntuserHiveLoaded) { reg unload ""HKU\OFFLINE_NTUSER"" 2>$null | Out-Null }");
            sb.AppendLine(@"    if ($systemHiveLoaded) { reg unload ""HKLM\OFFLINE_SYSTEM"" 2>$null | Out-Null }");
            sb.AppendLine(@"    if ($softwareHiveLoaded) { reg unload ""HKLM\OFFLINE_SOFTWARE"" 2>$null | Out-Null }");
            sb.AppendLine(@"    [gc]::Collect()");
            sb.AppendLine(@"    if ($wimMounted -and (Test-Path $mountDir)) { & $dismPath /unmount-wim /mountdir:$mountDir /discard 2>$null | Out-Null }");
            sb.AppendLine(@"    if ($isoMounted) { Dismount-DiskImage -ImagePath $isoPath -ErrorAction SilentlyContinue }");
            sb.AppendLine(@"    Remove-Item $temporaryOutputPath -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"    Remove-Item -Path $mountDir -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"    Remove-Item -Path $isoDir -Recurse -Force -ErrorAction SilentlyContinue");
            sb.AppendLine(@"}");
            sb.AppendLine(@"exit $scriptExitCode");

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
                await SaveCurrentRunStateAsync(_currentProcess);
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                // İptal kontrolü ile bekle
                while (!_currentProcess.HasExited)
                {
                    // İptal istendi mi kontrol et
                    if (cancellationToken.IsCancellationRequested)
                    {
                        OutputReceived?.Invoke(GetLocalizedString("LogCancelReceived"));
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
                await ComprehensiveCleanupAsync(scratchPath);

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
                
                OutputReceived?.Invoke("PowerShell process başlatıldı, input gönderiliyor...");

                // Input stream'e otomatik değerleri gönder
                var inputWriter = _currentProcess.StandardInput;

                // Drive letter input için
                OutputReceived?.Invoke($"ISO path gönderiliyor: {isoPath}");
                await inputWriter.WriteLineAsync(isoPath);
                await inputWriter.FlushAsync();

                // Image index input için
                OutputReceived?.Invoke($"Edition index gönderiliyor: {editionIndex}");
                await inputWriter.WriteLineAsync(editionIndex.ToString());
                await inputWriter.FlushAsync();

                // Scratch disk input için
                OutputReceived?.Invoke($"Scratch path gönderiliyor: {scratchPath}");
                await inputWriter.WriteLineAsync(scratchPath);
                await inputWriter.FlushAsync();

                OutputReceived?.Invoke("Tüm input'lar gönderildi, script çalışıyor...");
                
                // Input stream'i kapat
                inputWriter.Close();

                // Timeout ile bekle (60 dakika - ISO oluşturma uzun sürer)
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(60));
                var processTask = Task.Run(() => _currentProcess.WaitForExit());
                
                var completedTask = await Task.WhenAny(processTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    OutputReceived?.Invoke("İşlem zaman aşımına uğradı, temizlik yapılıyor...");
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
                        $driveBefore = [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'CDRom' } | ForEach-Object { $_.Name }
                        $mountResult = Mount-DiskImage -ImagePath $isoPath -PassThru
                        $driveLetter = $null
                        for ($i = 0; $i -lt 20; $i++) {
                            $candidate = [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'CDRom' -and $_.IsReady -and $driveBefore -notcontains $_.Name } | Select-Object -First 1
                            if ($candidate) { $driveLetter = $candidate.Name.TrimEnd('\'); break }
                            Start-Sleep -Milliseconds 500
                        }
                        if (-not $driveLetter) { throw 'Could not determine the drive letter of the mounted ISO' }
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

                foreach (var line in lines)
                {
                    var cleanLine = line.Trim();

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
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            OutputReceived?.Invoke(string.Format(GetLocalizedString("LogEditionParseLineError"), ex.Message, cleanLine));
                        }
                    }
                }

                if (editions.Count == 0)
                {
                    OutputReceived?.Invoke(GetLocalizedString("LogEditionParseFallback"));
                    // Fallback: Tüm sürümleri ekle
                    editions.Add("1 - Windows 11 Home");
                    editions.Add("2 - Windows 11 Home Single Language");
                    editions.Add("3 - Windows 11 Education");
                    editions.Add("4 - Windows 11 Pro");
                    editions.Add("5 - Windows 11 Pro Education");
                    editions.Add("6 - Windows 11 Pro for Workstations");
                }

                return editions.ToArray();
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogEditionsRetrievalFailed"), ex.Message));
                
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
                    $isoPath = '{escapedIsoPath}'
                    $driveBefore = [System.IO.DriveInfo]::GetDrives() | Where-Object {{ $_.DriveType -eq 'CDRom' }} | ForEach-Object {{ $_.Name }}
                    $mountResult = Mount-DiskImage -ImagePath $isoPath -PassThru
                    $driveLetter = $null
                    for ($i = 0; $i -lt 20; $i++) {{
                        $candidate = [System.IO.DriveInfo]::GetDrives() | Where-Object {{ $_.DriveType -eq 'CDRom' -and $_.IsReady -and $driveBefore -notcontains $_.Name }} | Select-Object -First 1
                        if ($candidate) {{ $driveLetter = $candidate.Name.TrimEnd('\'); break }}
                        Start-Sleep -Milliseconds 500
                    }}
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
                OutputReceived?.Invoke(GetLocalizedString("LogIsoUnmounted"));
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogIsoUnmountError"), ex.Message));
            }
        }

        private async Task<string> RunPowerShellCommandAsync(string command)
        {
            // Script -Command üzerinden komut satırına gömülürse iç içe tırnaklar/alt ifadeler
            // ($(...), "...") komut satırı ayrıştırmasında bozulabiliyor. Geçici .ps1 dosyasına
            // yazıp -File ile çalıştırmak bu sorunu tamamen ortadan kaldırır.
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"tiny11_cmd_{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(tempScriptPath, command, Encoding.UTF8);

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{tempScriptPath}\"",
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
            finally
            {
                try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { /* Ignore cleanup errors */ }
            }
        }

        private async Task CleanupEnvironmentAsync()
        {
            // Eski uygulama akışları da yalnızca bu instance'ın izlediği kaynakları temizler.
            // Sistemdeki tüm WIM mount'larını discard eden global cleanup bilinçli olarak yoktur.
            await CleanupAfterCancelAsync();
        }

        private async Task ComprehensiveCleanupAsync(string scratchPath)
        {
            try
            {
                OutputReceived?.Invoke(GetLocalizedString("LogComprehensiveCleanup"));

                Directory.CreateDirectory(scratchPath);

                // Önceki sürüm veya çöken bir instance tarafından bırakılan state kayıtlarından
                // yalnızca kaydı doğrulanan PowerShell process'ini ve Tiny11 dizinlerini kurtar.
                var activeRunDirectories = await RecoverTrackedRunsAsync(scratchPath);

                // State sistemi eklenmeden önceki sürümlerden kalan dizinler için geriye uyumlu
                // kurtarma: yalnızca seçilen scratch kökündeki tiny11_* dizinlerine dokunulur.
                await RecoverLegacyScratchDirectoriesAsync(scratchPath, activeRunDirectories);

                OutputReceived?.Invoke(GetLocalizedString("LogComprehensiveCleanupComplete"));
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupError"), ex.Message));
            }
        }

        private async Task SaveCurrentRunStateAsync(Process? powerShellProcess = null)
        {
            if (string.IsNullOrEmpty(_currentStatePath) ||
                string.IsNullOrEmpty(_currentScratchPath) ||
                string.IsNullOrEmpty(_currentRunId))
            {
                return;
            }

            try
            {
                int? processId = null;
                DateTime? processStartTimeUtc = null;

                if (powerShellProcess != null)
                {
                    processId = powerShellProcess.Id;
                    processStartTimeUtc = powerShellProcess.StartTime.ToUniversalTime();
                }

                var state = new BuildRunState
                {
                    RunId = _currentRunId,
                    ScratchPath = _currentScratchPath,
                    WorkDirectory = _currentWorkDir,
                    MountDirectory = _currentMountDir,
                    RetryMountDirectory = _currentMountDirRetry,
                    IsoDirectory = _currentIsoDir,
                    IsoPath = _currentIsoPath,
                    OwnerProcessId = Environment.ProcessId,
                    OwnerProcessStartTimeUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                    PowerShellProcessId = processId,
                    PowerShellProcessStartTimeUtc = processStartTimeUtc
                };

                Directory.CreateDirectory(_currentScratchPath);
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                var temporaryStatePath = _currentStatePath + ".tmp";
                await File.WriteAllTextAsync(temporaryStatePath, json, Encoding.UTF8);
                File.Move(temporaryStatePath, _currentStatePath, true);
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"),
                    $"Build state could not be saved: {ex.Message}"));
            }
        }

        private void DeleteCurrentRunState()
        {
            if (string.IsNullOrEmpty(_currentStatePath)) return;

            try
            {
                if (File.Exists(_currentStatePath)) File.Delete(_currentStatePath);
                if (File.Exists(_currentStatePath + ".tmp")) File.Delete(_currentStatePath + ".tmp");
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"), ex.Message));
            }
        }

        private void DeleteCurrentRunStateIfResourcesReleased()
        {
            var trackedDirectories = new[]
            {
                _currentWorkDir,
                _currentMountDir,
                _currentMountDirRetry,
                _currentIsoDir
            };

            if (trackedDirectories.Any(directory =>
                    !string.IsNullOrEmpty(directory) && Directory.Exists(directory)))
            {
                return;
            }

            if (_currentProcess != null)
            {
                try
                {
                    if (!_currentProcess.HasExited) return;
                }
                catch
                {
                    return;
                }
            }

            DeleteCurrentRunState();
        }

        private async Task<HashSet<string>> RecoverTrackedRunsAsync(string scratchPath)
        {
            var activeRunDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var statePath in Directory.GetFiles(scratchPath, ".tiny11-run-*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(statePath);
                    var state = JsonSerializer.Deserialize<BuildRunState>(json);
                    if (state == null || state.SchemaVersion != 1 ||
                        !PathsEqual(state.ScratchPath, scratchPath))
                    {
                        OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"),
                            $"Ignored invalid build state: {Path.GetFileName(statePath)}"));
                        continue;
                    }

                    var trackedDirectories = new[]
                    {
                        state.WorkDirectory,
                        state.MountDirectory,
                        state.RetryMountDirectory,
                        state.IsoDirectory
                    };

                    if (IsProcessInstanceAlive(state.OwnerProcessId, state.OwnerProcessStartTimeUtc))
                    {
                        foreach (var directory in trackedDirectories)
                        {
                            if (IsOwnedScratchDirectory(directory, scratchPath,
                                    "tiny11_work_", "tiny11_mount_", "tiny11_iso_"))
                            {
                                activeRunDirectories.Add(Path.GetFullPath(directory!));
                            }
                        }

                        OutputReceived?.Invoke($"Active Tiny11 run preserved: {state.RunId}");
                        continue;
                    }

                    await StopTrackedPowerShellProcessAsync(state);

                    foreach (var mountDirectory in new[] { state.MountDirectory, state.RetryMountDirectory })
                    {
                        if (IsOwnedScratchDirectory(mountDirectory, scratchPath, "tiny11_mount_"))
                        {
                            await DismountOwnedWimAsync(mountDirectory!);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(state.IsoPath))
                    {
                        await DismountTrackedIsoAsync(state.IsoPath);
                    }

                    foreach (var directory in trackedDirectories)
                    {
                        if (IsOwnedScratchDirectory(directory, scratchPath,
                                "tiny11_work_", "tiny11_mount_", "tiny11_iso_"))
                        {
                            TryDeleteOwnedDirectory(directory!);
                        }
                    }

                    var cleanupCompleted = trackedDirectories.All(directory =>
                        string.IsNullOrEmpty(directory) || !Directory.Exists(directory));
                    if (cleanupCompleted)
                    {
                        File.Delete(statePath);
                    }
                    else
                    {
                        OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"),
                            $"Build state retained for another recovery attempt: {Path.GetFileName(statePath)}"));
                    }
                }
                catch (Exception ex)
                {
                    OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"), ex.Message));
                }
            }

            return activeRunDirectories;
        }

        private async Task RecoverLegacyScratchDirectoriesAsync(string scratchPath, HashSet<string> activeRunDirectories)
        {
            foreach (var mountDirectory in Directory.GetDirectories(scratchPath, "tiny11_mount_*"))
            {
                if (!activeRunDirectories.Contains(Path.GetFullPath(mountDirectory)) &&
                    IsOwnedScratchDirectory(mountDirectory, scratchPath, "tiny11_mount_"))
                {
                    await DismountOwnedWimAsync(mountDirectory);
                }
            }

            foreach (var pattern in new[] { "tiny11_work_*", "tiny11_mount_*", "tiny11_iso_*" })
            {
                foreach (var directory in Directory.GetDirectories(scratchPath, pattern))
                {
                    if (!activeRunDirectories.Contains(Path.GetFullPath(directory)) &&
                        IsOwnedScratchDirectory(directory, scratchPath,
                            "tiny11_work_", "tiny11_mount_", "tiny11_iso_"))
                    {
                        TryDeleteOwnedDirectory(directory);
                    }
                }
            }
        }

        private static bool IsProcessInstanceAlive(int processId, DateTime processStartTimeUtc)
        {
            if (processId <= 0 || processStartTimeUtc == default) return false;

            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited &&
                       Math.Abs((process.StartTime.ToUniversalTime() - processStartTimeUtc).TotalSeconds) <= 1;
            }
            catch
            {
                return false;
            }
        }

        private async Task StopTrackedPowerShellProcessAsync(BuildRunState state)
        {
            if (!state.PowerShellProcessId.HasValue || !state.PowerShellProcessStartTimeUtc.HasValue)
                return;

            try
            {
                using var process = Process.GetProcessById(state.PowerShellProcessId.Value);
                if (process.HasExited ||
                    !process.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
                    Math.Abs((process.StartTime.ToUniversalTime() - state.PowerShellProcessStartTimeUtc.Value).TotalSeconds) > 1)
                {
                    return;
                }

                OutputReceived?.Invoke($"Stopping stale Tiny11 PowerShell process: {process.Id}");
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (ArgumentException)
            {
                // Process artık mevcut değil.
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"), ex.Message));
            }
        }

        private async Task DismountOwnedWimAsync(string mountDirectory)
        {
            if (!Directory.Exists(mountDirectory)) return;

            OutputReceived?.Invoke($"Discarding stale Tiny11 mount: {mountDirectory}");
            await RunCleanupCommandAsync($"dism /unmount-wim /mountdir:\"{mountDirectory}\" /discard");
        }

        private async Task DismountTrackedIsoAsync(string isoPath)
        {
            try
            {
                var escapedIsoPath = isoPath.Replace("'", "''");
                await RunPowerShellCommandAsync(
                    $"Dismount-DiskImage -ImagePath '{escapedIsoPath}' -ErrorAction SilentlyContinue");
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"), ex.Message));
            }
        }

        private static bool PathsEqual(string firstPath, string secondPath)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsOwnedScratchDirectory(string? candidatePath, string scratchPath, params string[] allowedPrefixes)
        {
            if (string.IsNullOrWhiteSpace(candidatePath)) return false;

            try
            {
                var fullCandidatePath = Path.GetFullPath(candidatePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullScratchPath = Path.GetFullPath(scratchPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var parentPath = Path.GetDirectoryName(fullCandidatePath);
                var directoryName = Path.GetFileName(fullCandidatePath);

                return parentPath != null &&
                       string.Equals(parentPath, fullScratchPath, StringComparison.OrdinalIgnoreCase) &&
                       allowedPrefixes.Any(prefix =>
                           directoryName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private void TryDeleteOwnedDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                OutputReceived?.Invoke(string.Format(GetLocalizedString("LogCleanupContinue"), ex.Message));
            }
        }
    }
}
