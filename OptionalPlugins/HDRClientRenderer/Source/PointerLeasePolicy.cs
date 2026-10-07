using System;
using System.Threading;

namespace HDRClientRenderer.Input
{
    [Flags]
    public enum PointerFlags
    {
        None = 0, Pending = 1, Applied = 2, Cancelled = 4, Guardian = 8,
        PointerValid = 16, Unavailable = 32, Disposed = 64
    }

    public enum PointerCancelReason
    {
        None, Released, Escape, ToolChange, FocusLost, PlayerLost, SharedInput,
        ScreenLost, Timeout, PointerInvalid, HeldDuringEntry, Disposed, ClockInvalid
    }

    // Values are captured from the native engine on the game thread. This policy
    // does not end tool firing, change shared input, or infer routing from AddScreen.
    public struct PointerEnvironment
    {
        public bool WindowActive, PlayerReady, GameplayFocused, OwnFocused, SharedInput;
        public bool OwnScreenClosing, EscapePressed, ToolExitPressed, NeutralTrigger;
        public bool PrimaryToolHeld, SecondaryToolHeld, PointerValid, InputObserved, ExistingToolShooting;
        public int Buttons;
        public double X, Y;
        public long Frame;
        public bool Neutral { get { return InputObserved && Buttons == 0 && !PrimaryToolHeld && !SecondaryToolHeld; } }
    }

    public struct PointerSnapshot
    {
        public long Frame;
        public PointerFlags Flags;
        public double X, Y;
        public int Pressed, Held, Released;
    }

    public sealed class PointerLeasePolicy
    {
        public const double PendingTimeout = 0.75;
        public const double PollTimeout = 1.0;
        public const double RoutingFreshness = 0.125;
        public const int ButtonMask = 31;
        enum Phase { Idle, Pending, Applied, Guardian }
        Phase phase;
        static long generation = DateTime.UtcNow.Ticks;
        long token, owner;
        string key;
        double acquiredAt, polledAt, routedAt = double.NegativeInfinity, observedAt;
        bool disposed, hasTime;
        int previousButtons;
        PointerSnapshot snapshot;
        public PointerCancelReason CancelReason { get; private set; }
        public bool NeedsScreen { get { return phase != Phase.Idle; } }
        public bool IsGuardian { get { return phase == Phase.Guardian; } }
        public bool IsDisposed { get { return disposed; } }
        public long Token { get { return token; } }
        public long Owner { get { return owner; } }
        public string Key { get { return key; } }

        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static bool ValidPointer(PointerEnvironment e)
        { return e.PointerValid && Finite(e.X) && Finite(e.Y) && e.X >= 0 && e.X <= 1 && e.Y >= 0 && e.Y <= 1; }
        bool Time(double now, PointerEnvironment e)
        {
            if (!Finite(now) || now < 0 || (hasTime && now < observedAt))
            { Cancel(PointerCancelReason.ClockInvalid, e); return false; }
            observedAt = now; hasTime = true; return true;
        }
        PointerCancelReason Safety(PointerEnvironment e)
        {
            if (!e.PlayerReady) return PointerCancelReason.PlayerLost;
            if (!e.WindowActive) return PointerCancelReason.FocusLost;
            if (e.SharedInput) return PointerCancelReason.SharedInput;
            if (!e.InputObserved) return PointerCancelReason.PointerInvalid;
            if (e.OwnScreenClosing) return PointerCancelReason.ScreenLost;
            if (e.EscapePressed) return PointerCancelReason.Escape;
            if (e.ToolExitPressed) return PointerCancelReason.ToolChange;
            if (e.ExistingToolShooting) return PointerCancelReason.ToolChange;
            return PointerCancelReason.None;
        }

        public long Acquire(long requestedOwner, string requestedKey, double now, PointerEnvironment e)
        {
            if (!Time(now, e) || disposed || phase != Phase.Idle || requestedOwner <= 0 ||
                string.IsNullOrWhiteSpace(requestedKey) || requestedKey.Length > 128 ||
                Safety(e) != PointerCancelReason.None || !e.GameplayFocused || !e.NeutralTrigger || !e.Neutral ||
                (e.Buttons & ~ButtonMask) != 0 || generation == long.MaxValue) return 0;
            long issued = Interlocked.Increment(ref generation);
            if (issued <= 0) return 0;
            token = issued; owner = requestedOwner; key = requestedKey;
            acquiredAt = polledAt = now; routedAt = double.NegativeInfinity;
            previousButtons = 0; snapshot = default(PointerSnapshot);
            CancelReason = PointerCancelReason.None; phase = Phase.Pending;
            return token;
        }

        public void Update(double now, PointerEnvironment e)
        {
            if (!Time(now, e) || phase == Phase.Idle || phase == Phase.Guardian) return;
            var reason = Safety(e);
            if (reason != PointerCancelReason.None) { Cancel(reason, e); return; }
            if ((e.Buttons & ~ButtonMask) != 0) { Cancel(PointerCancelReason.PointerInvalid, e); return; }
            if (phase == Phase.Applied && !ValidPointer(e)) { Cancel(PointerCancelReason.PointerInvalid, e); return; }
            if (phase == Phase.Pending)
            {
                // The ordinary gameplay screen may keep focus while AddScreen is queued.
                if (!e.OwnFocused && !e.GameplayFocused) { Cancel(PointerCancelReason.ScreenLost, e); return; }
                if (!e.Neutral) { Cancel(PointerCancelReason.HeldDuringEntry, e); return; }
                if (now - acquiredAt >= PendingTimeout) Cancel(PointerCancelReason.Timeout, e);
            }
            else if (!e.OwnFocused) Cancel(PointerCancelReason.ScreenLost, e);
            if (phase == Phase.Applied && now - polledAt >= PollTimeout) Cancel(PointerCancelReason.Timeout, e);
        }

        // Called only by this lease's MyGuiScreenBase.HandleInput after the native
        // adapter verifies GetScreenWithFocus() is this exact screen.
        public void RoutedInput(double now, PointerEnvironment e)
        {
            Update(now, e);
            if (phase == Phase.Guardian)
            {
                if (e.OwnFocused && e.WindowActive && e.Neutral && !e.SharedInput)
                    phase = Phase.Idle;
                return;
            }
            if (phase == Phase.Idle || !e.OwnFocused) return;
            if (!ValidPointer(e)) { Cancel(PointerCancelReason.PointerInvalid, e); return; }
            if (phase == Phase.Pending && !e.Neutral) { Cancel(PointerCancelReason.HeldDuringEntry, e); return; }
            if (phase == Phase.Pending) { phase = Phase.Applied; previousButtons = 0; }
            routedAt = now;
            snapshot.Frame = e.Frame; snapshot.X = e.X; snapshot.Y = e.Y;
            snapshot.Pressed = e.Buttons & ~previousButtons;
            snapshot.Held = e.Buttons;
            snapshot.Released = previousButtons & ~e.Buttons;
            snapshot.Flags = PointerFlags.Applied | PointerFlags.PointerValid;
            previousButtons = e.Buttons;
        }

        public PointerSnapshot Sample(long requestedToken, double now, PointerEnvironment e)
        {
            Update(now, e);
            if (requestedToken <= 0 || requestedToken != token)
                return new PointerSnapshot { Frame = e.Frame, Flags = PointerFlags.Unavailable | PointerFlags.Cancelled };
            if (phase == Phase.Applied)
            {
                if (now - routedAt > RoutingFreshness || !e.OwnFocused || e.SharedInput || !e.WindowActive)
                { Cancel(PointerCancelReason.ScreenLost, e); return Status(e.Frame); }
                polledAt = now; return snapshot;
            }
            if (phase == Phase.Pending) polledAt = now;
            return Status(e.Frame);
        }

        PointerSnapshot Status(long frame)
        {
            var flags = phase == Phase.Pending ? PointerFlags.Pending : PointerFlags.Cancelled;
            if (phase == Phase.Guardian) flags |= PointerFlags.Guardian;
            if (disposed) flags |= PointerFlags.Disposed;
            return new PointerSnapshot { Frame = frame, Flags = flags };
        }
        public bool Release(long requestedToken, double now, PointerEnvironment e)
        {
            if (requestedToken <= 0 || requestedToken != token) return false;
            Time(now, e);
            if (phase == Phase.Pending || phase == Phase.Applied) Cancel(PointerCancelReason.Released, e);
            return true;
        }
        public void Cancel(PointerCancelReason reason, PointerEnvironment e)
        {
            if (phase == Phase.Idle || phase == Phase.Guardian) return;
            CancelReason = reason; snapshot = default(PointerSnapshot); previousButtons = 0;
            // Always wait for one exclusive routed neutral observation before closing.
            // A release request between native input frames must not drop a held click.
            phase = Phase.Guardian;
        }
        public void Dispose(double now, PointerEnvironment e)
        {
            if (disposed) return;
            disposed = true; Time(now, e); Cancel(PointerCancelReason.Disposed, e);
        }
        // World unload has no live gameplay tool route to guard; the adapter can
        // remove its own screen after this terminal transition.
        public void WorldGone()
        {
            if (phase != Phase.Idle) CancelReason = PointerCancelReason.PlayerLost;
            phase = Phase.Idle; snapshot = default(PointerSnapshot); previousButtons = 0;
        }
    }
}
