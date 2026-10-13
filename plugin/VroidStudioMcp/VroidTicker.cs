namespace VroidStudioMcp;

/// <summary>Per-frame main-thread tick + coroutine host for tests.</summary>
public class VroidTicker : UnityEngine.MonoBehaviour
{
    public void Update() => Plugin.OnTick();
}
