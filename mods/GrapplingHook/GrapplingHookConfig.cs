using BepInEx.Configuration;
using UnityEngine;

namespace GrapplingHook
{
    internal class GrapplingHookConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<KeyCode> HookKey { get; }

        public ConfigEntry<float> MaxRange { get; }
        public ConfigEntry<float> PullSpeed { get; }
        public ConfigEntry<float> StopDistance { get; }
        public ConfigEntry<float> MaxPullDuration { get; }
        public ConfigEntry<int> HookLayerMask { get; }

        public ConfigEntry<bool> SuppressGravity { get; }
        public ConfigEntry<bool> ShowRope { get; }
        public ConfigEntry<float> RopeWidth { get; }

        public ConfigEntry<bool> Verbose { get; }

        public GrapplingHookConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "1. General", "Enabled", true,
                "Master switch for the Grappling Hook mod.");

            HookKey = cfg.Bind(
                "1. General", "HookKey", KeyCode.F,
                "Hotkey to fire the grappling hook (and to cancel a pull in progress). " +
                "Note: the game's default 'Interact' action is also bound to F — if you press F while " +
                "looking at an interactable (door, container, NPC), both will fire. Rebind here to avoid that.");

            MaxRange = cfg.Bind(
                "2. Hook", "MaxRange", 50f,
                new ConfigDescription(
                    "Maximum distance (in meters) the hook can reach.",
                    new AcceptableValueRange<float>(5f, 200f)));

            PullSpeed = cfg.Bind(
                "2. Hook", "PullSpeed", 22f,
                new ConfigDescription(
                    "How fast (meters/second) the player is pulled toward the hook point. " +
                    "Hero sprint is roughly ~7 m/s — anything above ~12 feels grapple-like.",
                    new AcceptableValueRange<float>(2f, 80f)));

            StopDistance = cfg.Bind(
                "2. Hook", "StopDistance", 1.5f,
                new ConfigDescription(
                    "How close (meters) to the hook point before the pull ends.",
                    new AcceptableValueRange<float>(0.25f, 10f)));

            MaxPullDuration = cfg.Bind(
                "2. Hook", "MaxPullDuration", 5f,
                new ConfigDescription(
                    "Safety timeout (seconds). If the pull hasn't reached the target by this time, abort. " +
                    "Prevents the player getting stuck against geometry.",
                    new AcceptableValueRange<float>(0.5f, 30f)));

            HookLayerMask = cfg.Bind(
                "2. Hook", "HookLayerMask", 26697,
                "Unity layer bitmask the hook can attach to. Default 26697 matches the game's NPC/ground layer mask " +
                "(terrain + walls + obstacles + buildings). Triggers are ignored regardless. " +
                "Set to -1 to allow ANY layer; useful if 26697 misses something.");

            SuppressGravity = cfg.Bind(
                "3. Feel", "SuppressGravity", true,
                "If true, gravity is zeroed each frame during the pull so the player flies cleanly to the target. " +
                "If false, the pull fights gravity and feels heavier (and may fail to reach high targets).");

            ShowRope = cfg.Bind(
                "3. Feel", "ShowRope", true,
                "Draw a rope (LineRenderer) from the player's chest to the hook point during the pull.");

            RopeWidth = cfg.Bind(
                "3. Feel", "RopeWidth", 0.04f,
                new ConfigDescription(
                    "Rope thickness in meters.",
                    new AcceptableValueRange<float>(0.01f, 0.3f)));

            Verbose = cfg.Bind(
                "4. Debug", "Verbose", false,
                "Log every fire/hit/cancel to BepInEx/LogOutput.log.");
        }
    }
}
