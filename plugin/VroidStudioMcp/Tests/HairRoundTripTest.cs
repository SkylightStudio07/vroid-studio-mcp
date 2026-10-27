using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VroidStudioMcp;

public sealed class HairRoundTripTest : VroidTest
{
    private static CommandDispatcher.Context C(params string[] args) => new() { Args = args };
    private string _hash;
    private IEnumerator StableFrame()
    {
        int same = 0;
        float elapsed = 0;
        _hash = null;
        while (same < 3 && elapsed < 15)
        {
            yield return null;
            elapsed += UnityEngine.Time.unscaledDeltaTime;
            string hash = Screenshot.ShotHash();
            same = Screenshot.LastHasContent && hash != "null" && hash != "no-rt" ? hash == _hash ? same + 1 : 1 : 0;
            _hash = hash;
        }
        if (same < 3) _hash = null;
    }
    private void Finish(bool ok, object detail)
    {
        var result = new Dictionary<string,object> { ["ok"] = ok, ["detail"] = detail };
        WriteResult("hair_roundtrip_result.json", result);
        Report("hair_roundtrip", ok, detail);
        FileOps.QuitClean();
    }
    protected override IEnumerator Run()
    {
        var open = C(CharacterPath);
        yield return SessionHandlers.Open(open);
        if (open.Error != null) { Finish(false, open.Error); yield break; }
        var list = C();
        yield return HairHandlers.List(list);
        var groups = (List<object>)((Dictionary<string,object>)list.Result)["groups"];
        var strand = groups.Cast<Dictionary<string,object>>().Where(g => (bool)g["visible"])
            .SelectMany(g => ((List<object>)g["strands"]).Cast<Dictionary<string,object>>())
            .FirstOrDefault(s => (bool)s["visible"] && (bool)s["move_supported"]);
        if (strand == null) { Finish(false, "Fixture requires visible brush hair"); yield break; }
        string id = (string)strand["strand_id"];
        var points = HairGeometry.Read(id);
        if (points.Length < 3) { Finish(false, "Fixture requires at least three points"); yield break; }
        var camera = C("frame", "head", "front");
        yield return CameraHandlers.Camera(camera);
        yield return StableFrame();
        string before = _hash;
        if (before == null) { Finish(false, "Baseline preview unstable"); yield break; }
        var move = C(id, (points.Length-2).ToString(), "-0.025", "0", "0", HairGeometry.Revision.ToString());
        yield return HairHandlers.Move(move);
        if (move.Error != null) { Finish(false, move.Error); yield break; }
        var moved = (Dictionary<string,object>)move.Result;
        bool changed = (string)moved["stable_preview_hash"] != before && HairGeometry.Hash(HairGeometry.Read(id)) != HairGeometry.Hash(points);
        var restore = C((string)moved["backup_id"], HairGeometry.Revision.ToString());
        yield return HairHandlers.Restore(restore);
        yield return StableFrame();
        bool restored = restore.Error == null && _hash == before && HairGeometry.Hash(HairGeometry.Read(id)) == HairGeometry.Hash(points);
        Finish(changed && restored, new Dictionary<string,object> { ["render_changed"] = changed,
            ["exact_restore"] = restored, ["restore_error"] = restore.Error });
    }
}
