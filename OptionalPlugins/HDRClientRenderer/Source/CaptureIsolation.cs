using System;
using System.Collections.Generic;

namespace HDRClientRenderer
{
    // A capture receives a separate list; never remove or edit the main-view billboards.
    internal static class CaptureIsolation
    {
        internal static bool IsHdrMaterial(string name)
        {
            return name == "HoloMap_Fill" || name == "HoloMap_Line" ||
                name != null && (name.StartsWith("HDR_ClientRaster_", StringComparison.Ordinal) ||
                name.StartsWith("HDR_ClientLcd_", StringComparison.Ordinal) || name.StartsWith("HDR_ClientPanorama_", StringComparison.Ordinal) ||
                name.StartsWith("HDR_ClientPortal_", StringComparison.Ordinal));
        }

        internal static List<T> Filter<T>(List<T> source, bool capturing, Func<T, string> material)
            where T : class
        {
            if (!capturing || source == null) return source;
            List<T> result = null;
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item != null && IsHdrMaterial(material(item)))
                {
                    if (result == null)
                    {
                        result = new List<T>(source.Count);
                        for (int j = 0; j < i; j++) result.Add(source[j]);
                    }
                }
                else if (result != null) result.Add(item);
            }
            return result ?? source;
        }
    }
}
