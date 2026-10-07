using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Input;
using VRageMath;
using Accessor = System.Func<object, int, object>;
using Space = System.Func<VRage.MyTuple<bool, float, VRageMath.MatrixD>>;
using RichString = VRage.MyTuple<System.Text.StringBuilder, VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>>;
using TextBuilder = VRage.MyTuple<VRage.MyTuple<System.Func<int,int,object>,System.Func<int>>,System.Func<VRageMath.Vector2I,int,object>,System.Func<object,int,object>,System.Action<System.Collections.Generic.IList<VRage.MyTuple<System.Text.StringBuilder,VRage.MyTuple<byte,float,VRageMath.Vector2I,VRageMath.Color>>>,VRageMath.Vector2I>,System.Action<System.Collections.Generic.IList<VRage.MyTuple<System.Text.StringBuilder,VRage.MyTuple<byte,float,VRageMath.Vector2I,VRageMath.Color>>>>,System.Action>;

namespace HoloMap
{
    // Small independent implementation of RHF's public API13 wire contract.
    // No framework code is embedded. The optional provider owns game input blocking.
    internal sealed class RichHudInputBridge
    {
        internal const long Provider = 1965654081, Queue = 1314086443;
        internal const int ApiVersion = 13, FocusMask = 7;
        readonly Action _afterInput;
        Action _unregister;
        Accessor _bind, _hud, _cursor;
        Func<Accessor, bool> _owns, _capture, _release;
        Func<int,bool> _requestApplied;
        Func<int> _appliedMask;
        Action _releaseApplied;
        Func<bool> _appliedReady;
        readonly Accessor _identity;
        internal bool Ready { get; private set; }
        // Stock API13 has no acknowledged engine-input barrier. Optional HDR module6
        // must prove receiver-applied engine blocking before reporting mask7.
        // A frame delay never substitutes for that provider-owned acknowledgement.
        internal bool SupportsAcknowledgedCapture { get { return _requestApplied!=null&&_appliedMask!=null&&_releaseApplied!=null&&_appliedReady!=null; } }
        internal bool Requested { get; private set; }
        internal int ObservedFrames { get; private set; }
        internal bool Owns
        {
            get
            {
                try { return Ready&&Requested&&SupportsAcknowledgedCapture&&_appliedReady()&&_appliedMask()==FocusMask&&_owns!=null&&_owns(_identity); }
                catch { return false; }
            }
        }

        internal RichHudInputBridge(Action afterInput)
        { _afterInput = afterInput; _identity = Identity; }

        object Identity(object value, int member)
        {
            switch(member)
            {
                case 0: return "HDR API";
                case 2: return (sbyte)0;
                case 3: return (ushort)0;
                case 4: return Vector2.Zero;
                case 5: return Vector2.Zero;
                case 6: return Vector3.Zero;
                case 7: return false;
                case 9: return Vector3D.Zero;
                case 10: return MatrixD.Identity;
                case 11: case 12: return true;
                default: return null;
            }
        }

        internal object Registration()
        { return new MyTuple<string,Action<int,object>,Action,int>("HDR API Input", Receive, ProviderClosed, ApiVersion); }

        internal void Receive(int kind, object data)
        {
            if(kind != 2) { if(kind == 3) Close(); return; }
            if(Ready) return;
            if(!(data is MyTuple<Action,Func<int,object>,int>)) return;
            var api = (MyTuple<Action,Func<int,object>,int>)data;
            _unregister = api.Item1;
            try
            {
                if(api.Item3 != ApiVersion || api.Item1 == null || api.Item2 == null)
                    throw new ArgumentException("Unsupported Rich HUD Framework API.");
                var bind = (MyTuple<Accessor,MyTuple<Func<int,object,int,object>,Func<int>>,MyTuple<Func<Vector2I,object,int,object>,Func<int,int>>,Func<Vector2I,int,bool>,MyTuple<Func<int,int,object>,Func<int>>,Action>)api.Item2(1);
                var hud = (MyTuple<MyTuple<Func<Space,bool>,Func<float,Space,bool>,Func<Accessor,bool>,Func<Accessor,bool>,Func<Accessor,bool>,Accessor>,Func<MyTuple<TextBuilder,MyTuple<Func<float>,Action<float>>,Func<Vector2>,Func<Vector2>,MyTuple<Func<Vector2>,Action<Vector2>>,Action<BoundingBox2,BoundingBox2,MatrixD[]>>>,Accessor,Action>)api.Item2(2);
                _bind=bind.Item1; _hud=hud.Item3; _cursor=hud.Item1.Item6;
                _owns=hud.Item1.Item3; _capture=hud.Item1.Item4; _release=hud.Item1.Item5;
                if(_bind==null||_hud==null||_cursor==null||_owns==null||_capture==null||_release==null)
                    throw new ArgumentException("Incomplete RHF input API.");
                // Boxed int has the same CLI unboxing representation as RHF's int enum.
                // This avoids copying SDK types or using reflection across mod assemblies.
                _bind(0,7); _hud(false,10);
                var extension=api.Item2(6);
                if(extension is MyTuple<int,Func<int,bool>,Func<int>,Action,Func<bool>>)
                {
                    var input=(MyTuple<int,Func<int,bool>,Func<int>,Action,Func<bool>>)extension;
                    if(input.Item1==1&&input.Item2!=null&&input.Item3!=null&&input.Item4!=null&&input.Item5!=null)
                    { _requestApplied=input.Item2; _appliedMask=input.Item3; _releaseApplied=input.Item4; _appliedReady=input.Item5; }
                }
                _hud((Action)AfterFrameworkInput,22);
                Ready=true;
            }
            catch { Close(); }
        }

        void AfterFrameworkInput()
        {
            if(!Ready||!Requested)return;
            try
            {
                // RHF runs BindManager.UpdateBlacklist before HUD client callbacks.
                // Module6 acknowledges applied engine state, not the local requested flag.
                // The extra callbacks and entry release only arm UI clicks after that proof.
                if(!SupportsAcknowledgedCapture||!_appliedReady()||_appliedMask()!=FocusMask||(int)_bind(null,7)!=FocusMask || (int)_hud(null,18)==0)
                { Release(); return; }
                if(!_owns(_identity)&&!_capture(_identity)) { Release(); return; }
                ObservedFrames=Math.Min(3,ObservedFrames+1);
                _afterInput();
            }
            catch { Release(); }
        }

        internal bool Request()
        {
            if(!Ready||!SupportsAcknowledgedCapture)return false;
            if(Requested)return true;
            try
            {
                // Do not acquire keyboard/mouse blocking over another framework UI.
                if((bool)_cursor(null,1)&&!_owns(_identity))return false;
                if(!_requestApplied(FocusMask)||!_appliedReady()||_appliedMask()!=FocusMask)
                {
                    // Module6 v1 is a synchronous result contract, not an async pending
                    // operation. Release must cancel queued work/epochs on every rejection,
                    // even when the provider returns false after partially requesting input.
                    Release(); return false;
                }
                _hud(true,10);
                Requested=true; ObservedFrames=0; return true;
            }
            catch { Release(); return false; }
        }

        internal Vector2 Cursor()
        {
            var point=(Vector2)_cursor(null,2);
            float width=(float)_hud(null,1),height=(float)_hud(null,2);
            if(!Geometry.Finite(point.X)||!Geometry.Finite(point.Y)||!Geometry.Finite(width)||!Geometry.Finite(height)||width<1||height<1)
                throw new ArgumentException("Invalid RHF cursor dimensions.");
            // Installed API13 source returns centered pixels, X right and Y up.
            return new Vector2(MathHelper.Clamp(.5f+point.X/width,0,1),MathHelper.Clamp(.5f-point.Y/height,0,1));
        }

        internal void Release()
        {
            Requested=false; ObservedFrames=0;
            // Independent attempts: failure in one provider accessor cannot skip cleanup.
            try { if(_release!=null)_release(_identity); } catch { }
            try { if(_hud!=null)_hud(false,10); } catch { }
            try { if(_releaseApplied!=null)_releaseApplied(); } catch { }
            try { if(_bind!=null)_bind(0,7); } catch { }
        }

        void ProviderClosed() { Release(); Ready=false; ClearDelegates(); }
        void ClearDelegates() { _bind=_hud=_cursor=null; _owns=_capture=_release=null; _unregister=null; _requestApplied=null; _appliedMask=null; _releaseApplied=null; _appliedReady=null; }
        internal void Close()
        {
            Release(); Ready=false;
            try { if(_hud!=null)_hud(null,22); } catch { }
            var unregister=_unregister; ClearDelegates();
            try { if(unregister!=null)unregister(); } catch { }
        }
    }

    public sealed partial class HoloMapSession
    {
        RichHudInputBridge _rhf;
        bool _rhfInitialized,_rhfEntryReleased,_rhfPrimary,_rhfEscape;
        int _rhfLastCallback;
        Vector2 _rhfCursor=new Vector2(.5f,.5f);
        bool IsRHFReady { get { return _rhf!=null&&_rhf.Ready&&_rhf.SupportsAcknowledgedCapture; } }
        bool UiInputOwned { get { return IsRHFReady&&_rhf.SupportsAcknowledgedCapture&&_rhf.Requested&&_rhfEntryReleased&&_rhf.ObservedFrames>=2&&_ticks-_rhfLastCallback<3&&_rhf.Owns; } }
        Vector2 UiInputCursor { get { return _rhfCursor; } }
        bool UiInputPrimaryPressed { get { bool result=_rhfPrimary; _rhfPrimary=false; return result&&UiInputOwned; } }
        bool UiInputEscapePressed { get { bool result=_rhfEscape; _rhfEscape=false; return result; } }

        void InitializeRHF()
        {
            if(_rhfInitialized||MyAPIGateway.Utilities==null||MyAPIGateway.Utilities.IsDedicated)return;
            _rhf=new RichHudInputBridge(HandleInputRHF);
            MyAPIGateway.Utilities.RegisterMessageHandler(RichHudInputBridge.Queue,RHFProviderAvailable);
            _rhfInitialized=true;
            MyAPIGateway.Utilities.SendModMessage(RichHudInputBridge.Provider,_rhf.Registration());
        }
        void RHFProviderAvailable(object data)
        {
            if(_rhfInitialized&&_rhf!=null&&!_rhf.Ready&&data is long&&(long)data==RichHudInputBridge.Provider)
                MyAPIGateway.Utilities.SendModMessage(RichHudInputBridge.Provider,_rhf.Registration());
        }
        bool CaptureUiInput()
        {
            if(!UiClientCanInteract()||!IsRHFReady||MyAPIGateway.Input.IsJoystickConnected()||MyAPIGateway.Input.IsAnyAltKeyPressed())return false;
            if(!_rhf.Requested)
            {
                _rhfEntryReleased=_rhfPrimary=_rhfEscape=false;
                if(!_rhf.Request())return false;
                _rhfLastCallback=_ticks;
            }
            return UiInputOwned;
        }
        void HandleInputRHF()
        {
            _rhfLastCallback=_ticks;
            if(!UiClientCanInteract()||MyAPIGateway.Input.IsJoystickConnected()||MyAPIGateway.Input.IsAnyAltKeyPressed()) { ReleaseUiInput(); return; }
            if(MyAPIGateway.Input.IsNewKeyPressed(MyKeys.Escape))
            { ReleaseUiInput(); _rhfEscape=true; return; }
            _rhfCursor=_rhf.Cursor();
            if(!_rhfEntryReleased)
            {
                if(_rhf.ObservedFrames>=2&&!MyAPIGateway.Input.IsLeftMousePressed()&&!MyAPIGateway.Input.IsRightMousePressed()&&!MyAPIGateway.Input.IsMiddleMousePressed())
                    _rhfEntryReleased=true;
                return;
            }
            if(UiInputOwned&&MyAPIGateway.Input.IsNewLeftMousePressed())_rhfPrimary=true;
        }
        void TickRHF()
        {
            if(!_rhfInitialized)InitializeRHF();
            if(_rhf!=null&&_rhf.Requested&&(!UiClientCanInteract()||MyAPIGateway.Input.IsJoystickConnected()||MyAPIGateway.Input.IsAnyAltKeyPressed()||_ticks-_rhfLastCallback>=3))ReleaseUiInput();
        }
        void ReleaseUiInput()
        {
            _rhfEntryReleased=_rhfPrimary=false;
            if(_rhf!=null)_rhf.Release();
        }
        void UnregisterRHF()
        {
            ReleaseUiInput();
            try { if(_rhfInitialized&&MyAPIGateway.Utilities!=null)MyAPIGateway.Utilities.UnregisterMessageHandler(RichHudInputBridge.Queue,RHFProviderAvailable); } catch { }
            if(_rhf!=null)_rhf.Close();
            _rhf=null; _rhfInitialized=false; _rhfEscape=false;
        }
    }
}
