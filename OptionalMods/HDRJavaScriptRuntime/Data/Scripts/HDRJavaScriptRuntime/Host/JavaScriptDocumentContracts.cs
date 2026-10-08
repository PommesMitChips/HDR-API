using System;

namespace HDRJavaScriptRuntime
{
    public sealed class JavaScriptDocumentStatus
    {
        public bool RendererReady;
        public long VisibleRevision;
        public string Error;
    }

    public sealed class JavaScriptDocumentEvent
    {
        public string Kind, NodeId, Action;
        public double Value;
        public long Revision;
    }

    public sealed class JavaScriptDocumentMutation
    { public string Kind, Key, Value; }

    /// <summary>A C# consumer grants only one already-owned HTML document; JavaScript never receives the endpoint or game objects.</summary>
    public interface IJavaScriptDocumentHost : IDisposable
    {
        long Generation { get; }
        bool Current { get; }
        JavaScriptDocumentStatus ReadStatus();
        JavaScriptDocumentEvent[] PollEvents();
        bool AllowsText(string nodeId);
        bool AllowsData(string key);
        void SetText(string nodeId, string value);
        void SetData(string key, string value);
        void ApplyMutations(JavaScriptDocumentMutation[] mutations);
        bool SourceReady(string choice, out string reason);
        void SelectSource(string choice);
    }

    /// <summary>Client host dispatch safeguards, independent of HDR geometry allowances and interpreter instruction limits.</summary>
    public sealed class JavaScriptHostLimits
    {
        public int CallbacksPerUpdate = 16;
        public int PendingEvents = 128;
        public int Timers = 64;
        public int EventHandlers = 128;
        public int MutationsPerDispatch = 64;
        public int TextCharacters = 16384;
        public double MaximumTimerDelayMs = 86400000;

        internal JavaScriptHostLimits Copy()
        {
            if (CallbacksPerUpdate < 1 || PendingEvents < 1 || Timers < 1 || EventHandlers < 1 ||
                MutationsPerDispatch < 1 || TextCharacters < 1 || !Finite(MaximumTimerDelayMs) || MaximumTimerDelayMs <= 0)
                throw new ArgumentException("JavaScript host limits require positive finite values.");
            return new JavaScriptHostLimits {
                CallbacksPerUpdate = CallbacksPerUpdate, PendingEvents = PendingEvents, Timers = Timers,
                EventHandlers = EventHandlers, MutationsPerDispatch = MutationsPerDispatch,
                TextCharacters = TextCharacters, MaximumTimerDelayMs = MaximumTimerDelayMs
            };
        }

        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
