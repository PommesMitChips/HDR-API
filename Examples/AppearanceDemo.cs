// Demo: gradients + clipping + text + holes for SVG and native geometry.
const string ConsoleName="Holo Map";
readonly HoloMapApi _holo=new HoloMapApi();
IMyTerminalBlock _console;
const string SvgPanel=@"<svg viewBox='0 0 100 100' data-gradient-resolution='4'>
 <defs>
  <linearGradient id='heat'><stop offset='0' stop-color='#00dfff'/><stop offset='1' stop-color='#ff9a30'/></linearGradient>
  <clipPath id='window'><rect x='8' y='8' width='84' height='84'/></clipPath>
 </defs>
 <g clip-path='url(#window)'>
  <path fill='url(#heat)' fill-rule='evenodd' d='M0 0 H100 V100 H0 Z M28 28 H72 V72 H28 Z'/>
  <text x='50' y='60' font-size='16' text-anchor='middle' fill='#c8fbff'>SVG</text>
 </g>
</svg>";
public Program(){Runtime.UpdateFrequency=UpdateFrequency.None;}
public void Main(string argument,UpdateType source)
{
    try
    {
        if(!_holo.IsActive&&!_holo.Activate(Me)){Echo("Enable updated HDR API and reload save.");return;}
        _console=_holo.FindTarget(GridTerminalSystem,ConsoleName);if(_console==null){Echo("Name a Console or Projector 'Holo Map'.");return;}
        if(argument=="clear"){_holo.Clear(_console);return;}
        _holo.Clear(_console);_holo.SetView(_console,new Vector3D(0,0.85,0),Vector3D.Zero,1);
        _holo.PutSvg(_console,"svg-panel",SvgPanel,new Vector3D(-0.6,0.15,0),0.008,4);
        _holo.SetTransparency(_console,"svg-panel",0.2f);_holo.SetEmission(_console,"svg-panel",0.8f);
        var outer=new[]{new Vector3D(-0.4,-0.4,0),new Vector3D(0.4,-0.4,0),new Vector3D(0.4,0.4,0),new Vector3D(-0.4,0.4,0)};
        var hole=new[]{new Vector3D(-0.15,-0.15,0),new Vector3D(0.15,-0.15,0),new Vector3D(0.15,0.15,0),new Vector3D(-0.15,0.15,0)};
        _holo.PutContours(_console,"native-panel",new[]{outer,hole},new Vector4(0,1,1,1),Vector4.One);
        _holo.SetClipBox(_console,"native-panel",new Vector3D(-0.32,-0.32,-1),new Vector3D(0.32,0.32,1));
        _holo.SetGradient(_console,"native-panel",new Vector3D(-0.4,0,0),new Vector3D(0.4,0,0),new Vector4(0,0.8f,1,1),new Vector4(1,0.6f,0.1f,1),4);
        _holo.SetTransform(_console,"native-panel",HoloMapApi.Pose(0.5,0.15,0));
        _holo.SetEmission(_console,"native-panel",1);
        _holo.PutText(_console,"native-text","PB",new Vector4(0.8f,1,1,1),0.13,HoloMapApi.Pose(0.5,0.15,0));
        _holo.PutCurve(_console,"curve",t=>new Vector3D(t,-0.4+0.06*Math.Sin(12*t),0),-0.9,0.9,Vector4.One,32);
        _holo.SetGradient(_console,"curve",new Vector3D(-0.9,0,0),new Vector3D(0.9,0,0),new Vector4(0,1,1,1),new Vector4(1,0.5f,0,1),3);
        _holo.SetClip(_console,"curve",new[]{new Vector4(1,0,0,0.7f),new Vector4(-1,0,0,0.7f)});
        _holo.PutVertices(_console,"markers",new[]{new Vector3D(-0.7,-0.4,0),new Vector3D(0.7,-0.4,0)},new Vector4(1,0.8f,0.2f,1));
        Echo("SVG + native gradients, clipping, holes, text, vertices, transparency and emission. Run clear to remove.");
    }
    catch(Exception e){Echo(e.Message);}
}
