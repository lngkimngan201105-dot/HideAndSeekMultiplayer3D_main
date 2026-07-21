using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PropHuntZoneDomeVisual : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private Shader energyShader;

    [Header("Shape")]
    [SerializeField, Range(24, 128)] private int radialSegments = 64;
    [SerializeField, Range(8, 32)] private int verticalSegments = 16;
    [SerializeField] private float groundOffset = 0.08f;

    [Header("Energy")]
    [SerializeField] private Color energyColor = new Color(0.04f, 0.76f, 1f, 0.85f);
    [SerializeField, Range(0f, 0.2f)] private float bodyAlpha = 0.075f;
    [SerializeField, Range(0f, 2f)] private float fresnelStrength = 1.1f;
    [SerializeField, Range(0f, 2f)] private float streakStrength = 1f;
    [SerializeField, Range(0.1f, 3f)] private float pulseSpeed = 0.72f;
    [SerializeField, Range(0.1f, 3f)] private float scrollSpeed = 0.48f;

    private Mesh _runtimeMesh;
    private Material _runtimeMaterial;

    public int VertexCount => meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.vertexCount : 0;
    public int TriangleCount => meshFilter != null && meshFilter.sharedMesh != null
        ? meshFilter.sharedMesh.triangles.Length / 3
        : 0;
    public bool ShaderSupported => meshRenderer != null && meshRenderer.sharedMaterial != null &&
                                   meshRenderer.sharedMaterial.shader != null &&
                                   meshRenderer.sharedMaterial.shader.isSupported;
    public float CurrentDomeHeight { get; private set; }

    public void Configure(MeshFilter configuredMeshFilter, MeshRenderer configuredMeshRenderer, Shader configuredShader)
    {
        meshFilter = configuredMeshFilter;
        meshRenderer = configuredMeshRenderer;
        energyShader = configuredShader;
    }

    private void Awake()
    {
        ResolveReferences();
        BuildRuntimeMesh();
        BuildRuntimeMaterial();
    }

    private void OnDestroy()
    {
        if (_runtimeMesh != null) Destroy(_runtimeMesh);
        if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
    }

    public void SetVisible(bool visible)
    {
        if (!visible && _runtimeMaterial != null)
        {
            _runtimeMaterial.SetFloat("_ShrinkIntensity", 0f);
        }

        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }
    }

    public void SetZone(Vector3 center, float radius, bool isShrinking)
    {
        radius = Mathf.Max(0.01f, radius);
        CurrentDomeHeight = Mathf.Clamp(radius * 0.65f, 12f, 60f);
        transform.SetPositionAndRotation(center + Vector3.up * groundOffset, Quaternion.identity);
        transform.localScale = new Vector3(radius, CurrentDomeHeight, radius);
        if (_runtimeMaterial != null)
        {
            _runtimeMaterial.SetFloat("_ShrinkIntensity", isShrinking ? 1f : 0f);
        }
    }

    private void ResolveReferences()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
    }

    private void BuildRuntimeMesh()
    {
        if (meshFilter == null || _runtimeMesh != null ||
            (meshFilter.sharedMesh != null && meshFilter.sharedMesh.vertexCount > 0))
        {
            return;
        }

        _runtimeMesh = CreateHemisphereMesh(radialSegments, verticalSegments, "PropHuntZoneDome_Runtime");
        _runtimeMesh.hideFlags = HideFlags.DontSave;
        meshFilter.sharedMesh = _runtimeMesh;
    }

    public static Mesh CreateHemisphereMesh(int horizontalSegments, int heightSegments, string meshName)
    {
        horizontalSegments = Mathf.Clamp(horizontalSegments, 24, 128);
        heightSegments = Mathf.Clamp(heightSegments, 8, 32);
        int rings = heightSegments + 1;
        int columns = horizontalSegments + 1;
        Vector3[] vertices = new Vector3[rings * columns];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[heightSegments * horizontalSegments * 6];

        for (int latitude = 0; latitude <= heightSegments; latitude++)
        {
            float vertical01 = latitude / (float)heightSegments;
            float elevation = vertical01 * Mathf.PI * 0.5f;
            float horizontalRadius = Mathf.Cos(elevation);
            float height = Mathf.Sin(elevation);

            for (int longitude = 0; longitude <= horizontalSegments; longitude++)
            {
                float horizontal01 = longitude / (float)horizontalSegments;
                float angle = horizontal01 * Mathf.PI * 2f;
                int vertexIndex = latitude * columns + longitude;
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle) * horizontalRadius,
                    height,
                    Mathf.Sin(angle) * horizontalRadius);
                vertices[vertexIndex] = direction;
                normals[vertexIndex] = direction.normalized;
                uv[vertexIndex] = new Vector2(horizontal01, vertical01);
            }
        }

        int triangleIndex = 0;
        for (int latitude = 0; latitude < heightSegments; latitude++)
        {
            for (int longitude = 0; longitude < horizontalSegments; longitude++)
            {
                int lowerLeft = latitude * columns + longitude;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + columns;
                int upperRight = upperLeft + 1;

                triangles[triangleIndex++] = lowerLeft;
                triangles[triangleIndex++] = upperLeft;
                triangles[triangleIndex++] = upperRight;
                triangles[triangleIndex++] = lowerLeft;
                triangles[triangleIndex++] = upperRight;
                triangles[triangleIndex++] = lowerRight;
            }
        }

        Mesh mesh = new Mesh
        {
            name = meshName
        };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private void BuildRuntimeMaterial()
    {
        if (meshRenderer == null || _runtimeMaterial != null)
        {
            return;
        }

        Material configuredMaterial = meshRenderer.sharedMaterial;
        Shader shader = configuredMaterial != null && configuredMaterial.shader != null
            ? configuredMaterial.shader
            : energyShader != null
                ? energyShader
                : Shader.Find("Prop Hunt/Zone Energy Dome");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("PropHuntZoneDomeVisual: Built-in energy shader is missing or unsupported; dome wall will stay hidden.");
            meshRenderer.enabled = false;
            return;
        }

        _runtimeMaterial = configuredMaterial != null
            ? new Material(configuredMaterial)
            : new Material(shader);
        _runtimeMaterial.name = "PropHuntZoneDome_Runtime";
        _runtimeMaterial.hideFlags = HideFlags.DontSave;
        _runtimeMaterial.SetColor("_EnergyColor", energyColor);
        _runtimeMaterial.SetFloat("_BodyAlpha", bodyAlpha);
        _runtimeMaterial.SetFloat("_FresnelStrength", fresnelStrength);
        _runtimeMaterial.SetFloat("_StreakStrength", streakStrength);
        _runtimeMaterial.SetFloat("_PulseSpeed", pulseSpeed);
        _runtimeMaterial.SetFloat("_ScrollSpeed", scrollSpeed);
        _runtimeMaterial.SetFloat("_ShrinkIntensity", 0f);
        meshRenderer.sharedMaterial = _runtimeMaterial;
        meshRenderer.enabled = true;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        meshRenderer.allowOcclusionWhenDynamic = false;
        meshRenderer.sortingOrder = 50;
    }
}
