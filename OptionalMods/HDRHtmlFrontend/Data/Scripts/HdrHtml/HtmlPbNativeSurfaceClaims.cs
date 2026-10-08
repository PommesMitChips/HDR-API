using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using IMyTextSurface=Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Hdr.Html
{
    // Shared within this optional mod assembly, not a claim over unrelated mods or remote client writers.
    internal static class HtmlPbNativeSurfaceClaims
    {
        sealed class Claim{internal IMyTextSurface Surface;internal object Token;}
        static readonly List<Claim> claims=new List<Claim>();
        internal static bool IsClaimed(IMyTextSurface surface){foreach(var claim in claims)if(ReferenceEquals(claim.Surface,surface))return true;return false;}
        internal static bool TryClaim(IMyTextSurface surface,object token)
        {if(surface==null||token==null||claims.Count>=64)return false;foreach(var claim in claims)if(ReferenceEquals(claim.Surface,surface))return false;claims.Add(new Claim{Surface=surface,Token=token});return true;}
        internal static bool Owns(IMyTextSurface surface,object token)
        {foreach(var claim in claims)if(ReferenceEquals(claim.Surface,surface)&&ReferenceEquals(claim.Token,token))return true;return false;}
        internal static void Release(IMyTextSurface surface,object token)
        {for(int i=claims.Count-1;i>=0;i--)if(ReferenceEquals(claims[i].Surface,surface)&&ReferenceEquals(claims[i].Token,token))claims.RemoveAt(i);}
    }
}
