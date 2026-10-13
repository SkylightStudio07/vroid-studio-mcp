using System.Diagnostics;
using BepInEx.Logging;

namespace VroidStudioMcp;

/// <summary>
/// Minimal timing instrumentation. Emits a single Info line per milestone so startup / model-build
/// regressions are visible without flooding the log. The stopwatch is shared across the plugin so
/// every line carries a monotonically increasing "since load" offset.
/// </summary>
public static class Perf
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static double SinceLoadMs => Clock.Elapsed.TotalMilliseconds;

    /// <summary>Start a standalone milestone timer (monotonic). Use with <see cref="Since"/>.</summary>
    public static Stopwatch Start() => Stopwatch.StartNew();

    public static void Mark(string tag, double elapsedMs)
        => PluginLog.LogInfo($"[perf] {tag}: {elapsedMs:F0} ms (t+{SinceLoadMs:F0})");

    /// <summary>Measure one milestone: returns the elapsed ms and logs "<tag>: <ms> ms".</summary>
    public static double Since(string tag, Stopwatch sw)
    {
        var ms = sw.Elapsed.TotalMilliseconds;
        Mark(tag, ms);
        return ms;
    }
}
