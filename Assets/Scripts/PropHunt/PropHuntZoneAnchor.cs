using UnityEngine;

public class PropHuntZoneAnchor : MonoBehaviour
{
    [SerializeField] private bool isEnabledAnchor = true;
    [SerializeField, Min(0.5f)] private float finalZoneRadius = 15f;
    [SerializeField] private HiderPlayableAreaBounds playableArea;
    [SerializeField] private LayerMask groundMask = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0.1f)] private float groundProbeHeight = 2f;
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 6f;
    [SerializeField, Range(0f, 1f)] private float minimumGroundNormalDot = 0.65f;

    private bool _isSelected;
    private bool _lastValidationResult;
    private string _lastValidationMessage = "Not validated";

    public bool IsEnabledAnchor => isEnabledAnchor;
    public bool IsValidAnchor => ValidateAnchor(out _);
    public bool IsSelected => _isSelected;
    public float FinalZoneRadius => finalZoneRadius;
    public string LastValidationMessage => _lastValidationMessage;

    public void Configure(
        HiderPlayableAreaBounds configuredPlayableArea,
        LayerMask configuredGroundMask,
        float configuredFinalZoneRadius)
    {
        playableArea = configuredPlayableArea;
        groundMask = configuredGroundMask;
        finalZoneRadius = Mathf.Max(0.5f, configuredFinalZoneRadius);
        ValidateAnchor(out _);
    }

    public bool ValidateAnchor(out string message)
    {
        if (!isEnabledAnchor)
        {
            return SetValidation(false, "Anchor is disabled", out message);
        }

        if (playableArea != null && !playableArea.ContainsXZ(transform.position, finalZoneRadius))
        {
            return SetValidation(false, "Final circle exceeds PlayableArea", out message);
        }

        Vector3 origin = transform.position + Vector3.up * groundProbeHeight;
        if (!Physics.Raycast(
                origin,
                Vector3.down,
                out RaycastHit hit,
                groundProbeHeight + groundProbeDistance,
                groundMask,
                QueryTriggerInteraction.Ignore))
        {
            return SetValidation(false, "No ground below anchor", out message);
        }

        if (Vector3.Dot(hit.normal, Vector3.up) < minimumGroundNormalDot)
        {
            return SetValidation(false, "Ground is too steep", out message);
        }

        foreach (Collider overlap in Physics.OverlapSphere(
                     transform.position,
                     0.2f,
                     ~0,
                     QueryTriggerInteraction.Collide))
        {
            if (overlap != null && overlap.isTrigger &&
                (playableArea == null || overlap != playableArea.BoundsCollider))
            {
                return SetValidation(false, $"Inside unrelated trigger '{overlap.name}'", out message);
            }
        }

        return SetValidation(true, $"Ground={hit.collider.name}", out message);
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
    }

    private bool SetValidation(bool valid, string validationMessage, out string message)
    {
        _lastValidationResult = valid;
        _lastValidationMessage = validationMessage;
        message = validationMessage;
        return valid;
    }

    private void OnDrawGizmos()
    {
        bool valid = Application.isPlaying ? _lastValidationResult : ValidateAnchor(out _);
        Color color = _isSelected
            ? new Color(1f, 0.82f, 0.12f, 1f)
            : valid
                ? new Color(0.15f, 0.95f, 0.55f, 0.85f)
                : new Color(1f, 0.2f, 0.2f, 0.85f);

        Gizmos.color = color;
        DrawCircle(transform.position + Vector3.up * 0.08f, finalZoneRadius, 64);
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.15f, _isSelected ? 0.45f : 0.25f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.8f,
            $"{name}\n{(valid ? "VALID" : "INVALID")}{(_isSelected ? " / SELECTED" : string.Empty)}"
        );
#endif
    }

    private static void DrawCircle(Vector3 center, float radius, int segments)
    {
        Vector3 previous = center + Vector3.right * radius;
        for (int index = 1; index <= segments; index++)
        {
            float angle = index / (float)segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
