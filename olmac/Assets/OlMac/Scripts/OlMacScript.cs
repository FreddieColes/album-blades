// Ol' Mac runtime script (ThunderScript: loads automatically from the mod's DLL, no JSON needed).
// For every live Ol' Mac it:
//   - hides every human mesh on him (body, head, shorts, eyes) and keeps them hidden
//   - draws Paper Mac on his skeleton: leans with his spine, falls with his ragdoll, faces you
//   - walks when moving, stands still when idle, flails when his hand is swinging,
//     flips to face the way he's walking, and freezes on one frame when he dies
// If this DLL doesn't load, the v0.3 particle drawing on his hip still works as a fallback.
using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;

namespace FluidLove.OlMac
{
    public class OlMacScript : ThunderScript
    {
        const string CreatureId = "OlMac";
        const float Size = 2.4f;            // metres, square the drawing sits in
        const int Columns = 6, Rows = 2;    // atlas: row 0 walk, row 1 attack
        const float WalkFps = 8f, AttackFps = 10f;
        const float SpineLean = 0.7f;       // 0 = always upright, 1 = fully follows his spine

        class Mac
        {
            public Creature creature;
            public Transform hips, head, hand;
            public GameObject quad;
            public Material mat;
            public float hipHeight = 1f;
            public bool hipMeasured;
            public Vector3 lastHips, lastHandRel;
            public bool faceLeft;
            public float attackUntil, attackStart;
            public float nextHide;
            public bool fallbackHidden;
        }

        readonly Dictionary<Creature, Mac> macs = new Dictionary<Creature, Mac>();
        readonly List<Creature> gone = new List<Creature>();
        static Mesh quadMesh;
        float nextScan;

        public override void ScriptLateUpdate()
        {
            base.ScriptLateUpdate();
            float t = Time.time;

            if (t >= nextScan)
            {
                nextScan = t + 0.5f;
                Scan();
            }

            Vector3 cam = CameraPos();
            gone.Clear();
            foreach (var kv in macs)
            {
                Mac m = kv.Value;
                if (m.creature == null || !m.creature.gameObject.activeInHierarchy) { gone.Add(kv.Key); continue; }
                try { Tick(m, cam, t); }
                catch (System.Exception e) { Debug.LogWarning("[OlMac] " + e.Message); }
            }
            foreach (var c in gone)
            {
                if (macs.TryGetValue(c, out Mac m) && m.quad) Object.Destroy(m.quad);
                macs.Remove(c);
            }
        }

        void Scan()
        {
            foreach (Creature c in Creature.allActive)
            {
                if (c == null || macs.ContainsKey(c) || c.data == null || c.data.id != CreatureId) continue;
                var m = new Mac { creature = c };
                Animator a = c.animator;
                if (a)
                {
                    m.hips = a.GetBoneTransform(HumanBodyBones.Hips);
                    m.head = a.GetBoneTransform(HumanBodyBones.Head);
                    m.hand = a.GetBoneTransform(HumanBodyBones.RightHand);
                }
                if (!m.hips) m.hips = c.transform;
                if (!m.head) m.head = m.hips;
                m.lastHips = m.hips.position;
                macs[c] = m;
            }
        }

        static Vector3 CameraPos()
        {
            if (Camera.main) return Camera.main.transform.position;
            return Vector3.zero;
        }

        void Tick(Mac m, Vector3 cam, float t)
        {
            Creature c = m.creature;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            // Build our drawing once we can borrow the material from the hip particle fallback
            if (!m.quad && !MakeQuad(m)) return;

            // Hide human meshes + the particle fallback (re-done regularly because the game re-enables some)
            if (t >= m.nextHide)
            {
                m.nextHide = t + 0.25f;
                HideHuman(m);
            }

            bool dead = c.isKilled;
            Vector3 hips = m.hips.position;
            Vector3 spine = (m.head.position - hips);
            spine = spine.sqrMagnitude > 0.0001f ? spine.normalized : Vector3.up;

            if (!dead && !m.hipMeasured && t > 0.5f)
            {
                float h = hips.y - c.transform.position.y;
                if (h > 0.6f && h < 1.3f) { m.hipHeight = h; m.hipMeasured = true; }
            }

            // Lean with the body when alive, follow it completely when dead
            Vector3 up = dead ? spine : Vector3.Slerp(Vector3.up, spine, SpineLean).normalized;
            Vector3 feet = hips - up * m.hipHeight;

            // Face the camera, turning only around his own up axis
            Vector3 n = cam - (feet + up * Size * 0.5f);
            n -= up * Vector3.Dot(n, up);
            if (n.sqrMagnitude < 0.0001f) n = c.transform.forward;
            m.quad.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(-n.normalized, up));
            m.quad.transform.localScale = Vector3.one * Size;

            // Movement: walk vs idle, and which way he's facing
            Vector3 vel = (hips - m.lastHips) / dt;
            m.lastHips = hips;
            Vector3 flatVel = new Vector3(vel.x, 0, vel.z);
            Vector3 view = hips - cam; view.y = 0;
            Vector3 camRight = view.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, view.normalized) : Vector3.right;
            float lateral = Vector3.Dot(flatVel, camRight);
            if (lateral < -0.15f) m.faceLeft = true;
            else if (lateral > 0.15f) m.faceLeft = false;

            // Attack: his weapon hand whipping around fast relative to his hips
            if (m.hand)
            {
                Vector3 rel = m.hand.position - hips;
                float handSpeed = (rel - m.lastHandRel).magnitude / dt;
                m.lastHandRel = rel;
                if (!dead && handSpeed > 2.2f)
                {
                    if (t > m.attackUntil) m.attackStart = t;
                    m.attackUntil = t + 0.5f;
                }
            }

            int row, col;
            bool mirror = false;
            if (dead) { row = 1; col = 0; }                       // frozen, arms out
            else if (t < m.attackUntil) { row = 1; col = (int)((t - m.attackStart) * AttackFps) % 6; }
            else if (flatVel.magnitude > 0.25f) { row = 0; col = (int)(t * WalkFps) % 6; mirror = m.faceLeft; }
            else { row = 0; col = 0; mirror = m.faceLeft; }        // standing still

            SetFrame(m, row, col, mirror);
        }

        bool MakeQuad(Mac m)
        {
            Material src = null;
            foreach (var r in m.creature.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r.sharedMaterial && r.gameObject.name == "Walk") { src = r.sharedMaterial; break; }
            }
            if (!src) return false;

            if (!quadMesh)
            {
                quadMesh = new Mesh { name = "OlMacQuad" };
                quadMesh.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
                quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                // Both windings so it's visible from either side
                quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
                quadMesh.RecalculateNormals();
                quadMesh.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1.5f, 1.5f, 1.5f));
            }

            m.quad = new GameObject("PaperMac");
            m.quad.AddComponent<MeshFilter>().sharedMesh = quadMesh;
            var mr = m.quad.AddComponent<MeshRenderer>();
            m.mat = new Material(src);
            mr.sharedMaterial = m.mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return true;
        }

        void HideHuman(Mac m)
        {
            foreach (Renderer r in m.creature.GetComponentsInChildren<Renderer>(true))
            {
                if (!r) continue;
                bool onItem = r.GetComponentInParent<Item>() != null;
                if (!onItem)
                {
                    // Anything that's part of the human: body, head, shorts, eyes, hair
                    if (r.enabled) r.enabled = false;
                }
                else if (r is ParticleSystemRenderer && (r.gameObject.name == "Walk" || r.gameObject.name == "Attack"))
                {
                    // Our own hip fallback drawing, not needed now the script is running
                    if (r.enabled) r.enabled = false;
                }
            }
        }

        static readonly string[] TexProps = { "_BaseMap", "_MainTex", "_BaseColorMap" };

        void SetFrame(Mac m, int row, int col, bool mirror)
        {
            float w = 1f / Columns, h = 1f / Rows;
            Vector2 scale = new Vector2(mirror ? -w : w, h);
            Vector2 offset = new Vector2(mirror ? (col + 1) * w : col * w, (Rows - 1 - row) * h);
            foreach (string p in TexProps)
            {
                if (!m.mat.HasProperty(p)) continue;
                m.mat.SetTextureScale(p, scale);
                m.mat.SetTextureOffset(p, offset);
            }
        }
    }
}
