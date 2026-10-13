using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VroidStudioMcp;

/// <summary>List/range preset previews: explicit list, out-of-range rejection, clear handling,
/// range bounds errors, and the base64 (VLM) mode.</summary>
public sealed class PreviewTest : VroidTest
{
    protected override IEnumerator Run()
    {
        yield return Open();
        yield return SwitchSection("HairEditor");

        var list = new CommandDispatcher.Context { Method = "preset_preview_list", Args = new[] { "Hairstyle", "Overall Hair", "1,2", "inline" } };
        yield return CommandDispatcher.TestPreviewList(list);
        int listCount = Icons.LastPreviews.Count;
        bool listOk = list.Error == null && listCount == 2 && Icons.LastPreviews.All(p => p.Png != null && p.Png.Length > 0);
        Report("preview-list", listOk, new Dictionary<string, object> { ["error"] = list.Error, ["count"] = listCount });

        // Previews must be self-describing: each item carries the same `name` as `presets` (index+name),
        // and no raw resourceName leaks into the result.
        var listItems = list.Result as List<object>;
        bool namesOk = listItems != null && listItems.Count == 2 && listItems.All(x =>
        {
            var d = x as Dictionary<string, object>;
            return d != null && !string.IsNullOrEmpty(d["name"] as string) && !d.ContainsKey("resourceName");
        });
        Report("preview-list-names", namesOk, list.Result);

        var oob = new CommandDispatcher.Context { Method = "preset_preview_list", Args = new[] { "Hairstyle", "Overall Hair", "99999", "inline" } };
        yield return CommandDispatcher.TestPreviewList(oob);
        bool oobOk = oob.Error != null;
        Report("preview-list-oob", oobOk, oob.Error);

        var clear = new CommandDispatcher.Context { Method = "preset_preview_list", Args = new[] { "Hairstyle", "Overall Hair", "0,1", "inline" } };
        yield return CommandDispatcher.TestPreviewList(clear);
        bool clearOk = clear.Error == null && Icons.LastPreviews.Count == 1;
        Report("preview-list-clear", clearOk, new Dictionary<string, object> { ["error"] = clear.Error, ["count"] = Icons.LastPreviews.Count });

        var range = new CommandDispatcher.Context { Method = "preset_preview_range", Args = new[] { "Hairstyle", "Overall Hair", "1", "2", "inline" } };
        yield return CommandDispatcher.TestPreviewRange(range);
        int rangeCount = Icons.LastPreviews.Count;
        bool rangeOk = range.Error == null && rangeCount == 2;
        Report("preview-range", rangeOk, new Dictionary<string, object> { ["error"] = range.Error, ["count"] = rangeCount });

        var b64 = new CommandDispatcher.Context { Method = "preset_preview_range", Args = new[] { "Hairstyle", "Overall Hair", "1", "1", "b64" } };
        yield return CommandDispatcher.TestPreviewRange(b64);
        var b64Items = b64.Result as List<object>;
        bool b64Ok = b64.Error == null && b64Items != null && b64Items.Count == 1
            && (b64Items[0] as Dictionary<string, object>)?.ContainsKey("data") == true;
        Report("preview-range-b64", b64Ok, b64.Error);

        // Out-of-range range must fail the whole call (no trimming).
        var rangeOob = new CommandDispatcher.Context { Method = "preset_preview_range", Args = new[] { "Hairstyle", "Overall Hair", "99999", "2", "inline" } };
        yield return CommandDispatcher.TestPreviewRange(rangeOob);
        bool rangeOobOk = rangeOob.Error != null;
        Report("preview-range-oob", rangeOobOk, rangeOob.Error);

        var rangeCountCtx = new CommandDispatcher.Context { Method = "preset_preview_range", Args = new[] { "Hairstyle", "Overall Hair", "0", "99", "inline" } };
        yield return CommandDispatcher.TestPreviewRange(rangeCountCtx);
        bool rangeCountOk = rangeCountCtx.Error != null;
        Report("preview-range-count", rangeCountOk, rangeCountCtx.Error);

        WriteResult("preview_result.json", new Dictionary<string, object>
        {
            ["list_ok"] = listOk,
            ["list_names_ok"] = namesOk,
            ["list_oob_ok"] = oobOk,
            ["list_clear_ok"] = clearOk,
            ["range_ok"] = rangeOk,
            ["range_b64_ok"] = b64Ok,
            ["range_oob_ok"] = rangeOobOk,
            ["range_count_ok"] = rangeCountOk,
        });
    }
}
