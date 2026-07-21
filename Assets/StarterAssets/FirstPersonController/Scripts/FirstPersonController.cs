using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Camera")]
		[Tooltip("The camera root that receives vertical look rotation")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 89.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -89.0f;

		// camera
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private Vector3 _externalHorizontalVelocity;
		private float _terminalVelocity = 53.0f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

	
#if ENABLE_INPUT_SYSTEM
		private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;
		private Transform _disguisedMovementCamera;
		private bool _useDisguisedCameraRelativeMovement;
		private Vector3 _lastValidGroundForward;
		private Vector3 _lastValidGroundRight;
		public bool IsControlLocked { get; private set; }
		public bool IsCameraLookLocked { get; private set; }

		private const float _threshold = 0.01f;

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			TopClamp = 89.0f;
			BottomClamp = -89.0f;

			if (CinemachineCameraTarget == null)
			{
				Transform cameraRoot = transform.Find("PlayerCameraRoot");
				if (cameraRoot != null)
				{
					CinemachineCameraTarget = cameraRoot.gameObject;
				}
			}

			_mainCamera = FindMainCamera();

			if (CinemachineCameraTarget != null && _mainCamera != null && _mainCamera.transform.parent != CinemachineCameraTarget.transform)
			{
				_mainCamera.transform.SetParent(CinemachineCameraTarget.transform, true);
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;

			Debug.Log(CinemachineCameraTarget != null
				? $"FirstPersonController: Found PlayerCameraRoot: {CinemachineCameraTarget.name}"
				: "FirstPersonController: PlayerCameraRoot not found.");

			Debug.Log(_mainCamera != null
				? $"FirstPersonController: Found mainCamera: {_mainCamera.name}"
				: "FirstPersonController: mainCamera not found.");
		}

		private void Update()
		{
			if (IsControlLocked)
			{
				GroundedCheck();
				return;
			}

			JumpAndGravity();
			GroundedCheck();
			Move();
		}

		private void LateUpdate()
		{
			if (IsControlLocked || IsCameraLookLocked)
			{
				return;
			}

			CameraRotation();
		}

		public void SetControlLocked(bool locked)
		{
			IsControlLocked = locked;
			_speed = 0f;
			_rotationVelocity = 0f;
			_verticalVelocity = 0f;
			_externalHorizontalVelocity = Vector3.zero;

			if (_input != null)
			{
				_input.move = Vector2.zero;
				_input.look = Vector2.zero;
				_input.jump = false;
				_input.sprint = false;
			}
		}

		public void SetCameraLookLocked(bool locked)
		{
			IsCameraLookLocked = locked;
		}

		public void SetDisguisedCameraRelativeMovement(Camera gameplayCamera, bool enabled)
		{
			_useDisguisedCameraRelativeMovement = enabled && gameplayCamera != null;
			_disguisedMovementCamera = _useDisguisedCameraRelativeMovement
				? gameplayCamera.transform
				: null;

			if (_useDisguisedCameraRelativeMovement)
			{
				UpdateGroundMovementBasis();
			}
			else
			{
				_lastValidGroundForward = Vector3.zero;
				_lastValidGroundRight = Vector3.zero;
			}
		}

		public void ApplyExternalVelocity(Vector3 velocity)
		{
			_externalHorizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
			_verticalVelocity = Mathf.Max(_verticalVelocity, velocity.y);
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation()
		{
			// if there is an input
			if (_input.look.sqrMagnitude >= _threshold && CinemachineCameraTarget != null)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
				
				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// Update camera root pitch
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}
		}

		private GameObject FindMainCamera()
		{
			Camera[] childCameras = GetComponentsInChildren<Camera>(true);
			foreach (Camera childCamera in childCameras)
			{
				if (childCamera.name == "mainCamera" || childCamera.name == "MainCamera" || childCamera.CompareTag("MainCamera"))
				{
					return childCamera.gameObject;
				}
			}

			return GameObject.FindGameObjectWithTag("MainCamera");
		}

		private void Move()
		{
			// set target speed based on move speed, sprint speed and if sprint is pressed
			float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

			// a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

			// note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is no input, set the target speed to 0
			if (_input.move == Vector2.zero) targetSpeed = 0.0f;

			// a reference to the players current horizontal velocity
			float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// accelerate or decelerate to target speed
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				// creates curved result rather than a linear one giving a more organic speed change
				// note T in Lerp is clamped, so we don't need to clamp our speed
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);

				// round speed to 3 decimal places
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

			Vector3 inputDirection = GetGroundMovementDirection(_input.move);

			// move the player
			Vector3 movementVelocity =
				inputDirection.normalized * _speed +
				_externalHorizontalVelocity +
				new Vector3(0.0f, _verticalVelocity, 0.0f);
			_controller.Move(movementVelocity * Time.deltaTime);
			_externalHorizontalVelocity = Vector3.MoveTowards(
				_externalHorizontalVelocity,
				Vector3.zero,
				8f * Time.deltaTime
			);
		}

		private Vector3 GetGroundMovementDirection(Vector2 moveInput)
		{
			if (moveInput == Vector2.zero)
			{
				return Vector3.zero;
			}

			if (!_useDisguisedCameraRelativeMovement || _disguisedMovementCamera == null)
			{
				return (
					transform.right * moveInput.x +
					transform.forward * moveInput.y
				).normalized;
			}

			UpdateGroundMovementBasis();
			Vector3 movementDirection =
				_lastValidGroundForward * moveInput.y +
				_lastValidGroundRight * moveInput.x;
			return movementDirection.sqrMagnitude > 1f
				? movementDirection.normalized
				: movementDirection;
		}

		private void UpdateGroundMovementBasis()
		{
			if (_disguisedMovementCamera == null)
			{
				return;
			}

			Vector3 cameraForward = Vector3.ProjectOnPlane(
				_disguisedMovementCamera.forward,
				Vector3.up
			);
			Vector3 cameraRight = Vector3.ProjectOnPlane(
				_disguisedMovementCamera.right,
				Vector3.up
			);

			if (cameraForward.sqrMagnitude > _threshold)
			{
				_lastValidGroundForward = cameraForward.normalized;
			}
			else if (_lastValidGroundForward.sqrMagnitude <= _threshold)
			{
				Vector3 capsuleForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
				_lastValidGroundForward = capsuleForward.sqrMagnitude > _threshold
					? capsuleForward.normalized
					: Vector3.forward;
			}

			if (cameraRight.sqrMagnitude > _threshold)
			{
				_lastValidGroundRight = cameraRight.normalized;
			}
			else if (_lastValidGroundRight.sqrMagnitude <= _threshold)
			{
				_lastValidGroundRight = Vector3.Cross(
					Vector3.up,
					_lastValidGroundForward
				).normalized;
			}
		}

		private void JumpAndGravity()
		{
			if (Grounded)
			{
				// reset the fall timeout timer
				_fallTimeoutDelta = FallTimeout;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump
				if (_input.jump && _jumpTimeoutDelta <= 0.0f)
				{
					// the square root of H * -2 * G = how much velocity needed to reach desired height
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// reset the jump timeout timer
				_jumpTimeoutDelta = JumpTimeout;

				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// if we are not grounded, do not jump
				_input.jump = false;
			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}
