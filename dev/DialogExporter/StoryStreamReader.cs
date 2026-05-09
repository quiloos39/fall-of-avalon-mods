using System.Buffers.Binary;

namespace DialogExporter;

// Mirrors BufferStreamReader semantics: sequential little-endian read of
// unmanaged values, plus the typed overloads from the game's StoryReader.cs.
//
// CRITICAL: every Read here must consume EXACTLY the bytes the matching
// game-side Write produced, otherwise the cursor goes out of alignment for
// the next step in the same chapter and decoding falls apart for the rest
// of the .story file.
public sealed class StoryStreamReader
{
    private readonly byte[] _buf;
    public int Pos;

    public StoryStreamReader(byte[] buf) { _buf = buf; }
    public int Length => _buf.Length;
    public bool AtEnd => Pos >= _buf.Length;

    public byte ReadByte() => _buf[Pos++];

    public bool ReadBool() => _buf[Pos++] != 0;

    public short ReadInt16()
    {
        short v = BinaryPrimitives.ReadInt16LittleEndian(_buf.AsSpan(Pos, 2));
        Pos += 2;
        return v;
    }

    public ushort ReadUInt16()
    {
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_buf.AsSpan(Pos, 2));
        Pos += 2;
        return v;
    }

    public int ReadInt32()
    {
        int v = BinaryPrimitives.ReadInt32LittleEndian(_buf.AsSpan(Pos, 4));
        Pos += 4;
        return v;
    }

    public uint ReadUInt32()
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buf.AsSpan(Pos, 4));
        Pos += 4;
        return v;
    }

    public long ReadInt64()
    {
        long v = BinaryPrimitives.ReadInt64LittleEndian(_buf.AsSpan(Pos, 8));
        Pos += 8;
        return v;
    }

    public ulong ReadUInt64()
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_buf.AsSpan(Pos, 8));
        Pos += 8;
        return v;
    }

    public float ReadFloat()
    {
        float v = BinaryPrimitives.ReadSingleLittleEndian(_buf.AsSpan(Pos, 4));
        Pos += 4;
        return v;
    }

    public double ReadDouble()
    {
        double v = BinaryPrimitives.ReadDoubleLittleEndian(_buf.AsSpan(Pos, 8));
        Pos += 8;
        return v;
    }

    public string ReadString()
    {
        int len = ReadInt32();
        if (len < 0) throw new InvalidDataException($"negative string length {len} at {Pos - 4}");
        var span = _buf.AsSpan(Pos, len * 2);
        Pos += len * 2;
        // BufferStreamReader writes length-prefixed UTF-16 LE chars (raw memory copy)
        Span<char> chars = len <= 256 ? stackalloc char[len] : new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(i * 2, 2));
        return new string(chars);
    }

    // ---- Typed game-side reads (matching StoryReader.Read overloads) ----

    // LightLocString: 4-byte LocalizationEntryId._id (raw uint, 0=invalid, otherwise (id-1) is the index)
    public uint ReadLightLocString() => ReadUInt32();

    // LocString: { string IdOverride, string ID } — Fallback is NOT serialized in the runtime story.
    public (string IdOverride, string Id) ReadLocString()
    {
        string idOverride = ReadString();
        string id = ReadString();
        return (idOverride, id);
    }

    // ActorRef: { string guid }
    public string ReadActorRefGuid() => ReadString();

    public string ReadActorStateName() => ReadString();

    // TemplateReference: { string Guid }
    public string ReadTemplateRefGuid() => ReadString();

    // RichEnumReference: { string EnumRef }
    public string ReadRichEnumRefStr() => ReadString();

    // LocationReference: { TargetType targetTypes (byte), string[] tags,
    //                       TemplateReference[] locationRefs, ActorRef[] actors }
    // TargetType is `enum : byte` — must read 1 byte, not 2.
    public (byte targetTypes, string[] tags, string[] locRefs, string[] actorRefs) ReadLocationReference()
    {
        byte tt = ReadByte();
        var tags = ReadStringArray();
        var locRefs = ReadTemplateRefArray();
        var actors = ReadActorRefArray();
        return (tt, tags, locRefs, actors);
    }

    // SceneReference: { ARAssetReference reference } — TWO strings (address + subObjectName).
    public (string address, string sub) ReadSceneReference()
    {
        string a = ReadString();
        string s = ReadString();
        return (a, s);
    }

    // SpriteReference: { ARSpriteReference arSpriteReference } — ARSpriteReference is { string Guid, string SubObjectName }
    public (string guid, string subObj) ReadARAssetRef()
    {
        string guid = ReadString();
        string sub = ReadString();
        return (guid, sub);
    }
    public (string guid, string subObj) ReadSpriteReference() => ReadARAssetRef();
    public (string guid, string subObj) ReadShareableSpriteReference() => ReadARAssetRef();
    public (string guid, string subObj) ReadShareableARAssetReference() => ReadARAssetRef();

    // EventReference (FMOD GUID): 16 raw bytes
    public byte[] ReadFmodGuid()
    {
        byte[] g = new byte[16];
        Buffer.BlockCopy(_buf, Pos, g, 0, 16);
        Pos += 16;
        return g;
    }

    // IntRange: int low, int high
    public (int low, int high) ReadIntRange() => (ReadInt32(), ReadInt32());

    // BaseAudioSource: { EventReference eventRef, int priorityOverride, bool isCopyrighted }
    // priorityOverride is INT, not byte — total = 16 + 4 + 1 = 21 bytes.
    public (byte[] guid, int prio, bool copyrighted) ReadBaseAudioSource()
    {
        var guid = ReadFmodGuid();
        int prio = ReadInt32();
        bool cp = ReadBool();
        return (guid, prio, cp);
    }

    public string[] ReadStringArray()
    {
        int n = ReadInt32();
        if (n == 0) return Array.Empty<string>();
        var arr = new string[n];
        for (int i = 0; i < n; i++) arr[i] = ReadString();
        return arr;
    }

    public string[] ReadActorRefArray()
    {
        int n = ReadInt32();
        if (n == 0) return Array.Empty<string>();
        var arr = new string[n];
        for (int i = 0; i < n; i++) arr[i] = ReadActorRefGuid();
        return arr;
    }

    public string[] ReadTemplateRefArray()
    {
        int n = ReadInt32();
        if (n == 0) return Array.Empty<string>();
        var arr = new string[n];
        for (int i = 0; i < n; i++) arr[i] = ReadTemplateRefGuid();
        return arr;
    }

    public string[] ReadRichEnumRefArray()
    {
        int n = ReadInt32();
        if (n == 0) return Array.Empty<string>();
        var arr = new string[n];
        for (int i = 0; i < n; i++) arr[i] = ReadRichEnumRefStr();
        return arr;
    }

    // Variable: { string name, float value, VariableType type } — see Variable.cs
    public (string name, float value, int type) ReadVariable()
    {
        string name = ReadString();
        float value = ReadFloat();
        int type = ReadInt32(); // VariableType is a default-int enum
        return (name, value, type);
    }

    // VariableDefine: { string name, Variable defaultValue, string[] context, Context[] contexts }
    public (string name, (string n, float v, int t) defaultValue, string[] context, (byte type, string template)[] contexts) ReadVariableDefine()
    {
        string name = ReadString();
        var dv = ReadVariable();
        var ctx = ReadStringArray();
        int n = ReadInt32();
        var ctxArr = new (byte, string)[n];
        for (int i = 0; i < n; i++) ctxArr[i] = ReadContext();
        return (name, dv, ctx, ctxArr);
    }

    // VariableReferenceDefine: { string name, TemplateReference template }
    public (string name, string templateGuid) ReadVariableReferenceDefine()
    {
        string name = ReadString();
        string tg = ReadTemplateRefGuid();
        return (name, tg);
    }

    // Context: { byte type, TemplateReference template }
    public (byte type, string template) ReadContext()
    {
        byte t = ReadByte();
        string tpl = ReadTemplateRefGuid();
        return (t, tpl);
    }

    // ItemSpawningData: { TemplateReference itemTemplateReference, int quantity, IntRange itemLvl }
    // CRITICAL: itemLvl is IntRange (low + high ints = 8 bytes), not a single int.
    public (string templateGuid, int quantity, int lvlLow, int lvlHigh) ReadItemSpawningData()
    {
        string tg = ReadTemplateRefGuid();
        int q = ReadInt32();
        int low = ReadInt32();
        int high = ReadInt32();
        return (tg, q, low, high);
    }

    public (string templateGuid, int quantity, int lvlLow, int lvlHigh)[] ReadItemSpawningDataArray()
    {
        int n = ReadInt32();
        if (n == 0) return Array.Empty<(string, int, int, int)>();
        var arr = new (string, int, int, int)[n];
        for (int i = 0; i < n; i++) arr[i] = ReadItemSpawningData();
        return arr;
    }

    // ActionData (used in some steps); structure per ActionData.cs — read it generically as 3 strings + a few primitives if encountered.
    // We expose it lazily; if a step needs ActionData[], it'll spell out the layout itself.

    public (double startTime, string emoteKey, int state, int expressionHandler, float roundDuration) ReadEmotionData()
    {
        // EmotionData.cs:
        //   Read(ref value.startTime);          // double
        //   Read(ref value.emotionKey);         // string
        //   Read(ref value.state);              // EmotionState (default-int enum) → 4 bytes
        //   Read(ref value.expressionHandler);  // ExpressionHandler (default-int enum) → 4 bytes
        //   Read(ref value.roundDuration);      // float
        double startTime = ReadDouble();
        string emoteKey = ReadString();
        int state = ReadInt32();
        int exprHandler = ReadInt32();
        float roundDur = ReadFloat();
        return (startTime, emoteKey, state, exprHandler, roundDur);
    }

    public (double startTime, string emoteKey, int state, int expressionHandler, float roundDuration)[] ReadEmotionDataArray()
    {
        int n = ReadInt32();
        if (n == 0) return Array.Empty<(double, string, int, int, float)>();
        var arr = new (double, string, int, int, float)[n];
        for (int i = 0; i < n; i++) arr[i] = ReadEmotionData();
        return arr;
    }

    public (string story, string chapterName) ReadStoryBookmark()
    {
        string story = ReadTemplateRefGuid();
        string chapter = ReadString();
        return (story, chapter);
    }

    public (uint targetChapterIdx, bool isMain, uint textIdx) ReadRuntimeChoice()
    {
        // RuntimeChoice serialization: targetChapter (int chapter index, -1 = null), isMainChoice (bool), text (LightLocString uint)
        int tci = ReadInt32();
        bool main = ReadBool();
        uint text = ReadLightLocString();
        return ((uint)tci, main, text);
    }

    // StoryConditionInput[]: { StoryConditions conditions; bool negate }
    public (int condIdx, bool negate)[] ReadConditionInputArray()
    {
        int n = ReadInt32();
        if (n == 0) return Array.Empty<(int, bool)>();
        var arr = new (int, bool)[n];
        for (int i = 0; i < n; i++)
        {
            int idx = ReadInt32();
            bool neg = ReadBool();
            arr[i] = (idx, neg);
        }
        return arr;
    }

    public int ReadChapterIndex() => ReadInt32(); // -1 = null

    // JournalGuid → ARGuid → SerializableGuid (16-byte struct, Explicit layout
    // overlapping a System.Guid with 4 ints). Generic Read<T>() reads exactly
    // 16 bytes — NOT a string.
    public byte[] ReadJournalGuid()
    {
        byte[] g = new byte[16];
        Buffer.BlockCopy(_buf, Pos, g, 0, 16);
        Pos += 16;
        return g;
    }

    // LoadingHandle: { ARAssetReference video, ARAssetReference subtitlesReference, EventReference videoAudio }
    public ((string g, string s) v, (string g, string s) sub, byte[] audio) ReadLoadingHandle()
    {
        var v = ReadARAssetRef();
        var s = ReadARAssetRef();
        var a = ReadFmodGuid();
        return (v, s, a);
    }

    // RichLabelUsage: { RichLabelUsageEntry[] entries, RichLabelConfigType enum:byte }
    public ((string guid, bool include)[] entries, byte configType) ReadRichLabelUsage()
    {
        int n = ReadInt32();
        var arr = new (string, bool)[n];
        for (int i = 0; i < n; i++) arr[i] = ReadRichLabelUsageEntry();
        byte ct = ReadByte();
        return (arr, ct);
    }

    public (string guid, bool include) ReadRichLabelUsageEntry()
    {
        string guid = ReadString();
        bool inc = ReadBool();
        return (guid, inc);
    }
}
