using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class FinalArtButtonFeedback : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [SerializeField] private Graphic hoverGraphic;
    [SerializeField] private RectTransform hoverTransform;
    [SerializeField] private Color hoverColor =
        new Color(0f, 0.9f, 1f, 0.16f);
    [SerializeField, Range(1f, 1.03f)] private float hoverScale = 1.015f;
    [SerializeField, Range(0.96f, 1f)] private float pressedScale = 0.985f;
    [SerializeField, Min(1f)] private float responseSpeed = 18f;

    private bool pointerInside;
    private bool pointerDown;

    public void Configure(
        Graphic configuredHover,
        RectTransform configuredHoverTransform,
        Color configuredColor,
        float configuredHoverScale = 1.015f,
        float configuredPressedScale = 0.985f)
    {
        hoverGraphic = configuredHover;
        hoverTransform = configuredHoverTransform;
        hoverColor = configuredColor;
        hoverScale = configuredHoverScale;
        pressedScale = configuredPressedScale;
        ApplyImmediate();
    }

    private void Awake()
    {
        if (hoverGraphic == null)
            hoverGraphic = GetComponentInChildren<Graphic>(true);
        if (hoverTransform == null && hoverGraphic != null)
            hoverTransform = hoverGraphic.rectTransform;
        if (hoverGraphic != null) hoverGraphic.raycastTarget = false;
        ApplyImmediate();
    }

    private void OnDisable()
    {
        pointerInside = false;
        pointerDown = false;
        ApplyImmediate();
    }

    private void Update()
    {
        float desiredScale = pointerDown
            ? pressedScale
            : pointerInside
                ? hoverScale
                : 1f;
        float blend = 1f - Mathf.Exp(-responseSpeed * Time.unscaledDeltaTime);
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            Vector3.one * desiredScale,
            blend);

        if (hoverTransform != null && hoverTransform != transform)
        {
            hoverTransform.localScale = Vector3.Lerp(
                hoverTransform.localScale,
                Vector3.one * desiredScale,
                blend);
        }

        if (hoverGraphic == null) return;
        Color desired = hoverColor;
        desired.a = pointerInside
            ? hoverColor.a * (pointerDown ? 0.55f : 1f)
            : 0f;
        hoverGraphic.color = Color.Lerp(hoverGraphic.color, desired, blend);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        pointerDown = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            pointerDown = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pointerDown = false;
    }

    private void ApplyImmediate()
    {
        transform.localScale = Vector3.one;
        if (hoverTransform != null && hoverTransform != transform)
            hoverTransform.localScale = Vector3.one;
        if (hoverGraphic == null) return;
        Color hidden = hoverColor;
        hidden.a = 0f;
        hoverGraphic.color = hidden;
        hoverGraphic.raycastTarget = false;
    }
}
