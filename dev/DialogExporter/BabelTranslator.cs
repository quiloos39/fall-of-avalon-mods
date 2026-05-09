using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace DialogExporter;

// Unity rich-text tags we strip / convert when rendering text for humans.
// The game pipes display strings through TextMeshPro which understands tags
// like <b>…</b>, <i>…</i>, <color=#fff>…</color>, <size=20>…</size>,
// <sprite name="..."/>, <indent=…>, <line-height=…>, etc. None of those help
// an LLM reading the export, so we either drop them or convert to markdown.
public static class RichText
{
    private static readonly Regex BoldOpen   = new(@"<\s*b\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BoldClose  = new(@"<\s*/\s*b\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ItalicOpen = new(@"<\s*i\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ItalicClose= new(@"<\s*/\s*i\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<\s*/?\s*(?:color|size|sprite|indent|line-height|align|cspace|font|gradient|mark|mspace|nobr|noparse|page|pos|rotate|space|style|sub|sup|u|s|strikethrough|voffset|width)[^>]*?>",
                                            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmptyTag = new(@"<\s*/?\s*>", RegexOptions.Compiled);

    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.IndexOf('<') < 0) return s;
        s = BoldOpen.Replace(s, "**");
        s = BoldClose.Replace(s, "**");
        s = ItalicOpen.Replace(s, "*");
        s = ItalicClose.Replace(s, "*");
        s = Tag.Replace(s, "");
        s = EmptyTag.Replace(s, "");
        return s;
    }
}

// Mirrors PreloadedBabelProvider's data layout. Loads keys + per-locale
// translations from languages.arch. LocalizationEntryId.Index is a flat
// uint indexing into BOTH keys_positions and the active locale's positions.
public sealed class BabelTranslator
{
    public string[] Keys { get; private set; } = Array.Empty<string>();
    public string[] Translations { get; private set; } = Array.Empty<string>();
    public string Locale { get; private set; } = "(none)";
    public List<string> AvailableLocales { get; } = new();

    public string Translate(uint indexValue) // raw uint from .story file (1-based; 0=invalid)
    {
        if (indexValue == 0) return string.Empty;
        uint idx = indexValue - 1;
        if (idx >= Translations.Length) return string.Empty;
        return Translations[idx];
    }

    // Same as Translate but with Unity rich-text tags stripped / converted.
    public string TranslateClean(uint indexValue) => RichText.Clean(Translate(indexValue));

    private Dictionary<string, int> _keyIndex;

    // Lookup by string key (used by LocString, which stores its key as a string).
    public string TranslateByKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (_keyIndex == null)
        {
            _keyIndex = new Dictionary<string, int>(Keys.Length, StringComparer.Ordinal);
            for (int i = 0; i < Keys.Length; i++) _keyIndex[Keys[i]] = i;
        }
        if (_keyIndex.TryGetValue(key, out int idx)) return Translations[idx] ?? string.Empty;
        return string.Empty;
    }

    public string TranslateByKeyClean(string key) => RichText.Clean(TranslateByKey(key));

    public string KeyOf(uint indexValue)
    {
        if (indexValue == 0) return string.Empty;
        uint idx = indexValue - 1;
        if (idx >= Keys.Length) return string.Empty;
        return Keys[idx];
    }

    public static BabelTranslator Load(string languagesArch, string locale)
    {
        var bundle = UnityFsBundle.Load(languagesArch);

        // Find available locales by directory entries that look like "<locale>/strings.blob"
        var t = new BabelTranslator();
        foreach (var e in bundle.Entries)
        {
            int slash = e.Path.IndexOf('/');
            if (slash > 0 && e.Path.EndsWith("/strings.blob", StringComparison.OrdinalIgnoreCase))
            {
                string loc = e.Path[..slash];
                if (!t.AvailableLocales.Contains(loc)) t.AvailableLocales.Add(loc);
            }
        }

        // Load keys (locale-independent)
        var keysData = ReadCharBlob(bundle, MatchEnding(bundle, "keys_data.blob", null));
        var keysPos = LogPositions(ReadStringPositions(bundle, MatchEnding(bundle, "keys_positions.blob", null)));
        t.Keys = SliceStrings(keysData, keysPos);

        // Load translations for the requested locale
        var stringsData = ReadCharBlob(bundle, MatchEnding(bundle, "strings.blob", locale));
        var stringsPos = LogPositions(ReadStringPositions(bundle, MatchEnding(bundle, "positions.blob", locale)));
        t.Translations = SliceStrings(stringsData, stringsPos);
        t.Locale = locale;

        return t;
    }

    private static UnityFsBundle.Entry MatchEnding(UnityFsBundle bundle, string fileName, string locale)
    {
        // Filenames inside the bundle use forward slashes. Keys files are at root,
        // language-specific files are at "<locale>/<file>".
        foreach (var e in bundle.Entries)
        {
            if (!e.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (locale == null)
            {
                // root-level: the path should NOT contain a directory matching a known locale folder
                if (!e.Path.Contains('/')) return e;
            }
            else
            {
                if (e.Path.StartsWith(locale + "/", StringComparison.OrdinalIgnoreCase)) return e;
            }
        }
        throw new FileNotFoundException(
            locale == null
                ? $"Babel archive is missing root file '{fileName}'"
                : $"Babel archive is missing '{locale}/{fileName}' (locale not present?)");
    }

    private static char[] ReadCharBlob(UnityFsBundle bundle, UnityFsBundle.Entry e)
    {
        // Raw UTF-16 LE chars (typical x86_64 layout).
        if ((e.Data.Length & 1) != 0)
            throw new InvalidDataException($"{e.Path}: char blob has odd byte length");
        char[] chars = new char[e.Data.Length / 2];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(e.Data.AsSpan(i * 2, 2));
        }
        return chars;
    }

    private static StringPosition[] LogPositions(StringPosition[] arr) => arr;

    private struct StringPosition { public uint CharStart; public uint CharLength; }

    private static StringPosition[] ReadStringPositions(UnityFsBundle bundle, UnityFsBundle.Entry e)
    {
        if (e.Data.Length % 8 != 0)
            throw new InvalidDataException($"{e.Path}: positions size not a multiple of 8");
        int count = e.Data.Length / 8;
        var arr = new StringPosition[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = new StringPosition
            {
                CharStart = BinaryPrimitives.ReadUInt32LittleEndian(e.Data.AsSpan(i * 8, 4)),
                CharLength = BinaryPrimitives.ReadUInt32LittleEndian(e.Data.AsSpan(i * 8 + 4, 4)),
            };
        }
        return arr;
    }

    private static string[] SliceStrings(char[] chars, StringPosition[] positions)
    {
        var arr = new string[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            ref var p = ref positions[i];
            if (p.CharStart + p.CharLength > chars.Length)
                throw new InvalidDataException(
                    $"position[{i}] (start={p.CharStart}, len={p.CharLength}) exceeds buffer length {chars.Length}");
            arr[i] = new string(chars, (int)p.CharStart, (int)p.CharLength);
        }
        return arr;
    }
}
