using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

namespace HoloMap
{
    public static class UiFocusLayout
    {
        public static bool Bounds(UiDisplay display, string bundle, out Vector2D center, out Vector2D size)
        {
            center = Vector2D.Zero; size = Vector2D.Zero;
            if (display == null) return false;
            double left=25,right=-25,bottom=25,top=-25;bool any=false;
            foreach(var w in display.Widgets)if(w.Visible&&w.Bundle==bundle)
            {left=Math.Min(left,w.X-w.Width*.5);right=Math.Max(right,w.X+w.Width*.5);bottom=Math.Min(bottom,w.Y-w.Height*.5);top=Math.Max(top,w.Y+w.Height*.5);any=true;}
            if(!any)return false;center=new Vector2D((left+right)*.5,(bottom+top)*.5);size=new Vector2D(Math.Max(.01,right-left),Math.Max(.01,top-bottom));return true;
        }
        public static Vector2D Point(Vector2 cursor,Vector2D center,double unitsPerScreenHeight,double aspect)
        {return center+new Vector2D((cursor.X-.5)*unitsPerScreenHeight*aspect,(.5-cursor.Y)*unitsPerScreenHeight);}
    }

    public sealed partial class HoloMapSession
    {
        UiDisplay _uiFocusDisplay;
        string _uiFocusBundle;
        Vector2 _uiFocusCursor=new Vector2(.5f,.5f);
        UiWidget _uiFocusHover;
        sealed class UiFocusPoints { public Geometry Geometry; public Vector3D[] Points; }
        readonly Dictionary<string,UiFocusPoints> _uiFocusPoints=new Dictionary<string,UiFocusPoints>();
        long _uiFocusTile;
        Vector3D _uiFocusCharacterLocal;
        int _uiFocusStartTick, _uiFocusLastActionTick;
        bool _uiFocusWasReady;
        bool UiFocusRequested { get { return _uiFocusDisplay!=null; } }
        bool UiFocusActive { get { return UiInputOwned&&_uiFocusDisplay!=null; } }
        bool BeginUiFocus(UiDisplay display,string bundle,long tileId)
        {
            if(display==null||!IsRHFReady||!UiClientCanInteract())return false;
            Vector2D center,size;if(!UiFocusLayout.Bounds(display,bundle,out center,out size))return false;
            var tile=MyAPIGateway.Entities.GetEntityById(tileId) as IMyTerminalBlock;
            if(tile==null||tile.Closed||!tile.IsWorking)return false;
            bool navigating=UiFocusRequested&&UiInputOwned;
            _uiFocusDisplay=display;_uiFocusBundle=bundle;_uiFocusTile=tileId;
            _uiFocusCharacterLocal=Vector3D.Transform(MyAPIGateway.Session.Player.Character.GetPosition(),MatrixD.Invert(tile.WorldMatrix));
            _uiFocusStartTick=_uiFocusLastActionTick=_ticks;_uiFocusHover=null;
            if(!navigating){_uiFocusCursor=new Vector2(.5f,.5f);_uiFocusWasReady=false;}
            CaptureUiInput();return true;
        }
        void ClearUiFocus()
        {
            _uiFocusDisplay=null;_uiFocusBundle=null;_uiFocusHover=null;_uiFocusTile=0;_uiFocusWasReady=false;
            _uiFocusPoints.Clear();
            try{ReleaseUiInput();}catch{}
        }
        bool UiFocusMetrics(out Vector2D center,out double units,out double aspect)
        {
            center=Vector2D.Zero;units=aspect=1;
            if(!UiFocusActive)return false;Vector2D size;
            if(!UiFocusCanvasBounds(out center,out size))return false;
            var area=MyAPIGateway.Input.GetMouseAreaSize();if(area.X<1||area.Y<1)return false;
            aspect=(double)area.X/area.Y;units=Math.Max(size.Y,size.X/aspect)/.8;return Geometry.Finite(units)&&units>0;
        }
        bool UiFocusCanvasBounds(out Vector2D center,out Vector2D size)
        {
            if(!UiFocusLayout.Bounds(_uiFocusDisplay,_uiFocusBundle,out center,out size))return false;
            Scene scene;if(!_scenes.TryGetValue(_uiFocusDisplay.TargetId,out scene))return false;
            double left=center.X-size.X*.5,right=center.X+size.X*.5,bottom=center.Y-size.Y*.5,top=center.Y+size.Y*.5;
            int noCompile=0;
            foreach(var canonical in scene.Items.Values)
            {
                if(IsProjectedId(canonical.Id))continue;
                if(!canonical.Visible||canonical.CallerId!=_uiFocusDisplay.CallerId||canonical.Layer!="ui-"+_uiFocusBundle||canonical.Opacity<=0)continue;
                var item=ClientDisplayItem(scene,GetAnimatedItem(scene.ConsoleId,canonical),ref noCompile);if(item==null)continue;
                foreach(var point in item.Geometry.Points)
                {var p=Vector3D.Transform(point,item.Transform);left=Math.Min(left,p.X);right=Math.Max(right,p.X);bottom=Math.Min(bottom,p.Y);top=Math.Max(top,p.Y);}
            }
            foreach(var label in scene.Labels.Values)
            {
                if(IsProjectedId(label.Id))continue;
                if(!label.Visible||label.CallerId!=_uiFocusDisplay.CallerId||label.Layer!="ui-"+_uiFocusBundle||label.Opacity<=0)continue;
                if(label.Glyphs==null){try{label.Glyphs=VectorFont.Layout(label.Text);}catch(ArgumentException){label.Glyphs=new List<VectorFont.PositionedGlyph>();}}
                foreach(var glyph in label.Glyphs)foreach(var point in glyph.Mesh.Points)
                {var p=label.Position+(point+glyph.Offset)*label.Height;left=Math.Min(left,p.X);right=Math.Max(right,p.X);bottom=Math.Min(bottom,p.Y);top=Math.Max(top,p.Y);}
            }
            center=new Vector2D((left+right)*.5,(bottom+top)*.5);size=new Vector2D(Math.Max(.01,right-left),Math.Max(.01,top-bottom));
            return Geometry.Finite(center.X)&&Geometry.Finite(center.Y)&&Geometry.Finite(size.X)&&Geometry.Finite(size.Y);
        }
        void TickUiFocus()
        {
            if(!UiFocusRequested)return;
            var current=GetUiDisplay(_uiFocusDisplay.CallerId,_uiFocusDisplay.TargetId);
            var tile=MyAPIGateway.Entities.GetEntityById(_uiFocusTile) as IMyTerminalBlock;
            var character=MyAPIGateway.Session.Player==null?null:MyAPIGateway.Session.Player.Character;
            if(!UiClientCanInteract()||current==null||current.Revision!=_uiFocusDisplay.Revision||tile==null||tile.Closed||!tile.IsWorking
                ||character==null||Vector3D.DistanceSquared(character.GetPosition(),tile.GetPosition())>25
                ||Vector3D.DistanceSquared(Vector3D.Transform(character.GetPosition(),MatrixD.Invert(tile.WorldMatrix)),_uiFocusCharacterLocal)>.1225
                ||_ticks-_uiFocusLastActionTick>1800||UiInputEscapePressed){ClearUiClient();return;}
            var caller=MyAPIGateway.Entities.GetEntityById(current.CallerId) as IMyProgrammableBlock;
            if(caller==null||caller.Closed||!caller.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId)||!tile.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId))
            {ClearUiClient();return;}
            bool ready=CaptureUiInput();
            if(!ready){if(_uiFocusWasReady||!IsRHFReady||_ticks-_uiFocusStartTick>120)ClearUiClient();return;}
            _uiFocusWasReady=true;
            _uiFocusCursor=UiInputCursor;
            if(!Geometry.Finite(_uiFocusCursor.X)||!Geometry.Finite(_uiFocusCursor.Y)){ClearUiClient();return;}
            _uiFocusCursor.X=MathHelper.Clamp(_uiFocusCursor.X,0,1);_uiFocusCursor.Y=MathHelper.Clamp(_uiFocusCursor.Y,0,1);
            Vector2D center;double units,aspect;if(!UiFocusMetrics(out center,out units,out aspect))return;
            var point=UiFocusLayout.Point(_uiFocusCursor,center,units,aspect);UiWidget hit=null;
            for(int i=current.Widgets.Count-1;i>=0;i--){var w=current.Widgets[i];if(w.Bundle==_uiFocusBundle&&UiWidgetLocallyVisible(current,w)&&UiFocusControlContains(current,w,point.X,point.Y)){hit=w;break;}}
            _uiFocusHover=hit;
            bool pressed=UiInputPrimaryPressed;
            if(hit!=null&&pressed)SendUiFocusPress(current,hit);
        }
        void DrawUiFocus(ref int budget,ref int compileBudget)
        {
            if(!UiFocusActive||budget<=0||!UiClientCanInteract())return;
            Vector2D center;double units,aspect;if(!UiFocusMetrics(out center,out units,out aspect))return;
            Scene scene;if(!_scenes.TryGetValue(_uiFocusDisplay.TargetId,out scene))return;
            var camera=MyAPIGateway.Session.Camera;var cameraWorld=camera.WorldMatrix;
            double halfHeight=Math.Tan(camera.FovWithZoom*.5)*.6;
            if(!Geometry.Finite(halfHeight)||halfHeight<=0)return;
            double scale=2*halfHeight/units;
            var placement=MatrixD.CreateTranslation(-center.X,-center.Y,0)*MatrixD.CreateScale(scale)
                *MatrixD.CreateTranslation(0,0,-.6)*cameraWorld;
            var volume=new DisplayVolume(MatrixD.CreateTranslation(0,0,-.6)*cameraWorld,
                new[]{new Vector4D(1,0,0,halfHeight*aspect),new Vector4D(-1,0,0,halfHeight*aspect),new Vector4D(0,1,0,halfHeight),new Vector4D(0,-1,0,halfHeight)});
            // A viewer-local backdrop separates the focused canvas from the world without moving the camera.
            var backdrop=MatrixD.CreateTranslation(0,0,-.605)*cameraWorld;
            var ba=Vector3D.Transform(new Vector3D(-halfHeight*aspect,-halfHeight,0),backdrop);
            var bb=Vector3D.Transform(new Vector3D(halfHeight*aspect,-halfHeight,0),backdrop);
            var bc=Vector3D.Transform(new Vector3D(halfHeight*aspect,halfHeight,0),backdrop);
            var bd=Vector3D.Transform(new Vector3D(-halfHeight*aspect,halfHeight,0),backdrop);
            var background=new Vector4(.015f,.025f,.035f,.96f);
            DrawVolumeTriangle(null,ba,bb,bc,Vector2.Zero,Vector2.Zero,Vector2.Zero,background,0,false,null,MatrixD.Identity,MatrixD.Identity,camera.Position,uint.MaxValue,ref budget);
            DrawVolumeTriangle(null,ba,bc,bd,Vector2.Zero,Vector2.Zero,Vector2.Zero,background,0,false,null,MatrixD.Identity,MatrixD.Identity,camera.Position,uint.MaxValue,ref budget);
            foreach(var canonical in scene.Items.Values)
            {
                if(IsProjectedId(canonical.Id))continue;
                if(canonical.CallerId!=_uiFocusDisplay.CallerId||canonical.Layer!="ui-"+_uiFocusBundle||!canonical.Visible)continue;
                var item=ClientDisplayItem(scene,GetAnimatedItem(scene.ConsoleId,canonical),ref compileBudget);if(item==null)continue;
                float alpha=ClientLayerAlpha(scene,item.CallerId,item.Layer)*item.Opacity;if(alpha<=0)continue;
                var g=item.Geometry;
                UiFocusPoints cache;
                if(!_uiFocusPoints.TryGetValue(item.Id,out cache)||!ReferenceEquals(cache.Geometry,g))
                {
                    if(_uiFocusPoints.Count>=MaxObjects&&!_uiFocusPoints.ContainsKey(item.Id))_uiFocusPoints.Clear();
                    cache=new UiFocusPoints{Geometry=g,Points=new Vector3D[g.Points.Length]};_uiFocusPoints[item.Id]=cache;
                }
                var points=cache.Points;
                for(int i=0;i<g.Points.Length;i++)
                {var p=Vector3D.Transform(g.Points[i],item.Transform);p.Z=0;points[i]=Vector3D.Transform(p,placement);}
                for(int i=0;i<g.Triangles.Length&&budget>0;i+=3)
                {
                    var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[i/3];color.W*=alpha;if(color.W<=0)continue;
                    DrawVolumeTriangle(volume,points[g.Triangles[i]],points[g.Triangles[i+1]],points[g.Triangles[i+2]],
                        item.UV==null?Vector2.Zero:item.UV[g.Triangles[i]],item.UV==null?Vector2.UnitX:item.UV[g.Triangles[i+1]],item.UV==null?Vector2.UnitY:item.UV[g.Triangles[i+2]],
                        color,item.Emission,false,item.Material,MatrixD.Identity,MatrixD.Identity,camera.Position,uint.MaxValue,ref budget);
                }
                for(int i=0;i<g.Edges.Length&&budget>0;i+=2)
                {
                    var a=points[g.Edges[i]];var b=points[g.Edges[i+1]];if(!volume.ClipLine(ref a,ref b))continue;
                    var color=item.EdgeColors==null?item.LineColor:item.EdgeColors[i/2];color.W*=alpha;if(color.W<=0)continue;
                    DrawUiFocusLine(a,b,color,(float)Math.Max(.0003,item.Thickness*scale),ref budget);
                }
            }
            foreach(var label in scene.Labels.Values)
            {
                if(IsProjectedId(label.Id))continue;
                if(!label.Visible||label.CallerId!=_uiFocusDisplay.CallerId||label.Layer!="ui-"+_uiFocusBundle)continue;
                float alpha=ClientLayerAlpha(scene,label.CallerId,label.Layer)*label.Opacity;if(alpha<=0)continue;
                if(label.Glyphs==null)
                {try{label.Glyphs=VectorFont.Layout(label.Text);}catch(ArgumentException){label.Glyphs=new List<VectorFont.PositionedGlyph>();}}
                var origin=label.Position;origin.Z=0;var color=label.Color;color.W*=alpha;
                foreach(var glyph in label.Glyphs)
                {
                    var mesh=glyph.Mesh;
                    for(int i=0;i<mesh.Triangles.Length&&budget>0;i+=3)
                    {
                        var a=Vector3D.Transform(origin+(mesh.Points[mesh.Triangles[i]]+glyph.Offset)*label.Height,placement)+cameraWorld.Backward*.001;
                        var b=Vector3D.Transform(origin+(mesh.Points[mesh.Triangles[i+1]]+glyph.Offset)*label.Height,placement)+cameraWorld.Backward*.001;
                        var c=Vector3D.Transform(origin+(mesh.Points[mesh.Triangles[i+2]]+glyph.Offset)*label.Height,placement)+cameraWorld.Backward*.001;
                        DrawVolumeTriangle(volume,a,b,c,Vector2.Zero,Vector2.Zero,Vector2.Zero,color,label.Emission,false,null,MatrixD.Identity,MatrixD.Identity,camera.Position,uint.MaxValue,ref budget);
                    }
                }
            }
            if(_uiFocusHover!=null)
            {
                var w=_uiFocusHover;
                var corners=new[]{new Vector3D(w.X-w.Width*.5,w.Y-w.Height*.5,0),new Vector3D(w.X+w.Width*.5,w.Y-w.Height*.5,0),
                    new Vector3D(w.X+w.Width*.5,w.Y+w.Height*.5,0),new Vector3D(w.X-w.Width*.5,w.Y+w.Height*.5,0)};
                var hoverPlacement=placement;
                if(w.Control!=null){Item item;if(!scene.Items.TryGetValue(Key(_uiFocusDisplay.CallerId,w.Control.Artwork),out item))return;hoverPlacement=item.Transform*placement;}
                for(int i=0;i<4;i++)
                {
                    var a=Vector3D.Transform(corners[i],hoverPlacement)+cameraWorld.Backward*.002;
                    var b=Vector3D.Transform(corners[(i+1)%4],hoverPlacement)+cameraWorld.Backward*.002;
                    if(volume.ClipLine(ref a,ref b))DrawUiFocusLine(a,b,new Vector4(1,.8f,.15f,1),.001f,ref budget);
                }
            }
            var cursor=UiFocusLayout.Point(_uiFocusCursor,center,units,aspect);
            var cursorWorld=Vector3D.Transform(new Vector3D(cursor.X,cursor.Y,0),placement)+cameraWorld.Backward*.002;
            DrawUiFocusLine(cursorWorld-cameraWorld.Right*.006,cursorWorld+cameraWorld.Right*.006,new Vector4(1,1,1,1),.0008f,ref budget);
            DrawUiFocusLine(cursorWorld-cameraWorld.Up*.006,cursorWorld+cameraWorld.Up*.006,new Vector4(1,1,1,1),.0008f,ref budget);
        }
        void DrawUiFocusLine(Vector3D a,Vector3D b,Vector4 color,float thickness,ref int budget)
        {
            if(budget<=0)return;var delta=b-a;double length=delta.Length();if(length<1e-9)return;
            var inverse=MatrixD.Identity;MyTransparentGeometry.AddLineBillboard(LineMaterial,Premultiply(color,1),a,uint.MaxValue,ref inverse,
                (Vector3)(delta/length),(float)length,thickness,VRageRender.MyBillboard.BlendTypeEnum.Standard);budget--;
        }
    }
}
