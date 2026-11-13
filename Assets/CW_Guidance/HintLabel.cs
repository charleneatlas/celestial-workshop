using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CW.Guidance
{
    /// <summary>
    /// One short world-space hint (TextMeshPro 3D, no canvas). Sits near its
    /// target but is pulled inside a cone around the viewer's gaze so it stays
    /// visible on a narrow field of view, yaws smoothly to face the viewer, and
    /// fades in/out. PlayCompleted turns it the success colour with a small pop
    /// and fades it away.
    ///
    /// Size: 14 mm cap height at 0.65 m (about 1.23 degrees), scaled with
    /// distance to keep the same angular size, clamped to 10-20 mm. Handwritten
    /// Caveat has thin strokes and a small x-height, so it gets more than the
    /// ~1 degree usually treated as the floor for comfortable VR reading, while
    /// staying small enough that an 8-word hint wraps to two lines inside a
    /// 16-degree width (the glasses' narrow view leaves little room).
    ///
    /// Runs in Play mode only; the text object is created on first use.
    /// </summary>
    [DisallowMultipleComponent]
    public class HintLabel : MonoBehaviour
    {
        private enum Phase { Hidden, Showing, Completing, Hiding }

        [Header("Look")]
        [Tooltip("Handwritten font, e.g. Assets/CW_Art/Fonts/Caveat SDF. Empty uses TMP's default font.")]
        public TMP_FontAsset font;
        [Tooltip("Soft light blue, matching the VolumePinchRotation guides.")]
        public Color textColor = new Color(0.75f, 0.9f, 1f, 1f);
        [Tooltip("Colour the text turns when its step is completed.")]
        public Color completedColor = new Color(1f, 0.9f, 0.55f, 1f);
        [Range(-0.5f, 0.5f)] public float faceDilate = 0.15f;
        [Tooltip("Dark soft halo behind the letters (TMP underlay) for contrast over bright passthrough.")]
        public bool backing = true;
        public Color backingColor = new Color(0.01f, 0.02f, 0.05f, 0.75f);
        [Range(0f, 1f)] public float backingSize = 0.6f;

        [Header("Size (metres / degrees)")]
        [Tooltip("Cap height at referenceDistance. 14 mm at 0.65 m is about 1.23 degrees.")]
        public float capHeight = 0.014f;
        public float referenceDistance = 0.65f;
        public float minCapHeight = 0.010f;
        public float maxCapHeight = 0.020f;
        [Tooltip("Text wraps beyond this angular width.")]
        public float maxWidthDegrees = 16f;

        [Header("Placement")]
        [Tooltip("The label centre is kept within this angle of the viewer's forward direction.")]
        public float maxGazeAngle = 12f;
        public float minDistance = 0.4f;
        public float maxDistance = 1.0f;
        [Tooltip("With no target, the label floats this far ahead of the viewer...")]
        public float fallbackDistance = 0.65f;
        [Tooltip("...and this many degrees below the centre of view.")]
        public float fallbackPitchDown = 8f;
        public float followSmoothTime = 0.2f;
        public float yawSharpness = 8f;

        [Header("Timing (seconds)")]
        public float fadeInSeconds = 0.35f;
        public float fadeOutSeconds = 0.3f;
        [Tooltip("How long the completed (success-coloured) text stays before fading.")]
        public float completedHoldSeconds = 0.6f;
        public float completedPopScale = 1.12f;

        [Tooltip("Head / camera. Empty uses Camera.main.")]
        public Transform viewer;

        private TextMeshPro text;
        private Material material;
        private float capInText = 1f;
        private float lastWidth = -1f;

        private Phase phase = Phase.Hidden;
        private float phaseTime;
        private float alpha;
        private bool snap;
        private Vector3 velocity;

        private Transform target;
        private Vector3 offset;
        private readonly List<Renderer> targetRenderers = new List<Renderer>();

        public bool IsVisible => phase != Phase.Hidden;
        /// <summary>True while the success acknowledgement is playing.</summary>
        public bool IsCompleting => phase == Phase.Completing;
        public string Text => text != null ? text.text : string.Empty;

        /// <summary>
        /// Shows (or replaces) the hint. Offset is from the top-centre of the
        /// target's bounds in viewer-relative axes: x = the viewer's horizontal
        /// right, y = world up, z = toward the viewer (metres). With no target
        /// the label floats in front of the viewer and the offset is ignored.
        /// </summary>
        public void Show(string message, Transform newTarget, Vector3 newOffset, Transform newViewer = null)
        {
            EnsureText();
            if (newViewer != null)
            {
                viewer = newViewer;
            }

            bool wasHidden = phase == Phase.Hidden;
            target = newTarget;
            offset = newOffset;
            GuidanceBounds.Collect(target, targetRenderers);

            // Same text already showing (e.g. a retarget): keep it, just move.
            bool sameText = phase == Phase.Showing && text.text == message;
            text.text = message;
            if (!sameText)
            {
                alpha = 0f;
            }

            phase = Phase.Showing;
            phaseTime = 0f;
            snap = wasHidden;
            text.gameObject.SetActive(true);
            Apply(textColor, alpha);
        }

        /// <summary>Fades the hint out.</summary>
        public void Hide()
        {
            if (phase == Phase.Hidden || phase == Phase.Hiding)
            {
                return;
            }

            phase = Phase.Hiding;
            phaseTime = 0f;
        }

        /// <summary>Hides at once, without a fade.</summary>
        public void HideImmediate()
        {
            phase = Phase.Hidden;
            alpha = 0f;
            target = null;
            targetRenderers.Clear();
            if (text != null)
            {
                text.gameObject.SetActive(false);
            }
        }

        /// <summary>Success acknowledgement: colour change, small pop, hold, fade.</summary>
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
            if (material != null)
            {
                Destroy(material);
            }
        }

        private void LateUpdate()
        {
            if (phase == Phase.Hidden || text == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            phaseTime += dt;
            Color color = textColor;
            float pop = 1f;

            switch (phase)
            {
                case Phase.Showing:
                    alpha = Mathf.MoveTowards(alpha, 1f, dt / Mathf.Max(0.01f, fadeInSeconds));
                    break;

                case Phase.Completing:
                    color = Color.Lerp(textColor, completedColor, Mathf.Clamp01(phaseTime / 0.15f));
                    pop = 1f + (completedPopScale - 1f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(phaseTime / 0.35f));
                    alpha = Mathf.Max(alpha, Mathf.Clamp01(phaseTime / 0.1f));
                    if (phaseTime > completedHoldSeconds)
                    {
                        alpha = 1f - (phaseTime - completedHoldSeconds) / Mathf.Max(0.01f, fadeOutSeconds);
                    }
                    break;

                case Phase.Hiding:
                    alpha = Mathf.MoveTowards(alpha, 0f, dt / Mathf.Max(0.01f, fadeOutSeconds));
                    break;
            }

            if (phase != Phase.Showing && alpha <= 0f)
            {
                HideImmediate();
                return;
            }

            Transform eye = viewer != null ? viewer : (Camera.main != null ? Camera.main.transform : null);
            if (eye != null)
            {
                Place(eye, dt, pop);
            }

            Apply(color, alpha);
        }

        private void Place(Transform eye, float dt, float pop)
        {
            Vector3 eyePosition = eye.position;
            Vector3 forward = eye.forward;
            Vector3 flatForward = Flat(forward, eye.up);

            Vector3 anchor;
            if (target != null)
            {
                Bounds bounds = GuidanceBounds.Compute(target, targetRenderers);
                Vector3 top = bounds.center + Vector3.up * bounds.extents.y;
                Vector3 toward = Flat(eyePosition - top, -flatForward);
                Vector3 right = Vector3.Cross(Vector3.up, -toward).normalized;
                anchor = top + right * offset.x + Vector3.up * offset.y + toward * offset.z;
            }
            else
            {
                Vector3 down = Quaternion.AngleAxis(fallbackPitchDown, eye.right) * forward;
                anchor = eyePosition + down * fallbackDistance;
            }

            // Keep it at a readable distance and inside the gaze cone.
            Vector3 offsetFromEye = anchor - eyePosition;
            float distance = offsetFromEye.magnitude;
            Vector3 direction = distance > 1e-4f ? offsetFromEye / distance : forward;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            if (Vector3.Angle(forward, direction) > maxGazeAngle)
            {
                direction = Vector3.RotateTowards(forward, direction, maxGazeAngle * Mathf.Deg2Rad, 0f);
            }

            Vector3 desired = eyePosition + direction * distance;
            Vector3 lookFlat = Flat(desired - eyePosition, flatForward);
            Quaternion desiredRotation = Quaternion.LookRotation(lookFlat, Vector3.up);

            if (snap)
            {
                transform.SetPositionAndRotation(desired, desiredRotation);
                velocity = Vector3.zero;
                snap = false;
            }
            else
            {
                Vector3 position = Vector3.SmoothDamp(transform.position, desired, ref velocity, followSmoothTime, Mathf.Infinity, dt);
                float t = 1f - Mathf.Exp(-yawSharpness * dt);
                transform.SetPositionAndRotation(position, Quaternion.Slerp(transform.rotation, desiredRotation, t));
            }

            // Constant angular size, clamped; width wraps at maxWidthDegrees.
            float viewDistance = Vector3.Distance(eyePosition, transform.position);
            float cap = Mathf.Clamp(capHeight * viewDistance / Mathf.Max(0.01f, referenceDistance), minCapHeight, maxCapHeight);
            float worldPerUnit = cap / Mathf.Max(1e-4f, capInText);
            float parentScale = Mathf.Max(1e-4f, transform.lossyScale.x);
            text.transform.localScale = Vector3.one * (worldPerUnit * pop / parentScale);

            float worldWidth = 2f * viewDistance * Mathf.Tan(0.5f * maxWidthDegrees * Mathf.Deg2Rad);
            float width = worldWidth / worldPerUnit;
            if (lastWidth < 0f || Mathf.Abs(width - lastWidth) > lastWidth * 0.02f)
            {
                lastWidth = width;
                text.rectTransform.sizeDelta = new Vector2(width, 20f);
            }
        }

        private void Apply(Color color, float a)
        {
            text.color = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(a));
            if (material != null && backing)
            {
                Color under = backingColor;
                under.a *= Mathf.Clamp01(a);
                material.SetColor(ShaderUtilities.ID_UnderlayColor, under);
            }
        }

        // Horizontal part of v, normalized; fallback when v is (nearly) vertical.
        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 1e-8f)
            {
                v = fallback;
                v.y = 0f;
            }

            return v.sqrMagnitude < 1e-8f ? Vector3.forward : v.normalized;
        }

        private void EnsureText()
        {
            if (text != null)
            {
                return;
            }

            var go = new GameObject("HintText");
            go.transform.SetParent(transform, false);
            text = go.AddComponent<TextMeshPro>();

            TMP_FontAsset useFont = font != null ? font : TMP_Settings.defaultFontAsset;
            if (useFont != null)
            {
                text.font = useFont;
                if (useFont.material != null)
                {
                    // Our own copy: the font asset's material stays untouched.
                    material = new Material(useFont.material) { name = useFont.name + " (hint)", hideFlags = HideFlags.DontSave };
                    material.SetFloat(ShaderUtilities.ID_FaceDilate, faceDilate);
                    if (backing)
                    {
                        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                        material.SetColor(ShaderUtilities.ID_UnderlayColor, backingColor);
                        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
                        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, backingSize);
                        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Lerp(0.3f, 0.8f, backingSize));
                    }

                    text.fontSharedMaterial = material;
                }
            }

            text.extraPadding = true;
            text.fontSize = 10f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = false;
            text.rectTransform.sizeDelta = new Vector2(100f, 20f);
            text.color = new Color(textColor.r, textColor.g, textColor.b, 0f);
            if (go.TryGetComponent(out MeshRenderer textRenderer))
            {
                textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                textRenderer.receiveShadows = false;
            }

            // Measure an "H" so capHeight means real cap height in metres.
            Vector2 h = text.GetPreferredValues("H");
            if (text.font != null)
            {
                var face = text.font.faceInfo;
                capInText = h.y * (face.capLine / Mathf.Max(1e-4f, face.lineHeight));
            }

            if (capInText <= 0f)
            {
                capInText = 7f; // rough cap height of a size-10 font, in text units
            }

            go.SetActive(false);
        }
    }
}
