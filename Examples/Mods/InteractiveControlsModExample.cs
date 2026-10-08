using System;
using Hdr.Mods;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRageMath;

namespace HdrExamples
{
    // HDR API 0.9.9, ALPHA. Copy with Api/Mods/HdrModApi.cs. Client-local HUD;
    // numeric controls and a caller-owned cooperative input route need no plugin.
    // Wire the three cooperative-input methods to YOUR mod's owned UI events.
    // Public mouse reads alone do not establish exclusive input suppression.
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class InteractiveControlsModExample : MySessionComponentBase
    {
        HdrModApi hdr;
        long hud, connection;
        int retry;
        double _throttle, _trim, _angle;
        bool dirty, ownsPointer, hasSample, leftHeld;
        double pointerX, pointerY;
        string lastEvent = "Waiting for caller-owned input";
        readonly Vector3D[] path = {
            Vector3D.Zero, new Vector3D(65,-60,0), new Vector3D(180,-85,0),
            new Vector3D(300,-40,0), new Vector3D(420,25,0)
        };
        const string Handle = @"<svg viewBox='0 0 32 32' xmlns='http://www.w3.org/2000/svg'>
            <circle cx='16' cy='16' r='13' fill='#164753' stroke='#75edec' stroke-width='2'/>
            <path d='M10 16 H22 M16 10 V22' fill='none' stroke='#f5bd55' stroke-width='2'/>
        </svg>";
        const string Rotor = @"<svg viewBox='0 0 120 120' xmlns='http://www.w3.org/2000/svg'>
            <path d='M47 54 H87 V42 L108 60 L87 78 V66 H47 Z' fill='#f5bd55' stroke='#ffe0a0' stroke-width='2'/>
            <circle cx='60' cy='60' r='8' fill='#123e4b' stroke='#75edec' stroke-width='2'/>
        </svg>";

        public override void BeforeStart()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
                hdr = new HdrModApi("example.hdr.interactive-controls");
        }

        // Invoke on the client simulation thread only after your UI owns the route.
        public void BeginCooperativeInput()
        {
            ownsPointer = true; hasSample = false; leftHeld = false;
        }
        // Coordinates are HDR HUD pixels, already converted from your UI viewport.
        public void SubmitOwnedPointer(double x, double y, bool leftButtonHeld)
        {
            if (!ownsPointer || double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y)) return;
            pointerX = x; pointerY = y; leftHeld = leftButtonHeld; hasSample = true;
        }
        public void EndCooperativeInput()
        {
            ownsPointer = false; hasSample = false; leftHeld = false;
            if (hdr != null && hdr.Ready && hud != 0) hdr.CancelPointer(hud);
        }

        // Explicit source integration: ordinary variables are read by registered
        // getters in UpdateBindings. No field-name reflection or Draw callback.
        public void SetThrottleFromSimulation(double requested) { _throttle = requested; dirty = true; }
        public void SetTrimFromSimulation(double requested) { _trim = requested; dirty = true; }
        public void SetAngleFromSimulation(double radians) { _angle = radians; dirty = true; }

        void Build()
        {
            hud = hdr.CreateHud(20);
            hdr.Rect(hud, "panel", 32, 32, 700, 500, new Vector4(.02f,.04f,.06f,.9f));
            hdr.Text(hud, "title", "HOLOGRAPHIC / CONTROLS", new Vector3D(64,62,0), 28, new Vector4(.75f,.98f,1,1));
            hdr.Text(hud, "line-label", "THROTTLE / LINE / 5% STEPS", new Vector3D(64,114,0), 16, new Vector4(.57f,.84f,.87f,1));
            hdr.Wires(hud, "line-track", new[] { new Vector3D(104,164,0), new Vector3D(524,164,0) }, new[] { new Vector2I(0,1) }, new Vector4(.09f,.48f,.57f,1), 3);
            hdr.Rect(hud, "line-handle", -14, -18, 28, 36, new Vector4(.46f,.93f,.93f,1));
            hdr.Transform(hud, "line-handle", MatrixD.CreateTranslation(104,164,0));
            hdr.Value(hud, "throttle", 0, 0, 100, 5);
            hdr.Control(hud, "throttle-control", "line-handle", new Vector4(-16,-20,32,40));
            hdr.BindControlValue(hud, "throttle-control", "throttle");
            hdr.ConstraintLine(hud, "throttle-control", Vector3D.Zero, new Vector3D(420,0,0));
            hdr.Draggable(hud, "throttle-control", true);

            hdr.Text(hud, "curve-label", "TRIM / POLYLINE / ARC LENGTH", new Vector3D(64,214,0), 16, new Vector4(.57f,.84f,.87f,1));
            var points = new Vector3D[path.Length]; var edges = new Vector2I[path.Length-1];
            for (int i = 0; i < path.Length; i++) { points[i] = path[i] + new Vector3D(104,325,0); if(i+1<path.Length) edges[i] = new Vector2I(i,i+1); }
            hdr.Wires(hud, "curve-track", points, edges, new Vector4(.09f,.48f,.57f,1), 3);
            hdr.Svg(hud, "curve-handle", Handle, MatrixD.CreateTranslation(104,325,0), 8);
            hdr.Value(hud, "trim", 0, 0, 1, .05);
            hdr.Control(hud, "curve-control", "curve-handle", new Vector4(-18,-18,36,36));
            hdr.BindControlValue(hud, "curve-control", "trim");
            hdr.ConstraintPath(hud, "curve-control", path);
            hdr.Draggable(hud, "curve-control", true);

            var ring = new Vector3D[40]; var ringEdges = new Vector2I[40];
            for (int i=0;i<40;i++) { double a = 2*Math.PI*i/40; ring[i] = new Vector3D(55*Math.Cos(a)+570,55*Math.Sin(a)+425,0); ringEdges[i] = new Vector2I(i,(i+1)%40); }
            hdr.Wires(hud, "dial-ring", ring, ringEdges, new Vector4(.09f,.48f,.57f,1), 2);
            hdr.Svg(hud, "rotor", Rotor, MatrixD.CreateTranslation(570,425,0), 8);
            hdr.Value(hud, "angle", 0, -Math.PI/2, Math.PI/2, Math.PI/12);
            hdr.Control(hud, "rotor-control", "rotor", new Vector4(-60,-60,120,120));
            hdr.BindControlValue(hud, "rotor-control", "angle");
            hdr.ConstraintRotation(hud, "rotor-control", Vector3D.Zero, Vector3D.UnitZ, -Math.PI/2, Math.PI/2);
            hdr.Draggable(hud, "rotor-control", true);
            hdr.Text(hud, "angle-label", "ROTOR / 15 DEG STEPS", new Vector3D(64,396,0), 16, new Vector4(.57f,.84f,.87f,1));
            hdr.Text(hud, "scope", "Client local / consumer owns input and replication", new Vector3D(64,506,0), 15, new Vector4(.57f,.84f,.87f,1));
            hdr.Effect(hud, "curve-handle", "flicker", .04, 8); // HUD stays planar.

            // Registration runs no callbacks. UpdateBindings below reconciles explicitly.
            hdr.BindValue(hud, "throttle", () => _throttle, v => { _throttle = v; dirty = true; });
            hdr.BindValue(hud, "trim", () => _trim, v => { _trim = v; dirty = true; });
            hdr.BindValue(hud, "angle", () => _angle, v => { _angle = v; dirty = true; });
            dirty = true;
        }

        public override void UpdateAfterSimulation()
        {
            if (hdr == null) return;
            if (!hdr.Ready) { ownsPointer = false; hasSample = false; if (++retry >= 60) { retry = 0; hdr.Request(); } return; }
            if (connection != hdr.ConnectionGeneration)
            { connection = hdr.ConnectionGeneration; hud = 0; ownsPointer = false; hasSample = false; lastEvent = "New owner lifetime / reconnect your UI input"; }
            if (hud == 0) Build();
            if (ownsPointer && hasSample)
            {
                // Caller samples its owned route each update. No global capture is requested.
                hdr.Pointer(hud, pointerX, pointerY, leftHeld); hasSample = false;
            }
            else hdr.CancelPointer(hud); // Losing the route or a fresh sample cancels the drag.
            hdr.UpdateBindings(); // Getters/setters run here, never during HDR Draw.
            foreach (var e in hdr.PollValueEvents(hud)) { lastEvent = e.Item1 + " / " + e.Item3 + " / revision " + e.Item4.Item2; dirty = true; }
            if (!dirty) return;
            dirty = false;
            hdr.Text(hud, "throttle-readout", _throttle.ToString("0") + " %", new Vector3D(582,152,0), 24, new Vector4(.96f,.74f,.33f,1));
            hdr.Text(hud, "trim-readout", (_trim*100).ToString("0") + " %", new Vector3D(582,284,0), 24, new Vector4(.96f,.74f,.33f,1));
            hdr.Text(hud, "angle-readout", (_angle*180/Math.PI).ToString("0") + " deg", new Vector3D(64,430,0), 28, new Vector4(.96f,.74f,.33f,1));
            hdr.Text(hud, "event", hdr.LastBindingError ?? lastEvent, new Vector3D(64,478,0), 14, new Vector4(.57f,.84f,.87f,1));
        }

        protected override void UnloadData()
        {
            if (hdr != null) hdr.Dispose();
            hdr = null; hud = 0; ownsPointer = hasSample = false;
        }
    }
}
