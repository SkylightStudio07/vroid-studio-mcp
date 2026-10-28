using System.Collections;
using System;
using System.Collections.Generic;

namespace VroidStudioMcp;

public static class ExpressionHandlers
{
    public static IEnumerator Preview(CommandDispatcher.Context ctx)
    {
        yield return CommandUtil.EnterSection("FaceEditor");
        Expressions.Preview(CommandUtil.Arg(ctx,0));
        string render = null;
        yield return CommandUtil.StablePreview(hash => render = hash);
        if (render == null) { ctx.Error = "Expression preview did not stabilize"; yield break; }
        ctx.Result = new Dictionary<string,object> { ["preview_only"] = true,
            ["is_expression"] = Interop.Get(Expressions.Face(),"IsExpression"), ["revision"] = TextureEditor.Revision,
            ["stable_preview_hash"] = render };
    }
    public static IEnumerator List(CommandDispatcher.Context ctx)
    {
        yield return CommandUtil.EnterSection("FaceEditor");
        ctx.Result = Expressions.List();
    }
    public static IEnumerator Set(CommandDispatcher.Context ctx)
    {
        string A(int n) => CommandUtil.Arg(ctx,n);
        Expressions.CheckRevision(A(4));
        if (!int.TryParse(A(1),out int part) || !ParamCodec.TryParseFinite(A(3),out float value))
            throw new ArgumentException("Expression requires part index and finite value");
        var parameter = Expressions.Parameter(A(0),part,A(2));
        float min = Convert.ToSingle(Interop.Get(parameter,"MinValue")), max = Convert.ToSingle(Interop.Get(parameter,"MaxValue"));
        if (value < min || value > max) throw new ArgumentException("Expression value outside native range");
        float before = Convert.ToSingle(Interop.Get(parameter,"Value"));
        if (before == value) { ctx.Result = new Dictionary<string,object> { ["changed"] = false, ["revision"] = TextureEditor.Revision }; yield break; }
        int signal = Signals.ModelVersion;
        var editing = TextureEditor.Current();
        Signals.Arm();
        Interop.Invoke(parameter,"set_Value",value);
        long revision = editing._revision;
        yield return CommandUtil.WaitUntil(() => Signals.ModelVersion != signal && VroidState._modelReady && !VroidState._modelUpdating,30f);
        if (Signals.ModelVersion == signal || !VroidState._modelReady || VroidState._modelUpdating)
        { ctx.Error = "Expression rebuild timed out; inspect saved checkpoint and current state"; yield break; }
        float actual = Convert.ToSingle(Interop.Get(Expressions.Parameter(A(0),part,A(2)),"Value"));
        if (Math.Abs(actual-value) > 0.001f) { ctx.Error = "Expression read-back mismatch"; yield break; }
        if (TextureEditor.Revision != revision) { ctx.Error = "STALE_EXPRESSION_REVISION: concurrent edit during verification"; yield break; }
        ctx.Result = new Dictionary<string,object> { ["changed"] = true, ["before"] = before, ["value"] = actual,
            ["revision"] = TextureEditor.Revision, ["category_key"] = A(0), ["part"] = part, ["parameter_key"] = A(2) };
    }
}
