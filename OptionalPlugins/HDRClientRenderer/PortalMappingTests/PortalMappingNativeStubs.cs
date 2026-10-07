namespace HDRClientRenderer
{
    // Mapping harness never begins native capture or allocates a GPU resource.
    internal static class DirectCameraCapture
    {internal static bool IsCapturing{get{return false;}}}
}
