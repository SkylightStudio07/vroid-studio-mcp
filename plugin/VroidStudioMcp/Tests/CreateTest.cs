using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using static VroidStudioMcp.Interop;
using static VroidStudioMcp.VroidState;

namespace VroidStudioMcp;

/// <summary>New-model creation via the Male/Female template: readiness, the "main menu first" guard,
/// and that the two templates produce distinct renders.</summary>
public sealed class CreateTest : VroidTest
{
    private bool _ok;
    private string _error;
    private string _hash;

    private IEnumerator ToModelSelect()
    {
        var mah = MainActionHandler();
        if (mah != null) Set(mah, "IsUnSaved", false);
        if (_inEdit && _editRootVm != null) Invoke(_editRootVm, "BackToModelSelect");
        yield return WaitUntil(() => !_inEdit, 10f);
        yield return Pause(0.5f);
    }

    private IEnumerator RunCreate(string gender)
    {
        var ctx = new CommandDispatcher.Context { Method = "create", Args = new[] { gender } };
        yield return CommandDispatcher.TestCreate(ctx);
        _ok = ctx.Error == null && _inEdit && _modelReady;
        _error = ctx.Error;
        _hash = _ok ? Shot() : null;
    }

    protected override IEnumerator Run()
    {
        yield return ToModelSelect();

        yield return RunCreate("female");
        bool femaleOk = _ok, femaleHashSet = _hash != null;
        string femaleHash = _hash, femaleErr = _error;
        Report("create-female", femaleOk, femaleErr);

        // While a model is open the guard must refuse and not touch the current model.
        var guard = new CommandDispatcher.Context { Method = "create", Args = new[] { "male" } };
        yield return CommandDispatcher.TestCreate(guard);
        bool guardOk = guard.Error != null && _inEdit;
        Report("create-guard", guardOk, guard.Error);

        var path = Path.Combine(BepInEx.Paths.GameRootPath, "create_saveas_test.vroid");
        if (File.Exists(path)) File.Delete(path);
        bool saveOk = false;
        try { saveOk = Invoke(MainActionHandler(), "SaveSync", path, true) is bool b && b; } catch { }
        yield return Pause(3f);
        bool saveAsOk = saveOk && File.Exists(path);
        Report("create-save-as", saveAsOk, path);

        yield return ToModelSelect();

        yield return RunCreate("male");
        bool maleOk = _ok;
        string maleHash = _hash, maleErr = _error;
        Report("create-male", maleOk, maleErr);

        bool distinct = femaleOk && maleOk && femaleHashSet && maleHash != null && femaleHash != maleHash;
        Report("create-distinct", distinct, new Dictionary<string, object> { ["female"] = femaleHash, ["male"] = maleHash });

        yield return ToModelSelect();

        WriteResult("create_result.json", new Dictionary<string, object>
        {
            ["female_ok"] = femaleOk,
            ["female_err"] = femaleErr,
            ["male_ok"] = maleOk,
            ["male_err"] = maleErr,
            ["guard_ok"] = guardOk,
            ["save_as_ok"] = saveAsOk,
            ["distinct"] = distinct,
            ["female_hash"] = femaleHash,
            ["male_hash"] = maleHash,
        });
    }
}
