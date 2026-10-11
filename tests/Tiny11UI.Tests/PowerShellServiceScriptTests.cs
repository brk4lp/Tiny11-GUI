using tiny11_ui.Models;
using tiny11_ui.Services;
using Xunit;
using System.Diagnostics;

namespace Tiny11UI.Tests;

public class PowerShellServiceScriptTests
{
    [Theory]
    [InlineData(-2146498548, 0)]
    [InlineData(0, 0)]
    [InlineData(0, 3010)]
    [InlineData(32, 0)]
    [InlineData(87, 0)]
    [InlineData(-2146498555, 0)]
    [InlineData(0, 32)]
    [InlineData(0, -2146498548)]
    public async Task HyperVRemoval_SkipsAbsentFeatureButPreservesServicingFailures(int queryExit, int removeExit)
    {
        var options = BareOptions();
        options.RemoveHyperV = true;
        var script = new PowerShellService(new LocalizationService()).PreviewScript(
            @"C:\windows.iso", @"C:\scratch", @"C:\tiny.iso", 1, options);
        var start = script.IndexOf("# Check Hyper-V availability", StringComparison.Ordinal);
        var end = script.IndexOf("# End Hyper-V servicing", start, StringComparison.Ordinal);
        var harness = """
            $ErrorActionPreference = 'Stop'
            $mountDir = 'C:\scratch folder\中文'
            $dismPath = 'Test-Dism'
            $script:queries = 0
            $script:removals = 0
            function Test-Dism {
                if ($args[0] -ne '/English' -or $args[1] -ne "/Image:$mountDir" -or $args[3] -ne '/FeatureName:Microsoft-Hyper-V-All') { throw 'Unexpected feature target' }
                if ($args.Count -eq 4 -and $args[2] -eq '/Get-FeatureInfo') {
                    $script:queries++
                    if (QUERY_EXIT -ne 0) { Write-Error 'feature query diagnostic' }
                    $global:LASTEXITCODE = QUERY_EXIT
                    'Feature Name : Microsoft-Hyper-V-All'
                } elseif ($args.Count -eq 6 -and $args[2] -eq '/Disable-Feature' -and $args[4] -eq '/Remove' -and $args[5] -eq '/NoRestart') {
                    $script:removals++
                    if (REMOVE_EXIT -ne 0 -and REMOVE_EXIT -ne 3010) { Write-Error 'feature removal diagnostic' }
                    $global:LASTEXITCODE = REMOVE_EXIT
                } else { throw 'Unexpected DISM arguments' }
            }
            $caught = $null
            try { GENERATED } catch { $caught = $_ }
            if ($ErrorActionPreference -ne 'Stop') { throw 'Caller error preference changed' }
            if ($script:queries -ne 1) { throw 'Feature availability was not queried' }
            if (QUERY_EXIT -eq -2146498548) {
                if ($caught -or $script:removals -ne 0) { throw 'Absent feature should skip removal' }
            } elseif (QUERY_EXIT -ne 0) {
                if (!$caught -or $caught -notlike '*Hyper-V feature query failed*feature query diagnostic*' -or $script:removals -ne 0) { throw 'Inventory failure was not preserved' }
            } else {
                if ($script:removals -ne 1) { throw 'Present feature was not removed' }
                if (REMOVE_EXIT -eq 0 -or REMOVE_EXIT -eq 3010) {
                    if ($caught) { throw $caught }
                } elseif (!$caught -or $caught -notlike '*Hyper-V removal failed*feature removal diagnostic*') { throw 'Removal failure was not preserved' }
            }
            """.Replace("QUERY_EXIT", queryExit.ToString()).Replace("REMOVE_EXIT", removeExit.ToString()).Replace("GENERATED", script[start..end]);
        await RunPowerShellHarness(harness);
    }

    [Fact]
    public async Task OneDriveRemoval_DeletesReadOnlyFileUsingNativePermissionTools()
    {
        var options = BareOptions();
        options.RemoveOneDrive = true;
        var script = new PowerShellService(new LocalizationService()).PreviewScript(
            @"C:\windows.iso", @"C:\scratch", @"C:\tiny.iso", 1, options);
        var start = script.IndexOf("$onedrivePaths =", StringComparison.Ordinal);
        var end = script.IndexOf("# Registry ayarlarını uygula", start, StringComparison.Ordinal);
        var harness = """
            $ErrorActionPreference = 'Stop'
            $mountDir = Join-Path $env:TEMP ('Tiny11 native [test] 中文 ' + [guid]::NewGuid())
            $folder = Join-Path $mountDir 'Windows\System32'
            [IO.Directory]::CreateDirectory($folder) | Out-Null
            $setupFile = Join-Path $folder 'OneDriveSetup.exe'
            $sentinel = Join-Path $folder 'keep.exe'
            [IO.File]::WriteAllText($setupFile, 'test')
            [IO.File]::WriteAllText($sentinel, 'keep')
            (Get-Item -LiteralPath $setupFile).IsReadOnly = $true
            try {
                $beforeAcl = [IO.File]::GetAccessControl($sentinel).GetSecurityDescriptorSddlForm('All')
                GENERATED
                if (Test-Path -LiteralPath $setupFile) { throw 'Read-only setup was not removed' }
                if ([IO.File]::ReadAllText($sentinel) -ne 'keep' -or [IO.File]::GetAccessControl($sentinel).GetSecurityDescriptorSddlForm('All') -ne $beforeAcl) { throw 'Unrelated file changed' }
            } finally {
                Remove-Item -LiteralPath $mountDir -Recurse -Force
            }
            """.Replace("GENERATED", script[start..end]);
        await RunPowerShellHarness(harness);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("protected")]
    [InlineData("ownerFailure")]
    [InlineData("aclFailure")]
    [InlineData("deleteFailure")]
    [InlineData("missing")]
    [InlineData("directory")]
    [InlineData("parentLink")]
    [InlineData("leafReparse")]
    [InlineData("attributeFailure")]
    [InlineData("trustedFile")]
    [InlineData("wimProtected")]
    [InlineData("wofProtected")]
    [InlineData("protectedSymlink")]
    [InlineData("reparseQueryFailure")]
    public async Task OneDriveRemoval_RetriesOnlyExactOfflineFilesAndReportsFailures(string scenario)
    {
        var options = BareOptions();
        options.RemoveOneDrive = true;
        var script = new PowerShellService(new LocalizationService()).PreviewScript(
            @"C:\windows.iso", @"C:\scratch", @"C:\tiny.iso", 1, options);
        var start = script.IndexOf("$onedrivePaths =", StringComparison.Ordinal);
        var end = script.IndexOf("# Registry ayarlarını uygula", start, StringComparison.Ordinal);
        var harness = """
            $ErrorActionPreference = 'Stop'
            $scenario = 'SCENARIO'
            $mountDir = Join-Path $env:TEMP ('Tiny11 [test] 中文 ' + [guid]::NewGuid())
            $folder = Join-Path $mountDir 'Windows\System32'
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            $expected = Join-Path $folder 'OneDriveSetup.exe'
            $sentinel = Join-Path $folder 'keep.exe'
            [IO.File]::WriteAllText($sentinel, 'keep')
            if ($scenario -eq 'directory') {
                [IO.Directory]::CreateDirectory($expected) | Out-Null
            } elseif ($scenario -ne 'missing') {
                [IO.File]::WriteAllText($expected, 'test')
                if ($scenario -notin @('plain', 'leafReparse')) { (Microsoft.PowerShell.Management\Get-Item -LiteralPath $expected).IsReadOnly = $true }
            }
            $script:deletes = 0
            $script:owns = 0
            $script:grants = 0
            $script:attributes = 0
            $script:takes = 0
            function Get-Item {
                param($LiteralPath, [switch]$Force, $ErrorAction)
                # GetFullPath expands existing 8.3 names such as the CI runner's TEMP path.
                # Match the normalized paths used by the generated parent traversal.
                $normalizedPath = [IO.Path]::GetFullPath($LiteralPath)
                if ($scenario -eq 'parentLink' -and $normalizedPath -eq [IO.Path]::GetFullPath($folder)) {
                    return [pscustomobject]@{ Attributes = [IO.FileAttributes]::ReparsePoint; PSIsContainer = $true }
                }
                if ($scenario -in @('leafReparse', 'wimProtected', 'wofProtected', 'protectedSymlink', 'reparseQueryFailure') -and $normalizedPath -eq [IO.Path]::GetFullPath($expected)) {
                    return [pscustomobject]@{ Attributes = [IO.FileAttributes]::ReparsePoint; PSIsContainer = $false }
                }
                Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force
            }
            function Remove-TestSetupFile {
                param($LiteralPath)
                if ($LiteralPath -ne $expected) { throw 'Unexpected deletion target' }
                $script:deletes++
                if ($scenario -eq 'deleteFailure' -or ($scenario -notin @('plain', 'leafReparse') -and $script:deletes -eq 1)) {
                    throw [UnauthorizedAccessException]::new('Access to the path is denied.')
                }
                [IO.File]::Delete($LiteralPath)
            }
            function icacls.exe {
                $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
                if ($args.Count -eq 4 -and $args[0] -eq $expected -and $args[1] -eq '/setowner' -and $args[2] -eq "*$sid" -and $args[3] -eq '/L') {
                    $script:owns++
                    if ($scenario -in @('ownerFailure', 'trustedFile', 'wimProtected', 'wofProtected', 'protectedSymlink', 'reparseQueryFailure')) {
                        Write-Error 'Access is denied (native stderr simulation)'
                        $global:LASTEXITCODE = 5
                    } else { $global:LASTEXITCODE = 0 }
                    'ownership diagnostic'
                    return
                }
                if ($args.Count -ne 4 -or $args[0] -ne $expected -or $args[1] -ne '/grant' -or $args[2] -ne "*${sid}:F" -or $args[3] -ne '/L') { throw 'Unexpected permission scope' }
                $script:grants++
                $global:LASTEXITCODE = if ($scenario -eq 'aclFailure') { 5 } else { 0 }
                'ACL diagnostic'
            }
            function fsutil.exe {
                if ($args.Count -ne 3 -or $args[0] -ne 'reparsepoint' -or $args[1] -ne 'query' -or $args[2] -ne $expected) { throw 'Unexpected reparse query' }
                $global:LASTEXITCODE = if ($scenario -eq 'reparseQueryFailure') { 5 } else { 0 }
                switch ($scenario) {
                    'protectedSymlink' { 'Reparse Tag Value : 0xa000000c' }
                    'wofProtected' { 'Reparse Tag Value : 0x80000017' }
                    default { 'Reparse Tag Value : 0x80000008' }
                }
            }
            function takeown.exe {
                if ($args.Count -ne 3 -or $args[0] -ne '/F' -or $args[1] -ne $expected -or $args[2] -ne '/A') { throw 'Unexpected ownership target' }
                $script:takes++
                $global:LASTEXITCODE = if ($scenario -eq 'ownerFailure') { 5 } else { 0 }
                'ownership diagnostic'
            }
            function attrib.exe {
                if ($args.Count -ne 5 -or $args[0] -ne '-R' -or $args[1] -ne '-S' -or $args[2] -ne '-H' -or $args[3] -ne $expected -or $args[4] -ne '/L') { throw 'Unexpected attribute scope' }
                $script:attributes++
                if ($scenario -eq 'attributeFailure') { $global:LASTEXITCODE = 5; 'attribute diagnostic'; return }
                (Microsoft.PowerShell.Management\Get-Item -LiteralPath $expected).IsReadOnly = $false
                $global:LASTEXITCODE = 0
            }
            try {
                $caught = $null
                try { GENERATED } catch { $caught = $_ }
                if ($ErrorActionPreference -ne 'Stop') { throw 'Caller error preference changed' }
                if ($scenario -in @('plain', 'protected', 'missing', 'leafReparse', 'trustedFile', 'wimProtected', 'wofProtected')) {
                    if ($caught) { throw $caught }
                    if (Test-Path -LiteralPath $expected) { throw 'Setup file remains' }
                    $expectedCalls = if ($scenario -in @('protected', 'trustedFile', 'wimProtected', 'wofProtected')) { 1 } else { 0 }
                    if ($script:owns -ne $expectedCalls -or $script:grants -ne $expectedCalls) { throw 'Unexpected permission changes' }
                    $expectedTakes = if ($scenario -in @('trustedFile', 'wimProtected', 'wofProtected')) { 1 } else { 0 }
                    if ($script:takes -ne $expectedTakes) { throw 'Unexpected ownership fallback' }
                } else {
                    if (!$caught -or !$caught.Exception.Message.Contains($expected)) { throw "Missing failing path: $caught" }
                    if (!(Test-Path -LiteralPath $expected)) { throw 'Failure unexpectedly deleted file' }
                    if ($scenario -eq 'ownerFailure' -and ($script:grants -ne 0 -or $script:deletes -ne 1 -or $caught -notlike '*ownership diagnostic*')) { throw 'Ownership failure was ignored' }
                    if ($scenario -eq 'aclFailure' -and ($script:deletes -ne 1 -or $caught -notlike '*ACL diagnostic*')) { throw 'ACL failure was ignored' }
                    if ($scenario -eq 'attributeFailure' -and ($script:deletes -ne 1 -or $caught -notlike '*attribute diagnostic*')) { throw 'Attribute failure was ignored' }
                    if ($scenario -in @('protectedSymlink', 'reparseQueryFailure') -and ($script:takes -ne 0 -or $script:grants -ne 0 -or $caught -notlike '*Cannot safely take ownership*')) { throw 'Unverified reparse target was touched' }
                    if ($scenario -in @('directory', 'parentLink') -and ($script:deletes -ne 0 -or $script:owns -ne 0 -or $script:grants -ne 0)) { throw 'Unsafe target was touched' }
                }
                if ([IO.File]::ReadAllText($sentinel) -ne 'keep') { throw 'Unrelated file changed' }
            } finally {
                Microsoft.PowerShell.Management\Remove-Item -LiteralPath $mountDir -Recurse -Force
            }
            """.Replace("SCENARIO", scenario).Replace("GENERATED", script[start..end].Replace("[IO.File]::Delete($path)", "Remove-TestSetupFile $path"));
        await RunPowerShellHarness(harness);
    }

    [Theory]
    [InlineData("packages", 0)]
    [InlineData("empty", 0)]
    [InlineData("packages", 87)]
    public async Task AppxInventory_UsesSelectedDismAndChecksResults(string scenario, int exitCode)
    {
        var script = GenerateScript();
        Assert.DoesNotContain("Get-AppxProvisionedPackage", script);
        var start = script.IndexOf("$appxOutput =", StringComparison.Ordinal);
        var end = script.IndexOf("foreach ($package in $packagesToRemove)", start, StringComparison.Ordinal);
        var harness = """
            $ErrorActionPreference = 'Stop'
            $mountDir = 'C:\scratch folder\中文'
            $script:called = $false
            function Selected-Dism {
                if ($args.Count -ne 3 -or $args[0] -ne '/English' -or $args[1] -ne "/Image:$mountDir" -or $args[2] -ne '/Get-ProvisionedAppxPackages') { throw 'Wrong DISM arguments' }
                $script:called = $true
                $global:LASTEXITCODE = EXIT_CODE
                'Deployment Image Servicing and Management tool'
                if ('SCENARIO' -eq 'packages') {
                    'DisplayName : 中文应用'
                    'PackageName : Microsoft.BingNews_1.0.0.0_neutral_~_8wekyb3d8bbwe'
                    '  PackageName : Microsoft.BingWeather_2.0.0.0_neutral_~_8wekyb3d8bbwe  '
                    'PackageName : Microsoft.BingNews_1.0.0.0_neutral_~_8wekyb3d8bbwe'
                }
                'native diagnostic'
            }
            $dismPath = 'Selected-Dism'
            $caught = $null
            try { GENERATED } catch { $caught = $_ }
            if (!$script:called) { throw 'Selected DISM was not used' }
            if (EXIT_CODE -ne 0) {
                if (!$caught -or $caught.Exception.Message -notlike '*AppX inventory failed with exit code 87*native diagnostic*') { throw "Unexpected error: $caught" }
            } else {
                if ($caught) { throw $caught }
                if ('SCENARIO' -eq 'empty') {
                    if ($installedPackages.Count -ne 0) { throw 'Empty inventory failed' }
                } elseif ($installedPackages.Count -ne 2 -or $installedPackages[0] -ne 'Microsoft.BingNews_1.0.0.0_neutral_~_8wekyb3d8bbwe' -or $installedPackages[1] -ne 'Microsoft.BingWeather_2.0.0.0_neutral_~_8wekyb3d8bbwe') {
                    throw 'Package identities parsed incorrectly'
                }
            }
            """.Replace("EXIT_CODE", exitCode.ToString()).Replace("SCENARIO", scenario).Replace("GENERATED", script[start..end]);
        await RunPowerShellHarness(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServicingNeverRunsWhileOfflineRegistryHivesAreLoaded(bool core)
    {
        var options = new ComponentRemovalOptions
        {
            RemoveHyperV = true, RemoveInputComponents = true, CleanupDriverStore = true
        };
        var script = new PowerShellService(new LocalizationService()).PreviewScript(
            @"C:\windows.iso", @"C:\scratch", @"C:\tiny.iso", 1, options, core);
        var start = script.IndexOf("$softwareHiveLoaded = $true", StringComparison.Ordinal);
        var end = script.IndexOf("$ntuserHiveLoaded = $false", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        Assert.DoesNotContain("& $dismPath", script[start..end]);
        Assert.True(script.IndexOf("Running deep cleanup", StringComparison.Ordinal) > end);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("namespace")]
    [InlineData("empty")]
    public async Task CustomUnattend_HandlesReadOnlyUtf8AndRejectsInvalidRoots(string scenario)
    {
        var options = BareOptions();
        options.BypassMSAccount = true;
        options.CustomAutounattendPath = @"C:\custom.xml";
        var script = new PowerShellService(new LocalizationService()).PreviewScript(
            @"C:\windows.iso", @"C:\scratch", @"C:\tiny.iso", 1, options);
        var start = script.IndexOf("$unattendPath =", StringComparison.Ordinal);
        var end = script.IndexOf("# WIM dosyasını mount et", start, StringComparison.Ordinal);
        var harness = """
            $ErrorActionPreference = 'Stop'
            $isoDir = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
            New-Item -ItemType Directory -Path $isoDir | Out-Null
            $path = Join-Path $isoDir 'autounattend.xml'
            try {
                $content = switch ('SCENARIO') {
                    'valid' { '<unattend xmlns="urn:schemas-microsoft-com:unattend"><settings pass="specialize"><!--中文--></settings></unattend>' }
                    'namespace' { '<unattend />' }
                    'empty' { '' }
                }
                [IO.File]::WriteAllText($path, $content, (New-Object Text.UTF8Encoding($false)))
                (Get-Item -LiteralPath $path).IsReadOnly = $true
                $unattendArchitecture = 'amd64'
                $caught = $null
                try {
                    GENERATED
                } catch { $caught = $_ }
                if ('SCENARIO' -eq 'valid') {
                    if ($caught) { throw $caught }
                    $doc = New-Object Xml.XmlDocument
                    $doc.Load($path)
                    $ns = New-Object Xml.XmlNamespaceManager($doc.NameTable)
                    $ns.AddNamespace('u', 'urn:schemas-microsoft-com:unattend')
                    if ($doc.SelectSingleNode('//u:OOBE/u:HideOnlineAccountScreens', $ns).InnerText -ne 'true') { throw 'Account setting missing' }
                    if (!$doc.OuterXml.Contains('中文')) { throw 'Existing Chinese content lost' }
                    if ($doc.SelectSingleNode('//u:component', $ns).GetAttribute('language') -ne 'neutral') { throw 'Language changed' }
                } elseif (!$caught -or $caught.Exception.Message -notlike '*Invalid autounattend.xml*') {
                    throw 'Invalid XML was not rejected clearly'
                }
            } finally {
                Remove-Item -LiteralPath $path -Force
                Remove-Item -LiteralPath $isoDir
            }
            """.Replace("SCENARIO", scenario).Replace("GENERATED", script[start..end]);
        await RunPowerShellHarness(harness);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    [InlineData(-2146498555)]
    [InlineData(32)]
    public async Task CoreRemoval_OnlyToleratesKnownInvalidPackageAndSuccess(int exitCode)
    {
        var script = new PowerShellService(new LocalizationService()).PreviewScript(
            @"C:\windows.iso", @"C:\scratch", @"C:\tiny.iso", 1, BareOptions(), true);
        var start = script.IndexOf("foreach ($pattern in $corePackagePatterns)", StringComparison.Ordinal);
        var end = script.IndexOf("$winRePath =", start, StringComparison.Ordinal);
        var harness = """
            $ErrorActionPreference = 'Stop'
            function Mock-Dism { $global:LASTEXITCODE = EXIT_CODE; 'native diagnostic' }
            $dismPath = 'Mock-Dism'
            $mountDir = 'unused'
            $corePackagePatterns = @('Microsoft-Windows-InternetExplorer-Optional-Package*')
            $packageNames = @('Microsoft-Windows-InternetExplorer-Optional-Package~31bf3856ad364e35~amd64~zh-CN~10.0.26100.1')
            $caught = $null
            try { GENERATED } catch { $caught = $_ }
            if (EXIT_CODE -eq 32) {
                if (!$caught -or $caught.Exception.Message -notlike '*exit code 32*native diagnostic*') { throw 'Unexpected failure handling' }
            } elseif ($caught) { throw $caught }
            """.Replace("EXIT_CODE", exitCode.ToString()).Replace("GENERATED", script[start..end]);
        await RunPowerShellHarness(harness);
    }

    private static async Task RunPowerShellHarness(string harness)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(harness)));
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, $"{await stdout}\n{await stderr}");
    }

    private static ComponentRemovalOptions BareOptions() => new()
    {
        RemoveEdge = false,
        RemoveOneDrive = false,
        RemoveCortana = false,
        RemoveTeams = false,
        RemoveXbox = false,
        DisableTelemetry = false,
        DisableWindowsUpdate = false,
        DisableSponsoredApps = false,
        DisableReservedStorage = false,
        DisableBitLocker = false,
        DisableDefender = false,
        BypassTPM = false,
        BypassCPU = false,
        BypassRAM = false,
        BypassSecureBoot = false,
        BypassMSAccount = false,
        SkipNetworkConnection = false,
        SkipPrivacyQuestions = false,
        CleanupComponentStore = false,
        CompressFinalImage = false,
        RemoveHyperV = false,
        RemoveRecall = false,
        RemoveWidgets = false,
        RemoveCopilot = false,
        RemoveInputComponents = false,
        CleanupDriverStore = false
    };

    private static string GenerateScript(
        string isoPath = @"C:\images\windows.iso",
        string scratchPath = @"C:\scratch",
        string outputPath = @"C:\output\tiny11.iso",
        int editionIndex = 3)
    {
        var service = new PowerShellService(new LocalizationService());
        return service.PreviewScript(isoPath, scratchPath, outputPath, editionIndex, new ComponentRemovalOptions());
    }

    [Fact]
    public void EsdExport_ValidatesResultAndUsesSingleImageIndex()
    {
        var script = GenerateScript();
        var export = script.IndexOf("Assert-NativeSuccess 'ESD to WIM export'", StringComparison.Ordinal);
        var resetIndex = script.IndexOf("$editionIndex = 1", StringComparison.Ordinal);
        var mount = script.IndexOf("/mount-wim /wimfile:$wimPath /index:$editionIndex", StringComparison.Ordinal);

        Assert.True(export >= 0);
        Assert.True(resetIndex > export);
        Assert.True(mount > resetIndex);
    }

    [Fact]
    public void Script_HasFailFastAndOwnedResourceCleanup()
    {
        var script = GenerateScript();

        Assert.Contains("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("} catch {", script);
        Assert.Contains("} finally {", script);
        Assert.Contains("if ($wimMounted -and (Test-Path $mountDir))", script);
        Assert.Contains("if ($isoMounted)", script);
        Assert.DoesNotContain("/cleanup-wim", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetProcessesByName", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsoOutput_IsBuiltTemporarilyAndAtomicallyPublished()
    {
        var script = GenerateScript();

        Assert.Contains("$temporaryOutputPath", script);
        Assert.Contains("Assert-NativeSuccess 'ISO creation'", script);
        Assert.Contains("[System.IO.File]::Replace($temporaryOutputPath, $outputPath", script);
        Assert.Contains("[System.IO.File]::Move($temporaryOutputPath, $outputPath)", script);
        Assert.DoesNotContain("$isoDir $outputPath", script);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task InitialRegistryCleanup_SkipsAbsentHivesAndChecksNativeFailures(bool hiveExists, int exitCode)
    {
        var script = GenerateScript();
        Assert.Contains("Clear-InitialRegistryHive 'HKLM\\OFFLINE_SOFTWARE'", script);
        Assert.Contains("Clear-InitialRegistryHive 'HKLM\\OFFLINE_SYSTEM'", script);
        Assert.Contains("Clear-InitialRegistryHive 'HKU\\OFFLINE_NTUSER'", script);
        Assert.Contains("[Console]::Error.WriteLine($_.InvocationInfo.PositionMessage)", script);
        var start = script.IndexOf("function Clear-InitialRegistryHive", StringComparison.Ordinal);
        var end = script.IndexOf("function Set-OfflineRegistryValue", start, StringComparison.Ordinal);
        var helper = script[start..end];
        // Exercise the generated helper in the same Windows PowerShell used by the app.
        // Mock registry access; only cmd.exe runs, so this never unloads real hives.
        var harness = """
            $ErrorActionPreference = 'Stop'
            $script:unloadCalls = 0
            function Test-Path { param($LiteralPath, $ErrorAction); return HIVE_EXISTS }
            function reg.exe {
                param($operation, $key)
                $script:unloadCalls++
                if ($operation -ne 'unload' -or $key -ne 'HKLM\OFFLINE_SOFTWARE') { throw 'Unexpected registry command' }
                & cmd.exe /d /c 'echo Simulated native stderr 1>&2 & exit /b NATIVE_EXIT'
                $global:LASTEXITCODE = $LASTEXITCODE
            }
            HELPER
            $caught = $null
            try { Clear-InitialRegistryHive 'HKLM\OFFLINE_SOFTWARE' } catch { $caught = $_ }
            if ($ErrorActionPreference -ne 'Stop') { throw 'Caller error preference changed' }
            if ($script:unloadCalls -ne EXPECTED_CALLS) { throw 'Unexpected unload count' }
            if (EXPECT_FAILURE) {
                if (!$caught -or $caught.Exception.Message -notlike '*Initial registry cleanup failed*exit code 1*Simulated native stderr*') {
                    throw "Missing actionable native failure: $caught"
                }
            } elseif ($caught) { throw $caught }
            """
            .Replace("HIVE_EXISTS", hiveExists ? "$true" : "$false")
            .Replace("NATIVE_EXIT", exitCode.ToString())
            .Replace("EXPECTED_CALLS", hiveExists ? "1" : "0")
            .Replace("EXPECT_FAILURE", hiveExists && exitCode != 0 ? "$true" : "$false")
            .Replace("HELPER", helper);
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(harness)));
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.True(process.ExitCode == 0, $"{await stdout}\n{await stderr}");
    }

    [Fact]
    public void UserPaths_EscapePowerShellSingleQuotes()
    {
        var script = GenerateScript(
            @"C:\input\user's.iso",
            @"C:\scratch\builder's",
            @"C:\output\user's tiny.iso");

        Assert.Contains("$isoPath = 'C:\\input\\user''s.iso'", script);
        Assert.Contains("$scratchPath = 'C:\\scratch\\builder''s'", script);
        Assert.Contains("$outputPath = 'C:\\output\\user''s tiny.iso'", script);
    }

    [Fact]
    public void OptionalNativeOperations_AreCheckedAndNotSilentlyIgnored()
    {
        var service = new PowerShellService(new LocalizationService());
        var options = new ComponentRemovalOptions
        {
            RemoveHyperV = true,
            RemoveRecall = true,
            RemoveInputComponents = true,
            CleanupDriverStore = true,
            CleanupComponentStore = true,
            CompressFinalImage = true
        };
        var script = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, options);

        Assert.Contains("Hyper-V removal failed with exit code $hyperVExit", script);
        Assert.Contains("Assert-NativeSuccess \"Capability removal: $name\"", script);
        Assert.Contains("Assert-NativeSuccess \"Driver removal: $pubName\"", script);
        Assert.Contains("Assert-NativeSuccess 'Component store cleanup'", script);
        Assert.Contains("Assert-NativeSuccess 'Final WIM compression'", script);
        Assert.DoesNotContain("try { Remove-Matching", script);
    }

    [Fact]
    public void BaseTinyPackageRemoval_RemainsEnabledWithoutOptionalApplicationChoices()
    {
        var packages = BareOptions().GetPackagesToRemove();

        Assert.Contains("Microsoft.BingNews", packages);
        Assert.Contains("Microsoft.WindowsFeedbackHub", packages);
        Assert.DoesNotContain(packages, package => package.Contains("Teams", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TeamsAndChat_IsOneFunctionalPackageFamilyChoice()
    {
        var options = BareOptions();
        options.RemoveTeams = true;

        var packages = options.GetPackagesToRemove();

        Assert.Contains("MicrosoftTeams", packages);
        Assert.Contains("MSTeams", packages);
        Assert.Contains("Microsoft.Windows.Teams", packages);
    }

    [Fact]
    public void DefenderOption_AppliesDefenderServicingToStandardBuild()
    {
        var service = new PowerShellService(new LocalizationService());
        var baseline = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, BareOptions());
        var options = BareOptions();
        options.DisableDefender = true;
        var standard = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, options);

        Assert.DoesNotContain("WinDefend", baseline);
        Assert.DoesNotContain("Windows-Defender-Client-Package", baseline);
        Assert.Contains("WinDefend", standard);
        Assert.Contains("Windows-Defender-Client-Package*", standard);
        Assert.Contains("Assert-NativeSuccess 'Defender package inventory'", standard);
        // CORE'a özgü yıkıcı işlemler standart build'e sızmamalı.
        Assert.DoesNotContain(@"Windows\System32\Recovery\winre.wim", standard);
        Assert.DoesNotContain(@"Services\wuauserv", standard);
    }

    [Fact]
    public void CoreBuild_ProducesAdditionalDestructiveOperations()
    {
        var service = new PowerShellService(new LocalizationService());
        var standard = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, BareOptions());
        var core = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, BareOptions(), isCoreBuild: true);

        Assert.Contains("# Tiny11 Builder - Standard Script", standard);
        Assert.DoesNotContain("CORE package inventory", standard);
        Assert.Contains("# Tiny11 Builder - CORE Script", core);
        Assert.Contains("CORE package inventory", core);
        Assert.Contains("Windows\\System32\\Recovery\\winre.wim", core);
        Assert.Contains("ControlSet001\\Services\\wuauserv", core);
        Assert.Contains("WinDefend", core);
    }

    [Fact]
    public void OobeCheckboxes_EmitSupportedUnattendSettingsIndependently()
    {
        var service = new PowerShellService(new LocalizationService());
        var options = BareOptions();
        options.SkipPrivacyQuestions = true;
        var privacy = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, options);
        options.SkipPrivacyQuestions = false;
        options.SkipNetworkConnection = true;
        var network = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, options);
        options.SkipNetworkConnection = false;
        options.BypassMSAccount = true;
        var account = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, options);

        Assert.Contains("Set-OobeValue 'ProtectYourPC' '3'", privacy);
        Assert.DoesNotContain("HideWirelessSetupInOOBE", privacy);
        Assert.Contains("Set-OobeValue 'HideWirelessSetupInOOBE' 'true'", network);
        Assert.DoesNotContain("HideOnlineAccountScreens", network);
        Assert.Contains("Set-OobeValue 'HideOnlineAccountScreens' 'true'", account);
        Assert.DoesNotContain("bypassnro.cmd", account, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalizedDismInventoriesAndRegistryWrites_AreChecked()
    {
        var options = BareOptions();
        options.RemoveRecall = true;
        options.CleanupDriverStore = true;
        options.BypassTPM = true;
        var service = new PowerShellService(new LocalizationService());
        var script = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, options);

        Assert.Contains("/English /Image:$mountDir /Get-Capabilities", script);
        Assert.Contains("/English /Image:$mountDir /Get-Drivers", script);
        Assert.Contains("Assert-NativeSuccess \"Registry write: $key\\$name\"", script);
        Assert.Contains("Assert-NativeSuccess \"AppX removal: $match\"", script);
        Assert.DoesNotContain("DisableAntiSpyware", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-AppxProvisionedPackage", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryBooleanOption_ChangesTheGeneratedBuildScript()
    {
        var service = new PowerShellService(new LocalizationService());
        var baseline = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, BareOptions());
        var booleanOptions = typeof(ComponentRemovalOptions).GetProperties().Where(property => property.PropertyType == typeof(bool));

        foreach (var property in booleanOptions)
        {
            var options = BareOptions();
            property.SetValue(options, true);
            var script = service.PreviewScript(@"C:\images\windows.iso", @"C:\scratch", @"C:\output\tiny11.iso", 3, options);

            Assert.NotEqual(baseline, script);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GeneratedScripts_HaveValidPowerShellSyntax(bool isCoreBuild, bool useCustomUnattend)
    {
        var options = new ComponentRemovalOptions
        {
            RemoveXbox = true,
            DisableWindowsUpdate = true,
            RemoveHyperV = true,
            RemoveInputComponents = true,
            CleanupDriverStore = true,
            CustomAutounattendPath = useCustomUnattend ? @"C:\answers\custom user's autounattend.xml" : null
        };
        var service = new PowerShellService(new LocalizationService());
        var script = service.PreviewScript(
            @"C:\images\windows.iso",
            @"C:\scratch",
            @"C:\output\tiny11.iso",
            3,
            options,
            isCoreBuild);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"tiny11-script-test-{Guid.NewGuid():N}.ps1");

        try
        {
            File.WriteAllText(scriptPath, script);
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("$tokens=$null; $errors=$null; [System.Management.Automation.Language.Parser]::ParseFile($env:TINY11_SCRIPT_TEST_PATH, [ref]$tokens, [ref]$errors) | Out-Null; if ($errors.Count) { $errors | ForEach-Object { [Console]::Error.WriteLine($_.Message) }; exit 1 }");
            startInfo.Environment["TINY11_SCRIPT_TEST_PATH"] = scriptPath;

            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"PowerShell parser rejected the generated script.\n{standardOutput}\n{standardError}");
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}
