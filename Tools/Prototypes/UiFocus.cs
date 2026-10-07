using System;
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
        // Dormant unless integration proves a safe, acknowledged input-capture lease.
        // Receiving an ordinary menu/focus acknowledgement must not set this flag.
        bool UiFocusCaptureReady;
        UiDisplay _uiFocusDisplay;
        string _uiFocusBundle;
        Vector2 _uiFocusCursor=new Vector2(.5f,.5f);
        bool UiFocusActive { get { return UiFocusCaptureReady&&_uiFocusDisplay!=null; } }
        void BeginUiFocus(UiDisplay display,string bundle)
        {
            if(!UiFocusCaptureReady||display==null)return;
            Vector2D center,size;if(!UiFocusLayout.Bounds(display,bundle,out center,out size))return;
            _uiFocusDisplay=display;_uiFocusBundle=bundle;_uiFocusCursor=new Vector2(.5f,.5f);
        }
        void ClearUiFocus(){_uiFocusDisplay=null;_uiFocusBundle=null;UiFocusCaptureReady=false;}
        bool UiFocusMetrics(out Vector2D center,out double units,out double aspect)
        {
            center=Vector2D.Zero;units=aspect=1;
            if(!UiFocusActive)return false;Vector2D size;
            if(!UiFocusLayout.Bounds(_uiFocusDisplay,_uiFocusBundle,out center,out size))return false;
            var area=MyAPIGateway.Input.GetMouseAreaSize();if(area.X<1||area.Y<1)return false;
            aspect=(double)area.X/area.Y;units=Math.Max(size.Y,size.X/aspect)/.8;return Geometry.Finite(units)&&units>0;
        }
        void TickUiFocus()
        {
            if(!UiFocusActive)return;
            var current=GetUiDisplay(_uiFocusDisplay.CallerId,_uiFocusDisplay.TargetId);
            if(!UiClientCanInteract()||current==null||current.Revision!=_uiFocusDisplay.Revision){ClearUiClient();return;}
            _uiFocusCursor+=new Vector2(MyAPIGateway.Input.GetMouseXForGamePlay(),MyAPIGateway.Input.GetMouseYForGamePlay())*.002f;
            _uiFocusCursor.X=MathHelper.Clamp(_uiFocusCursor.X,0,1);_uiFocusCursor.Y=MathHelper.Clamp(_uiFocusCursor.Y,0,1);
            Vector2D center;double units,aspect;if(!UiFocusMetrics(out center,out units,out aspect))return;
            var point=UiFocusLayout.Point(_uiFocusCursor,center,units,aspect);UiWidget hit=null;
            for(int i=current.Widgets.Count-1;i>=0;i--){var w=current.Widgets[i];if(w.Bundle==_uiFocusBundle&&UiWidgetLocallyVisible(current,w)&&UiRules.Contains(w,point.X,point.Y)){hit=w;break;}}
            if(hit!=null&&MyAPIGateway.Input.IsNewLeftMousePressed())SendUiPress(current,hit);
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
            foreach(var canonical in scene.Items.Values)
            {
                if(canonical.CallerId!=_uiFocusDisplay.CallerId||canonical.Layer!="ui-"+_uiFocusBundle||!canonical.Visible)continue;
                var item=ClientDisplayItem(scene,GetAnimatedItem(scene.ConsoleId,canonical),ref compileBudget);if(item==null)continue;
                float alpha=ClientLayerAlpha(scene,item.CallerId,item.Layer)*item.Opacity;if(alpha<=0)continue;
                var g=item.Geometry;var world=item.Transform*placement;
                for(int i=0;i<g.Points.Length;i++)g.WorldPoints[i]=Vector3D.Transform(g.Points[i],world);
                for(int i=0;i<g.Triangles.Length&&budget>0;i+=3)
                {
                    var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[i/3];color.W*=alpha;if(color.W<=0)continue;
                    DrawVolumeTriangle(volume,g.WorldPoints[g.Triangles[i]],g.WorldPoints[g.Triangles[i+1]],g.WorldPoints[g.Triangles[i+2]],
                        item.UV==null?Vector2.Zero:item.UV[g.Triangles[i]],item.UV==null?Vector2.UnitX:item.UV[g.Triangles[i+1]],item.UV==null?Vector2.UnitY:item.UV[g.Triangles[i+2]],
                        color,item.Emission,false,item.Material,MatrixD.Identity,MatrixD.Identity,camera.Position,uint.MaxValue,ref budget);
                }
                for(int i=0;i<g.Edges.Length&&budget>0;i+=2)
                {
                    var a=g.WorldPoints[g.Edges[i]];var b=g.WorldPoints[g.Edges[i+1]];if(!volume.ClipLine(ref a,ref b))continue;
                    var color=item.EdgeColors==null?item.LineColor:item.EdgeColors[i/2];color.W*=alpha;if(color.W<=0)continue;
                    DrawUiFocusLine(a,b,color,(float)Math.Max(.0003,item.Thickness*scale),ref budget);
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
