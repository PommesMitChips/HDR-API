using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
namespace HoloMap
{
 public sealed partial class HoloMapSession
 {
  sealed class LcdVectorEntry{public Item Item;public Label Label;public bool Construct;public int Order;public long Caller;public string Id;}
  sealed class LcdVectorSample{public double Time=-1,Rate;public readonly List<LcdVectorEntry> Entries=new List<LcdVectorEntry>();}
  readonly Dictionary<long,LcdVectorSample> _lcdVectorSamples=new Dictionary<long,LcdVectorSample>();
  static readonly Vector4D[] LcdClipPlanes={new Vector4D(1,0,0,1),new Vector4D(-1,0,0,1),new Vector4D(0,1,0,1),new Vector4D(0,-1,0,1)};
  LcdVectorSample SampleVectorLcd(Scene tile,Scene source,ref int compileBudget)
  {
   LcdVectorSample sample;if(!_lcdVectorSamples.TryGetValue(tile.ConsoleId,out sample)){sample=new LcdVectorSample();_lcdVectorSamples[tile.ConsoleId]=sample;}
   double rate=Math.Min(tile.LcdRefreshHz,ClientLcdRefreshCap),now=_ticks/60.0;
   sample.Rate=rate;
   if(!LcdSampleDue(tile.ConsoleId,rate))return sample;
   sample.Time=now;sample.Entries.Clear();
   foreach(var canonical in source.Items.Values)
   {
    if(IsProjectedId(canonical.Id)||!canonical.Visible)continue;var animated=GetAnimatedItem(source.ConsoleId,canonical);var item=ClientDisplayItem(source,animated,ref compileBudget);if(item==null)continue;
    // Compiled geometry is immutable and shared; sampling only freezes animation metadata.
    sample.Entries.Add(new LcdVectorEntry{Item=CloneAnimationItem(item),Caller=item.CallerId,Id=item.Id,Order=LayerOrder(source,item.CallerId,item.Layer)});
   }
   foreach(var label in source.Labels.Values)
   {
    if(IsProjectedId(label.Id)||!label.Visible)continue;
    if(label.Glyphs==null){try{label.Glyphs=VectorFont.Layout(label.Text);}catch(ArgumentException){label.Glyphs=new List<VectorFont.PositionedGlyph>();}}
    var copy=new Label{CallerId=label.CallerId,Id=label.Id,Text=label.Text,Position=label.Position,Color=label.Color,Height=label.Height,Opacity=label.Opacity,Emission=label.Emission,Visible=label.Visible,Layer=label.Layer,Glyphs=label.Glyphs};
    sample.Entries.Add(new LcdVectorEntry{Label=copy,Caller=label.CallerId,Id=label.Id,Order=LayerOrder(source,label.CallerId,label.Layer)});
   }
   if(source.TrackedRootId!=0)sample.Entries.Add(new LcdVectorEntry{Construct=true,Caller=source.TrackingCallerId,Id="",Order=LayerOrder(source,source.TrackingCallerId,source.ConstructLayer)});
   sample.Entries.Sort((a,b)=>{int order=a.Order.CompareTo(b.Order);if(order!=0)return order;order=a.Caller.CompareTo(b.Caller);if(order!=0)return order;return string.CompareOrdinal(a.Id,b.Id);});
   return sample;
  }
  void DrawVectorLcd(Scene tile,IMyTextPanel panel,LcdScreenBasis basis,ref int budget,ref int compileBudget)
  {
   if(budget<=0)return;
   Scene source=tile;if(tile.LcdSourceId!=0&&!_scenes.TryGetValue(tile.LcdSourceId,out source))return;
   var world=panel.WorldMatrix;var camera=MyAPIGateway.Session.Camera.Position;
   var normal=Vector3D.TransformNormal(basis.Normal,world);var center=Vector3D.Transform(basis.Center,world);
   if(!LcdPinnedProjection.FrontFacing(basis,world,camera))return; // Transparent LCDs are calibrated for their front face.
   var sample=SampleVectorLcd(tile,source,ref compileBudget);var inverse=MatrixD.Invert(world);uint parent=panel.Render.GetRenderObjectID();var view=LocalView(source);
   // Lateral clipping is identical at all bounded layer depths. Reuse one buffer.
   var volume=new DisplayVolume(LcdPinnedProjection.ScreenFrame(basis,world,.001),LcdClipPlanes);
   double units=(basis.HalfRight.Length()*tile.LcdColumns/tile.LcdWidth+basis.HalfUp.Length()*tile.LcdRows/tile.LcdHeight);
   for(int entryIndex=0;entryIndex<sample.Entries.Count&&budget>0;entryIndex++)
   {
    var entry=sample.Entries[entryIndex];double depth=.001+entryIndex*.0000005;
    var mapping=LcdPinnedProjection.CanvasToPanel(basis,tile.LcdWidth,tile.LcdHeight,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow,depth)*world;
    if(entry.Construct){DrawVectorLcdConstruct(source,view*mapping,volume,normal,world,inverse,camera,parent,ref budget);continue;}
    if(entry.Label!=null)
    {
     var label=entry.Label;float alpha=ClientLayerAlpha(source,label.CallerId,label.Layer)*label.Opacity;var color=label.Color;color.W*=alpha;if(color.W<=0)continue;
     foreach(var glyph in label.Glyphs)for(int t=0;t<glyph.Mesh.Triangles.Length&&budget>0;t+=3)
     {
      var g=glyph.Mesh;var a=label.Position+(g.Points[g.Triangles[t]]+glyph.Offset)*label.Height;
      var b=label.Position+(g.Points[g.Triangles[t+1]]+glyph.Offset)*label.Height;var c=label.Position+(g.Points[g.Triangles[t+2]]+glyph.Offset)*label.Height;
      var matrix=view*mapping;DrawVolumeTriangle(volume,Vector3D.Transform(a,matrix),Vector3D.Transform(b,matrix),Vector3D.Transform(c,matrix),Vector2.Zero,Vector2.Zero,Vector2.Zero,color,label.Emission,false,null,world,inverse,camera,parent,ref budget);
     }
     continue;
    }
    var item=entry.Item;float opacity=ClientLayerAlpha(source,item.CallerId,item.Layer)*item.Opacity;if(opacity<=0)continue;
    var geometry=item.Geometry;var effect=SampleLcdEffect(item);var transform=item.Transform*view*mapping;
    for(int i=0;i<geometry.Points.Length;i++)geometry.WorldPoints[i]=Vector3D.Transform(geometry.Points[i],transform);
    for(int t=0;t<geometry.Triangles.Length&&budget>0;t+=3)
    {
     int a=geometry.Triangles[t],b=geometry.Triangles[t+1],c=geometry.Triangles[t+2];
     var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[t/3];color=LcdEffectColor(effect,color,(geometry.Points[a]+geometry.Points[b]+geometry.Points[c])/3,t/3);color.W*=opacity;if(color.W<=0)continue;
     DrawVolumeTriangle(volume,geometry.WorldPoints[a],geometry.WorldPoints[b],geometry.WorldPoints[c],item.UV==null?Vector2.Zero:item.UV[a],item.UV==null?Vector2.UnitX:item.UV[b],item.UV==null?Vector2.UnitY:item.UV[c],color,item.Emission,false,item.Material,world,inverse,camera,parent,ref budget);
    }
    for(int e=0;e<geometry.Edges.Length&&budget>0;e+=2)
    {
     var color=item.EdgeColors==null?item.LineColor:item.EdgeColors[e/2];color=LcdEffectColor(effect,color,(geometry.Points[geometry.Edges[e]]+geometry.Points[geometry.Edges[e+1]])*.5,geometry.Triangles.Length/3+e/2);color.W*=opacity;if(color.W<=0)continue;
     DrawPinnedLine(volume,geometry.WorldPoints[geometry.Edges[e]],geometry.WorldPoints[geometry.Edges[e+1]],normal,Math.Max(.0001,item.Thickness*units),color,item.Emission,world,inverse,camera,parent,ref budget);
    }
   }
   // Retained content receives the frame allowance before any particle decoration.
   for(int i=0;i<sample.Entries.Count&&budget>=4;i++){var item=sample.Entries[i].Item;if(item==null||item.Effects==null)continue;var mapping=LcdPinnedProjection.CanvasToPanel(basis,tile.LcdWidth,tile.LcdHeight,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow,.001+i*.0000005)*world;DrawPinnedLcdEffectExtras(item,SampleLcdEffect(item),item.Transform*view*mapping,volume,ClientLayerAlpha(source,item.CallerId,item.Layer)*item.Opacity,world,inverse,camera,parent,ref budget);}
   DrawPinnedLcdHover(source,tile,basis,panel,view,volume,normal,inverse,camera,parent,ref budget);
  }
  void DrawPinnedLine(DisplayVolume volume,Vector3D a,Vector3D b,Vector3D normal,double width,Vector4 color,float emission,MatrixD world,MatrixD inverse,Vector3D camera,uint parent,ref int budget)
  {
   if(budget<4)return;var side=Vector3D.Cross(b-a,normal);if(side.LengthSquared()<1e-20)return;side.Normalize();side*=width*.5;
   DrawVolumeTriangle(volume,a-side,b-side,b+side,Vector2.Zero,Vector2.Zero,Vector2.Zero,color,emission,false,null,world,inverse,camera,parent,ref budget);
   DrawVolumeTriangle(volume,a-side,b+side,a+side,Vector2.Zero,Vector2.Zero,Vector2.Zero,color,emission,false,null,world,inverse,camera,parent,ref budget);
  }
  void DrawPinnedLcdHover(Scene source,Scene tile,LcdScreenBasis basis,IMyTextPanel panel,MatrixD view,DisplayVolume volume,Vector3D normal,MatrixD inverse,Vector3D camera,uint parent,ref int budget)
  {
   if(_uiHoveredDisplay==null||_uiHoveredWidget==null||_uiHoveredDisplay.TargetId!=source.ConsoleId)return;
   var w=_uiHoveredWidget;var points=new[]{new Vector3D(w.X-w.Width*.5,w.Y-w.Height*.5,0),new Vector3D(w.X+w.Width*.5,w.Y-w.Height*.5,0),new Vector3D(w.X+w.Width*.5,w.Y+w.Height*.5,0),new Vector3D(w.X-w.Width*.5,w.Y+w.Height*.5,0)};
   var world=panel.WorldMatrix;var map=view*LcdPinnedProjection.CanvasToPanel(basis,tile.LcdWidth,tile.LcdHeight,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow,.002)*world;
   for(int i=0;i<4;i++)points[i]=Vector3D.Transform(points[i],map);
   for(int i=0;i<4&&budget>0;i++)DrawPinnedLine(volume,points[i],points[(i+1)%4],normal,.003,new Vector4(1,.8f,0,1),0,world,inverse,camera,parent,ref budget);
  }
 }
}
