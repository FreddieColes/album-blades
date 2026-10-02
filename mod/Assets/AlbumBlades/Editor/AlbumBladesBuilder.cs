// Album Blades: turns Fluid Love albums into Blade & Sorcery: Nomad weapons.
// Menu: Album Blades > Build Nomad Mod   (or run headless via build.sh)
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
    public static class AlbumBladesBuilder
    {
        const string Root = "Assets/AlbumBlades";
        const string ModFolder = "AlbumBlades";
        const string Album = "PleasureIslandDLC";
        const string ItemId = "FluidLove_PleasureIslandDLC";
        const string PrefabAddress = "FluidLove.PleasureIslandDLC";
        const string IconAddress = "FluidLove.PleasureIslandDLC.Icon";

        // Paddle dimensions (metres). Weapon axis is +Y, grip centred on the origin.
        const float GripHalf = 0.09f, GripRadius = 0.017f;
        const float CaseW = 0.40f, CaseH = 0.40f, CaseT = 0.03f, CaseGap = 0.01f;
        static float CaseBottom => GripHalf + CaseGap;
        static float CaseCentreY => CaseBottom + CaseH * 0.5f;

        [MenuItem("Album Blades/1. Set up assets only")]
        public static void SetupMenu() { Setup(); Debug.Log("[AlbumBlades] Assets set up."); }

        [MenuItem("Album Blades/2. Build Nomad mod")]
        public static void BuildMenu()
        {
            string outDir = BuildAll();
            if (outDir != null) EditorUtility.RevealInFinder(outDir);
        }

        // Called by build.sh: Unity -batchmode -executeMethod FluidLove.AlbumBladesBuilder.BuildFromCommandLine
        public static void BuildFromCommandLine()
        {
            string outDir = null;
            try { outDir = BuildAll(); }
            catch (Exception e) { Debug.LogError("[AlbumBlades] FAILED: " + e); }
            EditorApplication.Exit(outDir != null ? 0 : 1);
        }

        static string BuildAll()
        {
            SetAndroid();
            AssetBundleGroup group = Setup();
            AssetBundleBuilder.exportFolderName = group.folderName;
            Debug.Log("[AlbumBlades] Building asset bundles...");
            if (!AssetBundleBuilder.Build(group, true))
            {
                Debug.LogError("[AlbumBlades] Asset bundle build failed, see errors above.");
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
            // Same shader platform step the SDK's own "Set Build mode to Nomad" menu does (via reflection so a renamed API can't break compilation).
            try
            {
                Type plat = FindType("ThunderRoad.AssetSorcery.AssetSorceryPlatform");
                Type rt = FindType("ThunderRoad.AssetSorcery.AssetSorceryPlatformRuntime");
                var get = rt?.GetMethod("AssetSorceryGetBuildPlatform");
                var set = plat?.GetMethod("AssetSorceryShaderSetPlatform");
                if (get != null && set != null) set.Invoke(null, new[] { get.Invoke(null, new object[] { true }) });
            }
            catch (Exception e) { Debug.LogWarning("[AlbumBlades] AssetSorcery platform step skipped: " + e.Message); }
            Debug.Log($"[AlbumBlades] Target {EditorUserBuildSettings.activeBuildTarget}, quality {Common.GetQualityLevel(true)}");
        }

        static Type FindType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        // ------------------------------------------------------------------ assets

        static AssetBundleGroup Setup()
        {
            string dir = $"{Root}/{Album}";
            string gen = $"{dir}/Generated";
            Directory.CreateDirectory(gen);
            AssetDatabase.Refresh();

            // Textures
            string atlasPath = $"{dir}/Art/{Album}_Atlas.png";
            string iconPath = $"{dir}/Art/{Album}_Icon.png";
            ImportTexture(atlasPath, 2048, false);
            ImportTexture(iconPath, 512, true);

            // Audio clips
            var clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { $"{dir}/Audio" }))
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
            if (clips.Count == 0) throw new Exception("No audio clips found in " + dir + "/Audio");
            Debug.Log($"[AlbumBlades] {clips.Count} clips");

            AudioContainer container = CreateOrReplace<AudioContainer>($"{gen}/{Album}_Clips.asset");
            container.sounds = clips;
            container.useShuffle = true;
            EditorUtility.SetDirty(container);

            Material mat = MakeMaterial($"{gen}/{Album}_Mat.mat", AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
            Mesh mesh = MakeMesh($"{gen}/{Album}_Mesh.asset");
            AssetDatabase.SaveAssets();

            string prefabPath = $"{dir}/{ItemId}.prefab";
            MakePrefab(prefabPath, mesh, mat, container);

            AddressableAssetGroup aa = MakeAddressables(prefabPath, iconPath);
            return MakeBundleGroup(aa);
        }

        static void ImportTexture(string path, int maxSize, bool isIcon)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) throw new Exception("Missing texture " + path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = !isIcon;
            ti.maxTextureSize = maxSize;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }

        static T CreateOrReplace<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var obj = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        static Material MakeMaterial(string path, Texture2D tex)
        {
            AssetDatabase.DeleteAsset(path);
            Material template = null;
            foreach (string g in AssetDatabase.FindAssets("OrangeItem t:Material"))
            {
                template = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (template) break;
            }
            Material m = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            foreach (string p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                if (m.HasProperty(p)) m.SetTexture(p, tex);
            foreach (string p in new[] { "_BaseColor", "_Color" })
                if (m.HasProperty(p)) m.SetColor(p, Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.65f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(m, path);
            Debug.Log($"[AlbumBlades] Material shader: {m.shader.name} (template: {(template ? template.name : "none")})");
            return m;
        }

        // ------------------------------------------------------------------ mesh

        class MeshBuilder
        {
            public List<Vector3> v = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> t = new List<int>();

            // Adds a quad whose front faces along 'normal'. Fixes winding automatically.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 normal)
            {
                int i = v.Count;
                v.AddRange(new[] { a, b, c, d });
                uv.AddRange(new[] { ua, ub, uc, ud });
                bool ok = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0;
                if (ok) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }

        // Atlas layout (2048 square, Unity UV origin bottom-left):
        // front cover u0-0.5 v0.5-1 | back insert u0.5-1 v0.5-1 | spine strip v0.4375-0.5 | dark grip v0-0.25
        static Mesh MakeMesh(string path)
        {
            var mb = new MeshBuilder();
            float x0 = -CaseW / 2, x1 = CaseW / 2, y0 = CaseBottom, y1 = CaseBottom + CaseH, z0 = -CaseT / 2, z1 = CaseT / 2;
            Vector2 D0 = new Vector2(0.05f, 0.05f), D1 = new Vector2(0.95f, 0.05f), D2 = new Vector2(0.95f, 0.2f), D3 = new Vector2(0.05f, 0.2f);

            // Front (+Z). Viewed from +Z, +X is on the viewer's left, so U runs from x1 to x0.
            mb.Quad(new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1),
                new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), Vector3.forward);
            // Back (-Z)
            mb.Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0),
                new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector3.back);
            // Spine (+X side), text runs bottom to top
            mb.Quad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.4375f), new Vector2(1f, 0.4375f), new Vector2(1f, 0.5f), Vector3.right);
            // Other edges, dark
            mb.Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), D0, D1, D2, D3, Vector3.left);
            mb.Quad(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), D0, D1, D2, D3, Vector3.up);
            mb.Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), D0, D1, D2, D3, Vector3.down);

            // Grip: 10-sided tube from -GripHalf to case bottom (it runs slightly into the case).
            int sides = 10;
            float gy0 = -GripHalf, gy1 = CaseBottom + 0.02f;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2 / sides, a1 = (s + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * GripRadius, 0, Mathf.Sin(a0) * GripRadius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * GripRadius, 0, Mathf.Sin(a1) * GripRadius);
                Vector3 n = ((p0 + p1) * 0.5f).normalized;
                float u0 = (float)s / sides, u1 = (float)(s + 1) / sides;
                mb.Quad(p0 + Vector3.up * gy0, p1 + Vector3.up * gy0, p1 + Vector3.up * gy1, p0 + Vector3.up * gy1,
                    new Vector2(u0, 0.01f), new Vector2(u1, 0.01f), new Vector2(u1, 0.24f), new Vector2(u0, 0.24f), n);
                // bottom cap (triangle fan as degenerate quad)
                Vector3 c = Vector3.up * gy0;
                mb.Quad(c, c, p1 + c, p0 + c, D0, D0, D1, D2, Vector3.down);
            }

            AssetDatabase.DeleteAsset(path);
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.SetVertices(mb.v);
            mesh.SetUVs(0, mb.uv);
            mesh.SetTriangles(mb.t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ prefab

        static void MakePrefab(string prefabPath, Mesh mesh, Material mat, AudioContainer container)
        {
            string templatePath = AssetDatabase.FindAssets("ProtoMaul t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "ProtoMaul");
            if (templatePath == null) throw new Exception("Could not find the SDK's ProtoMaul example prefab to use as a template.");

            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = ItemId;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            try
            {
                Transform T(string n) => go.transform.Find(n) ?? throw new Exception("Template is missing child " + n);
                void Place(Transform t, Vector3 pos) { t.localPosition = pos; }

                Item item = go.GetComponent<Item>();
                item.itemId = ItemId;

                // Visual mesh
                Transform meshT = T("Mesh");
                meshT.localPosition = Vector3.zero; meshT.localRotation = Quaternion.identity; meshT.localScale = Vector3.one;
                foreach (var c in meshT.GetComponents<MonoBehaviour>()) UnityEngine.Object.DestroyImmediate(c); // blood reveal needs special maps
                meshT.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = meshT.GetComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { mat };

                // Colliders: reuse the two collider groups, swap their shapes
                Transform cols = T("Colliders");
                cols.localPosition = Vector3.zero; cols.localRotation = Quaternion.identity;
                Transform album = cols.Find("Blade") ?? throw new Exception("Template Colliders/Blade missing");
                Transform grip = cols.Find("Blunt") ?? throw new Exception("Template Colliders/Blunt missing");
                album.name = "AlbumColliders"; grip.name = "GripColliders";
                foreach (Transform g in new[] { album, grip })
                {
                    g.localPosition = Vector3.zero; g.localRotation = Quaternion.identity;
                    for (int i = g.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(g.GetChild(i).gameObject);
                }
                PhysicMaterial wood = FindPhysicMaterial("Wood");

                var boxGo = new GameObject("CaseBox");
                boxGo.transform.SetParent(album, false);
                boxGo.transform.localPosition = new Vector3(0, CaseCentreY, 0);
                var box = boxGo.AddComponent<BoxCollider>();
                box.size = new Vector3(CaseW, CaseH, CaseT);
                box.sharedMaterial = wood;

                var capGo = new GameObject("GripCapsule");
                capGo.transform.SetParent(grip, false);
                capGo.transform.localPosition = new Vector3(0, (CaseBottom - GripHalf) * 0.5f, 0);
                var cap = capGo.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = GripRadius + 0.003f; cap.height = CaseBottom + GripHalf;
                cap.sharedMaterial = wood;

                ColliderGroup albumGroup = album.GetComponent<ColliderGroup>();
                ColliderGroup gripGroup = grip.GetComponent<ColliderGroup>();
                albumGroup.colliders = new List<Collider> { box };
                gripGroup.colliders = new List<Collider> { cap };

                // Damagers
                Transform face = T("BluntHead"); face.name = "AlbumFace";
                face.localPosition = new Vector3(0, CaseCentreY, 0); face.localRotation = Quaternion.identity;
                face.GetComponent<Damager>().colliderGroup = albumGroup;
                Transform gripDmg = go.transform.Cast<Transform>().First(t => t.name == "Blunt" && t.GetComponent<Damager>() != null);
                gripDmg.name = "GripBlunt";
                gripDmg.localPosition = Vector3.zero; gripDmg.localRotation = Quaternion.identity;
                gripDmg.GetComponent<Damager>().colliderGroup = gripGroup;

                // Handle
                Transform handleT = T("Handle");
                handleT.localPosition = Vector3.zero; handleT.localRotation = Quaternion.identity;
                Handle handle = handleT.GetComponent<Handle>();
                handle.axisLength = GripHalf * 2f - 0.02f;
                handle.touchRadius = 0.06f;
                handle.reach = 0.25f;

                // Misc points
                Place(T("HolderPoint"), new Vector3(0, 0.02f, 0));
                Place(T("SpawnPoint"), new Vector3(0, 0.05f, 0));
                Place(T("ParryPoint"), new Vector3(0, CaseCentreY, 0));
                Transform parry = T("Parry");
                parry.localPosition = new Vector3(0, CaseCentreY, 0);
                parry.localRotation = Quaternion.Euler(0, 0, 90);
                var pt = parry.GetComponent<ParryTarget>(); if (pt) pt.length = CaseW * 0.5f;
                Transform price = go.transform.Find("PriceTag"); if (price) Place(price, new Vector3(0, 0.0f, -0.025f));

                Transform prev = T("Preview");
                prev.localPosition = new Vector3(0, CaseCentreY * 0.75f, 0);
                var preview = prev.GetComponent<Preview>();
                preview.size = 0.7f;
                preview.renderers = new List<Renderer> { mr };

                // Inertia and centre of mass
                Transform inertia = go.transform.Find("InertiaTensorCollider");
                if (inertia)
                {
                    inertia.localPosition = new Vector3(0, CaseCentreY * 0.6f, 0); inertia.localRotation = Quaternion.identity;
                    var ic = inertia.GetComponent<CapsuleCollider>();
                    ic.direction = 1; ic.radius = CaseW * 0.35f; ic.height = CaseH + GripHalf * 2f;
                }
                var so = new SerializedObject(item);
                var com = so.FindProperty("customCenterOfMass");
                if (com != null) com.vector3Value = new Vector3(0, CaseCentreY * 0.55f, 0);
                so.ApplyModifiedPropertiesWithoutUndo();

                // Music: random clip on hit, and on trigger press
                var music = new GameObject("Music");
                music.transform.SetParent(go.transform, false);
                music.transform.localPosition = new Vector3(0, CaseCentreY, 0);
                music.AddComponent<AudioSource>().playOnAwake = false;
                var player = music.AddComponent<AudioContainerPlayer>();
                player.audioContainer = container;
                player.playOnAwake = false;
                player.audioMixer = AudioMixerName.Effect;

                var linker = go.AddComponent<ItemEventLinker>();
                linker.item = item;
                linker.itemEvents = new List<ItemEventLinker.ItemUnityEvent>();
                foreach (var ev in new[] { ItemEventLinker.ItemEvent.OnDamageDealt, ItemEventLinker.ItemEvent.OnGrabbedUsePress })
                {
                    var e = new ItemEventLinker.ItemUnityEvent { itemEvent = ev, onActivate = new UnityEvent<Item>() };
                    UnityEventTools.AddVoidPersistentListener(e.onActivate, new UnityAction(player.Play));
                    linker.itemEvents.Add(e);
                }

                try { item.SetupDefaultComponents(); } catch (Exception e) { Debug.LogWarning("[AlbumBlades] SetupDefaultComponents: " + e.Message); }
                item.renderers = new List<Renderer> { mr };

                AssetDatabase.DeleteAsset(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath, out bool ok);
                if (!ok) throw new Exception("Saving prefab failed");
                Debug.Log("[AlbumBlades] Prefab saved: " + prefabPath);
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
            Debug.LogWarning("[AlbumBlades] Physic material not found: " + name);
            return null;
        }

        // ------------------------------------------------------------------ addressables + bundle group

        static AddressableAssetGroup MakeAddressables(string prefabPath, string iconPath)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var grp = settings.FindGroup(ModFolder) ?? settings.CreateGroup(ModFolder, false, false, true, null,
                typeof(BundledAssetGroupSchema), typeof(ThunderRoadAAGroupSchema));
            if (grp.GetSchema<BundledAssetGroupSchema>() == null) grp.AddSchema<BundledAssetGroupSchema>();
            if (grp.GetSchema<ThunderRoadAAGroupSchema>() == null) grp.AddSchema<ThunderRoadAAGroupSchema>();
            grp.GetSchema<ThunderRoadAAGroupSchema>().sharedBundle = false;

            void Entry(string path, string address)
            {
                var e = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), grp, false, false);
                e.address = address;
                e.SetLabel("Android", true, true, false);
                e.SetLabel("Windows", true, true, false);
            }
            Entry(prefabPath, PrefabAddress);
            Entry(iconPath, IconAddress);
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
            g.modDescription = "Fluid Love albums as weapons. Every hit plays the album.";
            g.modAuthor = "Fluid Love";
            g.modVersion = "0.1";
            EditorUtility.SetDirty(g);
            AssetDatabase.SaveAssets();
            if (!g.CheckAddressableLabels(out string msg)) Debug.LogWarning("[AlbumBlades] Label check: " + msg);
            return g;
        }

        // ------------------------------------------------------------------ export

        static string Export(AssetBundleGroup group)
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string outDir = Environment.GetEnvironmentVariable("ALBUM_BLADES_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(projectRoot, "AlbumBladesOut");
            outDir = Path.Combine(outDir, group.folderName);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            string assets = Path.Combine(projectRoot, AssetBundleBuilder.assetsLocalPath);
            string catalog = Path.Combine(projectRoot, ThunderRoadSettings.current.catalogsEditorPath, FileManager.modsFolderName, group.folderName);
            AssetBundleBuilder.CopyDirectory(assets, outDir);
            if (Directory.Exists(catalog)) AssetBundleBuilder.CopyDirectory(catalog, outDir);
            else Debug.LogWarning("[AlbumBlades] No JSON catalog folder at " + catalog);
            AssetBundleBuilder.CopyDirectory(AssetBundleBuilderGUI.GenerateManifest(group), outDir);

            var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
            Debug.Log($"[AlbumBlades] DONE. Exported {files.Length} files to {outDir}");
            foreach (var f in files) Debug.Log("[AlbumBlades]   " + f.Substring(outDir.Length));
            return outDir;
        }
    }
}
