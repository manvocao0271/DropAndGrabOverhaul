namespace DropAndGrabOverhaul.Inputs;

internal enum DropGesture
{
    None,
    Tap,                // first press: drop the held item
    DoubleTap,          // second press within the window: drop the rest of the eligible items
    ForceDrop,          // held for ForceDropHold: main hotbar, ignoring the blacklist (level-triggered)
    ForceDropReserved,  // held for ForceDropHold + ReservedSlotsHold: also reserved slots (level-triggered)
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

// Turns raw key state into a DropGesture. No Unity dependencies (time and key state are passed
// in), so it is unit-testable. Call Update exactly once per frame.
internal sealed class DropGestureTracker
{
    private float lastTapTime = float.NegativeInfinity;
    private bool holding;
    private float holdStartTime;

    // Set by Reset while the key is still down: blocks any gesture until it is genuinely
    // released, so a hold that outlives whatever consumed the press (desk auto-place) can't
    // accumulate into an unintended force drop.
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
                // The press that began this hold was recorded as a tap; don't let it pair with
                // a tap after release into a double-tap.
                lastTapTime = float.NegativeInfinity;
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

    // Forgets any in-progress tap or hold, for when something else (desk auto-place) consumed the key.
    public void Reset(bool keyIsDown)
    {
        holding = false;
        lastTapTime = float.NegativeInfinity;
        suppressUntilReleased = keyIsDown;
    }
}
