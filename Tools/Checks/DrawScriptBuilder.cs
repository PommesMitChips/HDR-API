using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Migrate the existing typed examples into scripts using one mod-side endpoint.
internal static class DrawScriptBuilder
{
    public const string Bridge=@"
Func<string,object[],object> _draw;
object H(string command,params object[] args)
{
    if(_draw==null&&!Bind())throw new Exception(""Enable updated HDR API and reload save."");
    return _draw(command,args);
}
bool Bind()
{
    var property=Me.GetProperty(""HDR.Draw"");if(property==null)return false;
    _draw=property.As<Func<string,object[],object>>().GetValue(Me);return _draw!=null;
}
";
    public static string Generate(string source,string header,CSharpParseOptions options)
    {
        source=source.Replace("HoloMapApi.Pose(0.75,0.25,0,0.45,yaw:_angle)","HoloMapApi.Pose(0.75,0.25,0,0.45,0,_angle)");
        var tree=CSharpSyntaxTree.ParseText(header+source+"}",options);
        var program=tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First(c=>c.Identifier.ValueText=="Program");
        var rewritten=(ClassDeclarationSyntax)new Rewrite().Visit(program);
        return "// Uses HDR.Draw. Helpers and animation state live in the mod; no API class is appended.\n"
            +Bridge+string.Concat(rewritten.Members.Select(m=>m.ToFullString()));
    }
    sealed class Rewrite:CSharpSyntaxRewriter
    {
        static bool Api(ExpressionSyntax expression)=>expression.ToString()=="_holo"||expression.ToString()=="_map"||expression.ToString()=="HoloMapApi";
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {return node.Declaration.Type.ToString()=="HoloMapApi"?null:base.VisitFieldDeclaration(node);}
        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if(Api(node.Expression)&&node.Name.Identifier.ValueText=="IsActive")return SyntaxFactory.ParseExpression("(_draw != null)");
            if(Api(node.Expression)&&node.Name.Identifier.ValueText=="Version")return SyntaxFactory.ParseExpression("H(\"Version\")");
            return base.VisitMemberAccessExpression(node);
        }
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var member=node.Expression as MemberAccessExpressionSyntax;if(member==null||!Api(member.Expression))return base.VisitInvocationExpression(node);
            string name=member.Name.Identifier.ValueText;if(name=="Activate")return SyntaxFactory.ParseExpression("Bind()");
            bool utility=member.Expression.ToString()=="HoloMapApi";
            var original=node.ArgumentList.Arguments.ToArray();
            var arguments=new List<string>();
            int skip=utility||name=="FindTarget"||name=="FindTargets"||name=="UpdateAnimations"?0:1;
            for(int i=skip;i<original.Length;i++)
            {
                var expression=(ExpressionSyntax)Visit(original[i].Expression);
                if(name=="PutCurve"&&i==2)expression=SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName("Func<double,Vector3D>"),SyntaxFactory.ParenthesizedExpression(expression));
                if(original[i].NameColon!=null)throw new Exception("Named API arguments require an explicit command migration: "+node);
                arguments.Add(expression.ToFullString().Trim());
            }
            // Scalar Pose helper calls use named yaw in the old ConsoleDemo; handled before this rewriter.
            var shortNames=new Dictionary<string,string>{
                {"PutLine","line"},{"PutCircle","circle"},{"PutCurve","curve"},{"PutWires","wires"},{"PutPolygons","polygons"},{"PutContours","contours"},{"PutVertices","points"},
                {"PutSvg","svg"},{"PutSvgAsset","svg-asset"},{"PutImage","image"},{"SetView","view"},{"SetTransform","transform"},{"SetVisible","visible"},{"SetOpacity","opacity"},
                {"SetTransparency","transparency"},{"SetEmission","emission"},{"SetClip","clip"},{"SetClipBox","clip-box"},{"SetClipCircle","clip-circle"},{"SetGradient","gradient"},
                {"SetObjectLayer","layer"},{"SetLayerVisible","layer-visible"},{"SetLayerOpacity","layer-opacity"},{"GetLayerState","layer-state"},{"ToggleLayer","toggle"},{"SoloLayer","solo"},
                {"Remove","remove"},{"Clear","clear"},{"Animate","animate"}};
            string shortName;string operation=shortNames.TryGetValue(name,out shortName)?shortName:name;
            string result="H(\""+operation+"\""+(arguments.Count==0?"":","+string.Join(",",arguments))+")";
            if(name=="FindTarget")result="((IMyTerminalBlock)"+result+")";
            else if(name=="FindTargets")result="((List<IMyProjector>)"+result+")";
            else if(name=="GetLayerState")result="((VRage.MyTuple<bool,float>)"+result+")";
            else if(name=="ToggleLayer")result="((bool)"+result+")";
            else if(name=="Pose")result="((MatrixD)"+result+")";
            else if(name=="Rgb"||name=="Rgba")result="((Vector4)"+result+")";
            return SyntaxFactory.ParseExpression(result).WithTriviaFrom(node);
        }
    }
}
