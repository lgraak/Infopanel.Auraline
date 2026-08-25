using System.Text.RegularExpressions;

namespace Auraline.Host.Tests;

public sealed class WaveformPortabilityTests
{
    [Fact]
    public void WaveformCoreSourceFilesShouldNotReferenceWindowsOnlyApis()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        var repoRoot = FindRepositoryRoot(current);
        var waveformDir = Path.Combine(repoRoot.FullName, "src", "Auraline.Host", "Waveform");

        var forbiddenTokens = new[]
        {
            "Microsoft.Win32",
            "System.Windows.Forms",
            "System.Runtime.InteropServices",
            "Windows",
            "PInvoke",
            "Microsoft.Win32.Registry"
        };

        foreach (var file in Directory.EnumerateFiles(waveformDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                var pattern = $@"\b{Regex.Escape(token)}\b";
                Assert.DoesNotMatch(new Regex(pattern, RegexOptions.IgnoreCase), text);
            }
        }
    }

    private static DirectoryInfo FindRepositoryRoot(DirectoryInfo start)
    {
        var current = start;
        while (current.Parent is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))) return current;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root while validating waveform portability.");
    }
}
