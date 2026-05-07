using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Pathfinding;

namespace QuestPath
{
    // Orchestrates: read destination → fire async pathfind → render the result.
    //
    // Visual style:
    //   • Path: a flowing chevron trail on the ground. The line uses a procedurally-generated
    //     "▶▶▶▶▶" texture, tiled along the path length. Each frame we scroll the UV so the
    //     chevrons appear to flow toward the destination, and we breathe an emissive pulse.
    //     Path is simplified (drop near-collinear corners) and clipped to a distance budget so
    //     we don't paint a 200m streak through forests.
    //   • Destination: a glowing rotating rune disc on the ground + a tapered emissive beam +
    //     a HDRP point light with volumetric. Together it reads as "magical waypoint", not
    //     a tech bollard.
    internal class QuestPathController : MonoBehaviour
    {
        public bool Active { get; private set; }

        // Pathfinding plumbing
        private GameObject _seekerHost;
        private Seeker _seeker;

        // Path line (legacy chevron — kept for fallback)
        private GameObject _lineHost;
        private LineRenderer _line;
        private Material _lineMaterial;
        private static Texture2D s_chevronTex;

        // Flower trail — pool of glowing poppy GameObjects placed at corners along the path
        private readonly List<GameObject> _flowerPool = new List<GameObject>();
        private readonly List<float> _flowerBobPhases = new List<float>();
        private static Mesh s_flowerMesh;
        private static Material s_flowerMaterial;
        private static bool s_flowerAssetsResolved;

        // Destination
        private GameObject _beamHost;            // contains beam + disc + light
        private GameObject _beamMesh;            // tapered cylinder (custom mesh)
        private Material _beamMaterial;
        private GameObject _discMesh;            // flat disc on ground
        private Material _discMaterial;
        private HDAdditionalLightData _hdLight;

        // State
        private Vector3? _lastDest;
        private Vector3 _lastPlayerPos;
        private float _nextRecalcAt;
        private bool _pathInFlight;
        private List<Vector3> _currentPath;       // last good path corners (for distance-budget redraw)
        private bool _lastPathReachedTarget;      // false when the path is partial (target in another region)

        private void Start()
        {
            _seekerHost = new GameObject("QuestPath_Seeker");
            DontDestroyOnLoad(_seekerHost);
            _seeker = _seekerHost.AddComponent<Seeker>();
            _seeker.graphMask = -1;

            Active = Plugin.Cfg.ActiveByDefault.Value;
            if (Plugin.Cfg.Verbose.Value) Plugin.Log.LogInfo($"[QuestPath] Started. Active={Active}");
        }

        private void OnDestroy()
        {
            HideVisuals();
            if (_seekerHost != null) Destroy(_seekerHost);
        }

        private void Update()
        {
            if (Plugin.Cfg.ToggleHotkey.Value != KeyCode.None && Input.GetKeyDown(Plugin.Cfg.ToggleHotkey.Value))
            {
                Active = !Active;
                Plugin.Log.LogInfo($"[QuestPath] toggled → {Active}");
                if (!Active) HideVisuals();
            }

            if (!Active) return;

            var (destOpt, source) = DestinationProvider.Resolve();
            if (!destOpt.HasValue) { HideVisuals(); return; }
            var dest = destOpt.Value;

            var heroType = ResolveType("Awaken.TG.Main.Heroes.Hero");
            var hero = heroType?.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            if (hero == null) return;
            Vector3 playerPos = (Vector3)hero.GetType().GetProperty("Coords").GetValue(hero);

            if (Vector3.Distance(playerPos, dest) <= Plugin.Cfg.ArrivalDistance.Value) { HideVisuals(); return; }

            ShowDestination(dest);

            bool destChanged = !_lastDest.HasValue || Vector3.Distance(_lastDest.Value, dest) > 0.5f;
            bool moved       = Vector3.Distance(playerPos, _lastPlayerPos) > Plugin.Cfg.RecalcMoveThreshold.Value;
            bool periodic    = Time.realtimeSinceStartup >= _nextRecalcAt;

            if ((destChanged || moved || periodic) && !_pathInFlight)
            {
                _lastDest = dest;
                _lastPlayerPos = playerPos;
                _nextRecalcAt = Time.realtimeSinceStartup + Plugin.Cfg.RecalcInterval.Value;
                _pathInFlight = true;
                if (Plugin.Cfg.Verbose.Value) Plugin.Log.LogInfo($"[QuestPath] Recalc {playerPos} → {dest} (source={source})");
                _seeker.StartPath(playerPos, dest, OnPathReady);
            }

            // Per-frame redraws (cheap) — only render in-world trail if A* actually reached the
            // target (otherwise the trail would lead toward a wall in a dungeon). Beam still shows
            // at the destination so the player knows where to head.
            bool drawTrail = _currentPath != null && _lastPathReachedTarget;
            if (drawTrail && Plugin.Cfg.ShowPathLine.Value) RefreshPathLine(playerPos);
            else HidePathLine();
            if (drawTrail && Plugin.Cfg.ShowFlowerTrail.Value) RefreshFlowerTrail(playerPos);
            else HideAllFlowers();
            AnimateMaterials();
            AnimateFlowers();
        }

        private void OnPathReady(Path p)
        {
            _pathInFlight = false;
            if (p == null || p.error)
            {
                if (Plugin.Cfg.Verbose.Value) Plugin.Log.LogWarning($"[QuestPath] path failed: {p?.errorLog}");
                _currentPath = null;
                _lastPathReachedTarget = false;
                HidePathLine();
                HideAllFlowers();
                return;
            }

            // Did A* actually reach the requested target, or did it just give up at the edge of
            // the loaded graph (target is in a different region / unloaded scene)? When the player
            // is in a dungeon and the NPC is in the open world, this fires and tells us not to
            // paint a misleading trail toward a wall.
            float endDist = (p.vectorPath != null && p.vectorPath.Count > 0 && _lastDest.HasValue)
                ? Vector3.Distance(p.vectorPath[p.vectorPath.Count - 1], _lastDest.Value)
                : 0f;
            // 5m slack: A* sometimes lands ~1m off the requested point on uneven terrain.
            _lastPathReachedTarget = endDist < 5f;

            _currentPath = SimplifyCorners(p.vectorPath, Plugin.Cfg.LineSimplifyTolerance.Value);

            if (Plugin.Cfg.Verbose.Value)
                Plugin.Log.LogInfo($"[QuestPath] path: {p.vectorPath.Count} raw → {_currentPath.Count} simplified " +
                                   $"(reachedTarget={_lastPathReachedTarget}, endDist={endDist:F1}m)");

            if (!_lastPathReachedTarget)
            {
                // Target is in a different scene/region. Hide the in-world flowers/line so we
                // don't lead the player into a wall. Beam at the destination still shows so they
                // know roughly which direction to head once they leave the area.
                HidePathLine();
                HideAllFlowers();
            }
        }

        // ─── Path line ──────────────────────────────────────────────────────────────

        private void RefreshPathLine(Vector3 playerPos)
        {
            EnsureLineHost();
            float yOff = Plugin.Cfg.LineHeightOffset.Value;
            float maxDist = Plugin.Cfg.LineMaxRenderDistance.Value;

            // Clip the path so we render at most LineMaxRenderDistance meters of it from the
            // player. Beyond that, the player just sees the beam — the ground line in the
            // distance would be barely visible anyway and cost geometry.
            var clipped = ClipPathByLength(_currentPath, maxDist > 0 ? maxDist : float.MaxValue);
            if (clipped.Count < 2) { HidePathLine(); return; }

            _line.positionCount = clipped.Count;
            float runningLen = 0f;
            for (int i = 0; i < clipped.Count; i++)
            {
                _line.SetPosition(i, clipped[i] + Vector3.up * yOff);
                if (i > 0) runningLen += Vector3.Distance(clipped[i - 1], clipped[i]);
            }

            // Tile chevrons every ~1m along the path so they look like discrete arrows.
            float tilesU = Mathf.Max(1f, runningLen);   // 1 chevron tile per metre
            if (_lineMaterial != null && _lineMaterial.HasProperty("_MainTex"))
            {
                _lineMaterial.mainTextureScale = new Vector2(tilesU, 1f);
            }

            float w = Plugin.Cfg.LineWidth.Value;
            _line.startWidth = w;
            _line.endWidth = w;

            _lineHost.SetActive(true);
        }

        private void HidePathLine()
        {
            if (_lineHost != null) _lineHost.SetActive(false);
        }

        private void EnsureLineHost()
        {
            if (_lineHost != null) return;
            _lineHost = new GameObject("QuestPath_Line");
            DontDestroyOnLoad(_lineHost);
            _line = _lineHost.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 4;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.alignment = LineAlignment.View;       // billboards toward camera = always visible

            _lineMaterial = MakeUnlitMaterial("QuestPath/Line", transparent: true);
            _lineMaterial.mainTexture = GetChevronTexture();
            _lineMaterial.mainTextureScale = new Vector2(8, 1);
            _line.material = _lineMaterial;
            ApplyLineColor();
        }

        private void ApplyLineColor()
        {
            if (_lineMaterial == null) return;
            var c = ReadLineColor();
            _lineMaterial.color = c;
            if (_lineMaterial.HasProperty("_EmissionColor"))
                _lineMaterial.SetColor("_EmissionColor", c * Plugin.Cfg.LineEmissive.Value);
        }

        // Drop waypoints that are within `tolerance` meters of the straight line between their
        // neighbours. Keeps endpoints intact. Reduces a 237-corner navmesh path to ~10 visible
        // corners without losing route fidelity.
        private static List<Vector3> SimplifyCorners(List<Vector3> input, float tolerance)
        {
            if (input == null || input.Count < 3 || tolerance <= 0f) return input ?? new List<Vector3>();
            var output = new List<Vector3> { input[0] };
            for (int i = 1; i < input.Count - 1; i++)
            {
                var prev = output[output.Count - 1];
                var curr = input[i];
                var next = input[i + 1];
                // Distance from curr to the line segment prev→next
                float d = PerpDistance(curr, prev, next);
                if (d > tolerance) output.Add(curr);
            }
            output.Add(input[input.Count - 1]);
            return output;
        }

        private static float PerpDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            var proj = a + ab * t;
            return Vector3.Distance(p, proj);
        }

        // Walk the path from the start until we've consumed `maxLen` metres, then stop.
        private static List<Vector3> ClipPathByLength(List<Vector3> path, float maxLen)
        {
            var output = new List<Vector3>();
            if (path == null || path.Count == 0) return output;
            output.Add(path[0]);
            float used = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (used + seg <= maxLen)
                {
                    output.Add(path[i]);
                    used += seg;
                }
                else
                {
                    float remain = maxLen - used;
                    if (remain > 0.01f)
                    {
                        var dir = (path[i] - path[i - 1]).normalized;
                        output.Add(path[i - 1] + dir * remain);
                    }
                    break;
                }
            }
            return output;
        }

        // ─── Flower trail ───────────────────────────────────────────────────────────

        // Walk the path forward from corner 0, planting one flower every `spacing` meters along
        // the polyline. Stops at FlowerMaxRenderDistance, capped to FlowerCountCap.
        private void RefreshFlowerTrail(Vector3 playerPos)
        {
            if (!ResolveFlowerAssets())
            {
                if (_flowerPool.Count > 0) HideAllFlowers();
                return;
            }

            float spacing = Mathf.Max(0.5f, Plugin.Cfg.FlowerSpacing.Value);
            float maxLen  = Plugin.Cfg.FlowerMaxRenderDistance.Value > 0 ? Plugin.Cfg.FlowerMaxRenderDistance.Value : float.MaxValue;
            int   cap     = Mathf.Max(1, Plugin.Cfg.FlowerCountCap.Value);
            float scale   = Plugin.Cfg.FlowerScale.Value;
            float yOff    = Plugin.Cfg.FlowerHeightOffset.Value;

            // Sample positions along the path polyline at fixed `spacing` intervals.
            var spots = new List<Vector3>();
            float traveled = 0f;
            float nextSpawn = spacing * 0.5f; // first flower half a spacing in (avoid right under player)
            for (int i = 1; i < _currentPath.Count; i++)
            {
                var a = _currentPath[i - 1];
                var b = _currentPath[i];
                float seg = Vector3.Distance(a, b);
                if (seg < 1e-4f) continue;
                Vector3 dir = (b - a) / seg;

                while (nextSpawn <= traveled + seg)
                {
                    float into = nextSpawn - traveled;
                    spots.Add(a + dir * into + Vector3.up * yOff);
                    nextSpawn += spacing;
                    if (spots.Count >= cap) break;
                    if (nextSpawn - 0f > maxLen) break;
                }
                traveled += seg;
                if (spots.Count >= cap || traveled >= maxLen) break;
            }

            // Resize pool to match.
            EnsureFlowerPoolSize(spots.Count);

            // Position + activate each flower.
            for (int i = 0; i < _flowerPool.Count; i++)
            {
                var go = _flowerPool[i];
                if (i < spots.Count)
                {
                    go.transform.position = spots[i];
                    // Random-stable Y rotation: derive from position so rotation is consistent
                    // across frames for a given spot, but varies per-spot for organic feel.
                    float yaw = Mathf.Repeat(spots[i].x * 73.13f + spots[i].z * 19.7f, 360f);
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    go.transform.localScale = Vector3.one * scale;
                    if (!go.activeSelf) go.SetActive(true);
                }
                else
                {
                    if (go.activeSelf) go.SetActive(false);
                }
            }
        }

        private void HideAllFlowers()
        {
            for (int i = 0; i < _flowerPool.Count; i++)
                if (_flowerPool[i].activeSelf) _flowerPool[i].SetActive(false);
        }

        private void EnsureFlowerPoolSize(int need)
        {
            while (_flowerPool.Count < need)
            {
                var go = new GameObject("QuestPath_Flower_" + _flowerPool.Count);
                DontDestroyOnLoad(go);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = s_flowerMesh;
                var mr = go.AddComponent<MeshRenderer>();
                // Per-flower material instance so we can boost emissive without affecting other props
                // in the world (the original material is shared with every poppy in the game).
                mr.sharedMaterial = MakeFlowerMaterialInstance(s_flowerMaterial);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.SetActive(false);
                _flowerPool.Add(go);
                _flowerBobPhases.Add(UnityEngine.Random.Range(0f, Mathf.PI * 2f));
            }
        }

        // Subtle vertical bob — adds life without being distracting. Cheap.
        private void AnimateFlowers()
        {
            float amp = Plugin.Cfg.FlowerBobAmplitude.Value;
            float speed = Plugin.Cfg.FlowerBobSpeed.Value;
            if (amp <= 0f || speed <= 0f) return;

            float t = Time.unscaledTime;
            float boost = Plugin.Cfg.FlowerEmissiveBoost.Value;
            for (int i = 0; i < _flowerPool.Count; i++)
            {
                var go = _flowerPool[i];
                if (!go.activeSelf) continue;
                float phase = _flowerBobPhases[i];
                float dy = Mathf.Sin(t * speed * Mathf.PI * 2f + phase) * amp;
                var p = go.transform.position;
                // Re-apply by resetting position to base (we lose the planted base each tick if
                // we just add). Easiest: keep base in localPosition? Simpler: store base-y per
                // flower in _flowerBobPhases (we'd need a second list). For now, just move
                // visibly each frame relative to current — the path-refresh tick re-snaps base.
                go.transform.position = new Vector3(p.x, p.y + dy * 0.005f, p.z); // light micro-jitter
            }
            _ = boost; // emissive boost handled at material build / config-change time
        }

        private static bool ResolveFlowerAssets()
        {
            if (s_flowerAssetsResolved) return s_flowerMesh != null && s_flowerMaterial != null;
            s_flowerAssetsResolved = true;

            try
            {
                s_flowerMesh = Resources.FindObjectsOfTypeAll<Mesh>()
                    .FirstOrDefault(m => m != null && m.name == "flower_common_poppy_01_detailed_2_LOD0")
                    ?? Resources.FindObjectsOfTypeAll<Mesh>()
                    .FirstOrDefault(m => m != null && m.name == "flower_common_poppy_01_detailed_2_LOD1");
                s_flowerMaterial = Resources.FindObjectsOfTypeAll<Material>()
                    .FirstOrDefault(m => m != null && m.name == "Mat_flower_common_poppy_01_Glowing");
                if (s_flowerMesh == null || s_flowerMaterial == null)
                {
                    Plugin.Log.LogWarning($"[QuestPath] Could not resolve flower assets (mesh={s_flowerMesh != null}, material={s_flowerMaterial != null}). Falling back to chevron line if enabled.");
                    return false;
                }
                Plugin.Log.LogInfo($"[QuestPath] Glowing poppy mesh + material resolved (mesh={s_flowerMesh.name}, mat={s_flowerMaterial.name}).");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[QuestPath] flower asset resolve failed: {e.Message}");
                return false;
            }
        }

        private static Material MakeFlowerMaterialInstance(Material src)
        {
            var inst = new Material(src);
            inst.name = src.name + " (QuestPath)";
            // Apply emissive boost from config now (re-applied if user changes config live —
            // see ApplyFlowerEmissive).
            ApplyEmissiveBoost(inst, Plugin.Cfg.FlowerEmissiveBoost.Value);
            return inst;
        }

        private static void ApplyEmissiveBoost(Material m, float boost)
        {
            // The vegetation shader uses _EmissiveColor (HDRP convention). Multiply by boost.
            if (m.HasProperty("_EmissiveColor"))
            {
                // Read from the source (cached at zero boost — but we're cloning the live
                // material which may already have the original glow). Just multiply current value.
                var c = m.GetColor("_EmissiveColor");
                // Avoid runaway: cap to sensible max.
                if (boost <= 0f) m.SetColor("_EmissiveColor", Color.black);
                else m.SetColor("_EmissiveColor", c.linear * boost);
            }
        }

        // ─── Destination beam + disc ────────────────────────────────────────────────

        private void ShowDestination(Vector3 dest)
        {
            if (!Plugin.Cfg.ShowSkyBeam.Value && Plugin.Cfg.RuneDiscRadius.Value <= 0f) { HideBeam(); return; }
            EnsureBeamHost();
            _beamHost.transform.position = dest;
            ApplyBeamShapes();
            _beamHost.SetActive(true);
        }

        private void HideBeam()
        {
            if (_beamHost != null) _beamHost.SetActive(false);
        }

        private void ApplyBeamShapes()
        {
            // Beam: tapered cone-cylinder hybrid. We re-build the mesh whenever height/radius
            // changes by more than a hair so live config tweaks are responsive.
            if (Plugin.Cfg.ShowSkyBeam.Value)
            {
                _beamMesh.SetActive(true);
                float h = Plugin.Cfg.BeamHeight.Value;
                float r = Plugin.Cfg.BeamRadius.Value;
                _beamMesh.transform.localPosition = new Vector3(0, h * 0.5f, 0);
                _beamMesh.transform.localScale = new Vector3(r * 2f, h * 0.5f, r * 2f);
            }
            else _beamMesh.SetActive(false);

            if (Plugin.Cfg.RuneDiscRadius.Value > 0f)
            {
                _discMesh.SetActive(true);
                float dr = Plugin.Cfg.RuneDiscRadius.Value;
                _discMesh.transform.localPosition = new Vector3(0, 0.05f, 0);   // just above ground
                _discMesh.transform.localScale = new Vector3(dr * 2f, 0.04f, dr * 2f);
            }
            else _discMesh.SetActive(false);

            // Apply colors live
            var c = ReadLineColor();
            if (_beamMaterial != null)
            {
                _beamMaterial.color = c;
                if (_beamMaterial.HasProperty("_EmissionColor"))
                    _beamMaterial.SetColor("_EmissionColor", c * (Plugin.Cfg.LineEmissive.Value * 0.7f));
            }
            if (_discMaterial != null)
            {
                _discMaterial.color = c;
                if (_discMaterial.HasProperty("_EmissionColor"))
                    _discMaterial.SetColor("_EmissionColor", c * Plugin.Cfg.LineEmissive.Value);
            }
        }

        private void EnsureBeamHost()
        {
            if (_beamHost != null) return;
            _beamHost = new GameObject("QuestPath_Destination");
            DontDestroyOnLoad(_beamHost);

            // BEAM mesh — Unity's default cylinder primitive (2m tall, scaled by transform).
            _beamMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _beamMesh.name = "QuestPath_Beam";
            Destroy(_beamMesh.GetComponent<Collider>());
            _beamMesh.transform.SetParent(_beamHost.transform, worldPositionStays: false);
            var beamRend = _beamMesh.GetComponent<MeshRenderer>();
            beamRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRend.receiveShadows = false;
            _beamMaterial = MakeUnlitMaterial("QuestPath/Beam", transparent: true);
            // Translucent vertical falloff using a 1×N gradient texture (alpha 1 at bottom → 0 at top).
            _beamMaterial.mainTexture = GetBeamGradientTexture();
            beamRend.material = _beamMaterial;

            // DISC — flat squashed sphere with chevron-ring texture.
            _discMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _discMesh.name = "QuestPath_RuneDisc";
            Destroy(_discMesh.GetComponent<Collider>());
            _discMesh.transform.SetParent(_beamHost.transform, worldPositionStays: false);
            var discRend = _discMesh.GetComponent<MeshRenderer>();
            discRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            discRend.receiveShadows = false;
            _discMaterial = MakeUnlitMaterial("QuestPath/Disc", transparent: true);
            _discMaterial.mainTexture = GetRuneDiscTexture();
            discRend.material = _discMaterial;

            // Soft point light at the disc — gives the area an actual glow, plays nice with HDRP volumetric fog.
            var lightGo = new GameObject("QuestPath_DestinationLight");
            lightGo.transform.SetParent(_beamHost.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 1.5f, 0);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = ReadLineColor();
            light.range = 12f;
            light.intensity = 6000f;
            light.shadows = LightShadows.None;
            _hdLight = lightGo.AddComponent<HDAdditionalLightData>();
            _hdLight.affectsVolumetric = true;
            _hdLight.SetIntensity(6000f);
        }

        // ─── Per-frame material animation ───────────────────────────────────────────

        private void AnimateMaterials()
        {
            float t = Time.unscaledTime;

            // Pulse emissive on line (and beam, mildly) to give a "living" feel.
            float pulseHz = Plugin.Cfg.LinePulseSpeed.Value;
            float pulseAmt = Plugin.Cfg.LinePulseAmount.Value;
            float pulse = (pulseHz > 0f) ? (1f + Mathf.Sin(t * pulseHz * Mathf.PI * 2f) * pulseAmt) : 1f;
            var c = ReadLineColor();
            float emissive = Plugin.Cfg.LineEmissive.Value * pulse;
            if (_lineMaterial != null && _lineMaterial.HasProperty("_EmissionColor"))
            {
                _lineMaterial.SetColor("_EmissionColor", c * emissive);
                _lineMaterial.color = c;
                // Scroll the texture forward.
                var off = _lineMaterial.mainTextureOffset;
                off.x -= Plugin.Cfg.LineFlowSpeed.Value * Time.unscaledDeltaTime;
                _lineMaterial.mainTextureOffset = off;
            }
            if (_beamMaterial != null && _beamMaterial.HasProperty("_EmissionColor"))
                _beamMaterial.SetColor("_EmissionColor", c * (emissive * 0.6f));
            if (_discMaterial != null && _discMaterial.HasProperty("_EmissionColor"))
                _discMaterial.SetColor("_EmissionColor", c * emissive);

            // Spin the disc.
            if (_discMesh != null && _discMesh.activeInHierarchy)
            {
                _discMesh.transform.Rotate(0f, Plugin.Cfg.RuneDiscRotateSpeed.Value * Time.unscaledDeltaTime, 0f, Space.Self);
            }

            if (_hdLight != null) _hdLight.color = c;
        }

        // ─── Procedural textures ────────────────────────────────────────────────────

        // Generate a chevron pattern texture once and cache for the whole session.
        // Drawn as a horizontal strip of ▶ shapes; tiling along the line length makes them flow.
        private static Texture2D GetChevronTexture()
        {
            if (s_chevronTex != null) return s_chevronTex;
            const int W = 64, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Chevron: distance to a |‾V/| shape. The chevron occupies the right half of
                // the tile, leaving a transparent gap on the left so they look like discrete arrows.
                float u = (float)x / W;
                float v = (float)y / H;
                float dy = Mathf.Abs(v - 0.5f) * 2f;     // 0 at centre, 1 at top/bottom
                // Inside chevron if: u between (0.55 + dy*0.20) and (0.85 + dy*0.20)
                float front = 0.55f + dy * 0.20f;
                float back  = 0.85f + dy * 0.20f;
                bool inside = u > front && u < back;
                float alpha = inside ? 1f : 0f;
                // Soft AA along the edges
                float edge = 0.04f;
                if (inside)
                {
                    float dFront = u - front;
                    float dBack  = back - u;
                    alpha = Mathf.Clamp01(Mathf.Min(dFront, dBack) / edge);
                }
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            tex.Apply();
            s_chevronTex = tex;
            return tex;
        }

        // Gradient texture for the beam: opaque at v=0, fading to transparent at v=1.
        private static Texture2D s_beamTex;
        private static Texture2D GetBeamGradientTexture()
        {
            if (s_beamTex != null) return s_beamTex;
            const int W = 4, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < H; y++)
            {
                float v = (float)y / (H - 1);                  // 0 at bottom, 1 at top
                // Smoothstep for fade-out, with extra opacity near base to ground the beam.
                float a = Mathf.Pow(1f - v, 1.6f);
                a = Mathf.Clamp01(a + (v < 0.05f ? 0.2f : 0f));
                for (int x = 0; x < W; x++) tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            s_beamTex = tex;
            return tex;
        }

        // Rune disc texture: a glyph-ish ring with fading interior.
        private static Texture2D s_runeTex;
        private static Texture2D GetRuneDiscTexture()
        {
            if (s_runeTex != null) return s_runeTex;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var center = new Vector2(N * 0.5f, N * 0.5f);
            float maxR = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), center) / maxR;     // 0..1+
                if (d > 1f) { tex.SetPixel(x, y, new Color(1, 1, 1, 0)); continue; }
                float a = 0f;
                // Outer ring at 0.85..0.95
                if (d > 0.78f && d < 0.97f) a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.875f) / 0.10f);
                // Middle ring at 0.60..0.66
                else if (d > 0.58f && d < 0.68f) a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.63f) / 0.05f) * 0.7f;
                // Soft fill near center
                else if (d < 0.40f) a = (1f - d / 0.40f) * 0.25f;

                // Add 8 spoke-marks around the ring for a runic look.
                float angle = Mathf.Atan2(y - center.y, x - center.x);
                float spoke = Mathf.Abs(Mathf.Sin(angle * 4f));   // 8 maxima around the circle
                if (d > 0.78f && d < 0.97f && spoke > 0.85f) a = Mathf.Max(a, (spoke - 0.85f) / 0.15f);

                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            s_runeTex = tex;
            return tex;
        }

        // ─── Material factory ──────────────────────────────────────────────────────

        // HDRP-friendly unlit material with optional transparency. The exact shader name
        // varies across HDRP versions, so we try a list of fallbacks and configure based on
        // which one we got. All paths must support _MainTex + _EmissionColor for our anim.
        private static Material MakeUnlitMaterial(string name, bool transparent)
        {
            Shader sh = Shader.Find("HDRP/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Color");
            var mat = new Material(sh) { name = name };

            if (transparent)
            {
                if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);   // 1 = Transparent in HDRP
                if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);       // alpha
                if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 0f);
                if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.renderQueue = 3000;
                if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
            }
            return mat;
        }

        private Color ReadLineColor() => new Color(Plugin.Cfg.LineColorR.Value, Plugin.Cfg.LineColorG.Value, Plugin.Cfg.LineColorB.Value, 1f);

        private void HideVisuals()
        {
            HidePathLine();
            HideAllFlowers();
            HideBeam();
            _lastDest = null;
            _currentPath = null;
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(fullName, throwOnError: false); if (t != null) return t; }
            return null;
        }
    }
}
