using System;

namespace HDRJavaScriptRuntime
{
    /// <summary>Configurable interpreter safeguards. Allocation units are conservative accounting, not CLR heap bytes.</summary>
    public sealed class JavaScriptLimits
    {
        public int MaxStatements = 50000;
        public int MaxOperations = 100000;
        public int MaxRecursionDepth = 64;
        public int MaxSourceCharacters = 65536;
        public int MaxParseDepth = 128;
        public int MaxStringCharacters = 65536;
        public int MaxArrayLength = 8192;
        public int MaxObjectProperties = 2048;
        public int MaxCreatedObjects = 16384;
        public long MaxAllocationUnitsPerOperation = 8 * 1024 * 1024;
        public long MaxAllocationUnitsPerRealm = 64 * 1024 * 1024;
        public int MaxHostCallsPerOperation = 256;
        public double TimeoutMilliseconds = 25;

        public JavaScriptLimits Copy()
        {
            return new JavaScriptLimits {
                MaxStatements=MaxStatements, MaxOperations=MaxOperations, MaxRecursionDepth=MaxRecursionDepth,
                MaxSourceCharacters=MaxSourceCharacters, MaxParseDepth=MaxParseDepth,
                MaxStringCharacters=MaxStringCharacters, MaxArrayLength=MaxArrayLength,
                MaxObjectProperties=MaxObjectProperties, MaxCreatedObjects=MaxCreatedObjects,
                MaxAllocationUnitsPerOperation=MaxAllocationUnitsPerOperation,
                MaxAllocationUnitsPerRealm=MaxAllocationUnitsPerRealm,
                MaxHostCallsPerOperation=MaxHostCallsPerOperation, TimeoutMilliseconds=TimeoutMilliseconds
            };
        }

        public void Validate()
        {
            if (MaxStatements < 0 || MaxOperations < 0 || MaxRecursionDepth < 1 || MaxRecursionDepth > 256 ||
                MaxSourceCharacters < 1 || MaxParseDepth < 1 || MaxParseDepth > 256 || MaxStringCharacters < 1 ||
                MaxArrayLength < 1 || MaxObjectProperties < 1 || MaxCreatedObjects < 1 ||
                MaxAllocationUnitsPerOperation < 0 || MaxAllocationUnitsPerRealm < 0 || MaxHostCallsPerOperation < 0 ||
                double.IsNaN(TimeoutMilliseconds) || double.IsInfinity(TimeoutMilliseconds) || TimeoutMilliseconds < 0)
                throw new InvalidOperationException("Invalid JavaScript limits. Zero disables statement/operation/allocation/host-call/time quotas; structural size/depth limits must be positive.");
        }
    }
}
