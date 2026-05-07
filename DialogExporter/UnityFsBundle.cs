using System.Buffers.Binary;
using System.Text;
using K4os.Compression.LZ4;

namespace DialogExporter;

// Minimal reader for UnityFS asset bundles.
// We don't try to parse Unity asset metadata — every file inside the bundles
// we care about (story.arch, languages.arch) is a raw binary blob written
// with FileRead/BufferStreamReader, not a Unity SerializedFile. So we just
// need the file directory and the raw bytes for each entry.
public sealed class UnityFsBundle
{
    public sealed class Entry
    {
        public string Path;
        public byte[] Data;
    }

    public List<Entry> Entries { get; } = new();
    public Dictionary<string, Entry> ByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static UnityFsBundle Load(string path)
    {
        Console.WriteLine($"  [bundle] reading {path}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        byte[] file = File.ReadAllBytes(path);
        Console.WriteLine($"  [bundle] {file.Length:N0} bytes loaded in {sw.Elapsed.TotalSeconds:F1}s");
        var b = new UnityFsBundle();
        b.Parse(file);
        Console.WriteLine($"  [bundle] parsed {b.Entries.Count} entries in {sw.Elapsed.TotalSeconds:F1}s total");
        return b;
    }

    private void Parse(byte[] file)
    {
        var r = new BeReader(file);

        // Header
        string magic = r.ReadCString();
        if (magic != "UnityFS")
            throw new InvalidDataException($"Not a UnityFS bundle (magic={magic})");

        int version = r.ReadInt32();
        string unityVersion = r.ReadCString();
        string unityRevision = r.ReadCString();
        long bundleSize = r.ReadInt64();
        int compressedBlocksInfoSize = r.ReadInt32();
        int uncompressedBlocksInfoSize = r.ReadInt32();
        uint flags = r.ReadUInt32();

        if (version >= 7)
        {
            // Align to 16 bytes
            int pad = (16 - (r.Pos & 15)) & 15;
            r.Pos += pad;
        }

        // Blocks info — may be at end or after header
        bool blocksInfoAtEnd = (flags & 0x80) != 0;
        long blocksInfoOffset = blocksInfoAtEnd
            ? file.Length - compressedBlocksInfoSize
            : r.Pos;

        byte[] blocksInfoCompressed = new byte[compressedBlocksInfoSize];
        Buffer.BlockCopy(file, (int)blocksInfoOffset, blocksInfoCompressed, 0, compressedBlocksInfoSize);

        byte[] blocksInfo = DecompressIfNeeded(
            blocksInfoCompressed,
            uncompressedBlocksInfoSize,
            (int)(flags & 0x3F));

        // Parse blocks info
        var br = new BeReader(blocksInfo);
        br.Pos += 16; // skip uncompressed-data-hash
        int blockCount = br.ReadInt32();

        var blockInfos = new BlockInfo[blockCount];
        for (int i = 0; i < blockCount; i++)
        {
            blockInfos[i] = new BlockInfo
            {
                UncompressedSize = br.ReadInt32(),
                CompressedSize = br.ReadInt32(),
                Flags = br.ReadUInt16(),
            };
        }

        int dirCount = br.ReadInt32();
        var dirEntries = new DirectoryEntry[dirCount];
        for (int i = 0; i < dirCount; i++)
        {
            dirEntries[i] = new DirectoryEntry
            {
                Offset = br.ReadInt64(),
                Size = br.ReadInt64(),
                Flags = br.ReadUInt32(),
                Path = br.ReadCString(),
            };
        }

        // Decompress all data blocks into one big stream
        long dataStartOffset = blocksInfoAtEnd ? r.Pos : (r.Pos + compressedBlocksInfoSize);

        // Bit 9 (0x200) indicates the data section is aligned to a 16-byte
        // boundary AFTER the blocks info. Without this, all per-entry offsets
        // come out 1 byte too early on archives baked with this flag set.
        if ((flags & 0x200) != 0 && !blocksInfoAtEnd)
        {
            long misalign = dataStartOffset & 15;
            if (misalign != 0) dataStartOffset += (16 - misalign);
        }

        long dataLengthTotal = 0;
        foreach (var bi in blockInfos) dataLengthTotal += bi.UncompressedSize;
        Console.WriteLine($"  [bundle] flags=0x{flags:X} version={version} blocks={blockCount} dirs={dirCount} dataStart=0x{dataStartOffset:X} totalDecomp={dataLengthTotal:N0}");

        byte[] data = new byte[dataLengthTotal];
        long writePos = 0;
        long readPos = dataStartOffset;
        for (int i = 0; i < blockCount; i++)
        {
            var bi = blockInfos[i];
            byte[] compressed = new byte[bi.CompressedSize];
            Buffer.BlockCopy(file, (int)readPos, compressed, 0, bi.CompressedSize);
            int compType = bi.Flags & 0x3F;
            byte[] decomp = DecompressIfNeeded(compressed, bi.UncompressedSize, compType);
            Buffer.BlockCopy(decomp, 0, data, (int)writePos, bi.UncompressedSize);
            writePos += bi.UncompressedSize;
            readPos += bi.CompressedSize;
        }

        if (Environment.GetEnvironmentVariable("DEXPORTER_DUMP") == "1")
        {
            Console.WriteLine($"  bundle: {dirEntries.Length} entries, total uncompressed data {dataLengthTotal}");
            foreach (var de in dirEntries.Take(8))
                Console.WriteLine($"    {de.Path}: offset={de.Offset} size={de.Size} flags={de.Flags}");
            Console.WriteLine($"    [first 32 bytes of decompressed data] " +
                BitConverter.ToString(data, 0, 32));
        }

        // Slice each directory entry from the merged data
        foreach (var de in dirEntries)
        {
            byte[] entryData = new byte[de.Size];
            Buffer.BlockCopy(data, (int)de.Offset, entryData, 0, (int)de.Size);
            var entry = new Entry { Path = de.Path, Data = entryData };
            Entries.Add(entry);
            ByPath[de.Path] = entry;
        }
    }

    private static byte[] DecompressIfNeeded(byte[] input, int uncompressedSize, int compType)
    {
        return compType switch
        {
            0 => input,                                                // None
            2 or 3 => DecompressLz4(input, uncompressedSize),          // LZ4 / LZ4HC
            _ => throw new NotSupportedException($"compression type {compType} not supported"),
        };
    }

    private static byte[] DecompressLz4(byte[] src, int destSize)
    {
        byte[] dst = new byte[destSize];
        int written = LZ4Codec.Decode(src, 0, src.Length, dst, 0, destSize);
        if (written != destSize)
            throw new InvalidDataException($"LZ4: wrote {written} expected {destSize}");
        return dst;
    }

    private struct BlockInfo
    {
        public int UncompressedSize;
        public int CompressedSize;
        public ushort Flags;
    }

    private sealed class DirectoryEntry
    {
        public long Offset;
        public long Size;
        public uint Flags;
        public string Path;
    }

    // Big-endian binary reader (the bundle header is BE).
    private sealed class BeReader
    {
        private readonly byte[] _buf;
        public int Pos;

        public BeReader(byte[] buf) { _buf = buf; }

        public int ReadInt32()
        {
            int v = BinaryPrimitives.ReadInt32BigEndian(_buf.AsSpan(Pos, 4));
            Pos += 4;
            return v;
        }
        public uint ReadUInt32()
        {
            uint v = BinaryPrimitives.ReadUInt32BigEndian(_buf.AsSpan(Pos, 4));
            Pos += 4;
            return v;
        }
        public ushort ReadUInt16()
        {
            ushort v = BinaryPrimitives.ReadUInt16BigEndian(_buf.AsSpan(Pos, 2));
            Pos += 2;
            return v;
        }
        public long ReadInt64()
        {
            long v = BinaryPrimitives.ReadInt64BigEndian(_buf.AsSpan(Pos, 8));
            Pos += 8;
            return v;
        }
        public string ReadCString()
        {
            int start = Pos;
            while (Pos < _buf.Length && _buf[Pos] != 0) Pos++;
            string s = Encoding.UTF8.GetString(_buf, start, Pos - start);
            if (Pos < _buf.Length) Pos++; // skip the null terminator
            return s;
        }
    }
}
