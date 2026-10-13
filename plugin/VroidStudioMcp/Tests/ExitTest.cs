using System;
using System.Collections;
using static VroidStudioMcp.Interop;


namespace VroidStudioMcp;

/// <summary>Make an edit (dirty), then let the runner quit cleanly (no Unsaved dialog).</summary>
public sealed class ExitTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("BodyEditor");

        var selector = SelectorFor("BodyEditor");
        var tab = Categories.FindTab(selector, "Whole Body");
        if (tab != null) Invoke(selector, "set_SelectedCategory", tab);
        yield return Pause(0.9f);

        var param = TestSupport.FindParamByLabel(Get(tab, "ParameterCategoryViewModel"), "Fem Height");
        if (param != null)
        {
            Signals.Arm();
            Invoke(param, "set_CombinedValue", 0.6f);
            yield return Settle(1.5f);
        }

        var handler = VroidState.MainActionHandler();
        var unsaved = handler != null ? Get(handler, "IsUnSaved") : null;
        PluginLog.LogWarning($"exittest: IsUnSaved before QuitClean = {unsaved}");
    }
}
