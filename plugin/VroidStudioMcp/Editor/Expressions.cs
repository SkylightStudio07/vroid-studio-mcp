using System;
using System.Collections.Generic;
using System.Reflection;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

public static class Expressions
{
    internal static void Preview(string key)
    {
        var face = Face();
        if (key == "reset") { Invoke(face,"ResetExpression"); return; }
        object selected = null;
        foreach (var cat in Items(Get(face,"ExpressionParameterCategories"), "VRoid.Studio.ExpressionParameter.ExpressionParameterCategoryViewModel"))
            if ((string)Get(cat,"SynthesizedCompatKey") == key) selected = cat;
        if (selected == null) throw new ArgumentException("Unknown expression preview key");
        var selector = Get(face,"PresetsSelector");
        object node = FindExpression(Get(selector,"Categories"),0);
        if (node == null) throw new InvalidOperationException("Expression editor entry not found");
        Invoke(selector,"set_SelectedCategory",node);
        Invoke(face,"set_SelectedExpressionParameterCategory",selected);
        Invoke(face,"UpdateExpression");
    }
    private static object FindExpression(object node, int depth)
    {
        if (node == null || depth > 8) return null;
        if ((TypeName(node) ?? "").EndsWith(".ExpressionCategoryViewModel")) return node;
        foreach (var child in Enumerate(Get(node,"Children")))
        { var found = FindExpression(child,depth+1); if (found != null) return found; }
        return null;
    }
    internal static object Parameter(string categoryKey, int partIndex, string parameterKey)
    {
        foreach (var category in Items(Get(Face(), "ExpressionParameterCategories"), "VRoid.Studio.ExpressionParameter.ExpressionParameterCategoryViewModel"))
        {
            if ((string)Get(category,"SynthesizedCompatKey") != categoryKey) continue;
            var parts = Items(Get(category,"ParameterParts"), "VRoid.Studio.ExpressionParameter.ExpressionParameterPartViewModel");
            if (partIndex < 0 || partIndex >= parts.Count) throw new ArgumentException("Invalid expression part index");
            foreach (var p in Items(Get(parts[partIndex],"Parameters"), "VRoid.Studio.ExpressionParameter.ExpressionParameterViewModel"))
                if ((string)Get(Get(p,"_model"),"PhysicalKey") == parameterKey) return p;
        }
        throw new ArgumentException("Unknown expression category/parameter");
    }
    internal static List<object> Items(object source, string typeName)
    {
        if (source == null) return new();
        var element = FindType(typeName);
        var enumerable = typeof(Il2CppSystem.Collections.Generic.IEnumerable<>).MakeGenericType(element);
        var list = typeof(Il2CppSystem.Collections.Generic.List<>).MakeGenericType(element);
        var native = RewrapTo(source, enumerable);
        if (native == null) throw new InvalidOperationException("Cannot enumerate expression data");
        var copy = list.GetConstructor(new[] { enumerable }).Invoke(new[] { native });
        return IndexedRead(copy, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Cannot copy expression data");
    }
    internal static object Face()
    {
        TextureEditor.Current();
        return Get(VroidState._editRootVm, "FaceEditor") ?? throw new InvalidOperationException("Expression editor unavailable");
    }
    internal static void CheckRevision(string expected)
    {
        if (!long.TryParse(expected, out long revision) || revision != TextureEditor.Revision)
            throw new ArgumentException("STALE_EXPRESSION_REVISION: expected_revision must equal the current revision");
    }
    internal static object List()
    {
        var result = new List<object>();
        var source = Get(Face(), "ExpressionParameterCategories");
        foreach (var cat in Items(source, "VRoid.Studio.ExpressionParameter.ExpressionParameterCategoryViewModel"))
        {
            var parts = new List<object>();
            foreach (var part in Items(Get(cat, "ParameterParts"), "VRoid.Studio.ExpressionParameter.ExpressionParameterPartViewModel"))
            {
                var parameters = new List<object>();
                foreach (var parameter in Items(Get(part, "Parameters"), "VRoid.Studio.ExpressionParameter.ExpressionParameterViewModel"))
                {
                    var model = Get(parameter, "_model");
                    parameters.Add(new Dictionary<string,object> { ["label"] = Get(parameter, "TranslatedDisplayName"),
                        ["key"] = Get(model, "PhysicalKey"), ["synthesized_key"] = Get(model, "SynthesizedCompatKey"),
                        ["value"] = Get(parameter,"Value"), ["min"] = Get(parameter,"MinValue"), ["max"] = Get(parameter,"MaxValue") });
                }
                parts.Add(new Dictionary<string,object> { ["label"] = Get(part,"TranslatedDisplayName"), ["parameters"] = parameters });
            }
            result.Add(new Dictionary<string,object> { ["label"] = Get(cat,"TranslatedDisplayName"),
                ["key"] = Get(cat,"SynthesizedCompatKey"), ["parts"] = parts });
        }
        return new Dictionary<string,object> { ["categories"] = result, ["revision"] = TextureEditor.Revision };
    }
}
