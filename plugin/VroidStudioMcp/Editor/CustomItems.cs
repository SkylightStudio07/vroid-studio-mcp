using System.Collections.Generic;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

public static class CustomItems
{
    internal static object List(string section, string category)
    {
        var selector = Categories.FindSelectorByShort(section + ".PresetsSelectorViewModel");
        var tab = Categories.FindTab(selector, category);
        if (tab == null) throw new System.ArgumentException("unknown category; use categories first");
        var node = Get(tab,"PresetCategoryViewModel") ?? tab;
        var items = new List<object>();
        foreach (var option in Enumerate(Get(node,"CustomItemOptions")))
        {
            if (TypeName(option) != "VRoid.Studio.ModelPresetsSelector.CustomItemOptionViewModel") continue;
            var model = Get(option,"Model");
            items.Add(new Dictionary<string, object> {
                ["item_id"] = SafeStr(Get(model,"CustomItemId")),
                ["group_id"] = Get(model,"TransferableGroupId"),
                ["group_type"] = Get(model,"TransferableGroupTypeId"),
                ["revision"] = Get(model,"TransferableGroupRevision"),
                ["name"] = Get(option,"DisplayName"), ["selected"] = Get(option,"IsSelected"),
                ["apply_supported"] = false });
        }
        return new Dictionary<string, object> { ["items"] = items, ["import_supported"] = false,
            ["note"] = "Read-only installed-item discovery. Import/apply are not yet runtime validated." };
    }
}
