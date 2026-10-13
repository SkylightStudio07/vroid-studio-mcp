using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VroidStudioMcp;

internal static class MiniJson
{
    public static string Serialize(object value, bool pretty)
    {
        var sb = new StringBuilder();
        Write(sb, value, pretty, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object v, bool pretty, int indent)
    {
        switch (v)
        {
            case null: sb.Append("null"); return;
            case string s: WriteString(sb, s); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
            case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
            case IDictionary dict: WriteObject(sb, dict, pretty, indent); return;
            case IEnumerable en: WriteArray(sb, en, pretty, indent); return;
        }

        var t = v.GetType();
        if (t.IsPrimitive)
        {
            sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
            return;
        }
        WriteString(sb, v.ToString());
    }

    private static void WriteObject(StringBuilder sb, IDictionary dict, bool pretty, int indent)
    {
        sb.Append('{');
        bool first = true;
        foreach (DictionaryEntry kv in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            NewLine(sb, pretty, indent + 1);
            WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
            sb.Append(':');
            if (pretty) sb.Append(' ');
            Write(sb, kv.Value, pretty, indent + 1);
        }
        NewLine(sb, pretty, indent);
        sb.Append('}');
    }

    private static void WriteArray(StringBuilder sb, IEnumerable en, bool pretty, int indent)
    {
        sb.Append('[');
        bool first = true;
        foreach (var item in en)
        {
            if (!first) sb.Append(',');
            first = false;
            NewLine(sb, pretty, indent + 1);
            Write(sb, item, pretty, indent + 1);
        }
        NewLine(sb, pretty, indent);
        sb.Append(']');
    }

    private static void NewLine(StringBuilder sb, bool pretty, int indent)
    {
        if (!pretty) return;
        sb.Append('\n');
        sb.Append(' ', indent * 2);
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        if (s == null) { sb.Append("null"); return; }
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
