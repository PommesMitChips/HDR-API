// PB body: append Api/HoloMapApi.cs, or use the generated artifacts/ConsoleDemo.pb.cs.
const string ConsoleName = "Holo Map";
readonly HoloMapApi _holo = new HoloMapApi();
IMyTerminalBlock _console;
double _angle;
bool _animate;

public Program() { Runtime.UpdateFrequency = UpdateFrequency.None; }
public void Main(string argument, UpdateType source)
{
    try
    {
        if (!_holo.IsActive && !_holo.Activate(Me)) { Echo("Enable HDR API mod, then run this PB again."); return; }
        _console = _holo.FindTarget(GridTerminalSystem,ConsoleName);
        if (_console == null) { Echo("Name a Console or Projector Block '" + ConsoleName + "'."); return; }
        string command = (argument ?? "").Trim().ToLowerInvariant();
        if (command == "clear") { _holo.Clear(_console); _animate = false; Runtime.UpdateFrequency = UpdateFrequency.None; return; }
        if (command == "stop") { _animate = false; Runtime.UpdateFrequency = UpdateFrequency.None; return; }
        if (command == "spin") { _animate = true; Runtime.UpdateFrequency = UpdateFrequency.Update10; }
        if ((source & UpdateType.Update10) != 0 && _animate)
        {
            _angle += Math.Min(0.5, Runtime.TimeSinceLastRun.TotalSeconds) * 0.4;
            _holo.SetTransform(_console, "asteroid", HoloMapApi.Pose(0.75,0.25,0,0.45,yaw:_angle));
            Echo("Holo Map: rotating the retained mesh."); return;
        }
        if (command != "spin" && command != "" && command != "demo") { Echo("Commands: demo, spin, stop, clear"); return; }
        _holo.Clear(_console);
        _holo.SetView(_console, new Vector3D(0, 0.85, 0), Vector3D.Zero, 1);
        var cyan = new Vector4(0.1f, 0.9f, 1f, 0.9f);
        _holo.PutCircle(_console,"range",new Vector3D(0,-0.3,0),0.75,cyan,96,0.006f,Vector3D.Up);
        _holo.PutCurve(_console, "wave", t => new Vector3D(t, 0.15 * Math.Sin(8 * t) + 0.5, 0),
            -0.7, 0.7, new Vector4(1, 0.65f, 0.1f, 1), 96);
        _holo.PutWires(_console, "axes", new[] { Vector3D.Zero, new Vector3D(0.9, 0, 0), new Vector3D(0, 0.9, 0), new Vector3D(0, 0, 0.9) },
            new[] { new Vector2I(0, 1), new Vector2I(0, 2), new Vector2I(0, 3) }, new Vector4(0.5f, 0.7f, 1, 0.5f), 0.004f);
        // Synthetic asteroid-like mesh: six irregular vertices, eight triangular faces.
        // This demonstrates rendering only; it does not scan an asteroid.
        var points = new[] { new Vector3D(0.4, 0, 0), new Vector3D(-0.3, 0, 0),
            new Vector3D(0, 0.4, 0), new Vector3D(0, -0.25, 0), new Vector3D(0, 0, 0.35), new Vector3D(0, 0, -0.3) };
        var faces = new[] { new[] { 2, 0, 4 }, new[] { 2, 4, 1 }, new[] { 2, 1, 5 }, new[] { 2, 5, 0 },
            new[] { 3, 4, 0 }, new[] { 3, 1, 4 }, new[] { 3, 5, 1 }, new[] { 3, 0, 5 } };
        _holo.PutPolygons(_console, "asteroid", points, faces, cyan, new Vector4(0.1f, 0.6f, 0.9f, 0.3f), 0.006f, true);
        _holo.SetTransform(_console, "asteroid", HoloMapApi.Pose(0.75,0.25,0,0.45));
        _holo.TrackConstruct(_console, true, 0.55);
        Echo("Holo Map " + _holo.Version + ": live ship + subgrids, circle, curve, shaded mesh.\nCommands: spin, stop, clear");
    }
    catch (Exception ex) { _animate = false; Runtime.UpdateFrequency = UpdateFrequency.None; Echo(ex.Message); }
}
