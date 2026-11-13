using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CW.Guidance
{
    /// <summary>
    /// A subtle breathing halo around ONE target: a camera-facing quad with a
    /// procedurally generated soft ring, sized from the target's renderer
    /// bounds. It is its own object (a child of this component, never of the
    /// target) with its own runtime material, so the target's materials are
    /// never touched.
    ///
    /// Blending is additive One/One with a black-outside ring texture, so the
    /// look does not depend on the transparent shader keyword variant being in
    /// the build; destination alpha is left untouched (safe over passthrough).
    /// Assign <see cref="shader"/> in the prefab (Universal Render Pipeline/Unlit)
    /// so the shader is referenced and therefore included in builds.
    /// </summary>
    [DisallowMultipleComponent]
    public class TargetPulse : MonoBehaviour
    {
        private enum Phase { Hidden, Showing, Completing, Hiding }

        [Header("Material")]
        [Tooltip("Universal Render Pipeline/Unlit (referenced here so builds include it). Fallbacks: URP Particles/Unlit, Sprites/Default.")]
        public Shader shader;
        [Tooltip("Optional authored material; a runtime copy is used and its colour driven. Its main texture is replaced by the ring.")]
        public Material materialOverride;

        [Header("Look")]
        public Color color = new Color(0.75f, 0.9f, 1f, 1f);
        [Range(0f, 2f)] public float intensity = 0.6f;
        [Tooltip("Ring radius = bounding-sphere radius x this.")]
        public float radiusPadding = 1.12f;
        public float minRadius = 0.025f;
        public float maxRadius = 0.5f;

        [Header("Breathing")]
        public float breathePeriod = 1.8f;
        [Range(0f, 0.3f)] public float breatheScale = 0.06f;
        [Tooltip("How much dimmer the ring gets at the bottom of each breath.")]
        [Range(0f, 1f)] public float breatheDim = 0.5f;

        [Header("Timing (seconds)")]
        public float fadeInSeconds = 0.3f;
        public float fadeOutSeconds = 0.25f;
        [Tooltip("Success flash: the ring brightens, expands and fades.")]
        public float completeSeconds = 0.5f;
        public float completeScale = 1.6f;

        [Tooltip("Head / camera. Empty uses Camera.main.")]
        public Transform viewer;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        // The ring peaks at this fraction of the quad's half-size.
        private const float RingAt = 0.8f;

        private GameObject ring;
        private Material material;
        private Texture2D texture;
        private Mesh mesh;
        private bool setupFailed;

        private Phase phase = Phase.Hidden;
        private float phaseTime;
        private float alpha;
        private Transform target;
        private float radiusOverride;
        private readonly List<Renderer> targetRenderers = new List<Renderer>();

        public bool IsVisible => phase != Phase.Hidden;
        public Transform Target => target;

        /// <summary>
        /// Pulses around the target (replacing any previous one). radius &gt; 0
        /// overrides the size derived from the target's bounds.
        /// </summary>
        public void Show(Transform newTarget, float radius = 0f, Transform newViewer = null)
        {
            if (newViewer != null)
            {
                viewer = newViewer;
            }

            if (newTarget == null || !EnsureRing())
            {
                Hide();
                return;
            }

            bool sameTarget = newTarget == target && phase == Phase.Showing;
            target = newTarget;
            radiusOverride = radius;
            GuidanceBounds.Collect(target, targetRenderers);
            if (!sameTarget)
            {
                alpha = phase == Phase.Hidden ? 0f : alpha;
                phaseTime = 0f;
            }

            phase = Phase.Showing;
            ring.SetActive(true);
        }

        public void Hide()
        {
            if (phase == Phase.Hidden || phase == Phase.Hiding)
            {
                return;
            }

            phase = Phase.Hiding;
        }

        public void HideImmediate()
        {
            phase = Phase.Hidden;
            alpha = 0f;
            target = null;
            targetRenderers.Clear();
            if (ring != null)
            {
                ring.SetActive(false);
            }
        }

        /// <summary>Brief success flash, then hidden.</summary>
        public void PlayCompleted()
        {
            if (phase == Phase.Hidden)
            {
                return;
            }

            phase = Phase.Completing;
            phaseTime = 0f;
        }

        private void OnDisable()
        {
            HideImmediate();
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
            if (mesh != null) Destroy(mesh);
        }

        private void LateUpdate()
        {
            if (phase == Phase.Hidden || ring == null)
            {
                return;
            }

            // Target destroyed or deactivated: let the ring go.
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                if (phase != Phase.Hiding && phase != Phase.Completing)
                {
                    phase = Phase.Hiding;
                }

                if (target == null && phase == Phase.Hiding)
                {
                    HideImmediate();
                    return;
                }
            }

            float dt = Time.deltaTime;
            phaseTime += dt;
            float scale = 1f;
            float brightness;

            if (phase == Phase.Completing)
            {
                float k = Mathf.Clamp01(phaseTime / Mathf.Max(0.01f, completeSeconds));
                float ease = 1f - (1f - k) * (1f - k);
                scale = Mathf.Lerp(1f, completeScale, ease);
                brightness = 1.6f * (1f - k) * (1f - k);
                alpha = 1f;
                if (k >= 1f)
                {
                    HideImmediate();
                    return;
                }
            }
            else
            {
                float to = phase == Phase.Showing ? 1f : 0f;
                float seconds = phase == Phase.Showing ? fadeInSeconds : fadeOutSeconds;
                alpha = Mathf.MoveTowards(alpha, to, dt / Mathf.Max(0.01f, seconds));
                if (phase == Phase.Hiding && alpha <= 0f)
                {
                    HideImmediate();
                    return;
                }

                float breath = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * phaseTime / Mathf.Max(0.1f, breathePeriod));
                scale = 1f + breatheScale * breath;
                brightness = Mathf.Lerp(1f - breatheDim, 1f, breath) * alpha;
            }

            Transform eye = viewer != null ? viewer : (Camera.main != null ? Camera.main.transform : null);
            Bounds bounds = GuidanceBounds.Compute(target, targetRenderers);
            float radius = radiusOverride > 0f
                ? radiusOverride
                : Mathf.Clamp(bounds.extents.magnitude * radiusPadding, minRadius, maxRadius);

            Transform ringTransform = ring.transform;
            ringTransform.position = bounds.center;
            if (eye != null)
            {
                Vector3 away = bounds.center - eye.position;
                if (away.sqrMagnitude > 1e-8f)
                {
                    ringTransform.rotation = Quaternion.LookRotation(away, eye.up);
                }
            }

            float halfSize = radius / RingAt * scale;
            Vector3 parentScale = transform.lossyScale;
            ringTransform.localScale = new Vector3(
                halfSize / Mathf.Max(1e-4f, Mathf.Abs(parentScale.x)),
                halfSize / Mathf.Max(1e-4f, Mathf.Abs(parentScale.y)),
                halfSize / Mathf.Max(1e-4f, Mathf.Abs(parentScale.z)));

            float b = Mathf.Max(0f, brightness) * intensity;
            Color c = new Color(color.r * b, color.g * b, color.b * b, Mathf.Clamp01(brightness));
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, c);
            if (material.HasProperty(ColorId)) material.SetColor(ColorId, c);
        }

        private bool EnsureRing()
        {
            if (ring != null)
            {
                return true;
            }

            if (setupFailed)
            {
                return false;
            }

            material = CreateMaterial();
            if (material == null)
            {
                setupFailed = true;
                Debug.LogWarning("[TargetPulse] No usable shader found; target pulse disabled.", this);
                return false;
            }

            texture = CreateRingTexture(128);
            if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, texture);
            if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, texture);

            mesh = CreateQuad();
            ring = new GameObject("PulseRing");
            ring.transform.SetParent(transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = material;
            ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            ringRenderer.lightProbeUsage = LightProbeUsage.Off;
            ringRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ring.SetActive(false);
            return true;
        }

        private Material CreateMaterial()
        {
            if (materialOverride != null)
            {
                return new Material(materialOverride) { name = materialOverride.name + " (pulse)", hideFlags = HideFlags.DontSave };
            }

            Shader s = Usable(shader)
                ?? Usable(Shader.Find("Universal Render Pipeline/Unlit"))
                ?? Usable(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            bool urp = s != null;
            if (!urp)
            {
                s = Usable(Shader.Find("Sprites/Default")) ?? Usable(Shader.Find("Unlit/Transparent"));
            }

            if (s == null)
            {
                return null;
            }

            var m = new Material(s) { name = "Guidance Pulse", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_Surface"))
            {
                // URP Unlit / Particles Unlit: transparent, additive One/One,
                // destination alpha untouched, no depth write, both faces.
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 2f); // Additive in URP's BlendMode enum
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.Zero);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.DisableKeyword("_ALPHAMODULATE_ON");
                m.DisableKeyword("_ALPHATEST_ON");
            }

            m.renderQueue = (int)RenderQueue.Transparent + 10;
            return m;
        }

        private static Shader Usable(Shader s)
        {
            return s != null && s.isSupported ? s : null;
        }

        // Soft ring at RingAt of the half-size plus a faint glow; RGB = alpha
        // (black outside), so it works additively and with alpha blending.
        private static Texture2D CreateRingTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Guidance Pulse Ring",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float line = Mathf.Exp(-Sq((r - RingAt) / 0.05f));
                    float glow = 0.3f * Mathf.Exp(-Sq((r - RingAt) / 0.16f));
                    float a = Mathf.Clamp01(line + glow) * Mathf.Clamp01((1f - r) / 0.05f);
                    byte value = (byte)Mathf.RoundToInt(a * 255f);
                    pixels[y * size + x] = new Color32(value, value, value, value);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        private static float Sq(float x) => x * x;

        // Unit quad (-1..1) in the XY plane, white vertex colours (the
        // particle shader multiplies by them).
        private static Mesh CreateQuad()
        {
            var m = new Mesh { name = "Guidance Pulse Quad", hideFlags = HideFlags.DontSave };
            m.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f),
            };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }
    }
}
