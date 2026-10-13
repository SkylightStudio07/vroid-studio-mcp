using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VroidStudioMcp;

/// <summary>
/// Host &lt;-&gt; Wine path translation. The plugin runs inside a Windows process, which under Wine
/// (Linux/macOS) still uses Windows paths; host tools/agents prefer POSIX paths.
///
/// Wine detection and the Wine conversion API are resolved dynamically via GetProcAddress (the
/// <c>wine_get_*</c> symbols do NOT exist on real Windows, so they must never be statically
/// imported). Conversion tries Wine's own API first (exact: drive mappings, case, symlinks) and
/// falls back to the "Z: is the host root" heuristic. On real Windows both directions are a no-op.
/// </summary>
public static class WinePaths
{
    private static int _isWine = -1;   // -1 unknown, 0 no, 1 yes

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcessHeap();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool HeapFree(IntPtr heap, uint flags, IntPtr ptr);

    private delegate IntPtr DosFileNameFn(IntPtr unixUtf8);   // WCHAR *wine_get_dos_file_name(const char *)
    private delegate IntPtr UnixFileNameFn(IntPtr dosWide);   // char *wine_get_unix_file_name(const WCHAR *)

    private static DosFileNameFn _dosFn;
    private static UnixFileNameFn _unixFn;

    public static bool IsWine
    {
        get
        {
            if (_isWine < 0) _isWine = (ResolveWineApi() || HasWineVersion()) ? 1 : 0;
            return _isWine == 1;
        }
    }

    private static bool HasWineVersion()
    {
        try
        {
            var ntdll = GetModuleHandleW("ntdll.dll");
            return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
        }
        catch { return false; }
    }

    private static bool _apiTried;
    private static bool _apiOk;

    private static bool ResolveWineApi()
    {
        if (_apiTried) return _apiOk;
        _apiTried = true;
        try
        {
            var k32 = GetModuleHandleW("kernel32.dll");
            if (k32 == IntPtr.Zero) return false;
            var pDos = GetProcAddress(k32, "wine_get_dos_file_name");
            var pUnix = GetProcAddress(k32, "wine_get_unix_file_name");
            if (pDos == IntPtr.Zero || pUnix == IntPtr.Zero) return false;
            _dosFn = Marshal.GetDelegateForFunctionPointer<DosFileNameFn>(pDos);
            _unixFn = Marshal.GetDelegateForFunctionPointer<UnixFileNameFn>(pUnix);
            _apiOk = true;
        }
        catch { _apiOk = false; }
        return _apiOk;
    }

    public static bool IsWindowsPath(string p)
    {
        if (string.IsNullOrEmpty(p)) return false;
        if (p.StartsWith(@"\\")) return true;                       // UNC
        return p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':';
    }

    /// <summary>Host path (or a Windows path) -&gt; Windows path the engine can open.</summary>
    public static string ToDos(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (IsWindowsPath(path)) return path;
        if (!IsWine) return path;

        if (ResolveWineApi())
        {
            var r = CallDos(path);
            if (r != null) return r;
        }
        // heuristic: Z: is the host root
        if (path.StartsWith("/")) return "Z:" + path.Replace('/', '\\');
        return path;
    }

    /// <summary>Windows path -&gt; host (POSIX) path, so callers/agents see host paths.</summary>
    public static string ToHost(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (!IsWine || !IsWindowsPath(path)) return path;

        if (ResolveWineApi())
        {
            var r = CallUnix(path);
            if (r != null) return r;
        }
        // heuristic: Z: is the host root
        if (path.Length >= 2 && char.ToUpperInvariant(path[0]) == 'Z' && path[1] == ':')
        {
            var rest = path.Substring(2).Replace('\\', '/');
            return rest.StartsWith("/") ? rest : "/" + rest;
        }
        return path;
    }

    private static string CallDos(string unixPath)
    {
        IntPtr buf = IntPtr.Zero, res = IntPtr.Zero;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(unixPath);
            buf = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, buf, bytes.Length);
            Marshal.WriteByte(buf, bytes.Length, 0);
            res = _dosFn(buf);
            if (res == IntPtr.Zero) return null;
            var s = Marshal.PtrToStringUni(res);
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch { return null; }
        finally
        {
            if (res != IntPtr.Zero) try { HeapFree(GetProcessHeap(), 0, res); } catch { }
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        }
    }

    private static string CallUnix(string dosPath)
    {
        IntPtr wide = IntPtr.Zero, res = IntPtr.Zero;
        try
        {
            wide = Marshal.StringToHGlobalUni(dosPath);
            res = _unixFn(wide);
            if (res == IntPtr.Zero) return null;
            var s = Marshal.PtrToStringUTF8(res);
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch { return null; }
        finally
        {
            if (res != IntPtr.Zero) try { HeapFree(GetProcessHeap(), 0, res); } catch { }
            if (wide != IntPtr.Zero) Marshal.FreeHGlobal(wide);
        }
    }
}
