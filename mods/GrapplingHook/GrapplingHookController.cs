using Awaken.TG.Main.Cameras;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Combat;
using Awaken.TG.MVC;
using UnityEngine;

namespace GrapplingHook
{
    internal class GrapplingHookController : MonoBehaviour
    {
        private bool _pulling;
        private Vector3 _hookPoint;
        private float _pullElapsed;

        private GameObject _ropeGO;
        private LineRenderer _rope;

        public void Update()
        {
            var cfg = Plugin.Cfg;
            if (cfg == null || !cfg.Enabled.Value)
            {
                if (_pulling) StopPull("disabled");
                return;
            }

            var hero = Hero.Current;
            if (hero == null) { if (_pulling) StopPull("hero gone"); return; }

            var vhc = hero.VHeroController;
            if (vhc == null) { if (_pulling) StopPull("controller gone"); return; }

            if (Input.GetKeyDown(cfg.HookKey.Value))
            {
                if (_pulling) StopPull("user cancel");
                else TryFireHook(vhc);
            }

            if (_pulling)
            {
                TickPull(vhc);
            }
        }

        private void TryFireHook(VHeroController vhc)
        {
            var cfg = Plugin.Cfg;

            var camera = ResolveCamera();
            if (camera == null)
            {
                if (cfg.Verbose.Value) Plugin.Log.LogWarning("[GrapplingHook] No active camera; skipping fire.");
                return;
            }

            var ray = new Ray(camera.transform.position, camera.transform.forward);

            if (!Physics.Raycast(ray, out var hit, cfg.MaxRange.Value, cfg.HookLayerMask.Value, QueryTriggerInteraction.Ignore))
            {
                if (cfg.Verbose.Value) Plugin.Log.LogInfo($"[GrapplingHook] Fire missed (no surface within {cfg.MaxRange.Value}m).");
                return;
            }

            // Don't grapple onto the player itself (its own collider can match the cast at TPP camera angles).
            if (hit.collider != null && vhc.transform != null && hit.collider.transform.IsChildOf(vhc.transform))
            {
                if (cfg.Verbose.Value) Plugin.Log.LogInfo("[GrapplingHook] Fire hit self; ignored.");
                return;
            }

            _hookPoint = hit.point;
            _pulling = true;
            _pullElapsed = 0f;

            EnsureRope();
            if (cfg.ShowRope.Value && _rope != null) _rope.gameObject.SetActive(true);

            if (cfg.Verbose.Value)
            {
                var dist = Vector3.Distance(vhc.Transform.position, _hookPoint);
                Plugin.Log.LogInfo($"[GrapplingHook] Hook hit {hit.collider?.name} at {_hookPoint} (dist {dist:F1}m).");
            }
        }

        private void TickPull(VHeroController vhc)
        {
            var cfg = Plugin.Cfg;

            _pullElapsed += Time.deltaTime;
            if (_pullElapsed > cfg.MaxPullDuration.Value) { StopPull("timeout"); return; }

            var pos = vhc.Transform.position;
            var toTarget = _hookPoint - pos;
            var dist = toTarget.magnitude;
            if (dist <= cfg.StopDistance.Value) { StopPull("arrived"); return; }

            var dir = toTarget / dist;
            // Clamp the per-frame step so we don't overshoot the target on a long deltaTime spike.
            var step = Mathf.Min(dist - cfg.StopDistance.Value, cfg.PullSpeed.Value * Time.deltaTime);
            if (step <= 0f) { StopPull("arrived"); return; }

            if (cfg.SuppressGravity.Value) vhc.verticalVelocity = 0f;

            vhc.PerformMoveStep(dir * step);

            UpdateRope(vhc);
        }

        private void StopPull(string reason)
        {
            _pulling = false;
            _pullElapsed = 0f;
            if (_rope != null) _rope.gameObject.SetActive(false);
            if (Plugin.Cfg != null && Plugin.Cfg.Verbose.Value) Plugin.Log.LogInfo($"[GrapplingHook] Pull ended: {reason}.");
        }

        private static Camera ResolveCamera()
        {
            var gc = World.Any<GameCamera>();
            if (gc != null && gc.MainCamera != null) return gc.MainCamera;
            return Camera.main;
        }

        private void EnsureRope()
        {
            if (_rope != null) return;

            _ropeGO = new GameObject("GrapplingHook_Rope");
            _ropeGO.transform.SetParent(transform, worldPositionStays: false);
            _rope = _ropeGO.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.positionCount = 2;
            _rope.numCapVertices = 2;
            // "Sprites/Default" is unlit, additive-friendly, and ships with every Unity build.
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) _rope.material = new Material(shader);
            var color = new Color(0.55f, 0.42f, 0.25f);
            _rope.startColor = color;
            _rope.endColor = color;
            float w = Plugin.Cfg.RopeWidth.Value;
            _rope.startWidth = w;
            _rope.endWidth = w;
            _rope.gameObject.SetActive(false);
        }

        private void UpdateRope(VHeroController vhc)
        {
            var cfg = Plugin.Cfg;
            if (_rope == null) return;
            if (!cfg.ShowRope.Value)
            {
                if (_rope.gameObject.activeSelf) _rope.gameObject.SetActive(false);
                return;
            }

            // Anchor the rope at the hero's chest height — main hand transform may not be loaded.
            var pos = vhc.Transform.position;
            var anchor = pos + Vector3.up * 1.4f;

            if (!_rope.gameObject.activeSelf) _rope.gameObject.SetActive(true);
            // Width may have been changed in config at runtime.
            _rope.startWidth = cfg.RopeWidth.Value;
            _rope.endWidth = cfg.RopeWidth.Value;
            _rope.SetPosition(0, anchor);
            _rope.SetPosition(1, _hookPoint);
        }

        public void OnDestroy()
        {
            if (_ropeGO != null) Destroy(_ropeGO);
            _ropeGO = null;
            _rope = null;
        }
    }
}
