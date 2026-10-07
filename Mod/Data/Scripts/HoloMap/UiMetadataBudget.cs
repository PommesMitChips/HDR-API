using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
namespace HoloMap
{
    public static class UiMetadataRules
    {
        public const int GrowthMargin=16384;
        public const int AdmissionBytes=UiRules.ReplicationReserve-GrowthMargin;
        // Definition-only strings, constraints and repeated 16-double matrices
        // do not grow during a scalar edit. Reserve scalar/revision growth and
        // every containing protobuf length prefix, with an envelope allowance.
        public const int PassiveGrowthBound=UiValueRules.MaxTotalValues*24+UiRules.MaxTotalWidgets*29+UiRules.MaxDisplays*25+1024;
    }
    public sealed partial class HoloMapSession
    {
        void UiAdmitMetadata(UiDisplay candidate)
        {
            var publications=new List<UiDisplay>();foreach(var d in _uiDisplays.Values)if(d.CallerId!=candidate.CallerId||d.TargetId!=candidate.TargetId)publications.Add(d);publications.Add(candidate);
            if(UiMetadataRules.PassiveGrowthBound>UiMetadataRules.GrowthMargin)throw new ArgumentException("UI metadata growth proof exceeds its reserve.");
            if(MyAPIGateway.Utilities.SerializeToBinary(publications).Length>UiMetadataRules.AdmissionBytes)throw new ArgumentException("UI interaction metadata exceeds the shared network reserve.");
        }
    }
}
