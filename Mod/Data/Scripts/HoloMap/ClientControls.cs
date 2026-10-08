using System;
using Sandbox.ModAPI;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        // Viewer-local escape hatch: never changes shared scene state or sends a command.
        bool ClientRenderingEnabled = true;
        bool _clientControlsRegistered;
        void InitializeClientControls()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated) return;
            MyAPIGateway.Utilities.MessageEntered += ClientMessageEntered;
            _clientControlsRegistered = true;
        }
        void UnloadClientControls()
        {
            if (_clientControlsRegistered && MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.MessageEntered -= ClientMessageEntered;
            _clientControlsRegistered = false;
        }
        void ClientMessageEntered(string text, ref bool sendToOthers)
        {
            if (text == null) return;
            string command = text.Trim().ToLowerInvariant();
            if(command.StartsWith("/hdr work ",StringComparison.Ordinal))
            {sendToOthers=false;int cap;if(!int.TryParse(command.Substring(10),out cap)||cap<1000||cap>MaxConfiguredDrawWork){MyAPIGateway.Utilities.ShowMessage("HDR API","Use /hdr work 1000–200000.");return;}ClientDrawWorkCap=cap;MyAPIGateway.Utilities.ShowMessage("HDR API","Viewer draw-work cap: "+cap+" units per frame.");return;}
            if(command.StartsWith("/hdr lcd-rate ",StringComparison.Ordinal))
            {
                sendToOthers=false;double rate;
                if(!double.TryParse(command.Substring(14),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out rate)||!Geometry.Finite(rate)||rate<1||rate>60)
                {MyAPIGateway.Utilities.ShowMessage("HDR API","Use /hdr lcd-rate 1–60. This cap is viewer-local.");return;}
                ClientLcdRefreshCap=rate;InvalidateLcdSamples();MyAPIGateway.Utilities.ShowMessage("HDR API","Local LCD sampling cap: "+rate+" Hz. Native LCDs retain their engine refresh limit.");return;
            }
            if(command.StartsWith("/hdr drag-rate ",StringComparison.Ordinal))
            {
                sendToOthers=false;double rate;
                if(!double.TryParse(command.Substring(15),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out rate)
                    ||!Geometry.Finite(rate)||rate<1||rate>30)
                {MyAPIGateway.Utilities.ShowMessage("HDR API","Use /hdr drag-rate 1–30.");return;}
                SetUiDragRate(rate);
                MyAPIGateway.Utilities.ShowMessage("HDR API","UI drag update cap: "+(60d/_uiDragInterval).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+" Hz. Remote server limits remain authoritative.");return;
            }
            if (command != "/hdr off" && command != "/hdr on" && command != "/hdr status") return;
            sendToOthers = false;
            if (command == "/hdr off")
            {
                ClientRenderingEnabled = false;

                CancelModClientInteractions("render-disabled");

                ClearUiUseObjects();
                ClearUiClient();
                if(_uiDragLocal!=null)_uiDragLocal.Clear();
                lock(_uiDragAckGate){_uiDragAcks.Clear();_uiDragTerminalAcks.Clear();}
                ClearClientGeometry();
                ClearLocalLcdFrames();
                CloseConstructPreviews();
            }
            else if (command == "/hdr on") ClientRenderingEnabled = true;
            MyAPIGateway.Utilities.ShowMessage("HDR API", "Local hologram rendering " + (ClientRenderingEnabled ? "enabled." : "disabled."));
            if(command=="/hdr status")
            {
                MyAPIGateway.Utilities.ShowMessage("HDR API","UI drag update cap: "+(60d/_uiDragInterval).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+" Hz; pointer provider "+(_uiPointer!=null&&_uiPointer.Ready?"registered":"unavailable")+"; viewer "+(_uiViewerGranted?"focused":"inactive")+". Registration is separate from routed input ownership.");
                int reduced=0;foreach(var cache in _projectedCaches.Values)if(cache.SourceReduced)reduced++;if(reduced>0)MyAPIGateway.Utilities.ShowMessage("HDR API",reduced+" external view(s) using reduced color/detail to fit display work and geometry limits.");MyAPIGateway.Utilities.ShowMessage("HDR API","Viewer draw-work cap: "+ClientDrawWorkCap+" per frame.");
                int screens=0,views=0,sprites=0,screenErrors=0;foreach(var scene in _scenes.Values)screens+=scene.Screens.Count;foreach(var cache in _projectedCaches.Values){if(cache.View!=null)views++;if(cache.Sprites!=null)sprites++;if(cache.Error!=null)screenErrors++;}if(screens>0)MyAPIGateway.Utilities.ShowMessage("HDR API","Projected screens: "+screens+" configured, "+views+" prepared perspective views, "+sprites+" prepared sprite frames, "+screenErrors+" preparation/render errors.");
                int raster=0,textures=0;string rasterError=null;foreach(var cache in _projectedCaches.Values){if(cache.UiRaster!=null)raster++;if(cache.SourceTexture)textures++;if(rasterError==null)rasterError=cache.UiRasterError;}if(screens>0)MyAPIGateway.Utilities.ShowMessage("HDR API","Raster backend "+(_rasterBackend==null?"unavailable":"connected")+", "+raster+" UI textures, "+textures+" source textures."+(rasterError==null?"":" "+rasterError.Substring(0,Math.Min(rasterError.Length,160))));
                int culled=0;string sizes="";foreach(var cache in _projectedCaches.Values){if(!cache.Front){culled++;continue;}if(cache.AdaptiveKnown&&sizes.Length<200)sizes+=(sizes.Length==0?"":", ")+cache.Id+" "+cache.Adaptive.Resolution.X+"x"+cache.Adaptive.Resolution.Y;}if(screens>0)MyAPIGateway.Utilities.ShowMessage("HDR API","Client raster LOD: "+sizes+"; "+culled+" culled cached screens.");
                int native=0,vector=0,unsupported=0,faultFallback=0,disabled=0;
                foreach(var scene in _scenes.Values)
                {
                    var panel=MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTextPanel;if(panel==null)continue;
                    LcdBackendFault fault;_lcdBackendFaults.TryGetValue(scene.ConsoleId,out fault);
                    if(fault!=null&&fault.Disabled){disabled++;continue;}
                    LcdScreenBasis basis;
                    if(scene.LcdRenderer==1)
                    {if(fault!=null&&fault.NativeOnly){native++;faultFallback++;}else if(LcdScreenCalibration.TryGet(panel,out basis))vector++;else{native++;unsupported++;}}
                    else native++;
                }
                MyAPIGateway.Utilities.ShowMessage("HDR API","LCDs: "+vector+" calibrated vector, "+native+" native ("+unsupported+" unsupported-model fallback, "+faultFallback+" render-fault fallback), "+disabled+" disabled after errors. Local sampling cap "+ClientLcdRefreshCap+" Hz.");
                int errors=0;string first=null;
                foreach(var cache in _clientGeometry.Values)if(!string.IsNullOrEmpty(cache.Error)){errors++;if(first==null)first=cache.Error;}
                if(first!=null)MyAPIGateway.Utilities.ShowMessage("HDR API",errors+" display compilation error(s): "+first.Substring(0,Math.Min(first.Length,160)));
            }
        }
    }
}
