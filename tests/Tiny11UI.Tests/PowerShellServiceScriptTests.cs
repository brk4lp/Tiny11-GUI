using tiny11_ui.Models;
using tiny11_ui.Services;
using Xunit;
using System.Diagnostics;

namespace Tiny11UI.Tests;

public class PowerShellServiceScriptTests
{
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

        Assert.Contains("Assert-NativeSuccess 'Hyper-V removal'", script);
        Assert.Contains("Assert-NativeSuccess \"Capability removal: $name\"", script);
        Assert.Contains("Assert-NativeSuccess \"Driver removal: $pubName\"", script);
        Assert.Contains("Assert-NativeSuccess 'Component store cleanup'", script);
        Assert.Contains("Assert-NativeSuccess 'Final WIM compression'", script);
        Assert.DoesNotContain("try { Remove-Matching", script);
    }

    [Fact]
    public void GeneratedScript_HasValidPowerShellSyntax()
    {
        var script = GenerateScript();
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
