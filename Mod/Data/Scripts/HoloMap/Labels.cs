using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using VRage.Game;
using VRageRender;
using PbBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;

namespace HoloMap
{
    public sealed partial class HoloMapSession
    {
        sealed class Label
        {
            public long CallerId;
            public string Id, Text;
            public Vector3D Position;
            public Vector4 Color;
            public float Height;
            public float Opacity=1,Emission;
            public bool Visible = true;
            public string Layer = "";
            public List<VectorFont.PositionedGlyph> Glyphs;
        }
        static void LabelStyle(Vector3D position, string text, Vector4 color, float height)
        {
            Geometry.ValidatePoints(new[] { position }); Color(color);
            if (text == null || text.Length > 64) throw new ArgumentException("Label text must contain at most 64 characters.");
            if (!Geometry.Finite(height) || height < 0.01f || height > 0.5f) throw new ArgumentException("Label height must be 0.01–0.5 display meters.");
        }
        MyTuple<bool, string> PutLabel(PbBlock caller, PbBlock console, string id, Vector3D position, string text, Vector4 color, float height)
        {
            return Guard(() =>
            {
                var target = Authorize(caller, console); ValidateWriteId(caller,console,id); LabelStyle(position, text, color, height);
                var scene = GetScene(target.EntityId); string key = Key(caller.EntityId, id);
                if (!scene.Labels.ContainsKey(key) && scene.Labels.Count >= 16) throw new ArgumentException("Console label limit reached.");
                int characters = text.Length;
                foreach (var entry in scene.Labels) if (entry.Key != key) characters += entry.Value.Text.Length;
                if (characters > 256) throw new ArgumentException("Console label character budget exceeded.");
                if ((Vector3D.Transform(position, LocalView(scene)) - scene.Offset).LengthSquared() > MaxDisplayRadius * MaxDisplayRadius)
                    throw new ArgumentException("Label position exceeds the display radius.");
                Label prior; scene.Labels.TryGetValue(key, out prior);
                if(target is IMyTextPanel)scene.LcdCallerId=caller.EntityId; scene.Labels[key] = new Label { CallerId = caller.EntityId, Id = id, Position = position, Text = text,
                    Color = color, Height = height, Visible = prior == null || prior.Visible, Layer = prior == null ? (_screenWriteLayerPrefix==null?"":_screenWriteLayerPrefix+"default") : prior.Layer };
                scene.Labels[key].Opacity=prior==null?1:prior.Opacity;scene.Labels[key].Emission=prior==null?0:prior.Emission;
            });
        }
        void DrawLabels(Scene scene, MatrixD consoleWorld, MatrixD cameraWorld, uint anchorId, ref int budget)
        {
            var view = LocalView(scene) * consoleWorld;
            var inverse = MatrixD.Invert(consoleWorld);
            var right = cameraWorld.Right; var up = cameraWorld.Up;
            var volume=GetDisplayVolume(scene,MyAPIGateway.Entities.GetEntityById(scene.ConsoleId) as IMyTerminalBlock);
            foreach (var label in scene.Labels.Values)
            {
                if(IsProjectedId(label.Id))continue;
                float opacity = ClientLayerAlpha(scene, label.CallerId, label.Layer)*label.Opacity;
                if (!label.Visible || label.Color.W <= 0 || opacity <= 0) continue;
                var color = label.Color; color.W *= opacity;
                var origin = Vector3D.Transform(label.Position, view);
                // Text shaping/tessellation runs only on the viewing client.
                if(label.Glyphs==null)
                {
                    try {label.Glyphs=VectorFont.Layout(label.Text);}
                    catch(ArgumentException) {label.Glyphs=new List<VectorFont.PositionedGlyph>();}
                }
                foreach(var glyph in label.Glyphs)
                {
                    var mesh=glyph.Mesh;
                    for(int triangle=0;triangle<mesh.Triangles.Length;triangle+=3)
                    {
                        if(budget<=0)return;
                        var p=mesh.Points[mesh.Triangles[triangle]]+glyph.Offset;
                        var q=mesh.Points[mesh.Triangles[triangle+1]]+glyph.Offset;
                        var r=mesh.Points[mesh.Triangles[triangle+2]]+glyph.Offset;
                        var a=origin+(right*p.X+up*p.Y)*label.Height;
                        var b=origin+(right*q.X+up*q.Y)*label.Height;
                        var c=origin+(right*r.X+up*r.Y)*label.Height;
                        DrawVolumeTriangle(volume,a,b,c,Vector2.Zero,Vector2.Zero,Vector2.Zero,color,label.Emission,false,null,consoleWorld,inverse,cameraWorld.Translation,anchorId,ref budget);
                    }
                }
            }
        }
    }
    public struct GlyphRect { public double X, Y, Width; }
    public static class GlyphFont
    {
        // Self-contained, intentionally simple uppercase 5x7 lettering; no HUD/font dependency.
        static readonly Dictionary<char, byte[]> Rows = new Dictionary<char, byte[]>
        {
            {'A',new byte[]{14,17,17,31,17,17,17}}, {'B',new byte[]{30,17,17,30,17,17,30}},
            {'C',new byte[]{14,17,16,16,16,17,14}}, {'D',new byte[]{30,17,17,17,17,17,30}},
            {'E',new byte[]{31,16,16,30,16,16,31}}, {'F',new byte[]{31,16,16,30,16,16,16}},
            {'G',new byte[]{14,17,16,23,17,17,15}}, {'H',new byte[]{17,17,17,31,17,17,17}},
            {'I',new byte[]{14,4,4,4,4,4,14}}, {'J',new byte[]{7,2,2,2,2,18,12}},
            {'K',new byte[]{17,18,20,24,20,18,17}}, {'L',new byte[]{16,16,16,16,16,16,31}},
            {'M',new byte[]{17,27,21,21,17,17,17}}, {'N',new byte[]{17,25,21,19,17,17,17}},
            {'O',new byte[]{14,17,17,17,17,17,14}}, {'P',new byte[]{30,17,17,30,16,16,16}},
            {'Q',new byte[]{14,17,17,17,21,18,13}}, {'R',new byte[]{30,17,17,30,20,18,17}},
            {'S',new byte[]{15,16,16,14,1,1,30}}, {'T',new byte[]{31,4,4,4,4,4,4}},
            {'U',new byte[]{17,17,17,17,17,17,14}}, {'V',new byte[]{17,17,17,17,17,10,4}},
            {'W',new byte[]{17,17,17,21,21,21,10}}, {'X',new byte[]{17,17,10,4,10,17,17}},
            {'Y',new byte[]{17,17,10,4,4,4,4}}, {'Z',new byte[]{31,1,2,4,8,16,31}},
            {'0',new byte[]{14,17,19,21,25,17,14}}, {'1',new byte[]{4,12,4,4,4,4,14}},
            {'2',new byte[]{14,17,1,2,4,8,31}}, {'3',new byte[]{30,1,1,14,1,1,30}},
            {'4',new byte[]{2,6,10,18,31,2,2}}, {'5',new byte[]{31,16,16,30,1,1,30}},
            {'6',new byte[]{14,16,16,30,17,17,14}}, {'7',new byte[]{31,1,2,4,8,8,8}},
            {'8',new byte[]{14,17,17,14,17,17,14}}, {'9',new byte[]{14,17,17,15,1,1,14}},
            {' ',new byte[]{0,0,0,0,0,0,0}}, {'.',new byte[]{0,0,0,0,0,4,4}},
            {':',new byte[]{0,4,4,0,4,4,0}}, {'-',new byte[]{0,0,0,31,0,0,0}},
            {'+',new byte[]{0,4,4,31,4,4,0}}, {'/',new byte[]{1,2,2,4,8,8,16}},
            {'%',new byte[]{25,26,2,4,8,11,19}}, {'(',new byte[]{2,4,8,8,8,4,2}},
            {')',new byte[]{8,4,2,2,2,4,8}}, {'[',new byte[]{14,8,8,8,8,8,14}},
            {']',new byte[]{14,2,2,2,2,2,14}}, {'?',new byte[]{14,17,1,2,4,0,4}}
        };
        public static List<GlyphRect> Layout(string text)
        {
            if (text == null) throw new ArgumentException("Text is null.");
            var rectangles = new List<GlyphRect>(); double width = Math.Max(0, text.Length * 6 - 1);
            text = text.ToUpperInvariant();
            for (int character = 0; character < text.Length; character++)
            {
                byte[] rows; if (!Rows.TryGetValue(text[character], out rows)) rows = Rows['?'];
                for (int y = 0; y < 7; y++)
                {
                    int x = 0;
                    while (x < 5)
                    {
                        if ((rows[y] & (1 << (4 - x))) == 0) { x++; continue; }
                        int start = x;
                        while (x < 5 && (rows[y] & (1 << (4 - x))) != 0) x++;
                        rectangles.Add(new GlyphRect { X = character * 6 + start + (x - start) * 0.5 - width * 0.5,
                            Y = 3 - y, Width = x - start });
                    }
                }
            }
            return rectangles;
        }
    }
}

