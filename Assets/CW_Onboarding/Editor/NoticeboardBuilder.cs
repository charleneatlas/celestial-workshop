using System.Collections.Generic;
using System.IO;
using Oculus.Interaction.Input;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace CW.Onboarding.EditorTools
{
    /// <summary>
    /// Builds (or rebuilds in place) the workshop noticeboard prefab:
    /// Tools > Celestial Workshop > Build Workshop Noticeboard.
    ///
    /// Every tunable is a constant below, so the look is "edit, rebuild".
    /// Objects are reused by name, so scene overrides on the prefab survive a
    /// rebuild. Textures come from NoticeboardArt (regenerated unless a file
    /// is listed in KeepTextures, for hand-made replacements).
    ///
    /// Board frame: +X player's right, +Y up, +Z away from the player (text
    /// reads facing -Z). Units are metres; the parchment is 0.47 x 0.52.
    /// </summary>
    public static class NoticeboardBuilder
    {
        public const string Root = "Assets/CW_Onboarding";
        public const string PrefabPath = Root + "/WorkshopNoticeboard.prefab";
        const string ArtDir = Root + "/Art/Generated";
        const string MatDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";
        const string LogoPath = Root + "/Art/ProperMotion_Logo.png";

        const string FontTitle = "Assets/CW_Art/Fonts/Meddon SDF.asset";
        const string FontHand = "Assets/CW_Art/Fonts/Caveat SDF.asset";
        const string FontBody = "Assets/CW_Art/Fonts/ArchitectsDaughter SDF.asset";
        const string PenPrefab = "Assets/Prefabs/CW_Prefabs/SigningPen.prefab";
        const string PenGrip = "Assets/CW_Art/HandPoses/PenGrip.asset";
        const string GhostHand = "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/OpenXRGhost-HandRight.prefab";

        // Files never regenerated (drop hand-made art in with these names).
        static readonly HashSet<string> KeepTextures = new HashSet<string>();

        // ---- Copy ----
        const string Title = "Celestial<space=0.85em>Workshop";
        const string Tagline = "Craft the sky with your hands";
        const string Intro = "A hand-tracked experience. Set your controllers aside.";
        const string RulesHeading = "The Workshop Rules";
        static readonly string[] RuleHeads =
        {
            "Only hands may enter.",
            "The sky has no “up.”",
            "Shape the stars, then sign your work."
        };
        static readonly string[] RuleBodies =
        {
            "No controllers in the workshop!",
            "A constellation may look like a W from one part of Earth and an M from another. It’s all a matter of perspective.",
            "Use your sketch as your guide. Rotate the stars until their view from Earth matches it."
        };
        const string PlaceholderName = "Sky Crafter";
        const string TraceHint = "Trace here to enter";

        // ---- Colours ----
        static readonly Color InkText = new Color(0.22f, 0.12f, 0.05f, 1f);
        static readonly Color InkBody = new Color(0.27f, 0.16f, 0.08f, 1f);
        static readonly Color SignatureFace = new Color(1f, 0.86f, 0.55f, 1f);

        // ---- Sizes (m) ----
        const float PaperW = 0.47f, PaperH = 0.52f;
        const float TextZ = -0.0016f, InkZ = -0.0012f;
        const float LeftCol = -0.160f;      // rule text left edge
        const float RuleTextWidth = 0.252f;
        const float IllusX = 0.152f;

        // ---- Sorting ----
        const int SortWood = 0, SortShadow = 1, SortPaper = 2, SortInk = 3, SortText = 4, SortNails = 6, SortSignature = 7;

        [MenuItem("Tools/Celestial Workshop/Build Workshop Noticeboard")]
        public static void BuildMenu()
        {
            Debug.Log(Build(true));
        }

        public static string Build(bool regenerateArt)
        {
            Folder(Root + "/Art");
            Folder(ArtDir);
            Folder(MatDir);
            Folder(MeshDir);
            var log = new System.Text.StringBuilder();

            if (regenerateArt)
            {
                string tmp = Path.Combine(Path.GetTempPath(), "cw_noticeboard_art");
                log.Append(NoticeboardArt.GenerateAll(tmp));
                foreach (string file in Directory.GetFiles(tmp, "*.png"))
                {
                    string dest = ArtDir + "/" + Path.GetFileName(file);
                    if (KeepTextures.Contains(Path.GetFileNameWithoutExtension(file)) && File.Exists(dest))
                    {
                        continue;
                    }

                    File.Copy(file, dest, true);
                }

                AssetDatabase.Refresh();
            }

            ImportSettings();
            var mats = Materials();

            bool existing = File.Exists(PrefabPath);
            GameObject root = existing
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("Workshop Noticeboard");
            try
            {
                BuildHierarchy(root, mats);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (existing)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    Object.DestroyImmediate(root);
                }
            }

            AssetDatabase.SaveAssets();
            log.Append("Built ").Append(PrefabPath);
            return log.ToString();
        }

        // ================= Assets =================

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ImportSettings()
        {
            var paths = new List<string>(Directory.GetFiles(ArtDir, "*.png"));
            paths.Add(LogoPath);
            foreach (string file in paths)
            {
                string path = file.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null)
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(path);
                bool tiled = name == "Wood";
                bool changed = false;
                void Set<T>(T current, T value, System.Action<T> apply)
                {
                    if (!EqualityComparer<T>.Default.Equals(current, value))
                    {
                        apply(value);
                        changed = true;
                    }
                }

                Set(ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
                Set(ti.alphaIsTransparency, true, v => ti.alphaIsTransparency = v);
                Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
                Set(ti.wrapMode, tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, v => ti.wrapMode = v);
                Set(ti.anisoLevel, 4, v => ti.anisoLevel = v);
                Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
                Set(ti.maxTextureSize, 2048, v => ti.maxTextureSize = v);
                Set(ti.sRGBTexture, true, v => ti.sRGBTexture = v);
                if (changed)
                {
                    ti.SaveAndReimport();
                }
            }
        }

        static Texture2D Tex(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/" + name + ".png");
        }

        sealed class Mats
        {
            public readonly Dictionary<string, Material> ByName = new Dictionary<string, Material>();
            public Material Glow, Sparkle, Ghost, SignatureText, TitleText, Logo;
            public Material this[string name] => ByName[name];
        }

        static Material Mat(string name, Shader shader)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            return m;
        }

        static Material Surface(string name, Texture2D tex, float shade, bool zwrite, Color tint, bool twoSided = false)
        {
            Shader s = Shader.Find("CelestialWorkshop/NoticeboardSurface");
            Material m = Mat("NB_" + name, s);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Shade", shade);
            m.SetFloat("_ZWrite", zwrite ? 1f : 0f);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mats Materials()
        {
            var mats = new Mats();
            mats.ByName["Wood"] = Surface("Wood", Tex("Wood"), 0.85f, true, Color.white);
            mats.ByName["Parchment"] = Surface("Parchment", Tex("Parchment"), 0.45f, true, Color.white);
            mats.ByName["ParchmentShadow"] = Surface("ParchmentShadow", Tex("ParchmentShadow"), 0f, false, Color.white);
            mats.ByName["NailHead"] = Surface("NailHead", Tex("NailHead"), 1f, true, Color.white);
            foreach (string ink in new[] { "NailDent", "TitleStars", "Ribbon", "Divider", "RuleSeparator", "Flourish", "NumberOval1", "NumberOval2", "NumberOval3", "Controllers", "WM", "SketchGlobe", "Hands", "Underline", "SignatureSmudge", "GuideLine", "TraceFlourish" })
            {
                mats.ByName[ink] = Surface(ink, Tex(ink), 0.1f, false, Color.white, true);
            }

            var logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            mats.Logo = Surface("ProperMotionLogo", logoTex, 0.1f, false, new Color(0.22f, 0.12f, 0.05f, 1f), true);

            Shader glow = Shader.Find("CelestialWorkshop/StarGlow");
            mats.Glow = Mat("Trail_Glow", glow);
            mats.Glow.SetTexture("_MainTex", Tex("GlowDot"));
            mats.Glow.SetFloat("_Intensity", 1.3f);
            mats.Sparkle = Mat("Trail_Sparkle", glow);
            mats.Sparkle.SetTexture("_MainTex", Tex("SparkleStar"));
            mats.Sparkle.SetFloat("_Intensity", 1.8f);

            mats.Ghost = Mat("GhostHand", Shader.Find("CelestialWorkshop/GhostHand"));
            mats.Ghost.SetColor("_Color", new Color(1f, 0.9f, 0.76f, 0.32f));
            mats.Ghost.SetColor("_RimColor", new Color(1f, 0.84f, 0.52f, 0.95f));
            mats.Ghost.SetFloat("_RimPower", 2.2f);
            EditorUtility.SetDirty(mats.Ghost);

            // Gold signature: a copy of the cursive font's material with a soft warm underlay glow.
            var meddon = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontTitle);
            mats.SignatureText = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Signature_Gold.mat");
            if (mats.SignatureText == null)
            {
                mats.SignatureText = new Material(meddon.material) { name = "Signature_Gold" };
                AssetDatabase.CreateAsset(mats.SignatureText, MatDir + "/Signature_Gold.mat");
            }

            mats.TitleText = FontMaterial(meddon, "Title_Ink");
            mats.TitleText.SetFloat("_FaceDilate", 0.14f);
            EditorUtility.SetDirty(mats.TitleText);

            mats.SignatureText.shader = meddon.material.shader;
            mats.SignatureText.SetTexture("_MainTex", meddon.material.GetTexture("_MainTex"));
            mats.SignatureText.SetFloat("_TextureWidth", meddon.material.GetFloat("_TextureWidth"));
            mats.SignatureText.SetFloat("_TextureHeight", meddon.material.GetFloat("_TextureHeight"));
            mats.SignatureText.SetFloat("_GradientScale", meddon.material.GetFloat("_GradientScale"));
            mats.SignatureText.SetColor("_FaceColor", Color.white);
            mats.SignatureText.EnableKeyword("UNDERLAY_ON");
            mats.SignatureText.SetColor("_UnderlayColor", new Color(1f, 0.6f, 0.16f, 0.75f));
            mats.SignatureText.SetFloat("_UnderlaySoftness", 0.85f);
            mats.SignatureText.SetFloat("_UnderlayDilate", 0.8f);
            mats.SignatureText.SetFloat("_FaceDilate", 0.08f);
            EditorUtility.SetDirty(mats.SignatureText);
            return mats;
        }

        static Material FontMaterial(TMP_FontAsset font, string name)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(font.material) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = font.material.shader;
            m.CopyPropertiesFromMaterial(font.material);
            return m;
        }

        // ================= Hierarchy =================

        static Transform Node(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }

            t.gameObject.SetActive(true);
            return t;
        }

        static T Get<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static void Pose(Transform t, Vector3 pos, Vector3 euler = default, Vector3? scale = null)
        {
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = scale ?? Vector3.one;
        }

        static MeshRenderer Quad(Transform parent, string name, Material mat, Vector2 center, Vector2 size, int sort, float z = InkZ, float rotZ = 0f, bool flipX = false)
        {
            Transform t = Node(parent, name);
            Pose(t, new Vector3(center.x, center.y, z), new Vector3(0, 0, rotZ), new Vector3(size.x * (flipX ? -1f : 1f), size.y, 1f));
            Get<MeshFilter>(t.gameObject).sharedMesh = QuadMesh();
            var r = Get<MeshRenderer>(t.gameObject);
            r.sharedMaterial = mat;
            r.sortingOrder = sort;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static Mesh quadMesh;

        // Unit quad facing -Z (toward the player), UVs upright.
        static Mesh QuadMesh()
        {
            if (quadMesh != null)
            {
                return quadMesh;
            }

            string path = MeshDir + "/NB_Quad.asset";
            quadMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (quadMesh == null)
            {
                quadMesh = new Mesh { name = "NB_Quad" };
                quadMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
                quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                quadMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                AssetDatabase.CreateAsset(quadMesh, path);
            }

            return quadMesh;
        }

        static void BuildHierarchy(GameObject rootGo, Mats mats)
        {
            Transform root = rootGo.transform;
            rootGo.name = "Workshop Noticeboard";
            var onboarding = Get<WorkshopOnboarding>(rootGo);
            var displayName = Get<MetaDisplayName>(rootGo);

            Transform board = Node(root, "Board");
            Pose(board, Vector3.zero);

            BuildWood(Node(board, "Wood"), mats);
            BuildParchment(board, mats);
            BuildNails(Node(board, "Nails"), mats);

            Transform content = Node(board, "Content");
            Pose(content, Vector3.zero);
            BuildHeader(content, mats);
            BuildRules(content, mats);
            var agreement = BuildAgreement(content, mats);
            var sig = BuildSignature(content, mats, out TextMeshPro welcome, out GameObject traceHint);
            Quad(Node(content, "Maker Mark"), "Proper Motion Logo", mats.Logo, new Vector2(0.146f, -0.2355f), new Vector2(0.0655f, 0.0136f), SortInk);

            // Effects.
            Transform trailT = Node(board, "Star Trail");
            Pose(trailT, Vector3.zero);
            var trail = Get<StarTrail>(trailT.gameObject);
            SetRef(trail, "glowMaterial", mats.Glow);
            SetRef(trail, "sparkleMaterial", mats.Sparkle);
            SetFloat(trail, "glowSize", 0.021f);
            SetFloat(trail, "sparklesPerMetre", 190f);
            SetVector2(trail, "sparkleSize", new Vector2(0.0045f, 0.011f));

            Transform ghostT = Node(board, "Ghost Demo");
            Pose(ghostT, Vector3.zero);
            var ghost = Get<GhostHandDemo>(ghostT.gameObject);
            Transform demoTrailT = Node(ghostT, "Demo Trail");
            var demoTrail = Get<StarTrail>(demoTrailT.gameObject);
            SetRef(demoTrail, "glowMaterial", mats.Glow);
            SetRef(demoTrail, "sparkleMaterial", mats.Sparkle);
            SetFloat(demoTrail, "intensity", 0.35f);
            SetFloat(demoTrail, "headSize", 0.018f);
            SetFloat(demoTrail, "sparklesPerMetre", 60f);
            SetFloat(demoTrail, "glowPerMetre", 90f);

            SetRef(ghost, "signature", sig);
            SetRef(ghost, "ghostHandPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(GhostHand));
            SetRef(ghost, "gripPose", AssetDatabase.LoadAssetAtPath<PenGripPose>(PenGrip));
            SetRef(ghost, "penPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PenPrefab));
            SetRef(ghost, "ghostMaterial", mats.Ghost);
            SetRef(ghost, "demoTrail", demoTrail);
            SetFloat(ghost, "opacity", 0.75f);

            SetRef(sig, "trail", trail);

            // Controller wiring inside the prefab (scene references are set by the scene hookup).
            SetRef(onboarding, "board", board.gameObject);
            SetRef(onboarding, "signature", sig);
            SetRef(onboarding, "ghost", ghost);
            SetRef(onboarding, "trail", trail);
            SetRef(onboarding, "displayName", displayName);
            SetRef(onboarding, "agreementLead", agreement[0]);
            SetRef(onboarding, "agreementName", agreement[1]);
            SetRef(onboarding, "agreementTail", agreement[2]);
            SetRef(onboarding, "agreementUnderline", agreement[1].transform.parent.Find("Name Underline"));
            SetRef(onboarding, "welcomeText", welcome);
            SetRef(onboarding, "traceHint", traceHint);
        }

        // ---- Wood ----

        static void BuildWood(Transform wood, Mats mats)
        {
            Pose(wood, Vector3.zero);
            var rnd = new System.Random(5);
            float r() => (float)rnd.NextDouble() * 2f - 1f;
            // Three vertical back planks.
            const float backW = 0.535f, backH = 0.585f;
            float plankW = backW / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x = -backW * 0.5f + plankW * (i + 0.5f);
                Box(wood, "Plank " + (i + 1), mats["Wood"], new Vector3(x, r() * 0.003f, 0.031f + r() * 0.002f),
                    new Vector3(plankW - 0.003f, backH + r() * 0.006f, 0.016f), new Vector3(r() * 0.5f, r() * 0.5f, r() * 0.4f),
                    new Rect(0.05f + i * 0.31f, 0.02f, 0.28f, 0.96f), false);
            }

            // Top and bottom battens (grain runs across).
            Box(wood, "Batten Top", mats["Wood"], new Vector3(0.002f, 0.268f, 0.012f), new Vector3(0.565f, 0.05f, 0.02f), new Vector3(0, 0, -0.5f), new Rect(0.1f, 0.0f, 0.12f, 1f), true);
            Box(wood, "Batten Bottom", mats["Wood"], new Vector3(-0.002f, -0.268f, 0.012f), new Vector3(0.565f, 0.05f, 0.02f), new Vector3(0, 0, 0.4f), new Rect(0.6f, 0.0f, 0.12f, 1f), true);
        }

        static void Box(Transform parent, string name, Material mat, Vector3 pos, Vector3 size, Vector3 euler, Rect uv, bool rotateUv)
        {
            Transform t = Node(parent, name);
            Pose(t, pos, euler);
            string key = name.Replace(' ', '_');
            Get<MeshFilter>(t.gameObject).sharedMesh = BoxMesh("NB_" + key, size, uv, rotateUv);
            var mr = Get<MeshRenderer>(t.gameObject);
            mr.sharedMaterial = mat;
            mr.sortingOrder = SortWood;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Mesh BoxMesh(string name, Vector3 size, Rect uv, bool rotateUv)
        {
            string path = MeshDir + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create)
            {
                mesh = new Mesh { name = name };
            }

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var tris = new List<int>();
            Vector3 h = size * 0.5f;
            void Face(Vector3 normal, Vector3 right, Vector3 up, float w, float hh, float d, Rect r)
            {
                int b = v.Count;
                Vector3 c = normal * d;
                v.Add(c - right * w - up * hh); v.Add(c + right * w - up * hh); v.Add(c - right * w + up * hh); v.Add(c + right * w + up * hh);
                for (int i = 0; i < 4; i++) n.Add(normal);
                Vector2 Map(float a, float bb) => rotateUv ? new Vector2(r.x + bb * r.width, r.y + a * r.height) : new Vector2(r.x + a * r.width, r.y + bb * r.height);
                u.Add(Map(0, 0)); u.Add(Map(1, 0)); u.Add(Map(0, 1)); u.Add(Map(1, 1));
                tris.AddRange(new[] { b, b + 2, b + 1, b + 2, b + 3, b + 1 });
            }

            Rect side = new Rect(uv.x, uv.y, uv.width * 0.08f, uv.height);
            Face(Vector3.back, Vector3.right, Vector3.up, h.x, h.y, h.z, uv);
            Face(Vector3.forward, Vector3.left, Vector3.up, h.x, h.y, h.z, uv);
            Face(Vector3.left, Vector3.back, Vector3.up, h.z, h.y, h.x, side);
            Face(Vector3.right, Vector3.forward, Vector3.up, h.z, h.y, h.x, side);
            Face(Vector3.up, Vector3.right, Vector3.forward, h.x, h.z, h.y, new Rect(uv.x, uv.y, uv.width, uv.height * 0.05f));
            Face(Vector3.down, Vector3.right, Vector3.back, h.x, h.z, h.y, new Rect(uv.x, uv.y, uv.width, uv.height * 0.05f));
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, u);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            if (create)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        // ---- Parchment ----

        static void BuildParchment(Transform board, Mats mats)
        {
            Quad(board, "Parchment Shadow", mats["ParchmentShadow"], new Vector2(0.007f, -0.009f), new Vector2(PaperW * 1.1f, PaperH * 1.08f), SortShadow, 0.0012f);
            Transform t = Node(board, "Parchment");
            Pose(t, Vector3.zero);
            Get<MeshFilter>(t.gameObject).sharedMesh = ParchmentMesh();
            var mr = Get<MeshRenderer>(t.gameObject);
            mr.sharedMaterial = mats["Parchment"];
            mr.sortingOrder = SortPaper;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // A flat sheet whose edges and corners lift gently toward the player.
        static Mesh ParchmentMesh()
        {
            string path = MeshDir + "/NB_Parchment.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create)
            {
                mesh = new Mesh { name = "NB_Parchment" };
            }

            const int nx = 30, ny = 33;
            var v = new Vector3[(nx + 1) * (ny + 1)];
            var uv = new Vector2[v.Length];
            for (int y = 0; y <= ny; y++)
            {
                for (int x = 0; x <= nx; x++)
                {
                    float u = x / (float)nx, w = y / (float)ny;
                    float px = (u - 0.5f) * PaperW, py = (w - 0.5f) * PaperH;
                    float dx = Mathf.Min(u, 1f - u) * PaperW, dy = Mathf.Min(w, 1f - w) * PaperH;
                    float ex = Mathf.Clamp01(1f - dx / 0.035f), ey = Mathf.Clamp01(1f - dy / 0.035f);
                    float edge = Mathf.Max(ex * ex, ey * ey);
                    float corner = ex * ey;
                    float wave = Mathf.PerlinNoise(u * 4f, w * 4f) - 0.5f;
                    float z = -(edge * 0.0035f + corner * 0.006f) * (0.7f + 0.6f * Mathf.PerlinNoise(u * 3f + 2f, w * 3f));
                    z += wave * 0.0012f * Mathf.Max(edge, 0.25f);
                    v[y * (nx + 1) + x] = new Vector3(px, py, z);
                    uv[y * (nx + 1) + x] = new Vector2(u, w);
                }
            }

            var tris = new List<int>();
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int i = y * (nx + 1) + x;
                    tris.AddRange(new[] { i, i + nx + 1, i + 1, i + 1, i + nx + 1, i + nx + 2 });
                }
            }

            mesh.Clear();
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (create)
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        // ---- Nails: imperfectly hammered by hand ----

        static void BuildNails(Transform nails, Mats mats)
        {
            Pose(nails, Vector3.zero);
            var spots = new[]
            {
                new Vector2(-0.205f, 0.236f), new Vector2(0.207f, 0.232f), new Vector2(-0.203f, -0.236f), new Vector2(0.204f, -0.230f),
                new Vector2(0.018f, 0.247f)
            };
            var rnd = new System.Random(21);
            float r() => (float)rnd.NextDouble() * 2f - 1f;
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh sphere = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(probe);
            for (int i = 0; i < spots.Length; i++)
            {
                Vector2 p = spots[i] + new Vector2(r() * 0.006f, r() * 0.006f);
                float size = 0.0142f + r() * 0.0016f;
                float sink = 0.0006f + (float)rnd.NextDouble() * 0.0016f;
                Transform nail = Node(nails, "Nail " + (i + 1));
                Pose(nail, new Vector3(p.x, p.y, -0.0005f + sink), new Vector3(r() * 14f, r() * 14f, r() * 180f), new Vector3(size, size, size * 0.42f));
                Get<MeshFilter>(nail.gameObject).sharedMesh = sphere;
                var mr = Get<MeshRenderer>(nail.gameObject);
                mr.sharedMaterial = mats["NailHead"];
                mr.sortingOrder = SortNails;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Quad(nails, "Nail Dent " + (i + 1), mats["NailDent"], p + new Vector2(0.0015f, -0.002f), Vector2.one * (size * 2.1f), SortInk, InkZ, r() * 30f);
            }
        }

        // ---- Text ----

        static TextMeshPro Text(Transform parent, string name, string text, string fontPath, float cap, Color color,
            TextAlignmentOptions align, Vector2 pos, float width, bool wrap = false, FontStyles style = FontStyles.Normal,
            float lineSpacing = 0f, float maxWidth = 0f, int sort = SortText)
        {
            Transform t = Node(parent, name);
            var tmp = Get<TextMeshPro>(t.gameObject);
            t = tmp.transform; // adding TMP swaps in a RectTransform
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            tmp.font = font;
            tmp.fontSharedMaterial = font.material;
            tmp.text = text;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.lineSpacing = lineSpacing;
            tmp.enableAutoSizing = false;
            tmp.richText = true;
            tmp.isOrthographic = false;
            // Cap height (m) = fontSize * 0.1 * scale * capLine / pointSize for 3D TMP.
            var face = font.faceInfo;
            tmp.fontSize = cap / (0.1f * face.scale * face.capLine / face.pointSize);
            tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tmp.rectTransform.sizeDelta = new Vector2(width, wrap ? 0.08f : 0.03f);
            if (maxWidth > 0f && !wrap)
            {
                float w = tmp.GetPreferredValues(text).x;
                if (w > maxWidth)
                {
                    tmp.fontSize *= maxWidth / w;
                }
            }

            Vector2 center = pos;
            if (wrap)
            {
                // pos is the top-left corner for wrapped bodies.
                tmp.rectTransform.pivot = new Vector2(0f, 1f);
            }

            Pose(t, new Vector3(center.x, center.y, TextZ));
            var mr = t.GetComponent<MeshRenderer>();
            mr.sortingOrder = sort;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return tmp;
        }

        static void BuildHeader(Transform content, Mats mats)
        {
            Transform header = Node(content, "Header");
            Pose(header, Vector3.zero);
            Quad(header, "Title Stars", mats["TitleStars"], new Vector2(0f, 0.203f), new Vector2(0.44f, 0.086f), SortInk);
            var title = Text(header, "Title", Title, FontTitle, 0.034f, InkText, TextAlignmentOptions.Baseline, new Vector2(0f, 0.176f), 0.44f, maxWidth: 0.40f);
            title.fontSharedMaterial = mats.TitleText;
            Quad(header, "Tagline Ribbon", mats["Ribbon"], new Vector2(0f, 0.146f), new Vector2(0.30f, 0.0375f), SortInk);
            Text(header, "Tagline", Tagline, FontHand, 0.0105f, InkText, TextAlignmentOptions.Baseline, new Vector2(0f, 0.1415f), 0.3f, maxWidth: 0.22f);
            Quad(header, "Hands Icon", mats["Hands"], new Vector2(-0.180f, 0.1135f), new Vector2(0.054f, 0.0338f), SortInk);
            Text(header, "Intro", Intro, FontBody, 0.0068f, InkBody, TextAlignmentOptions.BaselineLeft, new Vector2(-0.150f + 0.18f, 0.1075f), 0.36f, maxWidth: 0.355f);
            Quad(header, "Divider Top", mats["Divider"], new Vector2(0f, 0.090f), new Vector2(0.40f, 0.0219f), SortInk);
            TextMeshPro heading = Text(header, "Rules Heading", RulesHeading, FontHand, 0.0155f, InkText, TextAlignmentOptions.Baseline, new Vector2(0f, 0.0605f), 0.3f, style: FontStyles.Bold);
            float hw = heading.GetPreferredValues(RulesHeading).x * 0.5f;
            Quad(header, "Flourish Left", mats["Flourish"], new Vector2(-hw - 0.042f, 0.0665f), new Vector2(0.07f, 0.0153f), SortInk);
            Quad(header, "Flourish Right", mats["Flourish"], new Vector2(hw + 0.042f, 0.0665f), new Vector2(0.07f, 0.0153f), SortInk, flipX: true);
        }

        static void BuildRules(Transform content, Mats mats)
        {
            Transform rules = Node(content, "Rules");
            Pose(rules, Vector3.zero);
            float[] headY = { 0.031f, -0.022f, -0.088f };
            string[] illus = { "Controllers", "WM", "SketchGlobe" };
            Vector2[] illusPos = { new Vector2(IllusX, 0.022f), new Vector2(IllusX, -0.036f), new Vector2(IllusX, -0.106f) };
            Vector2[] illusSize = { new Vector2(0.115f, 0.0629f), new Vector2(0.118f, 0.0692f), new Vector2(0.118f, 0.0645f) };
            for (int i = 0; i < 3; i++)
            {
                Transform rule = Node(rules, "Rule " + (i + 1));
                Pose(rule, Vector3.zero);
                float y = headY[i];
                Quad(rule, "Number Oval", mats["NumberOval" + (i + 1)], new Vector2(-0.186f, y + 0.0045f), new Vector2(0.032f, 0.028f), SortInk);
                Text(rule, "Number", (i + 1).ToString(), FontHand, 0.0125f, InkText, TextAlignmentOptions.Baseline, new Vector2(-0.186f, y - 0.0015f), 0.03f, style: FontStyles.Bold);
                Text(rule, "Heading", RuleHeads[i], FontHand, 0.0118f, InkText, TextAlignmentOptions.BaselineLeft, new Vector2(LeftCol + 0.15f, y), 0.30f, style: FontStyles.Bold, maxWidth: i == 2 ? 0.30f : 0.26f);
                Text(rule, "Body", RuleBodies[i], FontBody, 0.0064f, InkBody, TextAlignmentOptions.TopLeft, new Vector2(LeftCol + 0.002f, y - 0.0065f), RuleTextWidth, wrap: true, lineSpacing: -12f);
                Quad(rule, "Illustration", mats[illus[i]], illusPos[i], illusSize[i], SortInk);
            }

            Quad(rules, "Separator 1", mats["RuleSeparator"], new Vector2(0f, 0.0035f), new Vector2(0.40f, 0.0078f), SortInk);
            Quad(rules, "Separator 2", mats["RuleSeparator"], new Vector2(0f, -0.0685f), new Vector2(0.40f, 0.0078f), SortInk);
            Quad(rules, "Divider Bottom", mats["Divider"], new Vector2(0f, -0.1405f), new Vector2(0.40f, 0.0219f), SortInk);
        }

        static TextMeshPro[] BuildAgreement(Transform content, Mats mats)
        {
            Transform line = Node(content, "Agreement");
            Pose(line, new Vector3(0f, -0.1605f, 0f));
            var lead = Text(line, "Lead", "I,", FontBody, 0.0072f, InkBody, TextAlignmentOptions.Baseline, new Vector2(-0.15f, 0f), 0.02f);
            var name = Text(line, "Name", PlaceholderName, FontTitle, 0.0105f, InkText, TextAlignmentOptions.Baseline, new Vector2(-0.10f, 0.0005f), 0.08f);
            var tail = Text(line, "Tail", ", agree to follow the rules of the Celestial Workshop.", FontBody, 0.0072f, InkBody, TextAlignmentOptions.Baseline, new Vector2(0.06f, 0f), 0.3f);
            Quad(line, "Name Underline", mats["Underline"], new Vector2(-0.10f, -0.0035f), new Vector2(0.08f, 0.0075f), SortInk);
            // Lay out once here too, so the prefab looks right in the Editor.
            float gap = 0.007f;
            float wl = lead.GetPreferredValues(lead.text).x, wn = name.GetPreferredValues(name.text).x, wt = tail.GetPreferredValues(tail.text).x;
            float x = -(wl + wn + wt + 2f * gap) * 0.5f;
            void At(TextMeshPro t, float w) { t.rectTransform.sizeDelta = new Vector2(w + 0.002f, 0.03f); t.transform.localPosition = new Vector3(x + w * 0.5f, t.transform.localPosition.y, TextZ); x += w; }
            At(lead, wl); x += gap;
            float nameCenter = x + wn * 0.5f;
            At(name, wn); x += gap;
            At(tail, wt);
            Transform ul = line.Find("Name Underline");
            ul.localPosition = new Vector3(nameCenter, -0.0035f, InkZ);
            ul.localScale = new Vector3(wn + 2f * gap, 0.0075f, 1f);
            return new[] { lead, name, tail };
        }

        // Welcome line: right edge 0.102, clear of the logo's left edge (0.113).
        const float WelcomeCenterX = -0.048f;

        static SignatureReveal BuildSignature(Transform content, Mats mats, out TextMeshPro welcome, out GameObject traceHintGo)
        {
            Transform area = Node(content, "Signature Area");
            Pose(area, Vector3.zero);
            Quad(area, "Smudge", mats["SignatureSmudge"], new Vector2(0f, -0.199f), new Vector2(0.38f, 0.072f), SortInk);

            Transform sigT = Node(area, "Signature");
            Pose(sigT, new Vector3(0f, -0.2115f, TextZ - 0.0004f));
            var sig = Get<SignatureReveal>(sigT.gameObject);
            var guide = Quad(sigT, "Guide Line", mats["GuideLine"], new Vector2(0f, -0.001f), new Vector2(0.30f, 0.0059f), SortInk, 0.0006f);

            var name = Text(sigT, "Signed Name", PlaceholderName, FontTitle, 0.025f, SignatureFace, TextAlignmentOptions.Baseline, new Vector2(0f, 0.004f), 0.3f, sort: SortSignature);
            name.fontSharedMaterial = mats.SignatureText;
            name.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            SetRef(sig, "nameText", name);
            SetRef(sig, "penPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PenPrefab));
            SetRef(sig, "gripPose", AssetDatabase.LoadAssetAtPath<PenGripPose>(PenGrip));
            SetFloat(sig, "signingWidth", 0.30f);
            SetFloat(sig, "maxCapHeight", 0.025f);
            var so = new SerializedObject(sig);
            SerializedProperty hide = so.FindProperty("hideOnComplete");
            hide.arraySize = 1;
            hide.GetArrayElementAtIndex(0).objectReferenceValue = guide.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();

            Transform hint = Node(area, "Trace Hint");
            Pose(hint, Vector3.zero);
            Quad(hint, "Trace Flourish", mats["TraceFlourish"], new Vector2(0f, -0.2295f), new Vector2(0.20f, 0.015f), SortInk);
            Text(hint, "Trace Text", TraceHint, FontHand, 0.0092f, InkText, TextAlignmentOptions.Baseline, new Vector2(0f, -0.2325f), 0.12f);
            traceHintGo = hint.gameObject;

            // Shares the footer with the maker's mark (bottom right), so it sits left of
            // centre; WorkshopOnboarding shrinks long names to welcomeMaxWidth.
            welcome = Text(area, "Welcome", "Welcome to the workshop, " + PlaceholderName + "!", FontHand, 0.0118f, InkText, TextAlignmentOptions.Baseline, new Vector2(WelcomeCenterX, -0.2335f), 0.30f, style: FontStyles.Bold);
            welcome.gameObject.SetActive(false);
            return sig;
        }

        // ---- Serialized helpers ----

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"NoticeboardBuilder: no field {field} on {target.GetType().Name}");
                return;
            }

            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetVector2(Object target, string field, Vector2 value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).vector2Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"NoticeboardBuilder: no field {field} on {target.GetType().Name}");
                return;
            }

            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
