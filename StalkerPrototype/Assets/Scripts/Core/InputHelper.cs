using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// One place for all keyboard/mouse reading, so the rest of the code stays simple.
/// Unity has two input systems (old and new). This works with whichever one is turned on.
/// You don't need to touch this file.
/// </summary>
public static class InputHelper
{
#if ENABLE_INPUT_SYSTEM
    // ----- New Input System -----
    static Keyboard K => Keyboard.current;
    static Mouse M => Mouse.current;

    public static Vector2 Move()
    {
        if (K == null) return Vector2.zero;
        float x = (K.dKey.isPressed ? 1 : 0) - (K.aKey.isPressed ? 1 : 0);
        float y = (K.wKey.isPressed ? 1 : 0) - (K.sKey.isPressed ? 1 : 0);
        return new Vector2(x, y);
    }
    public static Vector2 Look() => M == null ? Vector2.zero : M.delta.ReadValue() * 0.1f;
    public static bool Sprint() => K != null && K.leftShiftKey.isPressed;
    public static bool Crouch() => K != null && (K.leftCtrlKey.isPressed || K.cKey.isPressed);
    public static bool JumpPressed() => K != null && K.spaceKey.wasPressedThisFrame;
    public static bool EscapePressed() => K != null && K.escapeKey.wasPressedThisFrame;
    public static bool ClickPressed() => M != null && M.leftButton.wasPressedThisFrame;
    public static bool DebugTogglePressed() => K != null && K.f1Key.wasPressedThisFrame;
    public static bool InteractPressed() => K != null && K.eKey.wasPressedThisFrame;
    public static bool RestartPressed() => K != null && K.rKey.wasPressedThisFrame;
    public static bool GrainTogglePressed() => K != null && K.gKey.wasPressedThisFrame;
#else
    // ----- Old Input Manager -----
    public static Vector2 Move() => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    public static Vector2 Look() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
    public static bool Sprint() => Input.GetKey(KeyCode.LeftShift);
    public static bool Crouch() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
    public static bool JumpPressed() => Input.GetKeyDown(KeyCode.Space);
    public static bool EscapePressed() => Input.GetKeyDown(KeyCode.Escape);
    public static bool ClickPressed() => Input.GetMouseButtonDown(0);
    public static bool DebugTogglePressed() => Input.GetKeyDown(KeyCode.F1);
    public static bool InteractPressed() => Input.GetKeyDown(KeyCode.E);
    public static bool RestartPressed() => Input.GetKeyDown(KeyCode.R);
    public static bool GrainTogglePressed() => Input.GetKeyDown(KeyCode.G);
#endif
}
