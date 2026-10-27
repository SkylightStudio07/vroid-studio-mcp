using System.Collections;
using System;
using System.Collections.Generic;
using System.IO;

namespace VroidStudioMcp;

public static class FaceBridgeHandlers
{
    public static IEnumerator Status(CommandDispatcher.Context ctx) {ctx.Result=FaceBridge.Status();yield break;}
    public static IEnumerator Import(CommandDispatcher.Context ctx)
    {
        if(!long.TryParse(CommandUtil.Arg(ctx,1),out var expected)){ctx.Error="expected_revision is required";yield break;}
        if(expected!=TextureEditor.Revision)throw new ArgumentException("STALE_FACE_TARGET: expected_revision must match current revision");
        var data=FaceBridge.ReadInput(CommandUtil.Arg(ctx,0),expected);
        string checkpoint=Path.Combine(TextureEditor.Workspace,"face-bridge","checkpoints",Guid.NewGuid().ToString("N")+".vroid");
        Directory.CreateDirectory(Path.GetDirectoryName(checkpoint));
        var save=new CommandDispatcher.Context{Args=new[]{checkpoint}};yield return SessionHandlers.Save(save);
        if(save.Error!=null){ctx.Error=save.Error;yield break;}
        int version=Signals.ModelVersion;
        FaceBridge.Store(data,expected);
        yield return CommandUtil.WaitUntil(()=>Signals.ModelVersion!=version&&VroidState._modelReady&&!VroidState._modelUpdating,30);
        if(Signals.ModelVersion==version){ctx.Error="FACE_STORE_TIMEOUT: no completed model rebuild";yield break;}
        FaceBridgeRuntime.Tick();
        if(!FaceBridgeRuntime.Active){ctx.Error="FACE_APPLY_FAILED: "+FaceBridgeRuntime.Error+"; checkpoint: "+checkpoint;yield break;}
        string render=null;yield return CommandUtil.StablePreview(hash=>render=hash);
        if(render==null){ctx.Error="FACE_PREVIEW_UNSTABLE; checkpoint: "+checkpoint;yield break;}
        var result=(Dictionary<string,object>)FaceBridge.Status();result["checkpoint_path"]=checkpoint;result["stable_preview_hash"]=render;ctx.Result=result;
    }
    public static IEnumerator Restore(CommandDispatcher.Context ctx)
    {
        if(!long.TryParse(CommandUtil.Arg(ctx,0),out var expected)){ctx.Error="expected_revision is required";yield break;}
        int version=Signals.ModelVersion;FaceBridge.Remove(expected);
        yield return CommandUtil.WaitUntil(()=>Signals.ModelVersion!=version&&VroidState._modelReady&&!VroidState._modelUpdating,30);
        if(Signals.ModelVersion==version){ctx.Error="FACE_RESTORE_TIMEOUT";yield break;}
        FaceBridgeRuntime.Tick();string render=null;yield return CommandUtil.StablePreview(hash=>render=hash);
        if(render==null){ctx.Error="FACE_PREVIEW_UNSTABLE";yield break;}
        ctx.Result=FaceBridge.Status();
    }
    public static IEnumerator Probe(CommandDispatcher.Context ctx)
    {
        ctx.Result=FaceBridge.Probe();
        yield break;
    }
}
