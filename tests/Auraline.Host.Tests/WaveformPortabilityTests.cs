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

    [Fact]
    public void TransportContractsAndSessionDomainShouldNotReferenceWindowsOnlyApis()
    {
        var repoRoot = FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        var paths = new[]
        {
            Path.Combine(repoRoot.FullName, "src", "Auraline.Contracts"),
            Path.Combine(repoRoot.FullName, "src", "Auraline.Host", "RenderSessions")
        };
        var forbiddenTokens = new[]
        {
            "System.IO.MemoryMappedFiles",
            "MemoryMappedFile",
            "Microsoft.Win32",
            "System.Windows.Forms",
            "InfoPanel.Plugins"
        };

        foreach (var directory in paths)
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var token in forbiddenTokens)
                    Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
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
