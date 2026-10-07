using System;
using Sandbox.ModAPI;

namespace HDRClientRenderer.Input
{
    // Owns the input session independently of renderer readiness/epochs. GPU reset,
    // settings changes and render-thread replacement must not revoke a mouse lease.
    internal sealed class PointerServices : IDisposable
    {
        internal const int RetryFrames = 60;
        readonly Action<string> report;
        object session;
        PointerProviderNative provider;
        int retryFrames, diagnostics;
        bool disposed;

        internal PointerServices(Action<string> report) { this.report = report; }

        internal void Update()
        {
            if (disposed) return;
            object current;
            try { current = MyAPIGateway.Session; }
            catch { LeaveSession(false); return; }
            if (!ReferenceEquals(session, current))
            {
                LeaveSession(true); session = current; retryFrames = 0;
            }
            if (session == null) return;
            if (provider == null)
            {
                if (retryFrames > 0) { retryFrames--; return; }
                retryFrames = RetryFrames;
                // No provider constructor/event attachment while utilities are
                // unavailable. Retry at most once per 60 plugin updates.
                try
                {
                    if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
                    provider = new PointerProviderNative();
                }
                catch (Exception error) { Report("pointer provider unavailable: " + error.GetType().Name); return; }
            }
            try { provider.Update(); }
            catch (Exception error)
            {
                Report("pointer provider update failed: " + error.GetType().Name);
                ReleaseProvider(false); retryFrames = RetryFrames;
            }
        }

        void Report(string message)
        {
            if (report == null || diagnostics >= 8) return;
            diagnostics++;
            try { report(message); } catch { }
        }
        void ReleaseProvider(bool worldGone)
        {
            var previous = provider; provider = null;
            if (previous == null) return;
            // The provider revokes before external withdrawal callbacks. Its own
            // GUI lifecycle drains held input after this manager drops the owner.
            try { if (worldGone) previous.LeaveSession(); }
            catch (Exception error) { Report("pointer world exit failed: " + error.GetType().Name); }
            finally
            {
                try { previous.Dispose(); }
                catch (Exception error) { Report("pointer provider disposal failed: " + error.GetType().Name); }
            }
        }
        void LeaveSession(bool worldGone)
        {
            ReleaseProvider(worldGone); session = null; retryFrames = 0;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; LeaveSession(false);
        }
    }
}
