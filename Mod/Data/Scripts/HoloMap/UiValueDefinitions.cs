using System;
using System.Collections.Generic;
using HDR.Interactions;
using ProtoBuf;
using VRageMath;
namespace HoloMap
{
    [ProtoContract] public sealed class UiNumericValue
    {
        [ProtoMember(1)] public string Id;
        [ProtoMember(2)] public double Value;
        [ProtoMember(3)] public double Min;
        [ProtoMember(4)] public double Max;
        [ProtoMember(5)] public double Step;
        [ProtoMember(6)] public long Revision;
    }
    [ProtoContract] public sealed class UiControlData
    {
        [ProtoMember(1)] public string Artwork;
        [ProtoMember(2)] public bool Draggable;
        [ProtoMember(3)] public string ValueId;
        [ProtoMember(4)] public string ConstraintKind;
        [ProtoMember(5)] public double[] Points;
        [ProtoMember(6)] public double[] Pivot;
        [ProtoMember(7)] public double[] Axis;
        [ProtoMember(8)] public double AngleMin;
        [ProtoMember(9)] public double AngleMax;
        [ProtoMember(10)] public double[] ReferencePose;
        [ProtoMember(11)] public double ReferenceValue;
        [ProtoMember(12)] public long SourceRevision;
    }
    public static class UiValueRules
    {
        public const int MaxValues=64,MaxTotalValues=256,MaxEvents=64;
        public static UiNumericValue Copy(UiNumericValue v)
        {return new UiNumericValue{Id=v.Id,Value=v.Value,Min=v.Min,Max=v.Max,Step=v.Step,Revision=v.Revision};}
        static double[] Copy(double[] a){return a==null?null:(double[])a.Clone();}
        public static UiControlData Copy(UiControlData c)
        {return c==null?null:new UiControlData{Artwork=c.Artwork,Draggable=c.Draggable,ValueId=c.ValueId,ConstraintKind=c.ConstraintKind,Points=Copy(c.Points),Pivot=Copy(c.Pivot),Axis=Copy(c.Axis),AngleMin=c.AngleMin,AngleMax=c.AngleMax,ReferencePose=Copy(c.ReferencePose),ReferenceValue=c.ReferenceValue,SourceRevision=c.SourceRevision};}
        public static NumericRange Range(UiNumericValue value)
        {NumericRange range;if(value==null||!NumericRange.TryCreate(value.Min,value.Max,value.Step,out range))throw new ArgumentException("Numeric value range and step are invalid.");return range;}
        public static MatrixD Matrix(double[] p)
        {if(p==null||p.Length!=16)throw new ArgumentException("Control reference pose requires 16 numbers.");foreach(double n in p)if(!Geometry.Finite(n))throw new ArgumentException("Control reference pose must be finite.");var m=new MatrixD(p[0],p[1],p[2],p[3],p[4],p[5],p[6],p[7],p[8],p[9],p[10],p[11],p[12],p[13],p[14],p[15]);double determinant=m.Determinant();if(!Geometry.Finite(determinant)||Math.Abs(m.M14)>1e-12||Math.Abs(m.M24)>1e-12||Math.Abs(m.M34)>1e-12||Math.Abs(m.M44-1)>1e-12||Math.Abs(determinant)<1e-15)throw new ArgumentException("Control reference pose must be nonsingular finite affine.");return m;}
        public static Vector3D Point(double[] p)
        {if(p==null||p.Length!=3)throw new ArgumentException("Constraint point requires three numbers.");return new Vector3D(p[0],p[1],p[2]);}
        public static Vector3D[] Points(double[] p)
        {if(p==null||p.Length<6||p.Length>768||p.Length%3!=0)throw new ArgumentException("Path requires 2–256 points.");var a=new Vector3D[p.Length/3];for(int i=0;i<a.Length;i++)a[i]=new Vector3D(p[3*i],p[3*i+1],p[3*i+2]);return a;}
        public static bool Constraint(UiControlData control,UiNumericValue value,out PathConstraint path,out RotationConstraint rotation)
        {
            path=null;rotation=null;if(control==null||value==null||control.ConstraintKind==null)return false;var range=Range(value);
            if(control.ConstraintKind=="line"||control.ConstraintKind=="path")return PathConstraint.TryPolyline(range,Points(control.Points),out path);
            if(control.ConstraintKind=="rotation"){NumericRange angles;if(!NumericRange.TryCreate(control.AngleMin,control.AngleMax,0,out angles))return false;return RotationConstraint.TryCreate(angles,Point(control.Pivot),Point(control.Axis),out rotation);}
            return false;
        }
        public static double ToAngle(UiControlData c,UiNumericValue v,double value)
        {return c.AngleMin+(value-v.Min)/(v.Max-v.Min)*(c.AngleMax-c.AngleMin);}
        public static double FromAngle(UiControlData c,UiNumericValue v,double angle)
        {return v.Min+(angle-c.AngleMin)/(c.AngleMax-c.AngleMin)*(v.Max-v.Min);}
        public static void Display(UiDisplay d)
        {
            if(d.Values==null||d.Values.Count>MaxValues||d.DataRevision<0||d.ValueNotify!=null&&d.ValueNotify.Length>128)throw new ArgumentException("Invalid numeric UI display.");
            var names=new Dictionary<string,UiNumericValue>();foreach(var v in d.Values){if(v==null)throw new ArgumentException("Missing UI value.");UiRules.Id(v.Id);if(names.ContainsKey(v.Id)||v.Revision<1||v.Revision>d.DataRevision)throw new ArgumentException("Invalid or duplicate value revision.");var range=Range(v);double canonical;if(!range.TryNormalize(v.Value,out canonical)||canonical!=v.Value)throw new ArgumentException("UI value must be canonical and snapped.");names.Add(v.Id,v);}
            var artwork=new HashSet<string>();foreach(var w in d.Widgets)if(w.Control!=null)
            {
                var c=w.Control;if(w.ActionKind!="control"||w.Argument!=""||string.IsNullOrEmpty(c.Artwork)||c.Artwork.Length>64||!artwork.Add(c.Artwork)||c.SourceRevision<1||c.SourceRevision>d.DataRevision||!Geometry.Finite(c.ReferenceValue)||!Geometry.Finite(c.AngleMin)||!Geometry.Finite(c.AngleMax))throw new ArgumentException("Invalid artwork control.");Matrix(c.ReferencePose);
                if(c.ConstraintKind==null&&(c.Points!=null||c.Pivot!=null||c.Axis!=null)||c.ConstraintKind=="rotation"&&c.Points!=null||(c.ConstraintKind=="line"||c.ConstraintKind=="path")&&(c.Pivot!=null||c.Axis!=null))throw new ArgumentException("Constraint fields do not match their declared kind.");
                if(c.ValueId!=null){UiNumericValue v;if(!names.TryGetValue(c.ValueId,out v))throw new ArgumentException("Control references a missing value.");double baseline;if(!Range(v).TryNormalize(c.ReferenceValue,out baseline)||baseline!=c.ReferenceValue)throw new ArgumentException("Control baseline must be canonical.");if(c.ConstraintKind!=null){PathConstraint p;RotationConstraint r;if(!Constraint(c,v,out p,out r))throw new ArgumentException("Invalid control constraint.");}}
                else if(c.ConstraintKind!=null)throw new ArgumentException("Bind a value before defining a constraint.");
            }
            foreach(var w in d.Widgets)if(w.ActionKind=="control"&&w.Control==null)throw new ArgumentException("A control action requires a registered artwork descriptor.");
        }
    }
}
