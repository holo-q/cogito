namespace Cogito;

using System.Threading;
using VTR;

// ── THE ELEVATOR ──  every phase, transition, and slow-step is telegraphed through VTR's
// structured trace (the live StderrSink + a JournaldSink so `spacejn cogito` reconstructs the
// span tree). "No trace, no elevator." This is the ONE emit seam cogito routes its narrative
// through — a central facade rather than a per-class logger, because the drive is read as one
// coherent story across subsystems, and the loop instrumentation wants a shared vocabulary.
//
// House law (one static readonly TraceLogger per component) is honored here: each subsystem gets
// exactly one logger, resolved once, in this header. `Note` is the freeform progress telegraph
// (every existing call site keeps working, now landing in the elevator instead of raw stdout —
// progress belongs on the trace plane, payload/reports stay on Console); `Phase` is the drive-loop
// probe (a live per-phase span paired with a GcProbe bracket); the slow-<unit> tripwire is a Warn
// on the subsystem logger (Warn bypasses the exclude filter, so the reaper still fires when the
// per-phase span plane is muted for a long detached drive).

public static class Trace
{
    // One component per subsystem — the dotted names `spacejn cogito | rg <sub>` filters on.
    public static readonly TraceLogger Root       = TraceLogger.Get("cogito");
    public static readonly TraceLogger Mesh       = TraceLogger.Get("cogito.mesh");
    public static readonly TraceLogger Cortex     = TraceLogger.Get("cogito.cortex");
    public static readonly TraceLogger Intake     = TraceLogger.Get("cogito.intake");
    public static readonly TraceLogger CritLock   = TraceLogger.Get("cogito.critlock");
    public static readonly TraceLogger Seriate    = TraceLogger.Get("cogito.seriate");
    public static readonly TraceLogger Engine     = TraceLogger.Get("cogito.engine");
    public static readonly TraceLogger Energy     = TraceLogger.Get("cogito.energy");
    public static readonly TraceLogger Gret       = TraceLogger.Get("cogito.gret");

    // Per-phase managed-byte probes — DARK by default (they ride VTR's telemetry plane, dropped
    // unless the component is listed in `telemetry.planes`). A perf hunt arms `cogito.mesh.gcprobe`
    // / `cogito.critlock.gcprobe` in vtr.conf and gets one `B/step` (`B/round`) readout per window,
    // per phase, loudest-first. When dark, Begin/Record are no-ops — the loop pays nothing.
    public static readonly GcProbe MeshGc     = new("cogito.mesh.gcprobe",      iterationUnit: "step");
    public static readonly GcProbe CortexGc   = new("cogito.cortex.gcprobe",    iterationUnit: "step");
    public static readonly GcProbe CritLockGc = new("cogito.critlock.gcprobe",  iterationUnit: "round");

    // A per-step (per-round) is "suspiciously slow" past this many ms — the frame.slow-style
    // reaper. Fixed for now (a drive over a small tape should be fast; the O(n²) re-induce wall
    // this is built to catch will cross it as the tape grows — the crash-out protocol's
    // "wall-time spike in INDUCE" alarm). Tunable to a config knob when the drive is fused.
    public const long StepSlowMs = 500;

    private static int _inited;

    /// Install the sinks once (idempotent). VTR defaults to a single StderrSink; this fans it out
    /// to a journald sink under the `cogito` identifier so `spacejn cogito` sees the span tree.
    /// Journald is a BONUS — if libsystemd is absent the add is swallowed and stderr stands alone.
    public static void Init()
    {
        if (Interlocked.Exchange(ref _inited, 1) != 0) return;
        try { TraceSinks.Add(new JournaldSink("cogito")); }
        catch { /* no libsystemd → stderr-only; the trace still telegraphs, just not to journald */ }
    }

    /// Monotonic wall-seconds for the GcProbe flush window — VTR's own clock, so no bare Stopwatch
    /// leaks into a consumer (the timing vocabulary stays inside VTR, log-hygiene doctrine).
    public static double NowSeconds => TraceClock.TimestampTicks() / (double)TraceClock.Frequency;

    /// Monotonic tick stamp for the step/round slow-tripwire — pair a top-of-loop `NowTicks` with an
    /// end-of-loop `ElapsedMs` so the drive bodies never touch VTR directly (everything via Trace).
    public static long NowTicks => TraceClock.TimestampTicks();
    public static long ElapsedMs(long startTicks) => TraceClock.ElapsedMilliseconds(startTicks);
    public static double ElapsedMsPrecise(long startTicks)
        => (TraceClock.TimestampTicks() - startTicks) * 1000.0 / TraceClock.Frequency;
    public static double ElapsedMsPrecise(long startTicks, long endTicks)
        => (endTicks - startTicks) * 1000.0 / TraceClock.Frequency;
    /// Tick-SUM → ms, for accumulated brackets (a fill/eval split summed across a loop) — same clock as NowTicks.
    public static long MsOf(long ticks) => ticks * 1000 / TraceClock.Frequency;

    /// The narrative telegraph — every drive progress/phase line routes here (root component, Event
    /// kind), landing in the live stderr trace + journald. Reports (the verbs' payload) stay on
    /// Console; this carries the story, not the product.
    public static void Note(string message) => Root.Event(message);

    /// One drive-loop phase: open a live VTR span (the per-phase wall, visible when the plane is
    /// armed) and bracket the per-phase GcProbe delta. `Dispose` closes both, attributing bytes to
    /// `key`. `boundary` marks the step/round's LAST phase so the probe's iteration count advances
    /// exactly once per step. Two-line open/Dispose (over `using(){}`) keeps the loop diff a pair of
    /// single-line inserts — the drive bodies are edited by parallel workers, so no re-indentation.
    public static PhaseScope MeshPhase(string key, bool boundary = false)     => new(Mesh, MeshGc, key, boundary);
    public static PhaseScope CortexPhase(string key, bool boundary = false)   => new(Cortex, CortexGc, key, boundary);
    public static PhaseScope CritLockPhase(string key, bool boundary = false) => new(CritLock, CritLockGc, key, boundary);
}

/// A drive-loop phase probe — a live trace span paired with a GcProbe bracket, closed together.
/// The GC baseline is captured AFTER the span opens and recorded BEFORE it closes, so the delta is
/// the phase body's own allocation, not the span machinery's.
public readonly struct PhaseScope : IDisposable
{
    private readonly IDisposable _span;
    private readonly GcProbe     _gc;
    private readonly string      _key;
    private readonly long        _base;
    private readonly bool        _boundary;

    internal PhaseScope(TraceLogger log, GcProbe gc, string key, bool boundary)
    {
        _gc       = gc;
        _key      = key;
        _boundary = boundary;
        _span     = log.Span(key);
        _base     = gc.Begin();
    }

    public void Dispose()
    {
        _gc.Record(_key, _base, Trace.NowSeconds, _boundary);
        _span.Dispose();
    }
}
