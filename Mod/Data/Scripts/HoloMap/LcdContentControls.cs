using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRage.Utils;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        // Native renderer accepts only Content values 0..3. Slot 2 is its legacy image mode.
        long _holoContentKey=2;
        const string HoloSurfaceScript="HDRAPI";
        IMyTerminalControlCombobox _lcdContentControl;
        Action<List<MyTerminalControlComboBoxItem>> _lcdNativeOptions;
        bool _lcdContentActive;
        const string LcdContentControlId="HDR.Content";
        static Sandbox.ModAPI.Ingame.IMyTextSurface ContentSurface(IMyTerminalBlock block)
        {
            var surface=block as Sandbox.ModAPI.Ingame.IMyTextSurface;
            if(surface!=null)return surface;
            var provider=block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
            if(provider==null||provider.SurfaceCount<1)return null;
            if(provider.SurfaceCount==1)return provider.GetSurface(0);
            // The public, whitelisted panel component exposes the same selected
            // index used by the native terminal. Console screens must be independent.
            var panels=block.Components.Get<Sandbox.Game.EntityComponents.MyMultiTextPanelComponent>();
            if(panels==null)return null;
            int index=panels.SelectedPanelIndex;
            return index>=0&&index<provider.SurfaceCount?provider.GetSurface(index):null;
        }
        internal static bool RepairLegacyLcdSurface(Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            long content=(long)surface.ContentType;
            // ContentType has byte storage: the former long keys were truncated.
            if(content<0x4F||content>0x4F+64)return false;
            surface.ContentType=ContentType.SCRIPT;
            surface.Script=HoloSurfaceScript;
            return true;
        }
        void RepairLegacyLcdContent()
        {
            // Recover saves made with the former invalid HDR menu key, before native
            // LCD components update. Only the exact retired HDR key range is changed.
            if(!MyAPIGateway.Multiplayer.IsServer)return;
            var grids=new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(grids,e=>e is VRage.Game.ModAPI.IMyCubeGrid);
            var blocks=new List<VRage.Game.ModAPI.IMySlimBlock>();
            foreach(var entity in grids)
            {
                blocks.Clear();((VRage.Game.ModAPI.IMyCubeGrid)entity).GetBlocks(blocks);
                foreach(var block in blocks)
                {
                    var provider=block.FatBlock as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
                    if(provider==null)continue;
                    for(int i=0;i<provider.SurfaceCount;i++)
                    {
                        var surface=provider.GetSurface(i);
                        if(surface!=null)RepairLegacyLcdSurface(surface);
                    }
                }
            }
        }
        void RegisterLcdContentControl()
        {
            if(_lcdContentActive)return;
            var control=MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox,IMyTerminalBlock>(LcdContentControlId);
            control.Title=MyStringId.GetOrCompute("Content");
            control.Tooltip=MyStringId.GetOrCompute("Select the LCD content, including HDR API.");
            control.SupportsMultipleBlocks=true;
            control.Visible=block=>ContentSurface(block)!=null;
            control.Getter=block=>
            {
                var surface=ContentSurface(block);
                if(surface==null)return (long)ContentType.NONE;
                return surface.ContentType==ContentType.SCRIPT&&surface.Script==HoloSurfaceScript?_holoContentKey:(long)surface.ContentType;
            };
            control.Setter=(block,value)=>
            {
                var surface=ContentSurface(block);
                if(surface==null)return;
                if(value==_holoContentKey)
                {surface.ContentType=ContentType.SCRIPT;surface.Script=HoloSurfaceScript;return;}
                if(value!=(long)ContentType.NONE&&value!=(long)ContentType.TEXT_AND_IMAGE&&value!=(long)ContentType.SCRIPT)return;
                surface.ContentType=(ContentType)value;
                if(value==(long)ContentType.SCRIPT&&surface.Script==HoloSurfaceScript)surface.Script="";
            };
            control.ComboBoxContent=items=>
            {
                if(_lcdNativeOptions!=null)_lcdNativeOptions(items);
                else
                {
                    items.Add(new MyTerminalControlComboBoxItem{Key=0,Value=MyStringId.GetOrCompute("None")});
                    items.Add(new MyTerminalControlComboBoxItem{Key=1,Value=MyStringId.GetOrCompute("Text and images")});
                    items.Add(new MyTerminalControlComboBoxItem{Key=3,Value=MyStringId.GetOrCompute("Script")});
                }
                foreach(var item in items)if(item.Key==_holoContentKey)return;
                items.Add(new MyTerminalControlComboBoxItem{Key=_holoContentKey,Value=MyStringId.GetOrCompute("HDR API")});
            };
            _lcdContentControl=control;
            MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(control);
            MyAPIGateway.TerminalControls.CustomControlGetter+=GetLcdContentControls;
            _lcdContentActive=true;
        }
        void GetLcdContentControls(IMyTerminalBlock block,List<IMyTerminalControl> controls)
        {
            // Never mutate a shared native control. Replace only the actual visible list.
            controls.RemoveAll(control=>control==_lcdContentControl);
            if(!_lcdContentActive||ContentSurface(block)==null)return;
            for(int i=controls.Count-1;i>=0;i--)
            {
                var native=controls[i] as IMyTerminalControlCombobox;
                if(native==null||(controls[i].Id!="Content"&&controls[i].Id!="ContentType"))continue;
                var options=new List<MyTerminalControlComboBoxItem>();
                if(native.ComboBoxContent==null)return;
                native.ComboBoxContent(options);
                foreach(var option in options)if(option.Key==_holoContentKey)return;
                // Other mods' custom numeric modes need their own setter: preserve that control.
                foreach(var option in options)if(option.Key<0||option.Key>3)return;
                _lcdNativeOptions=native.ComboBoxContent;
                _lcdContentControl.Enabled=native.Enabled;
                _lcdContentControl.SupportsMultipleBlocks=native.SupportsMultipleBlocks;
                controls[i]=_lcdContentControl;
                return;
            }
        }
        void UnregisterLcdContentControl()
        {
            _lcdContentActive=false;
            if(_lcdContentControl==null)return;
            MyAPIGateway.TerminalControls.CustomControlGetter-=GetLcdContentControls;
            MyAPIGateway.TerminalControls.RemoveControl<IMyTerminalBlock>(_lcdContentControl);
            _lcdContentControl=null;_lcdNativeOptions=null;
        }
    }
}
