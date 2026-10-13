using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using static VroidStudioMcp.Interop;

namespace VroidStudioMcp;

public static class Presets
{
    // Single definition of a preset's public display name, shared by `presets`, the preview commands
    // and `select_preset`: the overlay label when known, otherwise the internal id (resourceName,
    // then itemId). The raw ids stay on the dev/TCP surface (`presets_full`).
    public static string DisplayName(object option)
    {
        if ((TypeName(option) ?? "").Contains("ClearOption")) return "(clear)";
        var model = Get(option, "Model");
        var resourceName = Get(model, "resourceName") as string;
        var itemId = Get(model, "itemId") as string;
        return PresetLabels.Get(resourceName, itemId) ?? resourceName ?? itemId;
    }
}
