using System;
using Sandbox.ModAPI;

namespace HDRClientRenderer
{
    public sealed partial class HDRClientRendererPlugin
    {
        const string ClientSettingsFile="HDRClientRenderer.settings.xml";
        ClientRenderSettings clientSettings=new ClientRenderSettings();
        object controlsUtilities;
        bool settingsLoaded;
        void ApplyClientSettings()
        { pixelBudget.Configure(clientSettings);directCapture.Configure(clientSettings); displayOcclusion.SetEnabled(clientSettings.DisplayOcclusion); }
        void UpdateRenderControls()
        {
            var utilities=MyAPIGateway.Utilities;if(utilities==null)return;
            if(!settingsLoaded)
            {
                settingsLoaded=true;
                try{if(utilities.FileExistsInLocalStorage(ClientSettingsFile,typeof(HDRClientRendererPlugin)))using(var input=utilities.ReadFileInLocalStorage(ClientSettingsFile,typeof(HDRClientRendererPlugin)))
                    {var loaded=utilities.SerializeFromXML<ClientRenderSettings>(input.ReadToEnd());if(loaded!=null&&loaded.Valid)clientSettings=loaded;}}
                catch { /* First use has no saved local preferences. */ }
                ApplyClientSettings();Log(clientSettings.Describe());
            }
            if(ReferenceEquals(controlsUtilities,utilities))return;
            UnregisterRenderControls();utilities.MessageEntered+=ClientCameraMessage;controlsUtilities=utilities;
        }
        void UnregisterRenderControls()
        {
            var utilities=controlsUtilities as VRage.Game.ModAPI.IMyUtilities;
            if(utilities!=null)try{utilities.MessageEntered-=ClientCameraMessage;}catch{}
            controlsUtilities=null;
        }
        void ClientCameraMessage(string text,ref bool sendToOthers)
        {
            if(text==null)return;string command=text.Trim().ToLowerInvariant();
            if(command=="/hdr status")
            {sendToOthers=false;MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",clientSettings.Describe());MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",NativeProviderStatus());return;}
            if(command=="/hdr portal"||command=="/hdr portal status")
            {sendToOthers=false;MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",NativeProviderStatus());return;}
            const string prefix="/hdr camera";
            if(command!=prefix&&!command.StartsWith(prefix+" ",StringComparison.Ordinal))return;
            sendToOthers=false;string args=command.Length==prefix.Length?"":command.Substring(prefix.Length).Trim();
            if(args==""||args=="status") {MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",clientSettings.Describe());MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",directCapture.Describe());MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",panorama.Describe());MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",NativeProviderStatus());return;}
            if(!clientSettings.Apply(args))
            {MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer","Use /hdr camera pixels off|N|viewport X; passes off|N; rate off|Hz; adaptive on|off; occlusion on|off; tiles 1..64; tile-cost N.");return;}
            ApplyClientSettings();
            try{using(var output=MyAPIGateway.Utilities.WriteFileInLocalStorage(ClientSettingsFile,typeof(HDRClientRendererPlugin)))
                output.Write(MyAPIGateway.Utilities.SerializeToXML(clientSettings));}
            catch(Exception error){MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer","Applied for this session; saving failed: "+error.GetBaseException().Message);}
            MyAPIGateway.Utilities.ShowMessage("HDR Client Renderer",clientSettings.Describe());
        }
    }
}
