using Serilog.Core;
using Serilog.Events;

namespace Auraline.Host.Diagnostics;

public sealed class DiagnosticLogLevel(LoggingLevelSwitch levelSwitch)
{
    public string Current => levelSwitch.MinimumLevel == LogEventLevel.Debug ? "Debug" : "Info";

    public void Set(string level) => levelSwitch.MinimumLevel = level.Equals("Debug", StringComparison.OrdinalIgnoreCase)
        ? LogEventLevel.Debug
        : level.Equals("Info", StringComparison.OrdinalIgnoreCase)
            ? LogEventLevel.Information
            : throw new ArgumentException("Logging level must be Info or Debug.", nameof(level));
}
