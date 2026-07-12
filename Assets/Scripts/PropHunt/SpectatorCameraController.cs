using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using StarterAssets;

public class SpectatorCameraController : MonoBehaviour
{
    public PropTransformSystem propTransformSystem;
    public Transform playerRoot;
    public StarterAssetsInputs starterAssetsInputs;
    public float moveSpeed = 8f;
    public float lookSensitivity = 2f;
    public float maxRadius = 20f;
    public float maxHeight = 10f;
    public float minHeight = 0.5f;
    public bool invertY = false;

    private float _yaw;
    private float _pitch;

    private void OnEnable()
    {
        Vector3 angles = transform.eulerAngles;
        _yaw = angles.y;
        _pitch = angles.x > 180f ? angles.x - 360f : angles.x;
    }

    private void Update()
    {
        if (propTransformSystem == null || !propTransformSystem.IsSpectatorActive())
        {
            return;
        }

        RotateFromMouse();
        MoveFromKeyboard();
        ClampAroundPlayer();
    }

    private void RotateFromMouse()
    {
        Vector2 look = ReadMouseLookDelta();
        _yaw += look.x * lookSensitivity;

        if (invertY)
        {
            _pitch += look.y * lookSensitivity;
        }
        else
        {
            _pitch -= look.y * lookSensitivity;
        }

        _pitch = Mathf.Clamp(_pitch, -80f, 80f);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void MoveFromKeyboard()
    {
        Vector2 move = starterAssetsInputs != null ? starterAssetsInputs.move : ReadLegacyMove();
        Vector3 input = new Vector3(move.x, 0f, move.y);

        if (IsUpPressed())
        {
            input.y += 1f;
        }

        if (IsDownPressed())
        {
            input.y -= 1f;
        }

        Vector3 worldMove = transform.right * input.x + transform.forward * input.z + Vector3.up * input.y;
        transform.position += worldMove.normalized * (moveSpeed * Time.deltaTime);
    }

    private void ClampAroundPlayer()
    {
        if (playerRoot == null)
        {
            return;
        }

        Vector3 center = playerRoot.position;
        Vector3 offset = transform.position - center;
        Vector3 horizontalOffset = new Vector3(offset.x, 0f, offset.z);

        if (horizontalOffset.magnitude > maxRadius)
        {
            horizontalOffset = horizontalOffset.normalized * maxRadius;
        }

        float y = Mathf.Clamp(transform.position.y, center.y + minHeight, center.y + maxHeight);
        transform.position = new Vector3(center.x + horizontalOffset.x, y, center.z + horizontalOffset.z);
    }

    private Vector2 ReadMouseLookDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.delta.ReadValue() * 0.05f;
        }
#endif
        return starterAssetsInputs != null ? starterAssetsInputs.look : ReadLegacyMouseDelta();
    }

    private static Vector2 ReadLegacyMouseDelta()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
        return Vector2.zero;
#endif
    }

    private static Vector2 ReadLegacyMove()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#else
        return Vector2.zero;
#endif
    }

    private static bool IsUpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.Space);
#else
        return false;
#endif
    }

    private static bool IsDownPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed))
        {
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
#else
        return false;
#endif
    }
}
