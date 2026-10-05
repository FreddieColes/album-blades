// Ol' Mac: Paper Mac enemy for Blade & Sorcery: Nomad.
// Builds: sprite atlas, Paper Mac item (the drawing, carried in his hip holster),
// invisible banjo weapon (twangs Old Mac Daddy on every hit), invisible body material.
// Menu: Ol' Mac > Build Nomad mod   (or headless via build-olmac.sh)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThunderRoad;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

namespace FluidLove
{
    public static class OlMacBuilder
    {
        const string Root = "Assets/OlMac";
        const string Gen = Root + "/Generated";
        const string ModFolder = "OlMac";

        const string PaperId = "OlMac_Paper";
        const string BanjoId = "OlMac_Banjo";
        const string PaperAddress = "OlMac.Paper";
        const string BanjoAddress = "OlMac.Banjo";
        const string InvisibleAddress = "OlMac.Invisible";
        const string IconAddress = "OlMac.Icon";

        // Paper Mac placement (tweak these if he's too big/small/high/low or behind the body)
        const float PaperSize = 2.4f;       // metres, the square he's drawn in
        const float PaperLift = 0.085f;     // fraction of size; raises his feet onto the floor
        const float PaperForward = 0.07f;   // fraction of size; ~17cm towards the viewer

        // Atlas
        const int Columns = 6, Rows = 2, CellSize = 512;
        const int WhiteThreshold = 246; // background is pure white; his shirt (~243) survives

        // Banjo, held by the neck. Weapon axis is +Y. Pot (the round bit) at the far end.
        const float NeckLength = 0.5f;
        static readonly Vector3 PotCentre = new Vector3(0, 0.6f, 0);
        static readonly Vector3 PotSize = new Vector3(0.32f, 0.32f, 0.08f);

        [MenuItem("Ol' Mac/Build Nomad mod")]
        public static void BuildMenu()
        {
            string outDir = BuildAll();
            if (outDir != null) EditorUtility.RevealInFinder(outDir);
        }

        public static void BuildFromCommandLine()
        {
            string outDir = null;
            try { outDir = BuildAll(); }
            catch (Exception e) { Debug.LogError("[OlMac] FAILED: " + e); }
            EditorApplication.Exit(outDir != null ? 0 : 1);
        }

        static string BuildAll()
        {
            SetAndroid();
            AssetBundleGroup group = Setup();
            AssetBundleBuilder.exportFolderName = group.folderName;
            Debug.Log("[OlMac] Building asset bundles...");
            if (!AssetBundleBuilder.Build(group, true))
            {
                Debug.LogError("[OlMac] Asset bundle build failed, see errors above.");
                return null;
            }
            return Export(group);
        }

        static void SetAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            int q = Array.IndexOf(QualitySettings.names, "Android");
            if (q >= 0) QualitySettings.SetQualityLevel(q, true);
            Common.GetQualityLevel(true);
            try
            {
                Type plat = FindType("ThunderRoad.AssetSorcery.AssetSorceryPlatform");
                Type rt = FindType("ThunderRoad.AssetSorcery.AssetSorceryPlatformRuntime");
                var get = rt?.GetMethod("AssetSorceryGetBuildPlatform");
                var set = plat?.GetMethod("AssetSorceryShaderSetPlatform");
                if (get != null && set != null) set.Invoke(null, new[] { get.Invoke(null, new object[] { true }) });
            }
            catch (Exception e) { Debug.LogWarning("[OlMac] AssetSorcery platform step skipped: " + e.Message); }
            Debug.Log($"[OlMac] Target {EditorUserBuildSettings.activeBuildTarget}");
        }

        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        // ------------------------------------------------------------------ setup

        static AssetBundleGroup Setup()
        {
            Directory.CreateDirectory(Gen);
            AssetDatabase.Refresh();

            // Atlas + icon from the 11 drawings
            string atlasPath = BuildAtlas(out string iconPath);

            // Materials. v0.1 used custom shaders and Nomad drew them pink, so everything now
            // uses the game's own item shader (copied from the same material the album weapons use).
            Material template = FindTemplateMaterial();
            Texture2D atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);

            string paperMatPath = $"{Gen}/OlMac_Paper.mat";
            AssetDatabase.DeleteAsset(paperMatPath);
            var paperMat = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            paperMat.name = "OlMac_Paper";
            foreach (string p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                if (paperMat.HasProperty(p)) paperMat.SetTexture(p, atlasTex);
            foreach (string p in new[] { "_BaseColor", "_Color" })
                if (paperMat.HasProperty(p)) paperMat.SetColor(p, Color.white);
            if (paperMat.HasProperty("_BumpMap")) paperMat.SetTexture("_BumpMap", null);
            if (paperMat.HasProperty("_Smoothness")) paperMat.SetFloat("_Smoothness", 0f);
            if (paperMat.HasProperty("_Metallic")) paperMat.SetFloat("_Metallic", 0f);
            // Cut out the see-through background
            if (paperMat.HasProperty("_AlphaClip")) paperMat.SetFloat("_AlphaClip", 1f);
            if (paperMat.HasProperty("_Cutoff")) paperMat.SetFloat("_Cutoff", 0.5f);
            paperMat.EnableKeyword("_ALPHATEST_ON");
            paperMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            // Glow a bit so he looks like a flat cartoon, not a dark shaded card
            if (paperMat.HasProperty("_EmissionMap")) paperMat.SetTexture("_EmissionMap", atlasTex);
            if (paperMat.HasProperty("_EmissionColor")) paperMat.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.55f));
            paperMat.EnableKeyword("_EMISSION");
            paperMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(paperMat, paperMatPath);
            Debug.Log($"[OlMac] Paper material shader: {paperMat.shader.name} | AlphaClip:{paperMat.HasProperty("_AlphaClip")} Cutoff:{paperMat.HasProperty("_Cutoff")} Emission:{paperMat.HasProperty("_EmissionMap")}");

            // Body material: the game's own item shader with a fully see-through texture and
            // cutout on, so every pixel is thrown away. (v0.1's custom invisible shader flashed black.)
            string clearPath = $"{Gen}/OlMac_Clear.png";
            var clear = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            clear.SetPixels32(new Color32[16]);
            clear.Apply();
            File.WriteAllBytes(clearPath, clear.EncodeToPNG());
            AssetDatabase.ImportAsset(clearPath);
            var cti = (TextureImporter)AssetImporter.GetAtPath(clearPath);
            cti.alphaSource = TextureImporterAlphaSource.FromInput;
            cti.alphaIsTransparency = true;
            cti.mipmapEnabled = false;
            cti.textureCompression = TextureImporterCompression.Uncompressed;
            cti.SaveAndReimport();
            Texture2D clearTex = AssetDatabase.LoadAssetAtPath<Texture2D>(clearPath);

            string invisMatPath = $"{Gen}/OlMac_Invisible.mat";
            AssetDatabase.DeleteAsset(invisMatPath);
            var invisMat = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            invisMat.name = "OlMac_Invisible";
            foreach (string p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                if (invisMat.HasProperty(p)) invisMat.SetTexture(p, clearTex);
            foreach (string p in new[] { "_BaseColor", "_Color" })
                if (invisMat.HasProperty(p)) invisMat.SetColor(p, new Color(1, 1, 1, 0));
            if (invisMat.HasProperty("_BumpMap")) invisMat.SetTexture("_BumpMap", null);
            if (invisMat.HasProperty("_AlphaClip")) invisMat.SetFloat("_AlphaClip", 1f);
            if (invisMat.HasProperty("_Cutoff")) invisMat.SetFloat("_Cutoff", 0.99f);
            invisMat.EnableKeyword("_ALPHATEST_ON");
            invisMat.DisableKeyword("_EMISSION");
            invisMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            AssetDatabase.CreateAsset(invisMat, invisMatPath);

            // Audio: Old Mac Daddy clips for the banjo
            var clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { $"{Root}/Audio" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (AudioImporter)AssetImporter.GetAtPath(p);
                imp.forceToMono = true;
                imp.loadInBackground = false;
                var s = imp.defaultSampleSettings;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                imp.preloadAudioData = true;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(p));
            }
            Debug.Log($"[OlMac] {clips.Count} banjo clips");
            AudioContainer container = null;
            if (clips.Count > 0)
            {
                container = CreateOrReplace<AudioContainer>($"{Gen}/OlMac_BanjoClips.asset");
                container.sounds = clips;
                container.useShuffle = true;
                EditorUtility.SetDirty(container);
            }

            // Meshes
            Mesh paperMesh = MakePaperMesh($"{Gen}/OlMac_PaperMesh.asset");
            Mesh banjoMesh = MakeBoxMesh($"{Gen}/OlMac_BanjoMesh.asset", PotCentre, PotSize);
            AssetDatabase.SaveAssets();

            // Prefabs
            string paperPrefab = $"{Root}/{PaperId}.prefab";
            MakeItemPrefab(paperPrefab, PaperId, paperMesh, paperMat,
                colliderCentre: Vector3.zero, colliderSize: new Vector3(0.05f, 0.05f, 0.05f),
                gripLength: 0.05f, audio: null, hideMesh: true, paper: paperMat);

            string banjoPrefab = $"{Root}/{BanjoId}.prefab";
            MakeItemPrefab(banjoPrefab, BanjoId, banjoMesh, paperMat,
                colliderCentre: PotCentre, colliderSize: PotSize,
                gripLength: NeckLength * 0.4f, audio: container, hideMesh: true, paper: null);

            var entries = new List<(string path, string address)>
            {
                (paperPrefab, PaperAddress),
                (banjoPrefab, BanjoAddress),
                (invisMatPath, InvisibleAddress),
                (iconPath, IconAddress),
            };
            return MakeBundleGroup(MakeAddressables(entries));
        }

        static T CreateOrReplace<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var obj = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        // ------------------------------------------------------------------ atlas

        static string BuildAtlas(out string iconPath)
        {
            var walk = LoadSet("FatChickenWalking", 5);
            var attack = LoadSet("FatChickenStanding", 6);
            foreach (var t in walk.Concat(attack)) RemoveBackground(t);

            RectInt uw = UnionBounds(walk), ua = UnionBounds(attack);
            int maxW = Mathf.Max(uw.width, ua.width), maxH = Mathf.Max(uw.height, ua.height);
            float scale = Mathf.Min((float)CellSize / maxW, (float)CellSize / maxH);

            var atlas = new Texture2D(Columns * CellSize, Rows * CellSize, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[atlas.width * atlas.height]);
            for (int i = 0; i < walk.Count; i++) Blit(walk[i], uw, atlas, i * CellSize, CellSize, scale);
            for (int i = 0; i < attack.Count; i++) Blit(attack[i], ua, atlas, i * CellSize, 0, scale);
            // Walk only has 5 frames; fill the 6th cell with frame 3 so the loop never shows a blank
            if (walk.Count < Columns) Blit(walk[2], uw, atlas, walk.Count * CellSize, CellSize, scale);
            atlas.Apply();

            string atlasPath = $"{Gen}/OlMac_Atlas.png";
            File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());

            // Icon: first walk frame on its own
            var icon = new Texture2D(CellSize, CellSize, TextureFormat.RGBA32, false);
            icon.SetPixels(atlas.GetPixels(0, CellSize, CellSize, CellSize));
            icon.Apply();
            iconPath = $"{Gen}/OlMac_Icon.png";
            File.WriteAllBytes(iconPath, icon.EncodeToPNG());

            AssetDatabase.ImportAsset(atlasPath);
            AssetDatabase.ImportAsset(iconPath);
            foreach (string p in new[] { atlasPath, iconPath })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 4096;
                ti.SaveAndReimport();
            }
            Debug.Log($"[OlMac] Atlas {atlas.width}x{atlas.height}, scale {scale:F3}");
            return atlasPath;
        }

        static List<Texture2D> LoadSet(string prefix, int count)
        {
            var list = new List<Texture2D>();
            for (int i = 1; i <= count; i++)
            {
                string path = $"{Root}/Sprites/{prefix}{i}.png";
                if (!File.Exists(path)) throw new Exception($"Missing drawing {path}. Copy all 11 PNGs into olmac/Assets/OlMac/Sprites in the repo.");
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                tex.wrapMode = TextureWrapMode.Clamp;
                list.Add(tex);
            }
            return list;
        }

        static void RemoveBackground(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            var seen = new bool[px.Length];
            var queue = new Queue<int>();
            void TryAdd(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                int i = y * w + x;
                if (seen[i]) return;
                var c = px[i];
                if (!(c.a < 10 || (c.r >= WhiteThreshold && c.g >= WhiteThreshold && c.b >= WhiteThreshold))) return;
                seen[i] = true;
                queue.Enqueue(i);
            }
            for (int x = 0; x < w; x++) { TryAdd(x, 0); TryAdd(x, h - 1); }
            for (int y = 0; y < h; y++) { TryAdd(0, y); TryAdd(w - 1, y); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                px[i] = new Color32(0, 0, 0, 0);
                int x = i % w, y = i / w;
                TryAdd(x + 1, y); TryAdd(x - 1, y); TryAdd(x, y + 1); TryAdd(x, y - 1);
            }
            tex.SetPixels32(px);
            tex.Apply();
        }

        static RectInt Bounds(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height, minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 10)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            return maxX < 0 ? new RectInt(0, 0, w, h) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        static RectInt UnionBounds(List<Texture2D> set)
        {
            RectInt u = Bounds(set[0]);
            foreach (var t in set)
            {
                var b = Bounds(t);
                int x0 = Mathf.Min(u.xMin, b.xMin), y0 = Mathf.Min(u.yMin, b.yMin);
                int x1 = Mathf.Max(u.xMax, b.xMax), y1 = Mathf.Max(u.yMax, b.yMax);
                u = new RectInt(x0, y0, x1 - x0, y1 - y0);
            }
            return u;
        }

        // Centred left/right, feet on the floor of the cell
        static void Blit(Texture2D src, RectInt area, Texture2D dst, int cellX, int cellY, float scale)
        {
            int outW = Mathf.RoundToInt(area.width * scale), outH = Mathf.RoundToInt(area.height * scale);
            int offX = cellX + (CellSize - outW) / 2, offY = cellY;
            for (int y = 0; y < outH; y++)
                for (int x = 0; x < outW; x++)
                {
                    float u = (area.x + (x + 0.5f) / scale) / src.width;
                    float v = (area.y + (y + 0.5f) / scale) / src.height;
                    dst.SetPixel(offX + x, offY + y, src.GetPixelBilinear(u, v));
                }
        }

        // ------------------------------------------------------------------ meshes

        // A plain quad. The shader ignores its shape (it uses the UVs), but the bounds must be
        // big enough that Unity doesn't cull Mac when the holster is off screen.
        static Mesh MakePaperMesh(string path)
        {
            AssetDatabase.DeleteAsset(path);
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.SetVertices(new List<Vector3> { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Mesh MakeBoxMesh(string path, Vector3 centre, Vector3 size)
        {
            AssetDatabase.DeleteAsset(path);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = go.GetComponent<MeshFilter>().sharedMesh;
            var verts = src.vertices.Select(v => centre + Vector3.Scale(v, size)).ToArray();
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.vertices = verts;
            mesh.uv = src.uv;
            mesh.triangles = src.triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ prefabs (same proven template as the album weapons)

        static Material FindTemplateMaterial()
        {
            foreach (string g in AssetDatabase.FindAssets("OrangeItem t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (m) { Debug.Log($"[OlMac] Template material {m.name} uses shader {m.shader.name}"); return m; }
            }
            Debug.LogWarning("[OlMac] OrangeItem material not found, falling back to URP Lit");
            return null;
        }

        // Paper Mac as two particles that always stand upright and face the player (Vertical Billboard).
        // Each plays one row of the atlas. An LOD Group swaps them by distance: close = flail, far = walk.
        // All built-in Unity components, no scripts, no custom shaders.
        static void AddPaperParticles(GameObject root, Material mat)
        {
            var paper = new GameObject("Paper");
            paper.transform.SetParent(root.transform, false);

            ParticleSystemRenderer Make(string name, int row, int frames)
            {
                var go = new GameObject(name);
                go.transform.SetParent(paper.transform, false);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                const float life = 1000f;
                var main = ps.main;
                main.loop = true;
                main.duration = life;
                main.startLifetime = life;
                main.startSpeed = 0f;
                main.startSize = PaperSize;      // Mac is ~2.1m tall inside the square, so his comb covers the human head
                main.startRotation = 0f;
                main.startColor = Color.white;
                main.gravityModifier = 0f;
                main.maxParticles = 1;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.playOnAwake = true;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

                var em = ps.emission;
                em.enabled = true;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

                var shape = ps.shape;
                shape.enabled = false;

                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = Columns;
                tsa.numTilesY = Rows;
                tsa.animation = ParticleSystemAnimationType.SingleRow;
#pragma warning disable 0618
                tsa.useRandomRow = false;
#pragma warning restore 0618
                tsa.rowIndex = row;              // row 0 = top of the atlas
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                tsa.cycleCount = Mathf.RoundToInt(life * 8f / Columns); // about 8 frames a second

                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.alignment = ParticleSystemRenderSpace.View;
                r.minParticleSize = 0f;
                r.maxParticleSize = 10f;          // default 0.5 would shrink him when he's right in your face
                // y: lift so his feet sit on the floor (holster is at ~1m hip height).
                // z: pull the drawing ~17cm towards whoever's looking, so it sits just in front of the
                //    hidden body (covers the shorts) but close enough that hits land where you see him.
                //    v0.2 used -0.2 and he ended up BEHIND the body, so positive = towards you.
                r.pivot = new Vector3(0f, PaperLift, PaperForward);
                return r;
            }

            var walkR = Make("Walk", 0, 6);
            var attackR = Make("Attack", 1, 6);

            var lod = paper.AddComponent<LODGroup>();
            lod.localReferencePoint = Vector3.zero;
            lod.size = PaperSize;
            lod.fadeMode = LODFadeMode.None;
            // Fills ~45% of your view height = roughly 2m away
            lod.SetLODs(new[]
            {
                new LOD(0.45f, new Renderer[] { attackR }),
                new LOD(0.002f, new Renderer[] { walkR }),
            });
        }

        static void MakeItemPrefab(string prefabPath, string itemId, Mesh mesh, Material mat,
            Vector3 colliderCentre, Vector3 colliderSize, float gripLength, AudioContainer audio,
            bool hideMesh, Material paper)
        {
            string templatePath = AssetDatabase.FindAssets("ProtoMaul t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "ProtoMaul");
            if (templatePath == null) throw new Exception("Could not find the SDK's ProtoMaul example prefab.");

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(templatePath));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = itemId;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            try
            {
                Transform T(string n) => go.transform.Find(n) ?? throw new Exception("Template is missing child " + n);

                Item item = go.GetComponent<Item>();
                item.itemId = itemId;

                Transform meshT = T("Mesh");
                meshT.localPosition = Vector3.zero; meshT.localRotation = Quaternion.identity; meshT.localScale = Vector3.one;
                foreach (var c in meshT.GetComponents<MonoBehaviour>()) UnityEngine.Object.DestroyImmediate(c);
                meshT.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = meshT.GetComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { mat };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (hideMesh) mr.enabled = false;
                if (paper != null) AddPaperParticles(go, paper);

                Transform cols = T("Colliders");
                cols.localPosition = Vector3.zero; cols.localRotation = Quaternion.identity;
                Transform main = cols.Find("Blade") ?? throw new Exception("Template Colliders/Blade missing");
                Transform grip = cols.Find("Blunt") ?? throw new Exception("Template Colliders/Blunt missing");
                main.name = "MainColliders"; grip.name = "GripColliders";
                foreach (Transform g in new[] { main, grip })
                {
                    g.localPosition = Vector3.zero; g.localRotation = Quaternion.identity;
                    for (int i = g.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(g.GetChild(i).gameObject);
                }
                PhysicMaterial wood = FindPhysicMaterial("Wood");

                var boxGo = new GameObject("MainBox");
                boxGo.transform.SetParent(main, false);
                boxGo.transform.localPosition = colliderCentre;
                var box = boxGo.AddComponent<BoxCollider>();
                box.size = colliderSize;
                box.sharedMaterial = wood;

                var capGo = new GameObject("GripStrip");
                capGo.transform.SetParent(grip, false);
                capGo.transform.localPosition = new Vector3(0, gripLength * 0.5f, 0);
                var cap = capGo.AddComponent<BoxCollider>();
                cap.size = new Vector3(0.03f, Mathf.Max(gripLength, 0.03f), 0.03f);
                cap.sharedMaterial = wood;

                ColliderGroup mainGroup = main.GetComponent<ColliderGroup>();
                ColliderGroup gripGroup = grip.GetComponent<ColliderGroup>();
                mainGroup.colliders = new List<Collider> { box };
                gripGroup.colliders = new List<Collider> { cap };

                Transform face = T("BluntHead"); face.name = "MainHead";
                face.localPosition = colliderCentre; face.localRotation = Quaternion.identity;
                face.GetComponent<Damager>().colliderGroup = mainGroup;
                Transform gripDmg = go.transform.Cast<Transform>().First(t => t.name == "Blunt" && t.GetComponent<Damager>() != null);
                gripDmg.name = "GripBlunt";
                gripDmg.localPosition = Vector3.zero; gripDmg.localRotation = Quaternion.identity;
                gripDmg.GetComponent<Damager>().colliderGroup = gripGroup;

                Transform handleT = T("Handle");
                handleT.localPosition = new Vector3(0, gripLength * 0.5f, 0); handleT.localRotation = Quaternion.identity;
                Handle handle = handleT.GetComponent<Handle>();
                handle.axisLength = gripLength;
                handle.touchRadius = 0.06f;
                handle.reach = 0.25f;

                T("HolderPoint").localPosition = Vector3.zero;
                T("HolderPoint").localRotation = Quaternion.identity;
                T("SpawnPoint").localPosition = Vector3.zero;
                T("ParryPoint").localPosition = colliderCentre;
                Transform parry = T("Parry");
                parry.localPosition = colliderCentre;
                var pt = parry.GetComponent<ParryTarget>(); if (pt) pt.length = Mathf.Max(colliderSize.x, 0.05f) * 0.5f;
                Transform price = go.transform.Find("PriceTag"); if (price) price.localPosition = Vector3.zero;

                Transform prev = T("Preview");
                prev.localPosition = colliderCentre;
                var preview = prev.GetComponent<Preview>();
                preview.size = 0.6f;
                preview.renderers = new List<Renderer> { mr };

                Transform inertia = go.transform.Find("InertiaTensorCollider");
                if (inertia)
                {
                    inertia.localPosition = colliderCentre; inertia.localRotation = Quaternion.identity;
                    var ic = inertia.GetComponent<CapsuleCollider>();
                    ic.direction = 1; ic.radius = Mathf.Max(colliderSize.x * 0.4f, 0.02f); ic.height = Mathf.Max(colliderSize.y, 0.05f);
                }

                if (audio != null)
                {
                    var music = new GameObject("Twang");
                    music.transform.SetParent(go.transform, false);
                    music.transform.localPosition = colliderCentre;
                    var src = music.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.volume = 1f;
                    src.spatialBlend = 0.8f;
                    src.rolloffMode = AudioRolloffMode.Linear;
                    src.minDistance = 3f;
                    src.maxDistance = 30f;
                    var player = music.AddComponent<AudioContainerPlayer>();
                    player.audioContainer = audio;
                    player.playOnAwake = false;
                    player.audioMixer = AudioMixerName.Effect;

                    var linker = go.AddComponent<ItemEventLinker>();
                    linker.item = item;
                    linker.itemEvents = new List<ItemEventLinker.ItemUnityEvent>();
                    var e = new ItemEventLinker.ItemUnityEvent { itemEvent = ItemEventLinker.ItemEvent.OnDamageDealt, onActivate = new UnityEvent<Item>() };
                    UnityEventTools.AddVoidPersistentListener(e.onActivate, new UnityAction(player.Play));
                    linker.itemEvents.Add(e);
                }

                try { item.SetupDefaultComponents(); } catch (Exception ex) { Debug.LogWarning("[OlMac] SetupDefaultComponents: " + ex.Message); }
                item.renderers = new List<Renderer> { mr };

                AssetDatabase.DeleteAsset(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out bool ok);
                if (!ok) throw new Exception("Saving prefab failed: " + prefabPath);
                Debug.Log("[OlMac] Prefab saved: " + prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static PhysicMaterial FindPhysicMaterial(string name)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " t:PhysicMaterial"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<PhysicMaterial>(p);
            }
            return null;
        }

        // ------------------------------------------------------------------ addressables + bundle group

        static AddressableAssetGroup MakeAddressables(List<(string path, string address)> entries)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var grp = settings.FindGroup(ModFolder) ?? settings.CreateGroup(ModFolder, false, false, true, null,
                typeof(BundledAssetGroupSchema), typeof(ThunderRoadAAGroupSchema));
            if (grp.GetSchema<BundledAssetGroupSchema>() == null) grp.AddSchema<BundledAssetGroupSchema>();
            if (grp.GetSchema<ThunderRoadAAGroupSchema>() == null) grp.AddSchema<ThunderRoadAAGroupSchema>();
            grp.GetSchema<ThunderRoadAAGroupSchema>().sharedBundle = false;

            foreach (var (path, address) in entries)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) throw new Exception("Not an asset: " + path);
                var e = settings.CreateOrMoveEntry(guid, grp, false, false);
                e.address = address;
                e.SetLabel("Android", true, true, false);
                e.SetLabel("Windows", true, true, false);
            }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            return grp;
        }

        static AssetBundleGroup MakeBundleGroup(AddressableAssetGroup aa)
        {
            string path = $"{Root}/{ModFolder}.asset";
            var g = AssetDatabase.LoadAssetAtPath<AssetBundleGroup>(path);
            if (g == null) { g = ScriptableObject.CreateInstance<AssetBundleGroup>(); AssetDatabase.CreateAsset(g, path); }
            g.folderName = ModFolder;
            g.isDefault = false;
            g.addressableAssetGroups = new List<AddressableAssetGroup> { aa };
            g.selected = true;
            g.exportAfterBuild = false;
            g.modDescription = "Ol' Mac, the paper chicken man. Banjo in hand, Old Mac Daddy on every hit.";
            g.modAuthor = "Fluid Love";
            g.modVersion = "0.4";
            EditorUtility.SetDirty(g);
            AssetDatabase.SaveAssets();
            if (!g.CheckAddressableLabels(out string msg)) Debug.LogWarning("[OlMac] Label check: " + msg);
            return g;
        }

        // ------------------------------------------------------------------ export

        static string Export(AssetBundleGroup group)
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string outDir = Environment.GetEnvironmentVariable("OLMAC_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(projectRoot, "OlMacOut");
            outDir = Path.Combine(outDir, group.folderName);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            string assets = Path.Combine(projectRoot, AssetBundleBuilder.assetsLocalPath);
            string catalog = Path.Combine(projectRoot, ThunderRoadSettings.current.catalogsEditorPath, FileManager.modsFolderName, group.folderName);
            AssetBundleBuilder.CopyDirectory(assets, outDir);
            if (Directory.Exists(catalog)) AssetBundleBuilder.CopyDirectory(catalog, outDir);
            else Debug.LogWarning("[OlMac] No JSON catalog folder at " + catalog);
            AssetBundleBuilder.CopyDirectory(AssetBundleBuilderGUI.GenerateManifest(group), outDir);

            // The runtime script (hides the human, drives the drawing). Unity already compiled it for Android.
            string dll = Path.Combine(projectRoot, "Library", "ScriptAssemblies", "OlMac.Scripts.dll");
            if (File.Exists(dll))
            {
                File.Copy(dll, Path.Combine(outDir, "OlMac.Scripts.dll"), true);
                Debug.Log("[OlMac] Script DLL included");
            }
            else Debug.LogError("[OlMac] OlMac.Scripts.dll not found at " + dll + " - the mod will run without the script");

            var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
            Debug.Log($"[OlMac] DONE. Exported {files.Length} files to {outDir}");
            foreach (var f in files) Debug.Log("[OlMac]   " + f.Substring(outDir.Length));
            return outDir;
        }
    }
}
