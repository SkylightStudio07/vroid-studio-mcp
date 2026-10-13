using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using static VroidStudioMcp.Interop;
using static VroidStudioMcp.VroidState;

namespace VroidStudioMcp;

/// <summary>Deterministic checks for the MCP-facing behaviours: readiness after open, eager color
/// counts, save-as, and resetting state when returning to the model select.</summary>
public sealed class McpTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return WaitUntil(() => _modelReady, 15f);

        bool inEditAfterOpen = _inEdit;
        bool readyAfterOpen = _modelReady;
        Report("in_edit", inEditAfterOpen, inEditAfterOpen);
        Report("ready", readyAfterOpen, readyAfterOpen);

        int iris = Colors.CountForType(SelectorFor("FaceEditor"), "TransferableGroupType.N00.Level3.Iris");
        Report("eager-iris-colors", iris == 3, iris);

        var p = Path.Combine(BepInEx.Paths.GameRootPath, "mcp_saveas_test.vroid");
        if (File.Exists(p)) File.Delete(p);
        bool saveOk = false;
        try { saveOk = Invoke(MainActionHandler(), "SaveSync", p, true) is bool b && b; } catch { }
        yield return Pause(3f);
        bool saveAsOk = saveOk && File.Exists(p);
        Report("save-as", saveAsOk, p);

        Set(MainActionHandler(), "IsUnSaved", false);
        if (_editRootVm != null) Invoke(_editRootVm, "BackToModelSelect");
        yield return Pause(1.5f);
        bool menuReset = !_inEdit && !_modelReady;
        Report("menu-reset", menuReset, new Dictionary<string, object> { ["in_edit"] = _inEdit, ["ready"] = _modelReady });

        WriteResult("mcp_test_result.json", new Dictionary<string, object>
        {
            ["in_edit"] = inEditAfterOpen,
            ["ready"] = readyAfterOpen,
            ["iris_colors"] = iris,
            ["save_as_ok"] = saveAsOk,
            ["menu_reset"] = menuReset,
        });
    }
}
