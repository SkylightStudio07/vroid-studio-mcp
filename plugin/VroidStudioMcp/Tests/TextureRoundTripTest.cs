using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VroidStudioMcp;

public sealed class TextureRoundTripTest : VroidTest
{
    private static CommandDispatcher.Context C(params string[] args) => new() { Args = args };
    protected override IEnumerator Run()
    {
        var open = C(CharacterPath);
        yield return SessionHandlers.Open(open);
        if (open.Error != null) throw new InvalidOperationException(open.Error);
        var targets = C("Face");
        yield return TextureHandlers.Targets(targets);
        var target = ((List<object>)((Dictionary<string,object>)targets.Result)["targets"])
            .Cast<Dictionary<string,object>>().First(t => ((string)t["group_type"]).EndsWith(".Iris"));
        string id = (string)target["target_id"];
        var layers = C(id);
        yield return TextureHandlers.Layers(layers);
        string layer = (string)((Dictionary<string,object>)((List<object>)((Dictionary<string,object>)layers.Result)["layers"])[0])["layer_id"];
        var camera = C("frame","face","front");
        yield return CameraHandlers.Camera(camera);
        yield return StableShot();
        if (!Screenshot.LastHasContent) throw new InvalidOperationException("Preview has no content");
        string before = Stable;
        var source = TextureEditor.Read(id,layer);
        string file = TextureEditor.AllowedPath("tests/" + Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        var texture = TextureEditor.Decode(source.Png);
        try
        {
            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) { var p = pixels[i]; p.r = (byte)(255-p.r); pixels[i] = p; }
            texture.SetPixels32(pixels); texture.Apply();
            File.WriteAllBytes(file, ImageConversion.EncodeToPNG(texture));
        }
        finally { UnityEngine.Object.Destroy(texture); }
        var import = C(id,layer,file,TextureEditor.Revision.ToString());
        yield return TextureHandlers.Import(import);
        if (import.Error != null) throw new InvalidOperationException(import.Error);
        var imported = (Dictionary<string,object>)import.Result;
        bool changed = (string)imported["stable_preview_hash"] != before;
        var restore = C((string)imported["backup_id"],TextureEditor.Revision.ToString());
        yield return TextureHandlers.Restore(restore);
        yield return StableShot();
        bool restored = restore.Error == null && Stable == before && TextureEditor.Read(id,layer).PixelHash == source.PixelHash;
        var result = new Dictionary<string,object> { ["ok"] = changed && restored, ["render_changed"] = changed,
            ["exact_restore"] = restored, ["before"] = before, ["after"] = imported["stable_preview_hash"] };
        WriteResult("texture_roundtrip_result.json",result);
        Report("texture_roundtrip",changed && restored,result);
        FileOps.QuitClean();
    }
}
