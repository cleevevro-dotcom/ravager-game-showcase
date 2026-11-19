using UnityEngine;

/// <summary>
/// Samples desktop input (WASD/arrow keys + mouse button 0) every frame and publishes
/// combined values (desktop | mobile) to <see cref="InputService"/>. Runs very early so
/// movement and attack components see up-to-date values.
/// </summary>
[DefaultExecutionOrder(-100)]
public class InputDriver : MonoBehaviour
{
    void Update()
    {
        // Desktop axes.
        Vector2 kb = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical"));
        if (kb.sqrMagnitude > 1f) kb.Normalize();

        Vector2 mobile = InputService.MobileMove;
        Vector2 combined = kb.sqrMagnitude > 0.001f ? kb : mobile;
        if (combined.sqrMagnitude > 1f) combined.Normalize();
        InputService.MoveAxis = combined;

        // ATTACK INPUT
        // On Android/iOS Unity synthesizes mouse-button-0 from EVERY active touch — including
        // the left-side virtual joystick. That used to fire attack while the player was just
        // moving. So when we're on a touch platform OR there is any active touch right now,
        // we skip the desktop-mouse path entirely and rely solely on MobileAttackHeld, which
        // is set by the right-side AutoAttackTouchZone on its own OnPointerDown / OnPointerUp.
        bool desktopAttack = false;
        bool useDesktopMouse = !Application.isMobilePlatform && Input.touchCount == 0;
        if (useDesktopMouse)
        {
            desktopAttack = Input.GetMouseButton(0);
            // Don't fire when the click started over UI (joystick zone, attack zone, mutation
            // buttons, etc.) — the AutoAttackTouchZone path will pick it up if it really was on
            // the attack side.
            if (desktopAttack && UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                desktopAttack = false;
            }
        }
        InputService.AttackHeld = desktopAttack || InputService.MobileAttackHeld;
    }
}
