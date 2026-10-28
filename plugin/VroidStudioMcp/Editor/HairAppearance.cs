using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using VRoidCore.Common.SpecificTypes;
using VRoidCore.Editing;
using VRoidCore.Editing.Query;
using VRoidCore.Editing.History.Command;

namespace VroidStudioMcp;

public static class HairAppearance
{
    private static readonly List<Il2CppSystem.Action<Global>> Callbacks = new();
    internal static float Width() => new GetGlobalParametersQuery().Execute(TextureEditor.Current().editingModel)
        .ShadingParameters.HairParameterSet.OutlineWidth;

    private static SharedHairMaterialPath Material(string id)
    {
        if (!new GetGlobalParametersQuery().Execute(TextureEditor.Current().editingModel).HairMaterials.ContainsKey(id ?? ""))
            throw new ArgumentException("Unknown current hair material ID");
        return new SharedHairMaterialPath(id);
    }
    internal static UnityEngine.Color OutlineColor(string id) => new GetMaterialColorQuery(Material(id),"OutlineColor")
        .Execute(TextureEditor.Current().editingModel);
    internal static long SetOutlineColor(string id, UnityEngine.Color color)
    {
        var path = Material(id);
        var context = TextureEditor.Current();
        Signals.Arm();
        context.ExecuteSyncCommand(new SetMaterialColorCommand(path,"OutlineColor",color)
            .Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        context.FlushPendingHistory(false);
        return context._revision;
    }

    internal static object State()
    {
        var context = TextureEditor.Current();
        var global = new GetGlobalParametersQuery().Execute(context.editingModel);
        var materials = new List<object>();
        var values = new Il2CppReferenceArray<VRoidCore.Common.SpecificTypes.SharedMaterial.HairMaterial>(global.HairMaterials.Count);
        global.HairMaterials.Values.CopyTo(values,0);
        foreach (var material in values)
        {
            var path = new SharedHairMaterialPath(material.SharedHairMaterialId);
            var parameters = new List<object>();
            void Visit(object node, int depth)
            {
                if (depth > 6) return;
                node = Interop.RealObj(node);
                if (node is VRoidCore.Editing.Query.AdvancedEdit.MaterialColor color)
                {
                    var value = new GetMaterialColorQuery(path,color.parameterKey).Execute(context.editingModel);
                    parameters.Add(new Dictionary<string,object> { ["key"] = color.parameterKey,
                        ["kind"] = "color", ["value"] = new[] {value.r,value.g,value.b,value.a} });
                }
                if (node is VRoidCore.Editing.Query.AdvancedEdit.MaterialFloat number)
                    parameters.Add(new Dictionary<string,object> { ["key"] = number.parameterKey,
                        ["kind"] = "number", ["value"] = new GetMaterialFloatQuery(path,number.parameterKey).Execute(context.editingModel),
                        ["min"] = number.minValue, ["max"] = number.maxValue });
                foreach (string member in new[] { "childSections", "colors" })
                    foreach (var child in Interop.Enumerate(Interop.Get(node,member))) Visit(child,depth+1);
            }
            foreach (var section in new VRoidCore.Editing.Query.AdvancedEdit.GetHairMaterialQuery(path).Execute(context.editingModel).sections)
                Visit(section,0);
            materials.Add(new Dictionary<string,object> { ["material_id"] = material.SharedHairMaterialId,
                ["name"] = material.Name, ["parameters"] = parameters });
        }
        return new Dictionary<string,object> { ["revision"] = context._revision, ["outline_width"] = Width(),
            ["materials"] = materials };
    }

    internal static long SetWidth(float width)
    {
        if (Callbacks.Count >= 128) throw new ArgumentException("Restart before more hair appearance edits");
        var context = TextureEditor.Current();
        var callback = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Global>>(
            (Action<Global>)(global => global.ShadingParameters.HairParameterSet.OutlineWidth = width));
        Callbacks.Add(callback);
        Signals.Arm();
        context.ExecuteSyncCommand(new ModifyGlobalParametersCommand(Guid.NewGuid().ToString("N"),callback)
            .Cast<ISyncCommand<Context.HistoryManagerContext,Model>>());
        context.FlushPendingHistory(false);
        return context._revision;
    }
}
