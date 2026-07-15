using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PropInteractionUI : MonoBehaviour
{
    public PropTransformSystem propTransformSystem;
    public TextMeshProUGUI promptText;
    public Text legacyPromptText;

    [Header("Prompt Text")]
    public string copyPrompt = "E để copy hình dạng";
    public string disguisedPrompt = "R để trở lại người\nTab để đổi góc nhìn";
    public float disguisedPromptDuration = 4f;
    public float disguisedPromptFadeDuration = 0.5f;

    private PlayerDisguiseState _previousState;
    private float _disguisedPromptStartTime;

    private void Awake()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }

        _previousState = propTransformSystem != null
            ? propTransformSystem.currentState
            : PlayerDisguiseState.Human;
        SetPrompt(string.Empty, false, 0f);
    }

    private void Update()
    {
        if (propTransformSystem == null || (promptText == null && legacyPromptText == null))
        {
            return;
        }

        if (propTransformSystem.playerRole != PlayerRole.Hider)
        {
            SetPrompt(string.Empty, false, 0f);
            return;
        }

        if (propTransformSystem.currentState != _previousState)
        {
            if (propTransformSystem.currentState == PlayerDisguiseState.Disguised)
            {
                _disguisedPromptStartTime = Time.unscaledTime;
            }

            _previousState = propTransformSystem.currentState;
        }

        if (propTransformSystem.currentState == PlayerDisguiseState.Human)
        {
            bool canCopy = propTransformSystem.TryGetLookedAtProp(out _, out _);
            SetPrompt(copyPrompt, canCopy, 1f);
            return;
        }

        if (propTransformSystem.currentState != PlayerDisguiseState.Disguised)
        {
            SetPrompt(string.Empty, false, 0f);
            return;
        }

        float elapsed = Time.unscaledTime - _disguisedPromptStartTime;
        float alpha = 1f;
        if (elapsed > disguisedPromptDuration)
        {
            alpha = 1f - Mathf.Clamp01(
                (elapsed - disguisedPromptDuration) / Mathf.Max(0.01f, disguisedPromptFadeDuration)
            );
        }

        SetPrompt(disguisedPrompt, alpha > 0f, alpha);
    }

    private void SetPrompt(string message, bool visible, float alpha)
    {
        if (promptText != null)
        {
            promptText.text = message;
            Color color = promptText.color;
            color.a = alpha;
            promptText.color = color;
            promptText.gameObject.SetActive(visible);
        }

        if (legacyPromptText != null)
        {
            legacyPromptText.text = message;
            Color color = legacyPromptText.color;
            color.a = alpha;
            legacyPromptText.color = color;
            legacyPromptText.gameObject.SetActive(visible);
        }
    }
}
