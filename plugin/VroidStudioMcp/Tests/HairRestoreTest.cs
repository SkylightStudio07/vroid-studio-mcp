using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Restoring global hair colours after a fully empty Hairstyle section must be reliable
/// through the production path: clear every tab via `select_preset <clear>`, check the section is
/// empty (needs_preset), then select the Level-0 "Hairstyle Sets" preset and check the shared
/// colours come back on a non-Base tab.</summary>
public sealed class HairRestoreTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("HairEditor");

        var selector = SelectorFor("HairEditor");
        foreach (var tab in Enumerate(Get(Get(selector, "Categories"), "Children")).ToList())
        {
            var name = Get(tab, "Name") as string ?? Get(tab, "TranslatedDisplayName") as string;
            var presetNode = Get(tab, "PresetCategoryViewModel");
            var src = (object)presetNode ?? tab;
            var options = Enumerate(Get(src, "PresetOptions")).ToList();
            int clearIdx = options.FindIndex(o => (TypeName(o) ?? "").Contains("ClearOption"));
            if (string.IsNullOrEmpty(name) || clearIdx < 0) continue;
            var clear = new CommandDispatcher.Context { Method = "select_preset", Args = new[] { "Hairstyle", name, clearIdx.ToString() } };
            yield return CommandDispatcher.TestSelectPreset(clear);
        }

        var emptyFront = new CommandDispatcher.Context { Method = "enter", Args = new[] { "Hairstyle", "Front", "keep" } };
        yield return CommandDispatcher.TestEnter(emptyFront);
        var emptyRes = emptyFront.Result as Dictionary<string, object>;
        var emptyPars = emptyRes != null ? emptyRes["parameters"] as List<object> : null;
        bool empty = emptyFront.Error == null && (emptyPars == null || emptyPars.Count == 0)
            && emptyRes != null && emptyRes.TryGetValue("needs_preset", out var np) && np is bool nb && nb;

        // Restore through the production command.
        var restore = new CommandDispatcher.Context { Method = "select_preset", Args = new[] { "Hairstyle", "Hairstyle Sets", "1" } };
        yield return CommandDispatcher.TestSelectPreset(restore);

        var front = new CommandDispatcher.Context { Method = "enter", Args = new[] { "Hairstyle", "Front", "keep" } };
        yield return CommandDispatcher.TestEnter(front);
        var res = front.Result as Dictionary<string, object>;
        var pars = res != null ? res["parameters"] as List<object> : null;
        bool restored = pars != null && pars.OfType<Dictionary<string, object>>()
            .Any(p => (string)p["path"] == "Hairstyle/Main" && (string)p["name"] == "Main");

        bool ok = empty && restored;
        WriteResult("hairrestore_result.json", new Dictionary<string, object>
        {
            ["ok"] = ok,
            ["empty"] = empty,
            ["restored"] = restored,
            ["restore_error"] = restore.Error,
            ["empty_error"] = emptyFront.Error,
            ["enter_error"] = front.Error,
        });
        Report("hairrestore", ok, new Dictionary<string, object> { ["empty"] = empty, ["restored"] = restored });
    }
}
