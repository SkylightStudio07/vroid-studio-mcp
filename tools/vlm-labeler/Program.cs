using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VroidVlmLabeler;

public static class Program
{
    private sealed class Options
    {
        public readonly List<string> Sections = new();
        public string Category;
        public int Start;
        public int Limit = int.MaxValue;
        public bool Force;
        public bool DryRun;
        public bool NamesOnly;
        public string LabelsDir = "run/game/labels";
        public int OutPrefix = 10;
        public string Api = "";
        public string Model = "qwen3.8-27b";
        public string Open;
        public string Prompts;
        public int TimeoutSeconds = 600;
        public string Host = "127.0.0.1";
        public int Port = 17890;
    }

    public static int Main(string[] argv)
    {
        Options opt;
        try { opt = ParseArgs(argv); }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
        if (opt == null) { PrintHelp(); return 0; }

        using var rpc = new RpcClient(opt.Host, opt.Port, opt.TimeoutSeconds * 1000);
        using var vlm = new VlmClient(opt.Api, opt.Model, opt.TimeoutSeconds);
        var prompts = new PromptBook(opt.Prompts);

        var state = rpc.Call("state");
        if (!string.IsNullOrEmpty(opt.Open))
        {
            Console.WriteLine($"opening {opt.Open} ...");
            rpc.Call("open", opt.Open);
            state = rpc.Call("state");
        }
        if (!Bool(state, "in_edit"))
        {
            Console.Error.WriteLine("no model is open; pass --open <path.vroid> (Windows or Z:\\... form)");
            return 2;
        }

        var merged = LabelStore.LoadMerged(opt.LabelsDir);
        Console.WriteLine($"labels dir: {Path.GetFullPath(opt.LabelsDir)} ({merged.Count} known labels)");

        int labeled = 0, skippedExisting = 0, failed = 0, previewsMissing = 0;

        foreach (var section in SectionList(rpc))
        {
            if (opt.Sections.Count > 0 && !opt.Sections.Contains(section, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var category in CategoryList(rpc, section))
            {
                if (opt.Category != null && !string.Equals(opt.Category, category, StringComparison.OrdinalIgnoreCase)) continue;

                var entries = rpc.Call("presets_full", section, category);
                var existing = new Dictionary<string, string>(StringComparer.Ordinal);
                var fileName = $"{opt.OutPrefix:D2}-{LabelStore.Slug(section)}-{LabelStore.Slug(category)}.json";
                if (!opt.DryRun && !opt.NamesOnly) existing = LabelStore.ReadFile(Path.Combine(opt.LabelsDir, fileName));

                var pending = new List<(int Index, string Key)>();
                foreach (var entry in entries.EnumerateArray())
                {
                    var name = Str(entry, "name");
                    if (name == "(clear)") continue;
                    var resourceName = Str(entry, "resourceName");
                    var itemId = Str(entry, "itemId");
                    var key = resourceName ?? itemId;
                    if (string.IsNullOrEmpty(key)) continue;
                    int index = Int(entry, "index");
                    if (index < opt.Start) { skippedExisting++; continue; }
                    if (!opt.Force && (merged.ContainsKey(resourceName ?? "") || (!string.IsNullOrEmpty(itemId) && merged.ContainsKey(itemId))))
                    {
                        skippedExisting++;
                        continue;
                    }
                    pending.Add((index, key));
                }

                if (opt.NamesOnly)
                {
                    Console.WriteLine($"[{section}/{category}] {pending.Count} unlabeled of {entries.GetArrayLength()}");
                    foreach (var (idx, key) in pending) Console.WriteLine($"    {idx,4}  {key}");
                    continue;
                }

                if (pending.Count == 0)
                {
                    Console.WriteLine($"[{section}/{category}] nothing to label");
                    continue;
                }

                if (opt.DryRun)
                {
                    Console.WriteLine($"[{section}/{category}] would label {pending.Count} preset(s):");
                    foreach (var (idx, key) in pending) Console.WriteLine($"    {idx,4}  {key}");
                    continue;
                }

                Console.WriteLine($"[{section}/{category}] {pending.Count} to label -> {fileName}");
                labeled += ProcessCategory(rpc, vlm, prompts, opt, section, category, pending, existing,
                    fileName, ref skippedExisting, ref previewsMissing, ref failed);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"done: labeled={labeled} skipped_existing={skippedExisting} failed={failed} preview_missing={previewsMissing}");
        return failed > 0 ? 1 : 0;
    }

    private static int ProcessCategory(RpcClient rpc, VlmClient vlm, PromptBook prompts, Options opt,
        string section, string category, List<(int Index, string Key)> pending,
        Dictionary<string, string> existing, string fileName,
        ref int skippedExisting, ref int previewsMissing, ref int failed)
    {
        int labeled = 0;
        var systemPrompt = prompts.System;
        var taskPrompt = prompts.TaskFor(section, category);

        for (int p = 0; p < pending.Count; )
        {
            if (labeled >= opt.Limit) break;

            int runStart = pending[p].Index;
            int q = p + 1;
            while (q < pending.Count && pending[q].Index == pending[q - 1].Index + 1 && q - p < 12) q++;
            int count = q - p;

            JsonElement items;
            try { items = rpc.Call("preset_preview_range", section, category, runStart, count, "b64"); }
            catch (Exception e) { Console.Error.WriteLine($"  preview failed [{runStart}..{runStart + count - 1}]: {e.Message}"); failed += count; p = q; continue; }

            var byIndex = new Dictionary<int, JsonElement>();
            foreach (var it in items.EnumerateArray()) byIndex[Int(it, "index")] = it;

            for (int k = p; k < q; k++)
            {
                if (labeled >= opt.Limit) break;
                var (index, key) = pending[k];
                if (!byIndex.TryGetValue(index, out var item) || !Bool(item, "ok") || Str(item, "data") == null)
                {
                    previewsMissing++;
                    Console.Error.WriteLine($"  [{index}] no preview, skipped");
                    continue;
                }

                string label;
                try { label = LabelWithRetry(vlm, systemPrompt, taskPrompt, Str(item, "data")); }
                catch (Exception e) { failed++; Console.Error.WriteLine($"  [{index}] VLM failed: {e.Message}"); continue; }
                if (string.IsNullOrEmpty(label)) { failed++; Console.Error.WriteLine($"  [{index}] empty label, skipped"); continue; }

                existing[key] = label;
                LabelStore.Save(opt.LabelsDir, fileName, section, category, opt.Model, existing);
                labeled++;
                Console.WriteLine($"  [{index,4}] {label}   <- {key}");
            }
            p = q;
        }
        return labeled;
    }

    private static string LabelWithRetry(VlmClient vlm, string system, string task, string b64)
    {
        for (int attempt = 0; ; attempt++)
        {
            var label = vlm.Label(system, task, b64);
            if (!string.IsNullOrEmpty(label)) return label;
            if (attempt >= 1) return null;
        }
    }

    private static List<string> SectionList(RpcClient rpc)
    {
        var list = new List<string>();
        foreach (var s in rpc.Call("sections").EnumerateArray()) list.Add(Str(s, "id") ?? Str(s, "title"));
        return list;
    }

    private static List<string> CategoryList(RpcClient rpc, string section)
    {
        var list = new List<string>();
        foreach (var c in rpc.Call("categories", section).EnumerateArray()) { var n = Str(c, "name"); if (n != null) list.Add(n); }
        return list;
    }

    // ---------------- json helpers ----------------
    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    // ---------------- args ----------------
    private static Options ParseArgs(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new Exception($"{a} requires a value");
            switch (a)
            {
                case "-h": case "--help": return null;
                case "--section": o.Sections.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "--category": o.Category = Next(); break;
                case "--start": o.Start = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--limit": o.Limit = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--force": o.Force = true; break;
                case "--dry-run": case "--dry": o.DryRun = true; break;
                case "--names": case "--names-only": o.NamesOnly = true; break;
                case "--labels-dir": o.LabelsDir = Next(); break;
                case "--out-prefix": o.OutPrefix = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--api": o.Api = Next(); break;
                case "--model": o.Model = Next(); break;
                case "--open": o.Open = Next(); break;
                case "--prompts": o.Prompts = Next(); break;
                case "--timeout": o.TimeoutSeconds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--host": o.Host = Next(); break;
                case "--port": o.Port = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                default: throw new Exception($"unknown argument: {a} (try --help)");
            }
        }
        if (!o.DryRun && !o.NamesOnly && string.IsNullOrWhiteSpace(o.Api))
            throw new Exception("--api is required (OpenAI-compatible base URL; not needed with --dry-run/--names)");
        return o;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
@"vlm-labeler: label VRoid Studio presets with a vision model via the plugin's TCP JSON-RPC.

Usage: dotnet run --project tools/vlm-labeler -- [options]

  --section <list>    Comma-separated sections to process (Face,Hairstyle,Body,Outfit); default all
  --category <name>   Only this category (tab)
  --start <index>     Skip preset indices below this value
  --limit <n>         Stop after n VLM labels this run
  --force             Relabel presets that already have a label
  --dry-run           List what would be labeled, call no VLM, write nothing
  --names             List unlabeled presets and exit (no preview/VLM)
  --labels-dir <dir>  Overlay dir (default run/game/labels)
  --out-prefix <n>    Numeric filename prefix (default 10)
  --api <url>         OpenAI-compatible base URL (required unless --dry-run/--names)
  --model <id>        Model id (default qwen3.8-27b)
  --open <path>       Open this model first (Windows or Z:\... form)
  --prompts <file>    JSON prompt overrides: {system, sections, categories}
  --timeout <sec>     Per-call timeout (default 600)
  --host/--port       Command server (default 127.0.0.1:17890)
  -h, --help          Show this help");
    }
}
