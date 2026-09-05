using System.Text.RegularExpressions;
using Xunit;

namespace Tiny11UI.Tests;

public class LocalizationResourceTests
{
    private static readonly string[] Cultures =
    {
        "tr-TR", "ru-RU", "ja-JP", "de-DE", "fr-FR", "es-ES", "zh-CN"
    };

    [Theory]
    [MemberData(nameof(SupportedCultures))]
    public void Translation_ContainsEveryReferencedKeyWithMatchingPlaceholders(string culture)
    {
        var root = FindRepositoryRoot();
        var english = ReadResource(Path.Combine(root, "Resources", "Strings.en-US.txt"));
        var translation = ReadResource(Path.Combine(root, "Resources", $"Strings.{culture}.txt"));
        var referencedKeys = GetReferencedKeys(Path.Combine(root, "src"));

        foreach (var key in referencedKeys)
        {
            Assert.True(english.ContainsKey(key), $"English fallback is missing referenced key '{key}'.");
            Assert.True(translation.ContainsKey(key), $"{culture} is missing referenced key '{key}'.");

            var expected = GetPlaceholders(english[key]);
            var actual = GetPlaceholders(translation[key]);
            Assert.Equal(expected, actual);
        }
    }

    public static IEnumerable<object[]> SupportedCultures => Cultures.Select(culture => new object[] { culture });

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tiny11-ui.csproj")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Tiny11 GUI repository root.");
    }

    private static Dictionary<string, string> ReadResource(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            Assert.True(values.TryAdd(key, line[(separator + 1)..]), $"Duplicate localization key '{key}' in {path}.");
        }

        return values;
    }

    private static HashSet<string> GetReferencedKeys(string sourceDirectory)
    {
        var expression = new Regex(@"(?:GetString|GetLocalizedString)\(\s*""([^""]+)""", RegexOptions.Compiled);
        return Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => expression.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string[] GetPlaceholders(string value) =>
        Regex.Matches(value, @"\{\d+(?::[^}]*)?\}")
            .Select(match => match.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
}
