namespace DropAndGrabOverhaul.Inputs;

// What the drop key is currently doing, as decided by DropGestureTracker.
internal enum DropGesture
{
    None,

    // First press of the key: drop the held item.
    Tap,

    // Second press within the double-tap window: drop the rest of the eligible items.
    DoubleTap,

    // Held for ForceDropHold: drop the main hotbar, ignoring the blacklist. Level-triggered.
    ForceDrop,

    // Held for ForceDropHold + ReservedSlotsHold: also drop reserved slots. Level-triggered.
    ForceDropReserved,
}

internal readonly struct DropTimings
{
    public DropTimings(float doubleTapWindow, float forceDropHold, float reservedSlotsHold)
    {
        DoubleTapWindow = doubleTapWindow;
        ForceDropHold = forceDropHold;
        ReservedSlotsHold = reservedSlotsHold;
    }

    public float DoubleTapWindow { get; }
    public float ForceDropHold { get; }
    public float ReservedSlotsHold { get; }
}

// Turns raw key state into a DropGesture. Owns all the timing state (last tap, hold start) and has
// no Unity dependencies - time and key state are passed in - so the logic can be unit-tested.
//
// Call Update exactly once per frame. Unlike the separate per-gesture queries this replaced, there
// is no ordering requirement between calls: one call yields one answer.
internal sealed class DropGestureTracker
{
    private float lastTapTime = float.NegativeInfinity;
    private bool holding;
    private float holdStartTime;

    // Set by Reset while the key is still physically down. Blocks any gesture until it has been
    // genuinely released, so a hold that carries on past whatever consumed the press (desk
    // auto-sell) can't accumulate into an unintended force drop.
    private bool suppressUntilReleased;

    public DropGesture Update(float now, bool pressedThisFrame, bool isDown, in DropTimings timings)
    {
        if (suppressUntilReleased)
        {
            if (isDown)
                return DropGesture.None;

            suppressUntilReleased = false;
        }

        if (isDown)
        {
            if (!holding)
            {
                holding = true;
                holdStartTime = now;
            }

            float held = now - holdStartTime;
            if (held >= timings.ForceDropHold)
            {
                return held >= timings.ForceDropHold + timings.ReservedSlotsHold
                    ? DropGesture.ForceDropReserved
                    : DropGesture.ForceDrop;
            }
        }
        else
        {
            holding = false;
        }

        if (!pressedThisFrame)
            return DropGesture.None;

        if (now - lastTapTime < timings.DoubleTapWindow)
        {
            lastTapTime = float.NegativeInfinity;
            return DropGesture.DoubleTap;
        }

        lastTapTime = now;
        return DropGesture.Tap;
    }

    // Forgets any in-progress tap or hold. Used when something else (desk auto-sell) consumed the
    // key, so timestamps from before that don't leak into the next gesture.
    public void Reset(bool keyIsDown)
    {
        holding = false;
        lastTapTime = float.NegativeInfinity;
        suppressUntilReleased = keyIsDown;
    }
}
