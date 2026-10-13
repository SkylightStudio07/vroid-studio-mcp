using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine;
using static VroidStudioMcp.VroidState;
using static VroidStudioMcp.Screenshot;

namespace VroidStudioMcp;

/// <summary>Base for coroutine tests. The test just describes a linear sequence.</summary>
public abstract class VroidTest
{
    internal string CharacterPath;

    protected abstract IEnumerator Run();

    public IEnumerator Execute() => Run();

    protected static bool Ready()
    {
        var prt = ResolvePreviewRT();
        return Selectors.Count >= 4 && _cameraController != null && prt != null
            && (_modelReady || PreviewHasContent(prt));
    }

    protected IEnumerator Open()
    {
        FileOps.OpenCharacter(WinePaths.ToDos(CharacterPath));
        yield return WaitUntil(Ready, 40f);
    }

    protected static object SelectorFor(string section)
        => Categories.FindSelectorByShort(section + ".PresetsSelectorViewModel");

    protected static bool SelectorLoaded(object selector)
    {
        if (selector == null) return false;
        var probe = new List<object>();
        Categories.CollectCategories(Interop.Get(selector, "Categories"), probe);
        bool loading = Interop.Get(selector, "IsLoading") is bool b && b;
        return probe.Count > 0 && !loading;
    }

    protected IEnumerator SwitchSection(string section)
    {
        Sections.SwitchSectionForCat(section);
        yield return WaitUntil(() => SelectorLoaded(SelectorFor(section)), 15f);
    }

    private static readonly string[] AllSections = { "FaceEditor", "HairEditor", "BodyEditor", "ClothingEditor" };

    /// <summary>Select every tab of every section (instantiates lazy VMs, e.g. color selections).</summary>
    protected IEnumerator WalkAllCategories()
    {
        foreach (var section in AllSections)
        {
            yield return SwitchSection(section);
            var selector = SelectorFor(section);
            foreach (var tab in Interop.Enumerate(Interop.Get(Interop.Get(selector, "Categories"), "Children")).ToList())
            {
                Interop.Invoke(selector, "set_SelectedCategory", tab);
                yield return Pause(0.5f);
            }
        }
    }

    protected IEnumerator EnterCategory(string section, string tab)
    {
        var selector = SelectorFor(section);
        var t = Categories.FindTab(selector, tab);
        if (t != null) Interop.Invoke(selector, "set_SelectedCategory", t);
        yield return Pause(0.6f);
    }

    protected IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float t = 0f;
        while (!condition() && t < timeoutSeconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    protected IEnumerator Pause(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    /// <summary>Wait for the engine change signal, plus a small settle pause.</summary>
    protected IEnumerator Settle(float extra = 0.5f)
    {
        while (!Signals.Ready(8f)) yield return null;
        yield return Pause(extra);
    }

    protected string Shot() => ShotHash();

    /// <summary>Last hash produced by <see cref="StableShot"/>/<see cref="StableRegionHash"/>.</summary>
    protected string Stable;

    /// <summary>Capture the preview hash once 3 consecutive frames agree (scene settled); avoids
    /// comparing a half-rendered frame. Stores the result in <see cref="Stable"/>.</summary>
    protected IEnumerator StableShot(int need = 3, float timeout = 12f)
    {
        string last = null; int stable = 0; float t = 0f;
        while (t < timeout)
        {
            var h = Shot();
            if (h == last) { stable++; if (stable >= need) break; }
            else { stable = 1; last = h; }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        Stable = last;
    }

    /// <summary>Same as <see cref="StableShot"/> but for a fixed fractional region of the whole
    /// window (UI included), e.g. the section switcher.</summary>
    protected IEnumerator StableRegionHash(float fx, float fy, float fw, float fh, int need = 3, float timeout = 12f)
    {
        string last = null; int stable = 0; float t = 0f;
        while (t < timeout)
        {
            var h = WindowRegionHash(fx, fy, fw, fh);
            if (h == last) { stable++; if (stable >= need) break; }
            else { stable = 1; last = h; }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        Stable = last;
    }

    protected void Report(string name, bool ok, object detail = null)
        => PluginLog.LogWarning($"TEST {(ok ? "PASS" : "FAIL")} {name} {MiniJson.Serialize(detail, false)}");

    protected void WriteResult(string file, object payload) => Logging.WriteJson(file, payload);
}
