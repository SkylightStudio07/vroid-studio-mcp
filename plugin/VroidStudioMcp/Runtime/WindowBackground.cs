using System;
using System.Runtime.InteropServices;

namespace VroidStudioMcp;

/// <summary>
/// Keeps the editor responsive while its window is minimized.
///
/// The plugin forces <c>Application.runInBackground = true</c> so the main-thread command queue keeps
/// draining when the window loses focus. With a minimized window (invalid Vulkan presentation
/// surface) the running player still presents to it and the whole player loop freezes on restore:
/// the frame counter stops, queued ping/commands are not served, and only a window message (a click)
/// wakes it.
///
/// While the window is minimized we switch vsync off and give Unity a target frame rate. With vsync
/// off the present no longer blocks, and a target frame rate makes Unity pace frames with a timed
/// message-loop wait, so the loop keeps draining no matter what the renderer is doing. Both settings
/// are restored a moment after the window is visible again and the swapchain has settled.
///
/// ------------------------------------------------------------------------------------------------
/// ROOT CAUSE (researched in the DXVK sources, v3.x) - this whole class is a TEMPORARY WORKAROUND
/// for the narrow "DXVK + minimized window" case, to be removed once DXVK bounds its WSI waits (or
/// stops blocking on a non-presentable surface). Native Windows does NOT need it: there the app
/// uses the vendor D3D11/DXGI driver, where Present on a minimized/occluded window returns
/// DXGI_STATUS_OCCLUDED instead of blocking.
///
/// The fatal part is not the return codes but two *infinite* waits in DXVK's presenter
/// (src/dxvk/dxvk_presenter.cpp):
///   - acquireNextImage(): calls waitForSwapchainFence() -> vkWaitForFences(..., ~0ull) and
///     vkAcquireNextImageKHR(..., UINT64_MAX, ...), both with an infinite timeout. The comment there
///     is literally our case: "It is possible that we do not get a new swap chain here, e.g. because
///     the window is minimized."
///   - createSwapChain(): returns VK_NOT_READY when pickImageExtent() yields 0x0 (unmapped/minimized
///     window) - this is the "Presenter: Surface does not allow swapchain creation." log line.
///   - softError(): maps VK_ERROR_SURFACE_LOST_KHR / VK_ERROR_OUT_OF_DATE_KHR to VK_NOT_READY.
/// The Vulkan WSI underneath (Mesa RADV + Wine winevulkan + X11/Xwayland) can block for a long time
/// on a surface that is not presentable, so the render thread never returns and Unity's main loop
/// waits for it forever in WaitForMultipleObjectsEx (MsgWaitForMultipleObjects).
///
/// DXVK *does* try to emulate DXGI occlusion, so the API-level contract is fine:
///   - src/dxgi/dxgi_swapchain.cpp PresentBase(): for DISCARD/SEQUENTIAL swap effects it returns
///     DXGI_STATUS_OCCLUDED immediately when wsi::isMinimized(window);
///   - src/d3d11/d3d11_swapchain.cpp PresentImage(): returns DXGI_STATUS_OCCLUDED when
///     acquireNextImage() yields VK_NOT_READY.
/// The gap is that the flip-model path does not take that early return and still goes through the
/// presenter's blocking WSI calls, and those individual waits stay infinite regardless of the
/// OCCLUDED mapping. Windows does not render offscreen on occlusion either - it returns OCCLUDED and
/// keeps the back buffers; DXVK should do the same with bounded waits, not implicit offscreen render.
///
/// Turning vsync off makes the present non-blocking, and targetFrameRate keeps Unity's message-loop
/// wait finite (a plain vsync=0/targetFrameRate=-1 still wedges), so the queue keeps draining and
/// screenshots/commands work while minimized.
///
/// If this ever needs to stop touching native Windows behaviour, gate Tick() on WinePaths.IsWine.
/// ------------------------------------------------------------------------------------------------
/// </summary>
public static class WindowBackground
{
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lparam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lparam);

    private const uint GW_OWNER = 4;
    private const int BackgroundFps = 60;
    // Leave the swapchain a moment to settle before restoring the original pacing settings.
    private const float RestoreGraceSeconds = 2f;

    private static bool _unavailable;
    private static IntPtr _hwnd;
    private static bool _settingsChanged;
    private static int _savedVsync;
    private static int _savedFps;
    private static float _visibleSince;

    public static void Tick()
    {
        if (_unavailable) return;
        try
        {
            TickCore();
        }
        catch (Exception e)
        {
            _unavailable = true;
            PluginLog.LogWarning($"window background handling disabled: {e.Message}");
        }
    }

    private static void TickCore()
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = FindMainWindow();
            if (_hwnd == IntPtr.Zero) return;
        }

        if (IsIconic(_hwnd))
        {
            _visibleSince = 0f;
            if (_settingsChanged) return;
            _settingsChanged = true;
            _savedVsync = UnityEngine.QualitySettings.vSyncCount;
            _savedFps = UnityEngine.Application.targetFrameRate;
            UnityEngine.QualitySettings.vSyncCount = 0;
            if (UnityEngine.Application.targetFrameRate <= 0) UnityEngine.Application.targetFrameRate = BackgroundFps;
            PluginLog.LogInfo($"window minimized: vsync {_savedVsync} -> 0, targetFrameRate {_savedFps} -> {UnityEngine.Application.targetFrameRate}");
            return;
        }

        if (!_settingsChanged) return;
        if (_visibleSince == 0f) _visibleSince = UnityEngine.Time.realtimeSinceStartup;
        if (UnityEngine.Time.realtimeSinceStartup - _visibleSince < RestoreGraceSeconds) return;

        _settingsChanged = false;
        UnityEngine.QualitySettings.vSyncCount = _savedVsync;
        UnityEngine.Application.targetFrameRate = _savedFps;
        PluginLog.LogInfo($"window restored: vsync -> {_savedVsync}, targetFrameRate -> {_savedFps}");
    }

    private static IntPtr FindMainWindow()
    {
        uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        IntPtr result = IntPtr.Zero;
        try
        {
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out var wpid);
                if (wpid == pid && IsWindowVisible(h) && GetWindow(h, GW_OWNER) == IntPtr.Zero)
                { result = h; return false; }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return result;
    }
}
