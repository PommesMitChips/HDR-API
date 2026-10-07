using System;
using Sandbox.ModAPI;
using VRage;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        const string DisplayCapabilitiesVersion = "HDR.DisplayCapabilities/1";
        const int DisplaySupported = 1, DisplayNativeLcd = 2, DisplayVectorLcd = 4,
            DisplayFloating3D = 8, DisplayTableVolume = 16, DisplayProjectedSurfaces = 32, DisplayUi = 64;

        // Authorization reads access state without observing the caller or claiming a scene.
        // A PB runs on the server: these flags never assert another viewer's plugin support.
        MyTuple<string, string, int> GetDisplayCapabilities(PbBlock caller, PbBlock target)
        {
            var block = AuthorizeAccess(caller, target, false);
            var panel = block as IMyTextPanel;
            if (panel != null)
            {
                LcdScreenBasis basis;
                int flags = DisplaySupported | DisplayNativeLcd | DisplayUi;
                if (LcdScreenCalibration.TryGet(panel, out basis)) flags |= DisplayVectorLcd;
                return new MyTuple<string, string, int>(DisplayCapabilitiesVersion, "lcd", flags);
            }
            if (block is IMyProjector)
            {
                bool table = (block.BlockDefinition.SubtypeName ?? "").IndexOf("Console", StringComparison.OrdinalIgnoreCase) >= 0;
                int flags = DisplaySupported | DisplayFloating3D | DisplayProjectedSurfaces | DisplayUi;
                if (table) flags |= DisplayTableVolume;
                return new MyTuple<string, string, int>(DisplayCapabilitiesVersion, table ? "console" : "projector", flags);
            }
            // Text-surface providers on cockpits/PBs are not physical LCD panel anchors.
            return new MyTuple<string, string, int>(DisplayCapabilitiesVersion, "unsupported", 0);
        }
    }
}
