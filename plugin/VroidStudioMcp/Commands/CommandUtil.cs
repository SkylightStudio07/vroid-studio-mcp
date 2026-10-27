using System;
using System.Collections;
using System.Collections.Generic;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Shared helpers for command handlers: coroutine waits, argument resolution and the common
/// category-entry sequence.</summary>
public static class CommandUtil
{
    public static IEnumerator Done() { yield break; }

    internal static IEnumerator StablePreview(Action<string> completed)
    {
        string previous = null;
        int stable = 0;
        float elapsed = 0;
        while (stable < 3 && elapsed < 15f)
        {
            yield return null;
            elapsed += UnityEngine.Time.unscaledDeltaTime;
            if (VroidState._modelUpdating) { stable = 0; continue; }
            string hash = Screenshot.ShotHash();
            stable = !Screenshot.LastHasContent || hash == "null" || hash == "no-rt" ? 0 : hash == previous ? stable + 1 : 1;
            previous = hash;
        }
        completed(stable >= 3 ? previous : null);
    }

    // Waits use UNSCALED time on purpose: a modal dialog may set Time.timeScale = 0 (e.g. while a
    // model loads), and scaled deltaTime then freezes -> the handler never completes and the whole
    // command queue stalls. Unscaled time keeps the control channel alive.
    public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float t = 0f;
        while (!condition() && t < timeoutSeconds) { t += UnityEngine.Time.unscaledDeltaTime; yield return null; }
    }

    public static IEnumerator Pause(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += UnityEngine.Time.unscaledDeltaTime; yield return null; }
    }

    public static IEnumerator Settle(float extra = 0.5f)
    {
        while (!Signals.Ready(8f)) yield return null;
        yield return Pause(extra);
    }

    public static string Arg(CommandDispatcher.Context ctx, int i) => i < ctx.Args.Length ? ctx.Args[i] : null;

    // Resolve a section argument, setting a precise error on ctx (missing arg vs unknown name).
    // Returns null when the caller must bail (ctx.Error is already set).
    public static string ResolveSectionArg(CommandDispatcher.Context ctx, int i, string usage)
    {
        var raw = Arg(ctx, i);
        if (string.IsNullOrEmpty(raw)) { ctx.Error = usage; return null; }
        var s = Sections.ResolveSection(raw);
        if (s == null) { ctx.Error = $"unknown section '{raw}'; expected one of: {Sections.LabelsText()}"; return null; }
        return s;
    }

    // Actionable hint for an unknown tab: list the section's editable tab names (empty when the
    // selector is not loaded, so the error degrades to the plain form).
    public static string TabHint(string section)
    {
        var names = Categories.TabNames(Categories.FindSelectorByShort(section + ".PresetsSelectorViewModel"));
        return names.Count > 0 ? "; available: " + string.Join(", ", names) : "";
    }

    public static bool SelectorReady(string section)
    {
        var selector = Categories.FindSelectorByShort(section + ".PresetsSelectorViewModel");
        if (selector == null) return false;
        var probe = new List<object>();
        Categories.CollectCategories(Get(selector, "Categories"), probe);
        bool loading = Get(selector, "IsLoading") is bool b && b;
        return probe.Count > 0 && !loading;
    }

    public static IEnumerator EnterSection(string section)
    {
        Sections.SwitchSectionForCat(section);
        yield return WaitUntil(() => SelectorReady(section), 15f);
    }

    public static object EnterCategory(string section, string tab)
    {
        var selector = Categories.FindSelectorByShort(section + ".PresetsSelectorViewModel");
        var category = Categories.FindTab(selector, tab);
        // Arm before the mutation so a following Settle() actually waits for this category's engine
        // signal instead of relying on a stale baseline from an earlier Arm.
        if (category != null) { Signals.Arm(); Invoke(selector, "set_SelectedCategory", category); }
        return category;
    }

    public static bool IsPreviewMode(string mode) =>
        mode == "file" || mode == "inline" || mode == "both" || mode == "b64";
}
