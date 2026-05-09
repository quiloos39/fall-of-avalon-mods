namespace DialogExporter;

// Diagnostic — dumps a histogram of Babel key prefixes so we can see what
// categories of text are in the translation tables.
public static class KeysExplorer
{
    public static void RunAllPrefixes(BabelTranslator babel, string match = null)
    {
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < babel.Keys.Length; i++)
        {
            var k = babel.Keys[i];
            if (k == null) continue;
            if (!string.IsNullOrEmpty(match) && k.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
            int slash = k.IndexOf('/');
            string prefix = slash > 0 ? k[..slash] : "(no-slash)";
            counts.TryGetValue(prefix, out int c);
            counts[prefix] = c + 1;
        }
        Console.WriteLine($"All prefixes ({counts.Count} distinct{(match != null ? $" matching '{match}'" : "")}):");
        foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {kv.Key,-50} {kv.Value,6}");
    }

    // Lists distinct GUIDs (last underscore-separated token) under a prefix.
    public static void RunGuidsUnder(BabelTranslator babel, string filter)
    {
        var guidCounts = new Dictionary<string, int>();
        for (int i = 0; i < babel.Keys.Length; i++)
        {
            var k = babel.Keys[i];
            if (!k.StartsWith(filter)) continue;
            int last = k.LastIndexOf('_');
            if (last < 0) continue;
            string guid = k[(last + 1)..];
            guidCounts.TryGetValue(guid, out int c);
            guidCounts[guid] = c + 1;
        }
        Console.WriteLine($"Distinct trailing-GUIDs under '{filter}' ({guidCounts.Count} distinct):");
        foreach (var kv in guidCounts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {kv.Key}  {kv.Value,5} keys");
    }

    public static void RunFullDrill(BabelTranslator babel, string filter)
    {
        var subCounts = new Dictionary<string, int>();
        for (int i = 0; i < babel.Keys.Length; i++)
        {
            var k = babel.Keys[i];
            if (!k.StartsWith(filter)) continue;
            string rest = k[filter.Length..];
            int us = rest.IndexOf('_');
            if (us < 0) us = rest.Length;
            string sub = rest[..us];
            subCounts.TryGetValue(sub, out int c);
            subCounts[sub] = c + 1;
        }
        Console.WriteLine($"All sub-categories under '{filter}' ({subCounts.Count} distinct):");
        foreach (var kv in subCounts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {kv.Key,-40} {kv.Value,6}");
    }

    // Drills into a specific prefix to show its sub-categories.
    public static void RunDrilldown(BabelTranslator babel, string filter)
    {
        var subCounts = new SortedDictionary<string, int>();
        for (int i = 0; i < babel.Keys.Length; i++)
        {
            var k = babel.Keys[i];
            if (!k.StartsWith(filter)) continue;
            string rest = k[filter.Length..];
            int us = rest.IndexOf('_');
            if (us < 0) us = rest.Length;
            string sub = rest[..us];
            subCounts.TryGetValue(sub, out int c);
            subCounts[sub] = c + 1;
        }
        Console.WriteLine($"Sub-categories under '{filter}':");
        foreach (var kv in subCounts.OrderByDescending(kv => kv.Value).Take(50))
            Console.WriteLine($"  {kv.Key,-30} {kv.Value,6}");

        Console.WriteLine();
        Console.WriteLine($"Sample keys/values under '{filter}':");
        int n = 0;
        for (int i = 0; i < babel.Keys.Length && n < 40; i++)
        {
            var k = babel.Keys[i];
            if (!k.StartsWith(filter)) continue;
            string v = babel.Translations[i];
            v = (v ?? "").Replace('\n', ' ').Replace('\r', ' ');
            if (v.Length > 100) v = v[..100] + "…";
            Console.WriteLine($"  {k}\n    -> \"{v}\"");
            n++;
        }
    }

    public static void RunSearch(BabelTranslator babel, string substring)
    {
        bool searchValues = Environment.GetEnvironmentVariable("DEXPORTER_SEARCH_VALUES") == "1";
        int limit = int.TryParse(Environment.GetEnvironmentVariable("DEXPORTER_SEARCH_LIMIT") ?? "30", out var lim) ? lim : 30;
        Console.WriteLine($"{(searchValues ? "Values" : "Keys")} containing '{substring}':");
        int n = 0;
        for (int i = 0; i < babel.Keys.Length; i++)
        {
            string hay = searchValues ? babel.Translations[i] : babel.Keys[i];
            if (hay == null || hay.IndexOf(substring, StringComparison.OrdinalIgnoreCase) < 0) continue;
            string v = (babel.Translations[i] ?? "").Replace('\n', ' ');
            if (v.Length > 80) v = v[..80] + "…";
            Console.WriteLine($"  {babel.Keys[i]}\n    -> \"{v}\"");
            if (++n >= limit) break;
        }
        if (n == 0) Console.WriteLine("  (none)");
    }

    // For every non-empty translation in babel, check whether its text appears
    // anywhere in the categorised export. Reports coverage stats + samples of
    // uncovered translations so we can see what (if anything) is being lost.
    public static void RunCoverage(BabelTranslator babel, string exportRoot)
    {
        Console.WriteLine($"Scanning export corpus at: {exportRoot}");
        var corpus = new System.Text.StringBuilder();
        int files = 0;
        foreach (var f in Directory.EnumerateFiles(exportRoot, "*.md", SearchOption.AllDirectories))
        {
            try { corpus.Append(File.ReadAllText(f)); corpus.Append('\n'); files++; } catch { }
        }
        // Also include the raw flat dumps and per-story JSON for completeness.
        string rawDir = Path.GetFullPath(Path.Combine(exportRoot, "..", "exports"));
        if (Directory.Exists(rawDir))
        {
            foreach (var f in Directory.EnumerateFiles(rawDir, "*.txt", SearchOption.AllDirectories))
            { try { corpus.Append(File.ReadAllText(f)); corpus.Append('\n'); files++; } catch { } }
            foreach (var f in Directory.EnumerateFiles(rawDir, "*.json", SearchOption.AllDirectories))
            { try { corpus.Append(File.ReadAllText(f)); corpus.Append('\n'); files++; } catch { } }
        }
        string blob = corpus.ToString();
        Console.WriteLine($"  scanned {files} files, {blob.Length:N0} chars total");

        int total = 0, empty = 0, covered = 0;
        var uncoveredByPrefix = new Dictionary<string, int>();
        var uncoveredSamples = new Dictionary<string, List<(string key, string val)>>();

        for (int i = 0; i < babel.Translations.Length; i++)
        {
            string t = babel.Translations[i];
            string k = babel.Keys[i];
            if (string.IsNullOrWhiteSpace(t)) { empty++; continue; }
            total++;

            // Trim very short translations (<= 2 chars) — they're usually
            // single-letter abbreviations or punctuation that match anywhere
            // and would inflate "coverage".
            if (t.Length <= 2) { covered++; continue; }

            // Use first 60 chars as fingerprint; long translations may have
            // newlines munged differently in different writers.
            string needle = t.Length > 60 ? t[..60] : t;
            // Normalise newlines so multi-line translations match.
            needle = needle.Replace("\r", "").Replace("\n", " ");
            string hayNorm = blob;  // already raw; matched after normalising needle

            bool found = blob.Contains(needle);
            if (!found)
            {
                // Fallback: try the unnormalised needle.
                found = blob.Contains(t.Length > 60 ? t[..60] : t);
            }

            if (found) covered++;
            else
            {
                int slash = k.IndexOf('/');
                string pref = slash > 0 ? k[..slash] : "(no-slash)";
                uncoveredByPrefix.TryGetValue(pref, out int c);
                uncoveredByPrefix[pref] = c + 1;
                if (!uncoveredSamples.TryGetValue(pref, out var list))
                    uncoveredSamples[pref] = list = new();
                if (list.Count < 3) list.Add((k, t.Length > 80 ? t[..80] + "…" : t));
            }
        }
        Console.WriteLine();
        Console.WriteLine($"Babel rows total      : {babel.Translations.Length:N0}");
        Console.WriteLine($"  empty translations  : {empty:N0}");
        Console.WriteLine($"  non-empty           : {total:N0}");
        Console.WriteLine($"  covered in corpus   : {covered:N0}  ({100.0 * covered / total:F2}%)");
        Console.WriteLine($"  NOT covered         : {total - covered:N0}");
        Console.WriteLine();
        if (total - covered > 0)
        {
            Console.WriteLine("Uncovered translations grouped by key prefix (top 25):");
            foreach (var kv in uncoveredByPrefix.OrderByDescending(kv => kv.Value).Take(25))
            {
                Console.WriteLine($"  {kv.Key,-50} {kv.Value,6}");
                if (uncoveredSamples.TryGetValue(kv.Key, out var samples))
                    foreach (var (k, v) in samples)
                        Console.WriteLine($"      {k} = \"{v.Replace('\n',' ')}\"");
            }
        }

        // Optional: dump every uncovered key to a file for offline analysis.
        string dumpPath = Environment.GetEnvironmentVariable("DEXPORTER_COVERAGE_DUMP");
        if (!string.IsNullOrEmpty(dumpPath))
        {
            using var w = new StreamWriter(dumpPath);
            for (int i = 0; i < babel.Translations.Length; i++)
            {
                string t = babel.Translations[i];
                string k = babel.Keys[i];
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.Length <= 2) continue;
                string needle = t.Length > 60 ? t[..60] : t;
                needle = needle.Replace("\r", "").Replace("\n", " ");
                bool found = blob.Contains(needle) || blob.Contains(t.Length > 60 ? t[..60] : t);
                if (!found) w.WriteLine($"{k}\t{t.Replace('\t', ' ').Replace('\n', ' ')}");
            }
            Console.WriteLine($"\nDumped uncovered keys to: {dumpPath}");
        }
    }

    public static void Run(BabelTranslator babel)
    {
        var prefixCounts = new SortedDictionary<string, int>();
        foreach (var k in babel.Keys)
        {
            int slash = k.IndexOf('/');
            string prefix = slash > 0 ? k[..slash] : "(no-slash)";
            prefixCounts.TryGetValue(prefix, out int c);
            prefixCounts[prefix] = c + 1;
        }
        Console.WriteLine("Babel key prefixes:");
        foreach (var kv in prefixCounts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {kv.Key,-30} {kv.Value,6}");

        Console.WriteLine();
        Console.WriteLine("Sample 'Template/displayName_' keys:");
        int n = 0;
        foreach (var k in babel.Keys)
        {
            if (k.StartsWith("Template/displayName_"))
            {
                Console.WriteLine($"  {k}");
                if (++n >= 8) break;
            }
        }

        Console.WriteLine();
        Console.WriteLine("Sample of every prefix (first 3 keys each):");
        var seen = new Dictionary<string, int>();
        foreach (var k in babel.Keys)
        {
            int slash = k.IndexOf('/');
            string prefix = slash > 0 ? k[..slash] : "(no-slash)";
            seen.TryGetValue(prefix, out int c);
            if (c < 3)
            {
                Console.WriteLine($"  [{prefix}] {k} = \"{Truncate(babel.Translations[Array.IndexOf(babel.Keys, k)], 80)}\"");
                seen[prefix] = c + 1;
            }
        }
    }

    private static string Truncate(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\n', ' ').Replace('\r', ' ');
        return s.Length <= n ? s : s[..n] + "…";
    }
}
