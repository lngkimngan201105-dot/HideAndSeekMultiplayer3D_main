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
    public string disguisedPrompt = "R để trở lại người\nTab để quan sát";

    private void Awake()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }

        SetPrompt(string.Empty, false);
    }

    private void Update()
    {
        if (propTransformSystem == null || (promptText == null && legacyPromptText == null))
        {
            return;
        }

        if (propTransformSystem.playerRole != PlayerRole.Hider)
        {
            SetPrompt(string.Empty, false);
            return;
        }

        if (propTransformSystem.currentState == PlayerDisguiseState.Human)
        {
            bool canCopy = propTransformSystem.TryGetLookedAtProp(out _, out _);
            SetPrompt(copyPrompt, canCopy);
            return;
        }

        SetPrompt(disguisedPrompt, true);
    }

    private void SetPrompt(string message, bool visible)
    {
        if (promptText != null)
        {
            promptText.text = message;
            promptText.gameObject.SetActive(visible);
        }

        if (legacyPromptText != null)
        {
            legacyPromptText.text = message;
            legacyPromptText.gameObject.SetActive(visible);
        }
    }
}
