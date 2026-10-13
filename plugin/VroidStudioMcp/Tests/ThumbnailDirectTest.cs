using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

/// <summary>Regression for the direct thumbnail path: the address derivation and the bytes from
/// Thumbnails.Get must match the preset ViewModel pipeline (PrepareResources/Preview.Create/EncodeToPNG)
/// for built-in presets across sections.</summary>
public sealed class ThumbnailDirectTest : VroidTest
{
    private static readonly (string enumSection, string tab)[] Targets =
    {
        ("FaceEditor", "Irises"),
        ("HairEditor", "Overall Hair"),
        ("ClothingEditor", "Tops"),
    };

    protected override IEnumerator Run()
    {
        var deriveOk = Thumbnails.TryDerive(
            "pixiv/VRoid/Face/Iris/N00/155/TransferableGroupType.N00.Level3.Iris.transferablegroup",
            out var bundle, out var path)
            && bundle == "vroid-thumbnails-pixiv-vroid-face-iris-n00-155"
            && path == "Assets/vroid-vroidcoredata/AssetBundles/Thumbnails/vroid-thumbnails-pixiv-vroid-face-iris-n00-155/thumbnail.png";

        yield return Open();

        var rows = new List<object>();
        bool allEqual = true;

        foreach (var (enumSection, tab) in Targets)
        {
            yield return SwitchSection(enumSection);
            var options = Icons.Options(enumSection + ".PresetsSelectorViewModel", tab);

            int done = 0;
            foreach (var opt in options)
            {
                var rn = Get(Get(opt, "Model"), "resourceName") as string;
                if (string.IsNullOrEmpty(rn)) continue;

                var row = new Dictionary<string, object> { ["section"] = tab, ["resourceName"] = rn };
                var direct = Thumbnails.Get(rn);
                row["direct_bytes"] = direct?.Length;

                Icons.Prepare(opt);
                object preview = null;
                yield return WaitUntil(() => { preview = Get(opt, "Preview"); return preview != null; }, 6f);
                object task = preview != null ? Invoke(preview, "Create") : null;
                yield return WaitUntil(() => task != null && Get(task, "IsCompleted") is bool b && b, 5f);
                var vm = task != null ? Icons.TextureToPng(Get(task, "Result")) : null;
                row["vm_bytes"] = vm?.Length;

                bool equal = direct != null && vm != null && Hash(direct) == Hash(vm);
                row["equal"] = equal;
                if (!equal) allEqual = false;
                rows.Add(row);

                done++;
                if (done >= 2) break;
            }
        }

        var payload = new Dictionary<string, object> { ["derive_ok"] = deriveOk, ["all_equal"] = allEqual, ["rows"] = rows };
        WriteResult("thumbnaildirect_result.json", payload);
        Report("thumbnaildirect", deriveOk && allEqual, new Dictionary<string, object> { ["derive_ok"] = deriveOk, ["all_equal"] = allEqual });
    }

    private static string Hash(byte[] b)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(b));
    }
}
