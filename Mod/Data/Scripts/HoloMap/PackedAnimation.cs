using System;
using System.Globalization;
using System.Text;

namespace HoloMap
{
    public sealed class PackedAnimation
    {
        public const int MaxEncodedCharacters=60000, MaxDecodedBytes=2*1024*1024;
        public int Frames, Parts; public double Fps; public string[] Svg;
        public static byte[] Decompress(byte[] input)
        {
            if(input==null||input.Length<8||input[0]!='H'||input[1]!='M'||input[2]!='C'||input[3]!='1')throw new ArgumentException("Invalid HoloMap compressed header.");
            long length=(long)input[4]|((long)input[5]<<8)|((long)input[6]<<16)|((long)input[7]<<24);
            if(length<1||length>MaxDecodedBytes)throw new ArgumentException("Animation decompression budget exceeded.");
            var output=new byte[(int)length];int source=8,target=0;
            while(target<output.Length)
            {
                if(source>=input.Length)throw new ArgumentException("Truncated compressed animation.");int flags=input[source++];
                for(int bit=0;bit<8&&target<output.Length;bit++)
                {
                    if((flags&(1<<bit))!=0)
                    { if(source>=input.Length)throw new ArgumentException("Truncated animation literal.");output[target++]=input[source++]; }
                    else
                    {
                        if(source+3>input.Length)throw new ArgumentException("Truncated animation match.");
                        int distance=input[source]|(input[source+1]<<8),count=input[source+2]+3;source+=3;
                        if(distance==0||distance>target||count>output.Length-target)throw new ArgumentException("Invalid animation back-reference.");
                        for(int i=0;i<count;i++){output[target]=output[target-distance];target++;}
                    }
                }
            }
            if(source!=input.Length)throw new ArgumentException("Trailing compressed animation data.");return output;
        }
        public static PackedAnimation Decode(string encoded)
        {
            const string prefix="HoloMapAnimation1:";
            if(string.IsNullOrEmpty(encoded)||encoded.Length>MaxEncodedCharacters)throw new ArgumentException("Packed animation requires at most 60,000 Custom Data characters.");
            encoded=encoded.Trim();if(!encoded.StartsWith(prefix,StringComparison.Ordinal))throw new ArgumentException("Paste the HoloMapAnimation1 payload into PB Custom Data.");
            byte[] compressed;
            try{compressed=Convert.FromBase64String(encoded.Substring(prefix.Length));}catch(FormatException){throw new ArgumentException("Invalid animation base64.");}
            byte[] bytes=Decompress(compressed);
            // This format contains only ASCII SVG/decimal metadata. Reject rather than replace invalid encodings.
            foreach(byte b in bytes)if(b>127||b==0)throw new ArgumentException("Animation payload must be ASCII.");
            string text=Encoding.UTF8.GetString(bytes);string[] lines=text.Split('\n');
            int frames,parts;double fps;
            if(lines.Length<5||lines[0]!="HMA1"||!double.TryParse(lines[1],NumberStyles.Float,CultureInfo.InvariantCulture,out fps)
                ||!Geometry.Finite(fps)||fps<1||fps>10||!int.TryParse(lines[2],out frames)||frames<1||frames>128
                ||!int.TryParse(lines[3],out parts)||parts<1||parts>8||lines.Length!=4+frames*parts+1||lines[lines.Length-1]!="")throw new ArgumentException("Invalid animation frame table.");
            var svg=new string[frames*parts];for(int i=0;i<svg.Length;i++){string s=lines[4+i];if(s.Length<1||s.Length>HoloMap.Svg.MaxCharacters)throw new ArgumentException("Invalid animation SVG frame size.");svg[i]=s;}
            return new PackedAnimation{Frames=frames,Parts=parts,Fps=fps,Svg=svg};
        }
        public int FrameAt(double time)
        { if(!Geometry.Finite(time)||time<0||time>1e9)throw new ArgumentException("Animation time must be finite, nonnegative and at most 1e9 seconds.");return (int)(Math.Floor(time*Fps)%Frames); }
    }
}
