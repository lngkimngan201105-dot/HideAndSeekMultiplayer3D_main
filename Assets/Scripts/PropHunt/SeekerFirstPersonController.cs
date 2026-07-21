using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class SeekerFirstPersonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform cameraRoot;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 4f;
    [SerializeField, Min(0f)] private float sprintSpeed = 6f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -15f;

    [Header("Look")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minimumPitch = -89f;
    [SerializeField] private float maximumPitch = 89f;

    private float verticalVelocity;
    private float pitch;
    private bool controlActive;
    private int acceptInputAfterFrame;

    public bool IsControlActive => controlActive;
    public CharacterController CharacterController => characterController;
    public Transform CameraRoot => cameraRoot;

    private void Awake()
    {
        ResolveReferences();
        SetControlActive(false);
    }

    private void Update()
    {
        if (!controlActive || characterController == null || !characterController.enabled)
        {
            return;
        }

        if (Time.frameCount <= acceptInputAfterFrame)
        {
            return;
        }

        UpdateLook();
        UpdateMovement();
    }

    public void Configure(CharacterController configuredController, Transform configuredCameraRoot)
    {
        characterController = configuredController;
        cameraRoot = configuredCameraRoot;
        ResolveReferences();
    }

    public void SetControlActive(bool active)
    {
        ResolveReferences();
        controlActive = active;
        verticalVelocity = 0f;
        acceptInputAfterFrame = Time.frameCount + 1;
        if (characterController != null)
        {
            characterController.enabled = true;
        }
    }

    public void ResetMotion()
    {
        verticalVelocity = 0f;
    }

    public void TeleportTo(Transform spawnPoint)
    {
        if (spawnPoint == null) return;

        ResolveReferences();
        bool restoreController = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        pitch = 0f;
        verticalVelocity = 0f;
        if (cameraRoot != null) cameraRoot.localRotation = Quaternion.identity;
        if (characterController != null) characterController.enabled = restoreController;
        Physics.SyncTransforms();
    }

    private void UpdateMovement()
    {
        Vector2 move = ReadMove();
        bool sprint = IsSprintPressed();
        float speed = sprint ? sprintSpeed : walkSpeed;
        Vector3 direction = transform.right * move.x + transform.forward * move.y;
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        bool grounded = characterController.isGrounded;
        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (grounded && WasJumpPressed())
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;
        Vector3 velocity = direction * speed + Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void UpdateLook()
    {
        Vector2 look = ReadLook();
        transform.Rotate(Vector3.up, look.x * mouseSensitivity, Space.Self);
        pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, minimumPitch, maximumPitch);
        if (cameraRoot != null)
        {
            cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void ResolveReferences()
    {
        if (characterController == null) characterController = GetComponent<CharacterController>();
        if (cameraRoot == null)
        {
            Transform found = transform.Find("SeekerCameraRoot");
            if (found != null) cameraRoot = found;
        }
    }

    private static Vector2 ReadMove()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            float x = (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                      (Keyboard.current.aKey.isPressed ? 1f : 0f);
            float y = (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                      (Keyboard.current.sKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#else
        return Vector2.zero;
#endif
    }

    private static Vector2 ReadLook()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.delta.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 8f;
#else
        return Vector2.zero;
#endif
    }

    private static bool IsSprintPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
            return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
        return false;
#endif
    }

    private static bool WasJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.spaceKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Space);
#else
        return false;
#endif
    }
}
