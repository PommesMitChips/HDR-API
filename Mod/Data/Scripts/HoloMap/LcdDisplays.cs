using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
namespace HoloMap
{
 public static class LcdProjection
 {
  public static Vector2 Point(Vector3D p,double width,double height,Vector2 pixels,int columns,int rows,int column,int row)
  {return new Vector2((float)((p.X/width+0.5)*pixels.X*columns-column*pixels.X),(float)((0.5-p.Y/height)*pixels.Y*rows-row*pixels.Y));}
  public static void ValidateLayout(Vector3D[] p,int columns,int rows){Validate(2,2,columns,rows,0,0);if(p==null||p.Length!=columns*rows)throw new ArgumentException("Invalid LCD layout.");double dx=columns>1?p[1].X-p[0].X:0,dy=rows>1?p[0].Y-p[columns].Y:0;if(columns>1&&dx<=0.1||rows>1&&dy<=0.1)throw new ArgumentException("LCD panels need distinct rectangular slots.");for(int i=0;i<p.Length;i++)if(Math.Abs(p[i].X-(p[0].X+(i%columns)*dx))>0.1||Math.Abs(p[i].Y-(p[0].Y-(i/columns)*dy))>0.1)throw new ArgumentException("LCD group must form an evenly spaced rectangle.");}
  public static void Validate(double width,double height,int columns,int rows,int column,int row)
  {if(!Geometry.Finite(width)||!Geometry.Finite(height)||width<0.01||height<0.01||width>50||height>50||columns<1||rows<1||columns>8||rows>8||columns*rows>8||column<0||column>=columns||row<0||row>=rows)throw new ArgumentException("Invalid LCD canvas/tile configuration.");}
 }
 public sealed partial class HoloMapSession
 {
  
  void ValidateLcdDetach(IMyProgrammableBlock caller,long id){Scene old;if(!_scenes.TryGetValue(id,out old)||old.LcdSourceId==0)return;long master=old.LcdSourceId;foreach(var scene in _scenes.Values)if(scene.ConsoleId==master||scene.LcdSourceId==master){var panel=MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock;if(panel!=null&&!panel.Closed)Authorize(caller,panel);}}
  void DetachLcdGroup(long id){Scene old;if(!_scenes.TryGetValue(id,out old)||old.LcdSourceId==0)return;long master=old.LcdSourceId;foreach(var scene in _scenes.Values)if(scene.ConsoleId==master||scene.LcdSourceId==master){scene.LcdSourceId=0;scene.LcdColumns=scene.LcdRows=1;scene.LcdColumn=scene.LcdRow=0;scene.LcdWidth=scene.LcdHeight=2;}}
  void ConfigureLcd(IMyProgrammableBlock caller,IMyTerminalBlock target,double width,double height,int columns,int rows,int column,int row)
  {Authorize(caller,target);if(!(target is IMyTextPanel))throw new ArgumentException("LCD configuration needs an LCD panel.");LcdProjection.Validate(width,height,columns,rows,column,row);ValidateLcdDetach(caller,target.EntityId);DetachLcdGroup(target.EntityId);var s=GetScene(target.EntityId);s.LcdWidth=width;s.LcdHeight=height;s.LcdColumns=columns;s.LcdRows=rows;s.LcdColumn=column;s.LcdRow=row;s.LcdSourceId=0;s.LcdCallerId=caller.EntityId;_dirty=true;}
  void SetLcdBackground(IMyProgrammableBlock caller,IMyTerminalBlock target,Vector4 color,int surfaceIndex)
  {
   Authorize(caller,target);Color(color);
   var provider=target as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
   var surface=target as Sandbox.ModAPI.Ingame.IMyTextSurface;
   if(provider!=null)
   {if(surfaceIndex<0||surfaceIndex>=provider.SurfaceCount)throw new ArgumentException("Invalid LCD surface index.");surface=provider.GetSurface(surfaceIndex);}
   else if(surface==null||surfaceIndex!=0)throw new ArgumentException("Invalid LCD surface index.");
   if(surface==null)throw new ArgumentException("LCD surface is unavailable.");
   // Explicit user/PB configuration, not a synchronized write in the draw loop.
   // Native rendering overwrites the clear color's alpha with BackgroundAlpha.
   surface.ScriptBackgroundColor=LcdColor(color,0);
   surface.BackgroundAlpha=(byte)Math.Round(color.W*255);
  }
  void ReleaseLcd(IMyProgrammableBlock caller,IMyTerminalBlock target)
  {
   Authorize(caller,target);if(!(target is IMyTextPanel))throw new ArgumentException("LCD release needs an LCD panel.");
   Scene selected;if(!_scenes.TryGetValue(target.EntityId,out selected))return;
   ValidateLcdDetach(caller,target.EntityId);long master=selected.LcdSourceId==0?selected.ConsoleId:selected.LcdSourceId;
   var ids=new List<long>();foreach(var scene in _scenes.Values)if(scene.ConsoleId==master||scene.LcdSourceId==master)
   {if(scene.LcdCallerId!=caller.EntityId)throw new ArgumentException("Only the controlling PB can release an LCD group.");ids.Add(scene.ConsoleId);}
   foreach(long id in ids){ClearDrawAnimations(caller.EntityId,id);ClearPacked(caller.EntityId,id);ClearUiDisplay(caller.EntityId,id);_scenes.Remove(id);}
   _dirty=true;
  }
  void ConfigureLcdGroup(DrawContext context,string name,int columns,int rows,double width,double height)
  {
   LcdProjection.Validate(width,height,columns,rows,0,0);var panels=DrawTargets(context.Caller,name);panels.RemoveAll(p=>!(p is IMyTextPanel));if(panels.Count!=columns*rows)throw new ArgumentException("LCD group must contain exactly columns × rows panels.");
   foreach(var p in panels)Authorize(context.Caller,p);var reference=panels[0];var inverse=MatrixD.Invert(reference.WorldMatrix);
   foreach(var p in panels)if(Vector3D.Dot(reference.WorldMatrix.Forward,p.WorldMatrix.Forward)<0.999||Vector3D.Dot(reference.WorldMatrix.Up,p.WorldMatrix.Up)<0.999||Math.Abs(Vector3D.Transform(p.GetPosition(),inverse).Z)>0.1)throw new ArgumentException("Grouped LCD panels must face the same way and lie in one plane.");
   panels.Sort((a,b)=>{var pa=Vector3D.Transform(a.GetPosition(),inverse);var pb=Vector3D.Transform(b.GetPosition(),inverse);int y=Math.Round(pb.Y/0.1).CompareTo(Math.Round(pa.Y/0.1));return y!=0?y:pa.X.CompareTo(pb.X);});
   var layout=new Vector3D[panels.Count];for(int i=0;i<panels.Count;i++)layout[i]=Vector3D.Transform(panels[i].GetPosition(),inverse);LcdProjection.ValidateLayout(layout,columns,rows);
   int additional=0;foreach(var p in panels)if(!_scenes.ContainsKey(p.EntityId))additional++;if(_scenes.Count+additional>MaxConsoles)throw new ArgumentException("Display budget reached.");
   foreach(var p in panels)ValidateLcdDetach(context.Caller,p.EntityId);foreach(var p in panels)DetachLcdGroup(p.EntityId);long source=panels[0].EntityId;Scene prior;_scenes.TryGetValue(source,out prior);int renderer=prior==null?0:prior.LcdRenderer;double refresh=prior==null?6:prior.LcdRefreshHz;for(int i=0;i<panels.Count;i++){ConfigureLcd(context.Caller,panels[i],width,height,columns,rows,i%columns,i/columns);_scenes[panels[i].EntityId].LcdSourceId=source;_scenes[panels[i].EntityId].LcdRenderer=renderer;_scenes[panels[i].EntityId].LcdRefreshHz=refresh;}context.Target=panels[0];
  }
  readonly HashSet<long> _lcdDrawn=new HashSet<long>();
  void ClearLocalLcdFrame(long id){var panel=MyAPIGateway.Entities.GetEntityById(id) as IMyTextPanel;if(panel==null||panel.Closed)return;var surface=(Sandbox.ModAPI.Ingame.IMyTextSurface)panel;if(surface.ContentType==ContentType.SCRIPT&&surface.Script=="HDRAPI")using(var frame=surface.DrawFrame()){} }
  void PruneLocalLcdFrames(){if(MyAPIGateway.Utilities.IsDedicated)return;var remove=new List<long>();foreach(var id in _lcdDrawn){Scene tile;if(!_scenes.TryGetValue(id,out tile)||tile.LcdSourceId!=0&&!_scenes.ContainsKey(tile.LcdSourceId)){ClearLocalLcdFrame(id);remove.Add(id);}}foreach(var id in remove)_lcdDrawn.Remove(id);PruneLcdConstructs();PruneLcdBackends();}
  void ClearLocalLcdFrames(){if(MyAPIGateway.Utilities.IsDedicated)return;foreach(var id in _lcdDrawn)ClearLocalLcdFrame(id);_lcdDrawn.Clear();_lcdConstructs.Clear();ClearLcdBackends();}
  sealed class LcdEntry{public Item Item,DrawItem;public Label Label;public bool Construct;public long Caller;public string Id;public int Order;}
  void DrawLcdLabel(Scene scene,Scene tile,Label label,MySpriteDrawFrame frame,Vector2 origin,Vector2 size,MatrixD view,ref int sprites){if(!label.Visible||sprites<=0)return;float alpha=ClientLayerAlpha(scene,label.CallerId,label.Layer)*label.Opacity;var color=label.Color;color.W*=alpha;if(alpha<=0)return;var pos=origin+LcdProjection.Point(Vector3D.Transform(label.Position,view),tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);var sprite=MySprite.CreateText(label.Text,"Debug",LcdColor(color,label.Emission),(float)(label.Height*size.Y*tile.LcdRows/tile.LcdHeight/28),TextAlignment.CENTER);sprite.Position=pos;frame.Add(sprite);sprites--;}
  void DrawLcd(Scene tile,IMyTextPanel panel,ref int budget,ref int compileBudget)
  {
   if(budget<1)return;PruneLcdConstructs();Scene scene=tile;if(tile.LcdSourceId!=0&&!_scenes.TryGetValue(tile.LcdSourceId,out scene))return;
   var surface=(Sandbox.ModAPI.Ingame.IMyTextSurface)panel;if(surface.ContentType!=ContentType.SCRIPT||surface.Script!="HDRAPI")return;
   Vector2 size=surface.SurfaceSize,origin=(surface.TextureSize-size)*0.5f;int sprites=Math.Min(4096,budget);var view=LocalView(scene);
   // A nonempty registered HoloMap Script makes native DispatchSprites local-only on hosts too.
   using(var frame=surface.DrawFrame())
   {
    // The native offscreen clear already uses the surface's chosen background.
    // An extra opaque quad would defeat BackgroundAlpha on transparent LCDs.
    frame.Add(new MySprite{Type=SpriteType.CLIP_RECT,Position=origin,Size=size});sprites--; var entries=new List<LcdEntry>();foreach(var value in scene.Items.Values)if(!IsProjectedId(value.Id))entries.Add(new LcdEntry{Item=value,Caller=value.CallerId,Id=value.Id,Order=LayerOrder(scene,value.CallerId,value.Layer)});foreach(var value in scene.Labels.Values)if(!IsProjectedId(value.Id))entries.Add(new LcdEntry{Label=value,Caller=value.CallerId,Id=value.Id,Order=LayerOrder(scene,value.CallerId,value.Layer)});if(scene.TrackedRootId!=0)entries.Add(new LcdEntry{Construct=true,Caller=scene.TrackingCallerId,Id="",Order=LayerOrder(scene,scene.TrackingCallerId,scene.ConstructLayer)});entries.Sort((a,b)=>{int order=a.Order.CompareTo(b.Order);if(order!=0)return order;order=a.Caller.CompareTo(b.Caller);if(order!=0)return order;order=string.CompareOrdinal(a.Id,b.Id);if(order!=0)return order;return (a.Construct?0:a.Label==null?1:2).CompareTo(b.Construct?0:b.Label==null?1:2);});
    foreach(var entry in entries)
    {
     if(sprites<=0)break;if(entry.Construct){DrawLcdConstruct(scene,tile,frame,origin,size,ref sprites);continue;}if(entry.Label!=null){DrawLcdLabel(scene,tile,entry.Label,frame,origin,size,view,ref sprites);continue;}var canonical=entry.Item;
     if(!canonical.Visible||ClientLayerAlpha(scene,canonical.CallerId,canonical.Layer)<=0)continue;var animated=GetAnimatedItem(scene.ConsoleId,canonical);if(DrawLcdText(scene,tile,animated,frame,origin,size,view,ref sprites))continue;var item=ClientDisplayItem(scene,animated,ref compileBudget);if(item==null)continue;float alpha=ClientLayerAlpha(scene,item.CallerId,item.Layer)*item.Opacity;if(alpha<=0)continue;
     entry.DrawItem=item;var g=item.Geometry;var effect=SampleLcdEffect(item);var matrix=item.Transform*view;var points=new Vector2[g.Points.Length];for(int i=0;i<points.Length;i++)points[i]=origin+LcdProjection.Point(Vector3D.Transform(g.Points[i],matrix),tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);
     if(item.Material!=null){DrawLcdImage(frame,item,points,LcdEffectColor(effect,item.FillColor,(effect.Min+effect.Max)*.5,0),alpha,ref sprites);continue;}
     for(int i=0;i<g.Triangles.Length&&sprites>0;i+=3)
     {
      var color=item.TriangleColors==null?item.FillColor:item.TriangleColors[i/3];color=LcdEffectColor(effect,color,(g.Points[g.Triangles[i]]+g.Points[g.Triangles[i+1]]+g.Points[g.Triangles[i+2]])/3,i/3);color.W*=alpha;if(color.W<=0)continue;
      var lcdColor=LcdColor(color,item.Emission);
      var a=points[g.Triangles[i]];var b=points[g.Triangles[i+1]];var c=points[g.Triangles[i+2]];
      if(i+5<g.Triangles.Length)
      {
       var next=item.TriangleColors==null?item.FillColor:item.TriangleColors[i/3+1];next=LcdEffectColor(effect,next,(g.Points[g.Triangles[i+3]]+g.Points[g.Triangles[i+4]]+g.Points[g.Triangles[i+5]])/3,i/3+1);next.W*=alpha;
       LcdRectanglePiece rectangle;
       if(next.W>0&&lcdColor.Equals(LcdColor(next,item.Emission))&&LcdTriangleGeometry.TryRectangle(a,b,c,points[g.Triangles[i+3]],points[g.Triangles[i+4]],points[g.Triangles[i+5]],out rectangle))
       {
        // One quad has no alpha-masked internal diagonal. Never expand/overlap
        // triangles: that would brighten seams on translucent artwork.
        LcdRectangle(frame,rectangle,lcdColor,origin,size,ref sprites);i+=3;continue;
       }
      }
      LcdTriangle(frame,a,b,c,lcdColor,origin,size,ref sprites);
     }
     for(int i=0;i<g.Edges.Length&&sprites>0;i+=2){sprites--;var color=item.EdgeColors==null?item.LineColor:item.EdgeColors[i/2];color=LcdEffectColor(effect,color,(g.Points[g.Edges[i]]+g.Points[g.Edges[i+1]])*.5,g.Triangles.Length/3+i/2);color.W*=alpha;if(color.W>0)LcdLine(frame,points[g.Edges[i]],points[g.Edges[i+1]],Math.Max(1,(float)(item.Thickness*size.X*tile.LcdColumns/tile.LcdWidth)),LcdColor(color,item.Emission),origin,size,ref sprites);}
    }
    foreach(var entry in entries){var item=entry.DrawItem;if(item==null||item.Effects==null||sprites<=0)continue;DrawLcdEffectExtras(tile,item,SampleLcdEffect(item),frame,origin,size,item.Transform*view,ClientLayerAlpha(scene,item.CallerId,item.Layer)*item.Opacity,ref sprites);}
    DrawUiLcdHover(scene,tile,frame,origin,size,ref sprites);
   }
   _lcdDrawn.Add(tile.ConsoleId);budget-=Math.Min(4096,budget)-sprites;
  }
  bool DrawLcdText(Scene scene,Scene tile,Item item,MySpriteDrawFrame frame,Vector2 origin,Vector2 size,MatrixD view,ref int sprites){if(item.Effects!=null||item.TextSource==null||item.ClipPlanes!=null&&item.ClipPlanes.Length!=0||item.Gradient!=null)return false;var matrix=item.Transform*view;var x=Vector3D.TransformNormal(Vector3D.UnitX,matrix);var y=Vector3D.TransformNormal(Vector3D.UnitY,matrix);if(x.X<=0||y.Y<=0||Math.Abs(x.Y)>1e-6||Math.Abs(y.X)>1e-6)return false;if(sprites<=0)return true;float alpha=ClientLayerAlpha(scene,item.CallerId,item.Layer)*item.Opacity;var color=item.FillColor;color.W*=alpha;var position=origin+LcdProjection.Point(matrix.Translation,tile.LcdWidth,tile.LcdHeight,size,tile.LcdColumns,tile.LcdRows,tile.LcdColumn,tile.LcdRow);var align=item.TextAnchor=="start"?TextAlignment.LEFT:item.TextAnchor=="end"?TextAlignment.RIGHT:TextAlignment.CENTER;var sprite=MySprite.CreateText(item.TextSource,"Debug",LcdColor(color,item.Emission),(float)(item.TextHeight*y.Y*size.Y*tile.LcdRows/tile.LcdHeight/28),align);sprite.Position=position;frame.Add(sprite);sprites--;return true;}
  static void DrawLcdImage(MySpriteDrawFrame frame,Item item,Vector2[] points,Vector4 color,float alpha,ref int sprites){if(points.Length!=4||item.UV==null||item.ClipPlanes!=null&&item.ClipPlanes.Length!=0||item.Gradient!=null||sprites<=0)return;var definition=Sandbox.Definitions.MyDefinitionManager.Static.GetDefinition<Sandbox.Definitions.MyLCDTextureDefinition>(VRage.Utils.MyStringHash.GetOrCompute(item.Material));if(definition==null)return;var x=points[1]-points[0];var y=points[3]-points[0];float w=x.Length(),h=y.Length();if(w<.01f||h<.01f||Math.Abs(Vector2.Dot(x,y))>w*h*.001f)return;var sprite=MySprite.CreateSprite(item.Material,(points[0]+points[2])*.5f,new Vector2(w,h));color.W*=alpha;sprite.Color=LcdColor(color,item.Emission);sprite.RotationOrScale=(float)Math.Atan2(x.Y,x.X);frame.Add(sprite);sprites--;}
  static Color LcdColor(Vector4 c,float emission){return new Color(MathHelper.Clamp(c.X*(1+emission),0,1),MathHelper.Clamp(c.Y*(1+emission),0,1),MathHelper.Clamp(c.Z*(1+emission),0,1),MathHelper.Clamp(c.W,0,1));}
  static void LcdLine(MySpriteDrawFrame frame,Vector2 a,Vector2 b,float width,Color color,Vector2 origin,Vector2 size,ref int budget)
  {if(budget<=0)return;var d=b-a;float length=d.Length();if(length<0.01f||Math.Max(a.X,b.X)<origin.X||Math.Min(a.X,b.X)>origin.X+size.X||Math.Max(a.Y,b.Y)<origin.Y||Math.Min(a.Y,b.Y)>origin.Y+size.Y)return;var sprite=MySprite.CreateSprite("SquareSimple",(a+b)*0.5f,new Vector2(length,width));sprite.Color=color;sprite.RotationOrScale=(float)Math.Atan2(d.Y,d.X);frame.Add(sprite);budget--;}
  static void LcdTriangle(MySpriteDrawFrame frame,Vector2 a,Vector2 b,Vector2 c,Color color,Vector2 origin,Vector2 size,ref int budget)
  {
   if(budget<=0||Math.Max(a.X,Math.Max(b.X,c.X))<origin.X||Math.Min(a.X,Math.Min(b.X,c.X))>origin.X+size.X||Math.Max(a.Y,Math.Max(b.Y,c.Y))<origin.Y||Math.Min(a.Y,Math.Min(b.Y,c.Y))>origin.Y+size.Y)return;
   var pieces=LcdTriangleGeometry.Decompose(a,b,c);
   // Atomic fill: never render one half when the second cannot fit.
   if(pieces.Length>budget)return;
   foreach(var piece in pieces){var sprite=MySprite.CreateSprite(piece.Mirrored?"HDRAPI_TriangleLeft":"HDRAPI_TriangleRight",piece.Center,piece.Size);sprite.RotationOrScale=piece.Rotation;sprite.Color=color;frame.Add(sprite);budget--;}
  }
  static void LcdRectangle(MySpriteDrawFrame frame,LcdRectanglePiece rectangle,Color color,Vector2 origin,Vector2 size,ref int budget)
  {
   if(budget<=0)return;
   float co=Math.Abs((float)Math.Cos(rectangle.Rotation)),si=Math.Abs((float)Math.Sin(rectangle.Rotation));
   var half=new Vector2(co*rectangle.Size.X+si*rectangle.Size.Y,si*rectangle.Size.X+co*rectangle.Size.Y)*.5f;
   if(rectangle.Center.X+half.X<origin.X||rectangle.Center.X-half.X>origin.X+size.X
      ||rectangle.Center.Y+half.Y<origin.Y||rectangle.Center.Y-half.Y>origin.Y+size.Y)return;
   var sprite=MySprite.CreateSprite("SquareSimple",rectangle.Center,rectangle.Size);sprite.RotationOrScale=rectangle.Rotation;sprite.Color=color;frame.Add(sprite);budget--;
  }
 }
}
