using System;

namespace Jint
{
    /// <summary>HDR source-port accounting for the single-threaded Workshop host. No CLR memory or thread APIs are used.</summary>
    public static class HdrExecutionGuard
    {
        internal static Engine ActiveEngine;

        public static void Step()
        {
            if (ActiveEngine != null) { ActiveEngine.HdrStep(); ActiveEngine.HdrCharge(8); }
        }

        public static void CheckString(long characters)
        {
            var engine = ActiveEngine;
            if (engine == null) return;
            if (characters < 0 || characters > engine.HdrMaxStringCharacters)
                throw new InvalidOperationException("HDR JavaScript: string size limit exceeded.");
        }

        public static void ChargeString(int characters)
        {
            CheckString(characters);
            if (ActiveEngine != null) ActiveEngine.HdrCharge(24L + 2L * characters);
        }

        public static void CheckParseDepth(int depth)
        {
            if (depth > (ActiveEngine == null ? 128 : ActiveEngine.HdrMaxParseDepth))
                throw new InvalidOperationException("HDR JavaScript: parser depth limit exceeded.");
        }
    }
}
