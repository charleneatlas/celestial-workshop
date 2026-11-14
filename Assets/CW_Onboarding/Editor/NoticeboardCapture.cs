using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CW.Onboarding.EditorTools
{
    /// <summary>
    /// Renders the noticeboard prefab from a few viewpoints into PNGs (RGBA,
    /// transparent background for compositing over a room or black), without
    /// touching the open scenes: an additive temporary scene, layer 31 only,
    /// 2x supersampling, everything destroyed afterwards.
    /// </summary>
    public static class NoticeboardCapture
    {
        const int Layer = 31;

        public static string Capture(string outDir, string tag, string shots, int width, float revealFraction)
        {
            return Capture(outDir, tag, shots, width, revealFraction, false);
        }

        /// <summary>
        /// fx: also stages the effects as they look mid-signature: a star trail
        /// stroked along the revealed part of the name and the ghost hand posed
        /// at the reveal edge (their runtime code, stepped by hand).
        /// </summary>
        public static string Capture(string outDir, string tag, string shots, int width, float revealFraction, bool fx)
        {
            Directory.CreateDirectory(outDir);
            var written = new List<string>();
            Scene active = SceneManager.GetActiveScene();
            if (active.isDirty || string.IsNullOrEmpty(active.path))
            {
                // Unity refuses an additive scene next to an untitled one; dirty is fine.
            }

            Scene temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject instance = null;
            Camera cam = null;
            RenderTexture big = null, small = null;
            Texture2D read = null;
            RenderTexture prevActive = RenderTexture.active;
            var tempMats = new List<Material>();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CW_Onboarding/WorkshopNoticeboard.prefab");
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, temp);
                instance.transform.SetPositionAndRotation(new Vector3(0, 0, 0), Quaternion.identity);
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer = Layer;
                }

                foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.ForceMeshUpdate(true, true);
                }

                // Welcome state: the trace hint gone, the welcome line shown.
                if (ShowWelcome)
                {
                    foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Trace Hint")
                        {
                            t.gameObject.SetActive(false);
                        }
                        else if (t.name == "Welcome" && t.TryGetComponent(out TMP_Text w))
                        {
                            t.gameObject.SetActive(true);
                            Color c = w.color;
                            c.a = 1f;
                            w.color = c;
                            w.ForceMeshUpdate(true, true);
                        }
                    }
                }

                // Partial reveal preview on the signature (edit mode has no runtime).
                var sig = instance.GetComponentInChildren<SignatureReveal>(true);
                if (sig != null && revealFraction < 1f)
                {
                    var name = sig.GetComponentInChildren<TextMeshPro>(true);
                    var m = new Material(name.fontSharedMaterial);
                    tempMats.Add(m);
                    m.EnableKeyword("UNITY_UI_CLIP_RECT");
                    name.fontSharedMaterial = m;
                    name.ForceMeshUpdate();
                    Bounds b = name.textBounds;
                    float edge = Mathf.Lerp(b.min.x, b.max.x, revealFraction);
                    m.SetVector("_ClipRect", new Vector4(b.min.x - 5f, b.min.y - 5f, edge, b.max.y + 5f));
                    m.SetFloat("_MaskSoftnessX", 0.6f / Mathf.Max(1e-5f, name.transform.lossyScale.x) * 0.01f);
                    m.SetFloat("_MaskSoftnessY", 0f);
                }

                if (fx && sig != null)
                {
                    StageEffects(temp, instance, sig, revealFraction);
                    foreach (GameObject go in temp.GetRootGameObjects())
                    {
                        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                        {
                            t.gameObject.layer = Layer;
                        }
                    }
                }

                Shader.SetGlobalFloat("_CW_BoardFadeOut", 0f);

                var camGo = new GameObject("Capture Camera") { layer = Layer };
                SceneManager.MoveGameObjectToScene(camGo, temp);
                cam = camGo.AddComponent<Camera>();
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 10f;
                cam.enabled = false;

                foreach (string shot in shots.Split(','))
                {
                    int w = width, h = width;
                    Vector3 eye;
                    float fov;
                    switch (shot.Trim())
                    {
                        case "front":   // the player's view at 0.55 m, ~58 deg design area
                            eye = new Vector3(0f, 0.12f, -0.55f); fov = 58f; h = Mathf.RoundToInt(width * 1.0f); break;
                        case "hero":    // board-filling straight view like the concept
                            eye = new Vector3(0f, 0f, -0.62f); fov = 50f; h = Mathf.RoundToInt(width * 1.1f); break;
                        case "angle":   // three-quarter, to show depth and nails
                            eye = new Vector3(0.32f, 0.10f, -0.45f); fov = 50f; break;
                        case "rules":   // close-up of the rules
                            eye = new Vector3(0.0f, 0.0f, -0.30f); fov = 34f; break;
                        case "player":  // the player's eye, looking at the signature
                            eye = new Vector3(0f, 0.12f, -0.55f); fov = 40f; break;
                        case "hand":    // wide three-quarter view of the signing area (ghost hand)
                            eye = new Vector3(0.30f, -0.12f, -0.42f); fov = 70f; break;
                        case "sign":    // close-up of the signature area
                            eye = new Vector3(0f, -0.17f, -0.30f); fov = 34f; h = Mathf.RoundToInt(width * 0.6f); break;
                        default:
                            continue;
                    }

                    Vector3 target = shot.Trim() == "player" ? new Vector3(0f, -0.17f, 0f) : shot.Trim() == "hand" ? new Vector3(0.05f, -0.26f, -0.06f) : shot.Trim() == "sign" ? new Vector3(0f, -0.2f, 0f) : (shot.Trim() == "rules" ? new Vector3(0f, -0.02f, 0f) : Vector3.zero);
                    cam.transform.position = eye;
                    cam.transform.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
                    cam.fieldOfView = fov;
                    cam.aspect = w / (float)h;

                    big = new RenderTexture(w * 2, h * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
                    small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    big.Create();
                    small.Create();
                    cam.targetTexture = big;
                    var request = new RenderPipeline.StandardRequest { destination = big };
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                    {
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    }
                    else
                    {
                        cam.Render();
                    }

                    Graphics.Blit(big, small);
                    RenderTexture.active = small;
                    read = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                    read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    read.Apply();
                    string path = Path.Combine(outDir, $"{tag}_{shot.Trim()}.png");
                    File.WriteAllBytes(path, read.EncodeToPNG());
                    written.Add(path);
                    RenderTexture.active = prevActive;
                    cam.targetTexture = null;
                    Object.DestroyImmediate(read);
                    read = null;
                    big.Release();
                    small.Release();
                    Object.DestroyImmediate(big);
                    Object.DestroyImmediate(small);
                    big = small = null;
                }
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (cam != null) cam.targetTexture = null;
                if (read != null) Object.DestroyImmediate(read);
                if (big != null) { big.Release(); Object.DestroyImmediate(big); }
                if (small != null) { small.Release(); Object.DestroyImmediate(small); }
                foreach (Material m in tempMats) Object.DestroyImmediate(m);
                EditorSceneManager.CloseScene(temp, true);
            }

            return string.Join("\n", written);
        }

        /// <summary>Optional overrides for trying ghost poses in captures.</summary>
        /// <summary>Render the post-signing state (welcome line instead of the trace hint).</summary>
        public static bool ShowWelcome;

        public static Vector3? GhostAxis, GhostSide;
        public static bool GhostOpaque;

        static void StageEffects(Scene temp, GameObject instance, SignatureReveal sig, float reveal)
        {
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(temp);
            try
            {
                const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                var name = sig.GetComponentInChildren<TextMeshPro>(true);
                Bounds b = name.textBounds;
                Vector3 a = name.transform.TransformPoint(new Vector3(b.min.x, b.center.y - b.extents.y * 0.1f, 0f));
                Vector3 e = name.transform.TransformPoint(new Vector3(Mathf.Lerp(b.min.x, b.max.x, reveal), b.center.y - b.extents.y * 0.1f, 0f));

                var trail = instance.transform.Find("Board/Star Trail").GetComponent<StarTrail>();
                trail.BeginStroke(a);
                const int steps = 40;
                var systems = trail.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 1; i <= steps; i++)
                {
                    trail.StrokeTo(Vector3.Lerp(a, e, i / (float)steps));
                    foreach (ParticleSystem ps in systems)
                    {
                        ps.Simulate(0.025f, false, false, false);
                    }
                }

                typeof(StarTrail).GetMethod("LateUpdate", Any).Invoke(trail, null);
                for (int i = 0; i < 20; i++)
                {
                    typeof(StarTrail).GetMethod("LateUpdate", Any).Invoke(trail, null);
                }

                var ghost = instance.GetComponentInChildren<GhostHandDemo>(true);
                if (GhostAxis.HasValue || GhostSide.HasValue)
                {
                    var so = new SerializedObject(ghost);
                    if (GhostAxis.HasValue) so.FindProperty("penAxis").vector3Value = GhostAxis.Value;
                    if (GhostSide.HasValue) so.FindProperty("handSide").vector3Value = GhostSide.Value;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                ghost.PoseAt(e + sig.transform.up * 0.004f);
                typeof(GhostHandDemo).GetMethod("SetVisible", Any).Invoke(ghost, new object[] { true });
                typeof(GhostHandDemo).GetMethod("SetAlpha", Any).Invoke(ghost, new object[] { 0.55f });
                if (GhostOpaque)
                {
                    var mat = (Material)typeof(GhostHandDemo).GetField("materialInstance", Any).GetValue(ghost);
                    mat.SetColor("_Color", new Color(0.9f, 0.75f, 0.6f, 1f));
                    mat.SetFloat("_Fade", 1f);
                }
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
            }
        }
    }
}
