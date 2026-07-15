using UnityEngine;

[System.Serializable]
public class PropVisualPartData
{
    public Mesh mesh;
    public Material[] materials;
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    public Vector3 localScale = Vector3.one;
}

public class PropTarget : MonoBehaviour
{
    public string propId;
    public string displayName;
    public GameObject visualPrefab;
    public PropVisualPartData[] visualParts;
    public Vector3 visualOffset;
    public Vector3 visualRotationOffset;
    public float visualScale = 1f;
}
