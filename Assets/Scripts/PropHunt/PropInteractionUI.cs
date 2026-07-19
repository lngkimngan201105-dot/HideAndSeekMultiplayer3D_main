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

        // Disguised controls are shown only by the lower-left contextual HUD.
        // Keeping this legacy center prompt hidden also prevents showing R while wall-attached.
        SetPrompt(string.Empty, false, 0f);
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
