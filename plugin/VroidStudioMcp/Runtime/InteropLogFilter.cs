using System;
using System.Linq;
using System.Reflection;

namespace VroidStudioMcp;

/// <summary>
/// Il2CppInterop logs a Warning for every method it cannot detour natively (typically abstract /
/// interface constructors — "Derived classes must provide an implementation") and falls back to
/// managed Harmony handlers. We intentionally patch such constructors to capture view models, so the
/// message is expected and not actionable: drop exactly that line, keep every other log. The
/// Il2CppInterop logger is an internal Microsoft.Extensions.Logging.ILogger; we wrap it via
/// DispatchProxy (no compile-time dependency on Microsoft.Extensions.Logging).
/// </summary>
public static class InteropLogFilter
{
    private const string Dropped = "Failed to init IL2CPP patch backend";

    public static void Install()
    {
        try
        {
            var loggerType = Type.GetType("Il2CppInterop.Common.Logger, Il2CppInterop.Common")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.FullName == "Il2CppInterop.Common.Logger");
            var prop = loggerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var inner = prop?.GetValue(null);
            if (inner == null) return;
            var iface = inner.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == "Microsoft.Extensions.Logging.ILogger");
            if (iface == null) return;
            var create = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            var proxy = (FilterProxy)create.MakeGenericMethod(iface, typeof(FilterProxy)).Invoke(null, null);
            proxy.Inner = inner;
            prop.SetValue(null, proxy);
            PluginLog.LogInfo("interop log filter installed");
        }
        catch (Exception e) { PluginLog.LogWarning($"interop log filter: {e.Message}"); }
    }

    public class FilterProxy : DispatchProxy
    {
        internal object Inner;

        protected override object Invoke(MethodInfo target, object[] args)
        {
            try
            {
                if (target.Name == "Log" && args != null && args.Length >= 5
                    && args[0]?.ToString() == "Warning")
                {
                    var ex = args[3] as Exception;
                    var formatter = args[4] as Delegate;
                    var msg = ex?.Message;
                    if (msg == null && formatter != null)
                        msg = formatter.DynamicInvoke(args[2], null)?.ToString();
                    msg ??= args[2]?.ToString();
                    if (msg != null && msg.Contains(Dropped)) return null;
                }
            }
            catch { }
            return target.Invoke(Inner, args);
        }
    }
}
