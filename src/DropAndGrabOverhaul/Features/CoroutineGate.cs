using System.Collections;
using UnityEngine;

namespace DropAndGrabOverhaul.Features;

// Guarantees at most one instance of a routine runs at a time.
//
// The "running" flag is set before the coroutine starts and cleared in a finally inside the
// wrapper, rather than being derived from the Coroutine handle StartCoroutine returns. A routine
// that finishes without ever yielding (e.g. every item skipped) completes *inside* StartCoroutine,
// before its handle can be stored - a gate built on that handle would then be left permanently
// "busy". The gate belongs to the UpdateRunner instance, so it also dies with the runner.
internal sealed class CoroutineGate
{
    private bool running;

    public bool IsRunning => running;

    public void Start(MonoBehaviour host, IEnumerator routine)
    {
        if (running || !host.isActiveAndEnabled)
            return;

        running = true;
        try
        {
            host.StartCoroutine(Run(routine));
        }
        catch
        {
            running = false;
            throw;
        }
    }

    private IEnumerator Run(IEnumerator routine)
    {
        try
        {
            while (routine.MoveNext())
                yield return routine.Current;
        }
        finally
        {
            running = false;
        }
    }
}
