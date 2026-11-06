using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small farm diorama inside the spherical volume that stands for the Earth
/// observer. Parent it under the observer anchor (the Volume Entry Marker with
/// Orient To Observer Ray on): the anchor sits on the volume's shell where the
/// observer-to-center ray enters, its up pointing at the center. Everything
/// here is local to that anchor, so the diorama follows Observer Elevation /
/// Heading without a second placement system.
///
/// Local frame of this transform: origin on the shell, +Y toward the volume
/// center at (0, R, 0). The ground is a lens: its underside is a sphere
/// concentric with the shell (radius R - margin), its top a gentle convex cap
/// (Top Curvature Radius) that peaks Thickness above the underside. The rim is
/// cut where the lens is still Min Edge Thickness thick, so the ground is
/// inside the shell by construction. Children of Inhabitants are seated on
/// the top surface: keep their local X/Z and heading, the rest is set here.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(110)]
public sealed class ObserverDiorama : MonoBehaviour
{
    private static readonly int LightDirId = Shader.PropertyToID("_DioramaLightDir");

    [Tooltip("The spherical volume the diorama sits in (for its center and radius).")]
    [SerializeField]
    private CircularWindow volume;

    [Header("Placement on the shell")]
    [Tooltip("Slides the diorama along the inside of the shell, away from the observer ray (degrees). Stays contained at any value.")]
    [SerializeField]
    [Range(0f, 60f)]
    private float offsetAngle;

    [Tooltip("Direction of that slide around the observer ray (degrees, 0 = toward the top of the Earth view).")]
    [SerializeField]
    [Range(0f, 360f)]
    private float offsetDirection;

    [Tooltip("Turns the diorama about its own up axis (degrees).")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float spin;

    [Header("Ground")]
    [SerializeField]
    private MeshFilter ground;

    [Tooltip("Rim radius of the lens (m). Clamped where the lens would get thinner than Min Edge Thickness.")]
    [SerializeField]
    [Min(0.01f)]
    private float footprintRadius = 0.09f;

    [Tooltip("Lens thickness at its center (m).")]
    [SerializeField]
    [Min(0.002f)]
    private float thickness = 0.04f;

    [Tooltip("Radius of the convex top surface (m). Smaller = rounder hill.")]
    [SerializeField]
    [Min(0.05f)]
    private float topCurvatureRadius = 0.35f;

    [Tooltip("Gap between the lens underside and the volume shell (m).")]
    [SerializeField]
    [Min(0f)]
    private float shellMargin = 0.005f;

    [Tooltip("The rim is cut where the lens is this thick (m), so the edge isn't paper-thin.")]
    [SerializeField]
    [Min(0.001f)]
    private float minEdgeThickness = 0.004f;

    [SerializeField]
    [Range(12, 128)]
    private int radialSegments = 64;

    [SerializeField]
    [Range(2, 32)]
    private int rings = 12;

    [Header("Inhabitants")]
    [Tooltip("Direct children are seated on the ground (position on the surface, up along its normal, uniform Figure Scale).")]
    [SerializeField]
    private Transform inhabitants;

    [Tooltip("Uniform scale of every inhabitant. The figure meshes are about 1 unit tall.")]
    [SerializeField]
    [Min(0.001f)]
    private float figureScale = 0.024f;

    [Tooltip("Keep inhabitants at least this far inside the rim (m).")]
    [SerializeField]
    [Min(0f)]
    private float rimInset = 0.008f;

    [Tooltip("Stand inhabitants along the ground's normal (like a tiny planet) rather than straight along the diorama's up.")]
    [SerializeField]
    private bool alignToSurface = true;

    [Header("Lighting")]
    [Tooltip("Direction toward the fake sun, in the diorama's local space (DioramaToon shader).")]
    [SerializeField]
    private Vector3 lightDirection = new Vector3(0.45f, 1f, 0.3f);

    private Mesh groundMesh;
    private bool dirty = true;
    private float builtShellRadius = -1f;

    /// <summary>World-space shell radius of the volume.</summary>
    public float ShellRadius => volume != null
        ? volume.Radius * volume.transform.lossyScale.x
        : 0.22f;

    /// <summary>Lens rim radius actually used (local units), after the edge-thickness clamp.</summary>
    public float RimRadius { get; private set; }

    private void OnEnable()
    {
        dirty = true;
        Refresh();
    }

    private void OnValidate()
    {
        // Mesh assignment isn't allowed during OnValidate; rebuild next update.
        dirty = true;
    }

    private void OnDestroy()
    {
        DestroyGroundMesh();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void Refresh()
    {
        float shell = LocalShellRadius();
        if (dirty || !Mathf.Approximately(shell, builtShellRadius))
        {
            dirty = false;
            builtShellRadius = shell;
            ApplyPlacement(shell);
            BuildGround(shell);
        }

        SeatInhabitants(shell);
        Shader.SetGlobalVector(LightDirId, transform.TransformDirection(lightDirection).normalized);
    }

    // Shell radius in the parent's (anchor's) units.
    private float LocalShellRadius()
    {
        float parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
        return ShellRadius / Mathf.Max(1e-6f, parentScale);
    }

    private void ApplyPlacement(float shell)
    {
        Vector3 center = Vector3.up * shell;
        Vector3 slideAxis = Quaternion.AngleAxis(offsetDirection, Vector3.up) * Vector3.right;
        Quaternion slide = Quaternion.AngleAxis(offsetAngle, slideAxis);
        transform.localPosition = center + slide * (Vector3.down * shell);
        transform.localRotation = slide * Quaternion.AngleAxis(spin, Vector3.up);
        transform.localScale = Vector3.one;
    }

    // ---- Lens geometry (this transform's local frame, shell center at (0, R, 0)) ----

    private float UndersideRadius(float shell) => Mathf.Max(0.001f, shell - shellMargin);

    private float TopCenterY() => shellMargin + thickness - topCurvatureRadius;

    private float TopHeight(float r) =>
        TopCenterY() + Mathf.Sqrt(Mathf.Max(0f, topCurvatureRadius * topCurvatureRadius - r * r));

    private float BottomHeight(float r, float shell)
    {
        float under = UndersideRadius(shell);
        return shell - Mathf.Sqrt(Mathf.Max(0f, under * under - r * r));
    }

    private Vector3 TopNormal(float x, float z) =>
        new Vector3(x, TopHeight(Mathf.Sqrt(x * x + z * z)) - TopCenterY(), z).normalized;

    private float SolveRimRadius(float shell)
    {
        float limit = Mathf.Min(UndersideRadius(shell), topCurvatureRadius) * 0.999f;
        float wanted = Mathf.Min(footprintRadius, limit);
        float Gap(float r) => TopHeight(r) - BottomHeight(r, shell) - minEdgeThickness;
        if (Gap(wanted) >= 0f)
        {
            return wanted;
        }

        if (Gap(0f) <= 0f)
        {
            return 0.01f;
        }

        float lo = 0f, hi = wanted;
        for (int i = 0; i < 40; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Gap(mid) >= 0f)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void BuildGround(float shell)
    {
        RimRadius = SolveRimRadius(shell);
        if (ground == null)
        {
            return;
        }

        if (groundMesh == null)
        {
            groundMesh = new Mesh { name = "Diorama Ground (generated)", hideFlags = HideFlags.DontSave };
        }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var top = new List<int>();
        var soil = new List<int>();
        int n = radialSegments;

        // Top: center + rings, smooth normals of the convex cap.
        int topStart = vertices.Count;
        vertices.Add(new Vector3(0f, TopHeight(0f), 0f));
        normals.Add(Vector3.up);
        for (int ring = 1; ring <= rings; ring++)
        {
            float r = RimRadius * ring / rings;
            for (int s = 0; s < n; s++)
            {
                float a = 2f * Mathf.PI * s / n;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                vertices.Add(new Vector3(x, TopHeight(r), z));
                normals.Add(TopNormal(x, z));
            }
        }

        AddDisc(top, topStart, n, rings, faceUp: true);

        // Underside: concentric with the shell, normals away from its center.
        int bottomStart = vertices.Count;
        Vector3 center = Vector3.up * shell;
        vertices.Add(new Vector3(0f, BottomHeight(0f, shell), 0f));
        normals.Add(Vector3.down);
        for (int ring = 1; ring <= rings; ring++)
        {
            float r = RimRadius * ring / rings;
            for (int s = 0; s < n; s++)
            {
                float a = 2f * Mathf.PI * s / n;
                var p = new Vector3(Mathf.Cos(a) * r, BottomHeight(r, shell), Mathf.Sin(a) * r);
                vertices.Add(p);
                normals.Add((p - center).normalized);
            }
        }

        AddDisc(soil, bottomStart, n, rings, faceUp: false);

        // Rim wall between the two edges.
        int wallStart = vertices.Count;
        float yTop = TopHeight(RimRadius), yBottom = BottomHeight(RimRadius, shell);
        for (int s = 0; s < n; s++)
        {
            float a = 2f * Mathf.PI * s / n;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices.Add(radial * RimRadius + Vector3.up * yTop);
            vertices.Add(radial * RimRadius + Vector3.up * yBottom);
            normals.Add(radial);
            normals.Add(radial);
        }

        for (int s = 0; s < n; s++)
        {
            int a0 = wallStart + 2 * s, b0 = a0 + 1;
            int a1 = wallStart + 2 * ((s + 1) % n), b1 = a1 + 1;
            soil.AddRange(new[] { a0, a1, b0, b0, a1, b1 });
        }

        groundMesh.Clear();
        groundMesh.SetVertices(vertices);
        groundMesh.SetNormals(normals);
        groundMesh.subMeshCount = 2;
        groundMesh.SetTriangles(top, 0);
        groundMesh.SetTriangles(soil, 1);
        groundMesh.RecalculateBounds();
        ground.sharedMesh = groundMesh;
        ground.transform.localPosition = Vector3.zero;
        ground.transform.localRotation = Quaternion.identity;
        ground.transform.localScale = Vector3.one;
    }

    // Center fan + quad rings. Rings are laid out counterclockwise seen from
    // +Y, so a clockwise (front-facing in Unity) triangle seen from above
    // goes center -> next -> current.
    private static void AddDisc(List<int> triangles, int start, int n, int ringCount, bool faceUp)
    {
        void Tri(int a, int b, int c)
        {
            if (faceUp)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
            else
            {
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
            }
        }

        for (int s = 0; s < n; s++)
        {
            Tri(start, start + 1 + (s + 1) % n, start + 1 + s);
        }

        for (int ring = 1; ring < ringCount; ring++)
        {
            int inner = start + 1 + (ring - 1) * n;
            int outer = inner + n;
            for (int s = 0; s < n; s++)
            {
                int s1 = (s + 1) % n;
                Tri(inner + s, inner + s1, outer + s);
                Tri(outer + s, inner + s1, outer + s1);
            }
        }
    }

    private void DestroyGroundMesh()
    {
        if (groundMesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(groundMesh);
        }
        else
        {
            DestroyImmediate(groundMesh);
        }

        groundMesh = null;
    }

    // ---- Inhabitants ----

    private void SeatInhabitants(float shell)
    {
        if (inhabitants == null)
        {
            return;
        }

        if (inhabitants.parent == transform)
        {
            inhabitants.localPosition = Vector3.zero;
            inhabitants.localRotation = Quaternion.identity;
            inhabitants.localScale = Vector3.one;
        }

        float maxRadius = Mathf.Max(0f, RimRadius - rimInset);
        Vector3 scale = Vector3.one * figureScale;
        for (int i = 0; i < inhabitants.childCount; i++)
        {
            Transform figure = inhabitants.GetChild(i);
            Vector3 p = figure.localPosition;
            var flat = new Vector2(p.x, p.z);
            if (flat.magnitude > maxRadius)
            {
                flat = flat.normalized * maxRadius;
            }

            float r = flat.magnitude;
            var seat = new Vector3(flat.x, TopHeight(r), flat.y);
            Vector3 up = alignToSurface ? TopNormal(flat.x, flat.y) : Vector3.up;
            Vector3 heading = Vector3.ProjectOnPlane(figure.localRotation * Vector3.forward, up);
            if (heading.sqrMagnitude < 1e-8f)
            {
                heading = Vector3.ProjectOnPlane(Vector3.forward, up);
            }

            Quaternion rotation = Quaternion.LookRotation(heading.normalized, up);

            if ((figure.localPosition - seat).sqrMagnitude > 1e-12f)
            {
                figure.localPosition = seat;
            }

            if (Quaternion.Angle(figure.localRotation, rotation) > 1e-3f)
            {
                figure.localRotation = rotation;
            }

            if (figure.localScale != scale)
            {
                figure.localScale = scale;
            }
        }
    }

    // ---- Checks ----

    /// <summary>
    /// Largest distance of any diorama vertex from the volume center, and the
    /// shell radius, both in world units. Inside when maxDistance &lt; shell.
    /// </summary>
    public void MeasureContainment(out float maxDistance, out float shell, out string farthest)
    {
        maxDistance = 0f;
        shell = ShellRadius;
        farthest = "";
        if (volume == null)
        {
            return;
        }

        Vector3 center = volume.transform.position;
        var vertices = new List<Vector3>();
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            filter.sharedMesh.GetVertices(vertices);
            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            foreach (Vector3 v in vertices)
            {
                float d = Vector3.Distance(center, toWorld.MultiplyPoint3x4(v));
                if (d > maxDistance)
                {
                    maxDistance = d;
                    farthest = filter.name == "Model" && filter.transform.parent != null
                        ? filter.transform.parent.name
                        : filter.name;
                }
            }
        }
    }

    [ContextMenu("Check Containment")]
    private void CheckContainment()
    {
        Refresh();
        MeasureContainment(out float max, out float shell, out string farthest);
        string verdict = max < shell ? "inside" : "PROTRUDES";
        Debug.Log(
            $"ObserverDiorama: {verdict}. Farthest vertex {max:F4} m from the center ({farthest}), " +
            $"shell {shell:F4} m, clearance {(shell - max) * 1000f:F1} mm. Rim radius {RimRadius:F4}.",
            this
        );
    }
}
