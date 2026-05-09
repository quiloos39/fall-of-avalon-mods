using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Awaken.TG.Main.Fights.NPCs;

namespace GuardVariety
{
    // Runtime registry of (addressableKey -> Gender) populated as the player
    // explores. Used by Tier 3 prefab-swap to pick alternate female humanoid
    // prefabs for guards. Persists to BepInEx/config/com.user.guardvariety.prefabs.txt
    // so the first-session bootstrap gap (no female prefabs known yet) self-heals
    // across sessions — by the second session most common villager prefabs are
    // already on disk and guards can swap immediately.
    internal static class PrefabRegistry
    {
        private const string FileName = "com.user.guardvariety.prefabs.txt";

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Gender> _byAddress = new Dictionary<string, Gender>();
        private static List<string> _femaleAddresses = new List<string>();
        private static List<string> _maleAddresses = new List<string>();
        private static string _filePath;
        private static bool _loaded;

        public static int FemaleCount { get { lock (_lock) return _femaleAddresses.Count; } }
        public static int MaleCount { get { lock (_lock) return _maleAddresses.Count; } }

        public static void Load()
        {
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                _filePath = Path.Combine(Paths.ConfigPath, FileName);

                if (!File.Exists(_filePath))
                {
                    Plugin.Log.LogInfo($"[GuardVariety][Registry] No persisted registry at {_filePath} — starting empty.");
                    return;
                }

                try
                {
                    int count = 0;
                    foreach (var raw in File.ReadAllLines(_filePath))
                    {
                        var line = raw?.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                        // format: <gender>\t<address>
                        var sep = line.IndexOf('\t');
                        if (sep < 0) continue;
                        var genderStr = line.Substring(0, sep).Trim();
                        var address = line.Substring(sep + 1).Trim();
                        if (string.IsNullOrEmpty(address)) continue;
                        if (!Enum.TryParse<Gender>(genderStr, out var gender)) continue;
                        if (gender == Gender.None) continue;

                        _byAddress[address] = gender;
                        count++;
                    }
                    RebuildLists_NoLock();
                    Plugin.Log.LogInfo(
                        $"[GuardVariety][Registry] Loaded {count} prefab(s) from disk " +
                        $"({_femaleAddresses.Count}F, {_maleAddresses.Count}M).");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[GuardVariety][Registry] Failed to load: {e.GetBaseException().Message}");
                }
            }
        }

        public static void Register(string address, Gender gender)
        {
            if (string.IsNullOrEmpty(address) || gender == Gender.None) return;

            bool changed = false;
            int newF = 0, newM = 0;
            lock (_lock)
            {
                if (_byAddress.TryGetValue(address, out var existing) && existing == gender) return;
                _byAddress[address] = gender;
                RebuildLists_NoLock();
                changed = true;
                newF = _femaleAddresses.Count;
                newM = _maleAddresses.Count;
            }

            if (changed)
            {
                Persist();
                // First female entry is a milestone — log INFO so the user
                // knows discovery worked. Otherwise verbose-only to avoid spam.
                bool firstFemale = (gender == Gender.Female && newF == 1);
                if (firstFemale)
                {
                    Plugin.Log.LogInfo($"[GuardVariety][Registry] First Female prefab discovered: {address} (registry now has {newF}F, {newM}M). Female guard swaps can now fire on next guard spawn.");
                }
                else if (Plugin.Cfg != null && Plugin.Cfg.Verbose.Value)
                {
                    Plugin.Log.LogInfo($"[GuardVariety][Registry] +{gender}: {address} (now {newF}F, {newM}M)");
                }
            }
        }

        // Picks a female prefab address by seed. Uniform over the current pool.
        // Returns null if no female prefab is registered yet.
        public static string PickFemale(int seed)
        {
            lock (_lock)
            {
                if (_femaleAddresses.Count == 0) return null;
                int i = (int)((uint)seed % (uint)_femaleAddresses.Count);
                return _femaleAddresses[i];
            }
        }

        private static void RebuildLists_NoLock()
        {
            var f = new List<string>();
            var m = new List<string>();
            foreach (var kvp in _byAddress)
            {
                if (kvp.Value == Gender.Female) f.Add(kvp.Key);
                else if (kvp.Value == Gender.Male) m.Add(kvp.Key);
            }
            f.Sort(StringComparer.Ordinal);
            m.Sort(StringComparer.Ordinal);
            _femaleAddresses = f;
            _maleAddresses = m;
        }

        private static void Persist()
        {
            try
            {
                if (string.IsNullOrEmpty(_filePath)) return;
                Dictionary<string, Gender> snapshot;
                lock (_lock) snapshot = new Dictionary<string, Gender>(_byAddress);

                using (var w = new StreamWriter(_filePath, append: false))
                {
                    w.WriteLine("# GuardVariety prefab registry — auto-generated. Format: <gender>\\t<addressableKey>");
                    foreach (var kvp in snapshot)
                    {
                        w.Write(kvp.Value);
                        w.Write('\t');
                        w.WriteLine(kvp.Key);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[GuardVariety][Registry] Persist failed: {e.GetBaseException().Message}");
            }
        }
    }
}
