using System;
namespace HDRClientRenderer
{
    // Optional local capability supplied by the separate 360 Camera client.
    // A cube-face selector is not proof that CameraLCD captured that direction.
    internal sealed class CubeCaptureGuard
    {
        Func<long,int,bool> ready;
        internal void Register(Func<long,int,bool> endpoint){ready=endpoint;}
        internal void Unregister(Func<long,int,bool> endpoint){if(ReferenceEquals(ready,endpoint))ready=null;}
        internal void Clear(){ready=null;}
        internal bool CanRelay(long lcd,int surface,string customData,out string reason)
        {
            reason=null;int face=ReadFace(customData);if(face==-2)return true;
            if(face<0||surface!=0){reason="Invalid Camera360.Face selector or LCD surface index.";return false;}
            var query=ready;
            if(query==null){reason="360 Camera Capture is not registered on this client. Enable it and fully restart.";return false;}
            try{if(query(lcd,face))return true;}
            catch{reason="360 Camera Capture could not validate the selected face.";return false;}
            reason="The selected 360 Camera face has no verified completed capture yet.";return false;
        }
        static int ReadFace(string data)
        {
            if(string.IsNullOrEmpty(data))return -2;
            if(data.Length>65536)return data.IndexOf("Camera360.Face=",StringComparison.OrdinalIgnoreCase)>=0?-1:-2;
            foreach(string raw in data.Split('\n'))
            {
                string line=raw.Trim();const string key="Camera360.Face=";
                if(!line.StartsWith(key,StringComparison.OrdinalIgnoreCase))continue;
                string value=line.Substring(key.Length).Trim().ToLowerInvariant();int number;
                if(int.TryParse(value,out number))return number>=0&&number<6?number:-1;
                switch(value){case "front":return 0;case "right":return 1;case "back":return 2;case "left":return 3;case "up":return 4;case "down":return 5;default:return -1;}
            }
            return -2;
        }
    }
}
