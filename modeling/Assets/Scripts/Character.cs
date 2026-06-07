using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

[RequireComponent(typeof(CharacterController))]
public class Character : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f;
    [SerializeField] private float dashSpeed = 12f;
    [SerializeField] private float dashDuration = 0.18f;
    [SerializeField] private float rotationSpeed = 14f;
    [SerializeField] private float jumpHeight = 1.4f;
    [SerializeField] private float gravity = -24f;

    [Header("Camera")]
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] private float cameraDistance = 4.5f;
    [SerializeField] private float mouseSensitivity = 2.5f;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 65f;
    [SerializeField] private float cameraSmoothTime = 0.04f;
    [SerializeField] private Camera controlledCamera;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string animationLayerName = "Base Layer";
    [SerializeField] private string idleStateName = "KA_Idle";
    [SerializeField] private string runStartStateName = "KA_Run05_Start";
    [SerializeField] private string runStateName = "KA_Run05";
    [SerializeField] private string runStopStateName = "KA_Run05_Stop";
    [SerializeField] private float locomotionTransitionTime = 0.08f;
    [SerializeField] private string attackStateName = "";
    [SerializeField] private string dashStateName = "";
    [SerializeField] private string jumpStartStateName = "";
    [SerializeField] private string jumpLoopStateName = "";
    [SerializeField] private string jumpEndStateName = "";
    [SerializeField] private float actionTransitionTime = 0.05f;
    [SerializeField, Range(0.5f, 1f)] private float jumpStartToLoopTime = 0.75f;
    [SerializeField] private float jumpTransitionTime = 0.12f;

    private CharacterController controller;
    private Camera mainCamera;
    private Vector3 verticalVelocity;
    private Vector3 dashDirection;
    private Vector3 cameraVelocity;
    private float yaw;
    private float pitch = 18f;
    private float dashTimer;
    private bool hasSpeedParameter;
    private bool hasGroundedParameter;
    private bool hasJumpParameter;
    private bool hasAttackParameter;
    private bool hasDashParameter;
    private bool hasIdleState;
    private bool hasRunStartState;
    private bool hasRunState;
    private bool hasRunStopState;
    private bool hasAttackState;
    private bool hasDashState;
    private bool hasJumpStartState;
    private bool hasJumpLoopState;
    private bool hasJumpEndState;
    private int activeActionStateHash;
    private int activeActionStartedFrame;
    private bool activeActionReturnsToIdle;
    private bool isAttackActionPlaying;
    private bool isDashActionPlaying;
    private JumpAnimationState jumpAnimationState;
    private LocomotionAnimationState locomotionAnimationState;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DashHash = Animator.StringToHash("Dash");

    private enum LocomotionAnimationState
    {
        Idle,
        RunStart,
        Run,
        RunStop
    }

    private enum JumpAnimationState
    {
        Grounded,
        Start,
        Loop,
        End
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        mainCamera = controlledCamera != null ? controlledCamera : Camera.main;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (cameraTarget == null || !cameraTarget.IsChildOf(transform))
        {
            cameraTarget = transform;
        }

        CacheAnimatorParameters();
        CacheAnimatorStates();
        yaw = transform.eulerAngles.y;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SnapCameraToTarget();
        PlayLocomotionState(LocomotionAnimationState.Idle, 0f);
    }

    private void Update()
    {
        HandleCameraInput();
        HandleActionInput();
        HandleMovement();
        UpdateAnimator();
    }

    private void LateUpdate()
    {
        UpdateCamera();
    }

    private void HandleCameraInput()
    {
        Vector2 mouseDelta = GetMouseDelta();
        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (GetEscapeDown())
        {
            bool lockCursor = Cursor.lockState != CursorLockMode.Locked;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
        }
    }

    private void HandleActionInput()
    {
        if (GetLeftMouseDown())
        {
            if (isAttackActionPlaying && IsActionStatePlaying())
            {
                return;
            }

            PlayActionState(attackStateName, hasAttackState, AttackHash, hasAttackParameter);
        }

        if (GetRightMouseDown())
        {
            if (isDashActionPlaying && IsActionStatePlaying())
            {
                return;
            }

            Vector3 inputDirection = GetInputDirection();
            dashDirection = inputDirection.sqrMagnitude > 0.001f ? inputDirection : transform.forward;
            dashTimer = dashDuration;
            PlayActionState(dashStateName, hasDashState, DashHash, hasDashParameter);
        }
    }

    private void HandleMovement()
    {
        bool grounded = controller.isGrounded;

        if (grounded && verticalVelocity.y < 0f)
        {
            verticalVelocity.y = -2f;
        }

        Vector3 moveDirection = GetInputDirection();
        Vector3 horizontalVelocity = moveDirection * moveSpeed;

        if (dashTimer > 0f)
        {
            horizontalVelocity = dashDirection * dashSpeed;
            dashTimer -= Time.deltaTime;
        }

        if (grounded && jumpAnimationState == JumpAnimationState.Grounded && GetJumpDown())
        {
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            PlayJumpStart();
        }

        verticalVelocity.y += gravity * Time.deltaTime;
        controller.Move((horizontalVelocity + verticalVelocity) * Time.deltaTime);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private Vector3 GetInputDirection()
    {
        Vector2 input = GetMoveInput();
        input = Vector2.ClampMagnitude(input, 1f);

        Quaternion cameraYaw = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = cameraYaw * Vector3.forward;
        Vector3 right = cameraYaw * Vector3.right;

        return (forward * input.y + right * input.x).normalized;
    }

    private void UpdateCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = controlledCamera != null ? controlledCamera : Camera.main;

            if (mainCamera == null)
            {
                return;
            }
        }

        Pose cameraPose = GetCameraPose();

        mainCamera.transform.position = Vector3.SmoothDamp(
            mainCamera.transform.position,
            cameraPose.position,
            ref cameraVelocity,
            cameraSmoothTime
        );
        mainCamera.transform.rotation = cameraPose.rotation;
    }

    private void SnapCameraToTarget()
    {
        if (mainCamera == null)
        {
            return;
        }

        Pose cameraPose = GetCameraPose();
        mainCamera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
        cameraVelocity = Vector3.zero;
    }

    private Pose GetCameraPose()
    {
        Quaternion cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 targetPosition = cameraTarget.position + cameraOffset;
        Vector3 desiredPosition = targetPosition - cameraRotation * Vector3.forward * cameraDistance;
        return new Pose(desiredPosition, cameraRotation);
    }

    private void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        Vector2 input = GetMoveInput();
        bool wantsToMove = Vector2.ClampMagnitude(input, 1f).sqrMagnitude > 0.001f;

        UpdateJumpAnimation();

        if (!IsActionStatePlaying() && jumpAnimationState == JumpAnimationState.Grounded)
        {
            UpdateLocomotionAnimation(wantsToMove);
        }

        if (hasSpeedParameter)
        {
            animator.SetFloat(SpeedHash, Vector2.ClampMagnitude(input, 1f).magnitude, 0.08f, Time.deltaTime);
        }

        if (hasGroundedParameter)
        {
            animator.SetBool(GroundedHash, controller.isGrounded);
        }
    }

    private void CacheAnimatorParameters()
    {
        if (animator == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            hasSpeedParameter |= parameter.nameHash == SpeedHash && parameter.type == AnimatorControllerParameterType.Float;
            hasGroundedParameter |= parameter.nameHash == GroundedHash && parameter.type == AnimatorControllerParameterType.Bool;
            hasJumpParameter |= parameter.nameHash == JumpHash && parameter.type == AnimatorControllerParameterType.Trigger;
            hasAttackParameter |= parameter.nameHash == AttackHash && parameter.type == AnimatorControllerParameterType.Trigger;
            hasDashParameter |= parameter.nameHash == DashHash && parameter.type == AnimatorControllerParameterType.Trigger;
        }
    }

    private void CacheAnimatorStates()
    {
        if (animator == null)
        {
            return;
        }

        hasIdleState = HasAnimatorState(idleStateName);
        hasRunStartState = HasAnimatorState(runStartStateName);
        hasRunState = HasAnimatorState(runStateName);
        hasRunStopState = HasAnimatorState(runStopStateName);
        hasAttackState = HasAnimatorState(attackStateName);
        hasDashState = HasAnimatorState(dashStateName);
        hasJumpStartState = HasAnimatorState(jumpStartStateName);
        hasJumpLoopState = HasAnimatorState(jumpLoopStateName);
        hasJumpEndState = HasAnimatorState(jumpEndStateName);
    }

    private void UpdateLocomotionAnimation(bool wantsToMove)
    {
        if (!hasRunState)
        {
            PlayLocomotionState(LocomotionAnimationState.Idle, locomotionTransitionTime);
            return;
        }

        if (wantsToMove)
        {
            if (locomotionAnimationState == LocomotionAnimationState.Idle ||
                locomotionAnimationState == LocomotionAnimationState.RunStop)
            {
                PlayLocomotionState(
                    hasRunStartState ? LocomotionAnimationState.RunStart : LocomotionAnimationState.Run,
                    locomotionTransitionTime
                );
                return;
            }

            if (locomotionAnimationState == LocomotionAnimationState.RunStart && IsCurrentStateFinished())
            {
                PlayLocomotionState(LocomotionAnimationState.Run, locomotionTransitionTime);
            }

            return;
        }

        if (locomotionAnimationState == LocomotionAnimationState.Run ||
            locomotionAnimationState == LocomotionAnimationState.RunStart)
        {
            PlayLocomotionState(
                hasRunStopState ? LocomotionAnimationState.RunStop : LocomotionAnimationState.Idle,
                locomotionTransitionTime
            );
            return;
        }

        if (locomotionAnimationState == LocomotionAnimationState.RunStop && IsCurrentStateFinished())
        {
            if (hasIdleState)
            {
                PlayLocomotionState(LocomotionAnimationState.Idle, locomotionTransitionTime);
            }
        }
    }

    private void PlayLocomotionState(LocomotionAnimationState state, float transitionTime)
    {
        if (animator == null)
        {
            return;
        }

        string stateName = GetStateName(state);

        if (string.IsNullOrEmpty(stateName) || !HasAnimatorState(stateName))
        {
            return;
        }

        string statePath = GetStatePath(stateName);

        if (transitionTime <= 0f)
        {
            animator.Play(statePath, 0, 0f);
        }
        else
        {
            animator.CrossFadeInFixedTime(statePath, transitionTime, 0, 0f);
        }

        locomotionAnimationState = state;
    }

    private bool IsCurrentStateFinished()
    {
        if (animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.normalizedTime >= 0.95f;
    }

    private bool IsCurrentStatePast(float normalizedTime)
    {
        if (animator == null || animator.IsInTransition(0))
        {
            return false;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.normalizedTime >= normalizedTime;
    }

    private string GetStateName(LocomotionAnimationState state)
    {
        switch (state)
        {
            case LocomotionAnimationState.Idle:
                return idleStateName;
            case LocomotionAnimationState.RunStart:
                return runStartStateName;
            case LocomotionAnimationState.Run:
                return runStateName;
            case LocomotionAnimationState.RunStop:
                return runStopStateName;
            default:
                return string.Empty;
        }
    }

    private bool HasAnimatorState(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        return animator.HasState(0, Animator.StringToHash(GetStatePath(stateName)));
    }

    private string GetStatePath(string stateName)
    {
        return $"{animationLayerName}.{stateName}";
    }

    private void PlayActionState(string stateName, bool stateExists, int triggerHash, bool triggerExists)
    {
        if (animator != null && stateExists)
        {
            string statePath = GetStatePath(stateName);
            activeActionStateHash = Animator.StringToHash(statePath);
            activeActionStartedFrame = Time.frameCount;
            activeActionReturnsToIdle =
                (!string.IsNullOrEmpty(attackStateName) && stateName == attackStateName) ||
                (!string.IsNullOrEmpty(dashStateName) && stateName == dashStateName);
            isAttackActionPlaying = !string.IsNullOrEmpty(attackStateName) && stateName == attackStateName;
            isDashActionPlaying = activeActionReturnsToIdle;
            animator.Play(statePath, 0, 0f);
            return;
        }

        activeActionStateHash = 0;
        activeActionStartedFrame = 0;
        activeActionReturnsToIdle = false;
        isAttackActionPlaying = false;
        isDashActionPlaying = false;
        SetTriggerIfExists(triggerHash, triggerExists);
    }

    private bool IsActionStatePlaying()
    {
        if (animator == null || activeActionStateHash == 0)
        {
            return false;
        }

        if (activeActionStartedFrame == Time.frameCount)
        {
            return true;
        }

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextStateInfo = animator.GetNextAnimatorStateInfo(0);
            return nextStateInfo.fullPathHash == activeActionStateHash;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

        if (stateInfo.fullPathHash != activeActionStateHash)
        {
            bool shouldReturnToIdle = activeActionReturnsToIdle;
            activeActionStateHash = 0;
            activeActionStartedFrame = 0;
            activeActionReturnsToIdle = false;
            isAttackActionPlaying = false;
            isDashActionPlaying = false;

            if (shouldReturnToIdle)
            {
                PlayLocomotionState(LocomotionAnimationState.Idle, locomotionTransitionTime);
            }

            return false;
        }

        if (stateInfo.normalizedTime >= 0.95f)
        {
            bool shouldReturnToIdle = activeActionReturnsToIdle;
            activeActionStateHash = 0;
            activeActionStartedFrame = 0;
            activeActionReturnsToIdle = false;
            isAttackActionPlaying = false;
            isDashActionPlaying = false;

            if (shouldReturnToIdle)
            {
                PlayLocomotionState(LocomotionAnimationState.Idle, locomotionTransitionTime);
            }

            return false;
        }

        return true;
    }

    private void PlayJumpStart()
    {
        if (hasJumpStartState)
        {
            PlayState(jumpStartStateName, actionTransitionTime);
            jumpAnimationState = JumpAnimationState.Start;
            return;
        }

        if (hasJumpLoopState)
        {
            PlayState(jumpLoopStateName, actionTransitionTime);
            jumpAnimationState = JumpAnimationState.Loop;
            return;
        }

        SetTriggerIfExists(JumpHash, hasJumpParameter);
    }

    private void UpdateJumpAnimation()
    {
        if (animator == null || jumpAnimationState == JumpAnimationState.Grounded)
        {
            return;
        }

        bool grounded = controller.isGrounded;

        if (jumpAnimationState == JumpAnimationState.Start && IsCurrentStatePast(jumpStartToLoopTime))
        {
            if (hasJumpLoopState)
            {
                PlayState(jumpLoopStateName, jumpTransitionTime);
            }

            jumpAnimationState = JumpAnimationState.Loop;
            return;
        }

        if (!grounded)
        {
            return;
        }

        if (jumpAnimationState == JumpAnimationState.Start || jumpAnimationState == JumpAnimationState.Loop)
        {
            if (hasJumpEndState)
            {
                PlayState(jumpEndStateName, jumpTransitionTime);
                jumpAnimationState = JumpAnimationState.End;
                return;
            }

            jumpAnimationState = JumpAnimationState.Grounded;
            PlayLocomotionState(LocomotionAnimationState.Idle, locomotionTransitionTime);
            return;
        }

        if (jumpAnimationState == JumpAnimationState.End && IsCurrentStateFinished())
        {
            jumpAnimationState = JumpAnimationState.Grounded;
            PlayLocomotionState(LocomotionAnimationState.Idle, locomotionTransitionTime);
        }
    }

    private void PlayState(string stateName, float transitionTime)
    {
        string statePath = GetStatePath(stateName);

        if (transitionTime <= 0f)
        {
            animator.Play(statePath, 0, 0f);
        }
        else
        {
            animator.CrossFadeInFixedTime(statePath, transitionTime, 0, 0f);
        }
    }

    private void SetTriggerIfExists(int parameterHash, bool parameterExists)
    {
        if (animator != null && parameterExists)
        {
            animator.SetTrigger(parameterHash);
        }
    }

    private Vector2 GetMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return Vector2.zero;
        }

        float x = ReadAxis(keyboard.aKey, keyboard.dKey);
        float y = ReadAxis(keyboard.sKey, keyboard.wKey);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    private Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse == null ? Vector2.zero : mouse.delta.ReadValue();
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }

    private bool GetJumpDown()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private bool GetEscapeDown()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private bool GetLeftMouseDown()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private bool GetRightMouseDown()
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(1);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private float ReadAxis(ButtonControl negative, ButtonControl positive)
    {
        float value = 0f;

        if (negative.isPressed)
        {
            value -= 1f;
        }

        if (positive.isPressed)
        {
            value += 1f;
        }

        return value;
    }
#endif
}
