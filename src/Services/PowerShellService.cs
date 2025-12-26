using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace tiny11_ui.Services
{
    public class PowerShellService
    {
        public event Action<string>? OutputReceived;
        public event Action<string>? ErrorReceived;
        public event Action<int>? ProcessCompleted;

        private Process? _currentProcess;

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
                        ErrorReceived?.Invoke(e.Data);
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
                        OutputReceived?.Invoke($"❌ İşlem başarısız oldu (Exit Code: {exitCode}), temizlik yapılıyor...");
                        await CleanupEnvironmentAsync();
                        return false;
                    }
                    else
                    {
                        OutputReceived?.Invoke("✅ İşlem başarıyla tamamlandı!");
                        
                        // Varsayılan tiny11.iso dosyasını kullanıcının istediği yere kopyala
                        var defaultOutputPath = Path.Combine(scriptPath, "tiny11.iso");
                        if (File.Exists(defaultOutputPath) && !string.IsNullOrEmpty(outputPath))
                        {
                            try
                            {
                                OutputReceived?.Invoke($"📁 ISO dosyası kopyalanıyor: {outputPath}");
                                
                                // Hedef dizini oluştur
                                var outputDir = Path.GetDirectoryName(outputPath);
                                if (!Directory.Exists(outputDir))
                                {
                                    Directory.CreateDirectory(outputDir);
                                }
                                
                                File.Copy(defaultOutputPath, outputPath, true);
                                OutputReceived?.Invoke($"✅ ISO dosyası başarıyla kaydedildi: {outputPath}");
                            }
                            catch (Exception copyEx)
                            {
                                ErrorReceived?.Invoke($"❌ ISO kopyalama hatası: {copyEx.Message}");
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
                // Daha güvenli ve basit yaklaşım: DISM komutunu direkt kullan
                var escapedIsoPath = isoPath.Replace("'", "''");
                var script = $@"
                    try {{
                        Write-Host 'ISO mount ediliyor: {escapedIsoPath}'
                        $mountResult = Mount-DiskImage -ImagePath '{escapedIsoPath}' -PassThru
                        $driveLetter = ($mountResult | Get-Volume).DriveLetter + ':'
                        Write-Host ""Mount edilen sürücü: $driveLetter""
                        
                        $wimPath = """"
                        if (Test-Path ""$driveLetter\sources\install.wim"") {{
                            $wimPath = ""$driveLetter\sources\install.wim""
                            Write-Host ""install.wim bulundu: $wimPath""
                        }} elseif (Test-Path ""$driveLetter\sources\install.esd"") {{
                            $wimPath = ""$driveLetter\sources\install.esd""
                            Write-Host ""install.esd bulundu: $wimPath""
                        }} else {{
                            throw 'Windows imaj dosyası bulunamadı'
                        }}
                        
                        # DISM kullanarak Windows sürümlerini listele
                        $images = Get-WindowsImage -ImagePath $wimPath
                        foreach ($image in $images) {{
                            Write-Output ""INDEX:$($image.ImageIndex):NAME:$($image.ImageName):DRIVE:$driveLetter""
                        }}
                        
                    }} catch {{
                        Write-Error $_.Exception.Message
                    }} finally {{
                        # ISO'yu unmount et
                        try {{
                            Write-Host 'ISO unmount ediliyor...'
                            Dismount-DiskImage -ImagePath '{escapedIsoPath}' -ErrorAction SilentlyContinue
                        }} catch {{ 
                            Write-Host 'Unmount hatası: ' + $_.Exception.Message
                        }}
                    }}
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
                
                OutputReceived?.Invoke("✅ Sistem temizliği tamamlandı");
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke($"Temizlik hatası: {ex.Message}");
            }
        }

        private async Task ComprehensiveCleanupAsync()
        {
            try
            {
                OutputReceived?.Invoke("🧹 Kapsamlı sistem temizliği yapılıyor...");
                
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
                        OutputReceived?.Invoke($"⚠️ Temizlik hatası (devam edilebilir): {ex.Message}");
                    }
                }
                
                OutputReceived?.Invoke("✅ Kapsamlı temizlik tamamlandı");
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke($"Kapsamlı temizlik hatası: {ex.Message}");
            }
        }
    }
}