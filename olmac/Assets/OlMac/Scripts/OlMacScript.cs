// Ol' Mac runtime script. Attached to the Paper Mac item through its JSON (ItemModule),
// so it starts on every Ol' Mac as soon as he spawns with the drawing on his hip.
// For that Ol' Mac it:
//   - hides every human mesh on him (body, head, shorts, eyes) and keeps them hidden
//   - draws Paper Mac on his skeleton: leans with his spine, falls with his ragdoll, faces you
//   - walks when moving, stands still when idle, flails when his hand is swinging,
//     flips to face the way he's walking, and freezes on one frame when he dies
// Spawned on its own from the item book (no creature), it leaves the particle drawing alone.
using ThunderRoad;
using UnityEngine;

namespace FluidLove.OlMac
{
    public class OlMacPaperModule : ItemModule
    {
        public override void OnItemLoaded(Item item)
        {
            base.OnItemLoaded(item);
            if (!item.gameObject.GetComponent<OlMacPaper>()) item.gameObject.AddComponent<OlMacPaper>();
        }
    }

    public class OlMacPaper : MonoBehaviour
    {
        const float Size = 2.4f;            // metres, square the drawing sits in
        const int Columns = 6, Rows = 2;    // atlas: row 0 walk, row 1 attack
        const float WalkFps = 8f, AttackFps = 10f;
        const float SpineLean = 0.7f;       // 0 = always upright, 1 = fully follows his spine

        Creature creature;
        Transform hips, head, hand;
        GameObject quad;
        Material mat;
        Renderer[] fallback;
        float hipHeight = 1f;
        bool hipMeasured;
        Vector3 lastHips, lastHandRel;
        bool faceLeft;
        float attackUntil, attackStart, nextHide, nextFind, bornAt;

        static Mesh quadMesh;
        static readonly string[] TexProps = { "_BaseMap", "_MainTex", "_BaseColorMap" };

        void OnEnable() { bornAt = Time.time; creature = null; }

        void OnDisable() { if (quad) quad.SetActive(false); }

        void OnDestroy() { if (quad) Destroy(quad); if (mat) Destroy(mat); }

        void LateUpdate()
        {
            float t = Time.time;

            // Who's carrying us? (Only an Ol' Mac creature counts.)
            if (!creature)
            {
                if (t < nextFind) return;
                nextFind = t + 0.25f;
                Creature c = GetComponentInParent<Creature>();
                if (!c || c.data == null || c.data.id != "OlMac") { if (quad) quad.SetActive(false); return; }
                Attach(c);
            }
            else if (GetComponentInParent<Creature>() != creature)
            {
                // Pulled off his hip (e.g. grabbed by the player) - go back to the plain drawing
                Detach();
                return;
            }

            if (!quad && !MakeQuad()) return;
            quad.SetActive(true);

            if (t >= nextHide) { nextHide = t + 0.25f; HideHuman(); }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            bool dead = creature.isKilled;
            Vector3 hp = hips.position;
            Vector3 spine = head.position - hp;
            spine = spine.sqrMagnitude > 0.0001f ? spine.normalized : Vector3.up;

            if (!dead && !hipMeasured && t - bornAt > 0.5f)
            {
                float h = hp.y - creature.transform.position.y;
                if (h > 0.6f && h < 1.3f) { hipHeight = h; hipMeasured = true; }
            }

            // Lean with the body when alive, follow it completely when dead
            Vector3 up = dead ? spine : Vector3.Slerp(Vector3.up, spine, SpineLean).normalized;
            Vector3 feet = hp - up * hipHeight;

            // Face the camera, turning only around his own up axis
            Vector3 cam = Camera.main ? Camera.main.transform.position : hp + creature.transform.forward;
            Vector3 n = cam - (feet + up * Size * 0.5f);
            n -= up * Vector3.Dot(n, up);
            if (n.sqrMagnitude < 0.0001f) n = creature.transform.forward;
            quad.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(-n.normalized, up));
            quad.transform.localScale = Vector3.one * Size;

            // Movement: walk vs idle, and which way he's facing
            Vector3 vel = (hp - lastHips) / dt;
            lastHips = hp;
            Vector3 flatVel = new Vector3(vel.x, 0, vel.z);
            Vector3 view = hp - cam; view.y = 0;
            Vector3 camRight = view.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, view.normalized) : Vector3.right;
            float lateral = Vector3.Dot(flatVel, camRight);
            if (lateral < -0.15f) faceLeft = true;
            else if (lateral > 0.15f) faceLeft = false;

            // Attack: weapon hand whipping round fast relative to his hips
            if (hand)
            {
                Vector3 rel = hand.position - hp;
                float handSpeed = (rel - lastHandRel).magnitude / dt;
                lastHandRel = rel;
                if (!dead && handSpeed > 2.2f)
                {
                    if (t > attackUntil) attackStart = t;
                    attackUntil = t + 0.5f;
                }
            }

            int row, col;
            bool mirror = false;
            if (dead) { row = 1; col = 0; }                                     // frozen, arms out
            else if (t < attackUntil) { row = 1; col = (int)((t - attackStart) * AttackFps) % 6; }
            else if (flatVel.magnitude > 0.25f) { row = 0; col = (int)(t * WalkFps) % 6; mirror = faceLeft; }
            else { row = 0; col = 0; mirror = faceLeft; }                       // standing still

            SetFrame(row, col, mirror);
        }

        void Attach(Creature c)
        {
            creature = c;
            Animator a = c.animator;
            if (a)
            {
                hips = a.GetBoneTransform(HumanBodyBones.Hips);
                head = a.GetBoneTransform(HumanBodyBones.Head);
                hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            }
            if (!hips) hips = c.transform;
            if (!head) head = hips;
            lastHips = hips.position;
            hipMeasured = false;
            bornAt = Time.time;
            nextHide = 0f;
        }

        void Detach()
        {
            creature = null;
            if (quad) quad.SetActive(false);
            if (fallback != null) foreach (var r in fallback) if (r) r.enabled = true;
        }

        bool MakeQuad()
        {
            fallback = GetComponentsInChildren<ParticleSystemRenderer>(true);
            Material src = null;
            foreach (var r in fallback) if (r && r.sharedMaterial) { src = r.sharedMaterial; break; }
            if (!src) return false;

            if (!quadMesh)
            {
                // Two separate faces (front + back), each with its own corners and normals.
                // v0.4 shared corners between both faces, the normals cancelled to zero and the
                // game's lit shader drew him black.
                var bl = new Vector3(-0.5f, 0, 0); var br = new Vector3(0.5f, 0, 0);
                var tr = new Vector3(0.5f, 1, 0);  var tl = new Vector3(-0.5f, 1, 0);
                quadMesh = new Mesh { name = "OlMacQuad" };
                quadMesh.vertices = new[] { bl, br, tr, tl, bl, br, tr, tl };
                quadMesh.uv = new[]
                {
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                };
                quadMesh.normals = new[]
                {
                    Vector3.back, Vector3.back, Vector3.back, Vector3.back,             // faces -Z
                    Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, // faces +Z
                };
                var white = Color.white;
                quadMesh.colors = new[] { white, white, white, white, white, white, white, white };
                quadMesh.tangents = new[]
                {
                    new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1),
                    new Vector4(-1, 0, 0, 1), new Vector4(-1, 0, 0, 1), new Vector4(-1, 0, 0, 1), new Vector4(-1, 0, 0, 1),
                };
                // Unity front faces are clockwise as seen by the viewer
                quadMesh.triangles = new[] { 0, 3, 2, 0, 2, 1,   4, 5, 6, 4, 6, 7 };
                quadMesh.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1.5f, 1.5f, 1.5f));
            }

            quad = new GameObject("PaperMac");
            quad.AddComponent<MeshFilter>().sharedMesh = quadMesh;
            var mr = quad.AddComponent<MeshRenderer>();
            mat = new Material(src);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return true;
        }

        void HideHuman()
        {
            foreach (Renderer r in creature.GetComponentsInChildren<Renderer>(true))
            {
                if (!r) continue;
                if (r.GetComponentInParent<Item>() == null)
                {
                    if (r.enabled) r.enabled = false;           // body, head, shorts, eyes, hair
                }
            }
            if (fallback != null) foreach (var r in fallback) if (r && r.enabled) r.enabled = false; // hip particle drawing
        }

        void SetFrame(int row, int col, bool mirror)
        {
            float w = 1f / Columns, h = 1f / Rows;
            Vector2 scale = new Vector2(mirror ? -w : w, h);
            Vector2 offset = new Vector2(mirror ? (col + 1) * w : col * w, (Rows - 1 - row) * h);
            foreach (string p in TexProps)
            {
                if (!mat.HasProperty(p)) continue;
                mat.SetTextureScale(p, scale);
                mat.SetTextureOffset(p, offset);
            }
        }
    }
}
