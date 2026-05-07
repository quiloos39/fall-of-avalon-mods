namespace DialogExporter;

public static class DiagDumpEntries
{
    public static void Run(string path)
    {
        var bundle = UnityFsBundle.Load(path);
        Console.WriteLine($"=== {Path.GetFileName(path)} ({bundle.Entries.Count} entries) ===");
        foreach (var e in bundle.Entries)
        {
            Console.WriteLine($"  {e.Path}    [{e.Data.Length:N0} bytes]");
        }
    }
}
