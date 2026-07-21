using System.Collections.Generic;
using UnityEngine;

public readonly struct PropWallGeometry
{
    public PropWallGeometry(
        Bounds localBounds,
        Vector3 detectedBackLocalDirection,
        float confidence,
        bool hasDetectedBack)
    {
        LocalBounds = localBounds;
        DetectedBackLocalDirection = detectedBackLocalDirection;
        Confidence = confidence;
        HasDetectedBack = hasDetectedBack;
    }

    public Bounds LocalBounds { get; }
    public Vector3 DetectedBackLocalDirection { get; }
    public float Confidence { get; }
    public bool HasDetectedBack { get; }
}

public static class PropWallGeometryAnalyzer
{
    private const int DirectionSampleCount = 24;
    private const float MinimumBackConfidence = 0.12f;

    private static readonly Dictionary<string, PropWallGeometry> GeometryCache =
        new Dictionary<string, PropWallGeometry>();

    public static bool TryGetOrAnalyze(
        Transform visualRoot,
        string cacheKey,
        out PropWallGeometry geometry)
    {
        if (!string.IsNullOrEmpty(cacheKey) &&
            GeometryCache.TryGetValue(cacheKey, out PropWallGeometry cachedGeometry))
        {
            if (!TryCalculateLocalBounds(visualRoot, out Bounds currentLocalBounds))
            {
                geometry = default;
                return false;
            }

            geometry = new PropWallGeometry(
                currentLocalBounds,
                cachedGeometry.DetectedBackLocalDirection,
                cachedGeometry.Confidence,
                cachedGeometry.HasDetectedBack
            );
            return true;
        }

        if (!TryAnalyze(visualRoot, out geometry))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(cacheKey))
        {
            GeometryCache[cacheKey] = geometry;
        }

        return true;
    }

    private static bool TryCalculateLocalBounds(Transform visualRoot, out Bounds localBounds)
    {
        localBounds = default;
        if (visualRoot == null)
        {
            return false;
        }

        bool hasBounds = false;
        foreach (MeshFilter meshFilter in visualRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
            {
                continue;
            }

            Matrix4x4 meshToRoot =
                visualRoot.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
            EncapsulateMeshBounds(mesh.bounds, meshToRoot, ref localBounds, ref hasBounds);
        }

        return hasBounds;
    }

    private static bool TryAnalyze(Transform visualRoot, out PropWallGeometry geometry)
    {
        geometry = default;
        if (visualRoot == null)
        {
            return false;
        }

        MeshFilter[] meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0)
        {
            return false;
        }

        float[] directionScores = new float[DirectionSampleCount];
        bool hasBounds = false;
        bool hasReadableTriangles = false;
        Bounds localBounds = default;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null)
            {
                continue;
            }

            Matrix4x4 meshToRoot =
                visualRoot.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
            EncapsulateMeshBounds(mesh.bounds, meshToRoot, ref localBounds, ref hasBounds);

            if (!mesh.isReadable)
            {
                continue;
            }

            Vector3[] vertices;
            int[] triangles;
            try
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
            }
            catch (UnityException)
            {
                continue;
            }

            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                Vector3 a = meshToRoot.MultiplyPoint3x4(vertices[triangles[index]]);
                Vector3 b = meshToRoot.MultiplyPoint3x4(vertices[triangles[index + 1]]);
                Vector3 c = meshToRoot.MultiplyPoint3x4(vertices[triangles[index + 2]]);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float doubledArea = cross.magnitude;
                if (doubledArea <= 0.000001f)
                {
                    continue;
                }

                hasReadableTriangles = true;
                Vector3 triangleNormal = cross / doubledArea;
                float triangleArea = doubledArea * 0.5f;
                for (int directionIndex = 0;
                     directionIndex < DirectionSampleCount;
                     directionIndex++)
                {
                    float radians = directionIndex * Mathf.PI * 2f / DirectionSampleCount;
                    Vector3 candidateDirection =
                        new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                    float facing = Mathf.Max(0f, Vector3.Dot(triangleNormal, candidateDirection));
                    directionScores[directionIndex] += triangleArea * facing * facing;
                }
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        int lowestScoreIndex = 0;
        float lowestScore = directionScores[0];
        for (int index = 0; index < directionScores.Length; index++)
        {
            float score = directionScores[index];
            if (score < lowestScore)
            {
                lowestScore = score;
                lowestScoreIndex = index;
            }
        }

        int oppositeDirectionIndex =
            (lowestScoreIndex + DirectionSampleCount / 2) % DirectionSampleCount;
        float oppositeScore = directionScores[oppositeDirectionIndex];
        float confidence = hasReadableTriangles && oppositeScore > 0.000001f
            ? Mathf.Clamp01((oppositeScore - lowestScore) / oppositeScore)
            : 0f;
        float backRadians = lowestScoreIndex * Mathf.PI * 2f / DirectionSampleCount;
        Vector3 backDirection =
            new Vector3(Mathf.Sin(backRadians), 0f, Mathf.Cos(backRadians));

        geometry = new PropWallGeometry(
            localBounds,
            backDirection,
            confidence,
            hasReadableTriangles && confidence >= MinimumBackConfidence
        );
        return true;
    }

    private static void EncapsulateMeshBounds(
        Bounds meshBounds,
        Matrix4x4 meshToRoot,
        ref Bounds combinedBounds,
        ref bool hasBounds)
    {
        Vector3 minimum = meshBounds.min;
        Vector3 maximum = meshBounds.max;
        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = new Vector3(
                        x == 0 ? minimum.x : maximum.x,
                        y == 0 ? minimum.y : maximum.y,
                        z == 0 ? minimum.z : maximum.z
                    );
                    Vector3 localPoint = meshToRoot.MultiplyPoint3x4(corner);
                    if (!hasBounds)
                    {
                        combinedBounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(localPoint);
                    }
                }
            }
        }
    }
}
