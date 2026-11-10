using UnityEngine;

/// <summary>
/// Simple first-person movement: WASD to move, mouse to look, Shift to sprint,
/// Ctrl or C to crouch, Space to jump. It also tells the AI how much noise you're making.
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(PlayerAIState))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Speed (metres per second)")]
    public float walkSpeed = 3.5f;
    public float sprintSpeed = 6f;
    public float crouchSpeed = 1.6f;

    [Header("Stamina (sprinting is limited, so use it wisely)")]
    [Tooltip("Seconds of sprinting on a full bar.")]
    public float maxSprintSeconds = 5f;
    [Tooltip("Seconds of stamina refilled per second while not sprinting.")]
    public float staminaRegenPerSecond = 0.6f;
    [Tooltip("After running out, you must recover this much (seconds) before you can sprint again.")]
    public float minStaminaToSprint = 1.5f;
    /// <summary>0 = empty, 1 = full. The HUD draws this.</summary>
    public float Stamina01 => stamina / maxSprintSeconds;

    [Header("Mouse")]
    public float lookSensitivity = 2f;

    [Header("Body")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.1f;
    public float jumpHeight = 1f;
    public float gravity = -20f;

    [Header("Noise (0 = silent, 1 = very loud). Monsters can hear this.")]
    [Range(0f, 1f)] public float walkNoise = 0.4f;
    [Range(0f, 1f)] public float sprintNoise = 1f;
    [Range(0f, 1f)] public float crouchNoise = 0.1f;

    [Tooltip("True while hiding: you can look around but not move.")]
    public bool movementLocked;

    CharacterController controller;
    PlayerAIState ai;
    Transform cam;
    float pitch;              // looking up/down
    float verticalVelocity;   // falling/jumping speed
    float stamina;
    bool exhausted;           // ran out: can't sprint until recovered a bit

    void Awake()
    {
        stamina = maxSprintSeconds;
        controller = GetComponent<CharacterController>();
        ai = GetComponent<PlayerAIState>();
        cam = ai.playerCamera != null ? ai.playerCamera.transform : GetComponentInChildren<Camera>().transform;
    }

    void Start() => SetCursorLocked(true);

    // Update runs once every frame (about 60+ times a second).
    void Update()
    {
        // --- Mouse lock: Esc frees the mouse, clicking captures it again ---
        if (InputHelper.EscapePressed()) SetCursorLocked(false);
        if (InputHelper.ClickPressed() && !NetSession.IsPointerOverButtons()) SetCursorLocked(true);   // (not when clicking Leave / Copy code)

        // --- Looking around ---
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 look = InputHelper.Look() * lookSensitivity;
            transform.Rotate(0f, look.x, 0f);                       // turn the whole body left/right
            pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);         // tilt only the camera up/down
        }

        // --- While hiding: only looking around, no moving and no noise ---
        if (movementLocked)
        {
            cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            ai.moveState = MoveState.Still;
            ai.noiseLevel = 0f;
            return;
        }

        // --- Crouching: shrink the body and lower the camera smoothly ---
        bool crouching = InputHelper.Crouch();
        bool wantsToMove = InputHelper.Move().sqrMagnitude > 0.01f;
        bool sprinting = !crouching && InputHelper.Sprint() && wantsToMove && !exhausted;

        // --- Stamina: sprinting drains it, everything else refills it ---
        if (sprinting) stamina -= Time.deltaTime;
        else stamina += staminaRegenPerSecond * Time.deltaTime;
        stamina = Mathf.Clamp(stamina, 0f, maxSprintSeconds);
        if (stamina <= 0f) exhausted = true;
        if (exhausted && stamina >= minStaminaToSprint) exhausted = false;
        if (exhausted) sprinting = false;
        float targetHeight = crouching ? crouchHeight : standHeight;
        controller.height = Mathf.Lerp(controller.height, targetHeight, 10f * Time.deltaTime);
        controller.center = new Vector3(0f, controller.height / 2f, 0f);
        cam.localPosition = new Vector3(0f, controller.height - 0.2f, 0f);
        cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // --- Walking ---
        Vector2 input = Vector2.ClampMagnitude(InputHelper.Move(), 1f);
        float speed = crouching ? crouchSpeed : sprinting ? sprintSpeed : walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

        // --- Gravity and jumping ---
        if (controller.isGrounded)
        {
            verticalVelocity = -2f;   // small push down keeps us stuck to the floor
            if (InputHelper.JumpPressed() && !crouching)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        // Time.deltaTime = seconds since last frame, so speed is the same at any frame rate.
        controller.Move(move * Time.deltaTime);

        // --- Tell the AI what we're doing ---
        bool moving = input.sqrMagnitude > 0.01f;
        ai.moveState = !moving ? MoveState.Still : crouching ? MoveState.Crouch : sprinting ? MoveState.Sprint : MoveState.Walk;
        ai.noiseLevel = ai.moveState switch
        {
            MoveState.Crouch => crouchNoise,
            MoveState.Walk => walkNoise,
            MoveState.Sprint => sprintNoise,
            _ => 0f,
        };
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
