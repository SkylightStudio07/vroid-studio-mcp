using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VroidStudioMcp;

/// <summary>
/// Fuzzy reproduction of the "control channel hangs when a modal pauses the app" bug: a modal can
/// set <c>Time.timeScale = 0</c> (e.g. while a heavy model is loading), and any handler that waits on
/// scaled <c>Time.deltaTime</c> then never completes, stalling the whole command queue.
///
/// The dispatcher's waits must use unscaled time. Here we drive those waits manually with an
/// unscaled watchdog, so a regression is reported as FAIL (and the queue stays alive) instead of
/// hanging forever. No model needed.
/// </summary>
public sealed class TimeScaleFuzzTest : VroidTest
{
    private bool _driveOk;

    protected override IEnumerator Run()
    {
        float saved = Time.timeScale;
        var rng = new System.Random(0x5EED);
        var cases = new List<object>();
        bool allOk = true;
        try
        {
            var scales = new List<float>();
            for (int i = 0; i < 8; i++)
            {
                // every other case is exactly 0 (the dangerous value); the rest are small positives
                if (i % 2 == 0) scales.Add(0f);
                else scales.Add((float)(rng.NextDouble() * 0.5));
            }
            scales.Add(0f);

            foreach (var ts in scales)
            {
                Time.timeScale = ts;

                yield return Drive(CommandDispatcher.Pause(0.15f), 2f);
                bool pauseOk = _driveOk;

                int ticks = 1 + rng.Next(4);
                int n = 0;
                yield return Drive(CommandDispatcher.WaitUntil(() => n++ >= ticks, 2f), 2f);
                bool condOk = _driveOk;

                yield return Drive(CommandDispatcher.WaitUntil(() => false, 0.05f), 2f);
                bool timeoutOk = _driveOk;

                bool ok = pauseOk && condOk && timeoutOk;
                allOk &= ok;
                cases.Add(new Dictionary<string, object>
                {
                    ["timeScale"] = ts,
                    ["pause"] = pauseOk,
                    ["condition"] = condOk,
                    ["timeout"] = timeoutOk,
                });
            }
        }
        finally
        {
            Time.timeScale = saved;
        }

        var detail = new Dictionary<string, object>
        {
            ["cases"] = cases,
            ["restoredTimeScale"] = Time.timeScale,
        };
        Report("timescalefuzz", allOk, detail);
        WriteResult("timescalefuzz_result.json", new Dictionary<string, object> { ["ok"] = allOk, ["detail"] = detail });
    }

    /// <summary>Step a dispatcher wait manually; <see cref="_driveOk"/> is false if it does not
    /// finish within a real-time budget (i.e. it hung on scaled time).</summary>
    private IEnumerator Drive(IEnumerator it, float budgetSeconds)
    {
        _driveOk = false;
        float t0 = Time.realtimeSinceStartup;
        while (true)
        {
            bool moved;
            try { moved = it.MoveNext(); }
            catch { break; }
            if (!moved) { _driveOk = true; break; }
            if (Time.realtimeSinceStartup - t0 > budgetSeconds) break;
            yield return null;
        }
    }
}
