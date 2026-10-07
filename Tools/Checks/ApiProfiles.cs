using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Build-time source selection. Typed delegate calls stay ordinary C# in the PB.
internal static class ApiProfiles
{
    const string Imports="using System;using System.Collections.Generic;using Sandbox.ModAPI.Ingame;using Sandbox.ModAPI.Interfaces;using VRageMath;";
    public static string Generate(string fullApi,string body,string header,IEnumerable<MetadataReference> references,CSharpParseOptions options)
    {
        var probeTree=CSharpSyntaxTree.ParseText(header+body+"\n"+fullApi+"}",options);
        var probe=CSharpCompilation.Create("ProfileProbe",new[]{probeTree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Check(probe);
        var probeModel=probe.GetSemanticModel(probeTree);
        var apiInProbe=probeTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="HoloMapApi");
        var roots=new HashSet<string>{"Activate","IsActive"};
        foreach(var name in probeTree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if(name.Ancestors().Contains(apiInProbe))continue;
            var symbol=probeModel.GetSymbolInfo(name).Symbol;
            if(symbol?.ContainingType?.Name=="HoloMapApi")roots.Add(symbol.Name);
        }
        bool animations=roots.Overlaps(new[]{"Animate","UpdateAnimations","PlaySvgFrames","StopAnimation"});
        var apiTree=CSharpSyntaxTree.ParseText(Imports+fullApi,options);
        if(!animations)
        {
            var root=(CSharpSyntaxNode)new RemoveUnusedAnimationTracking().Visit(apiTree.GetRoot());
            apiTree=CSharpSyntaxTree.Create(root,options);
        }
        var compilation=CSharpCompilation.Create("ProfileDependencies",new[]{apiTree},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Check(compilation);var model=compilation.GetSemanticModel(apiTree);
        var cls=apiTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="HoloMapApi");
        var apiSymbol=model.GetDeclaredSymbol(cls);
        var byName=new Dictionary<string,List<MemberDeclarationSyntax>>();
        foreach(var member in cls.Members)foreach(string name in Names(member))
        {if(!byName.TryGetValue(name,out var members))byName[name]=members=new List<MemberDeclarationSyntax>();members.Add(member);}
        var needed=new HashSet<string>(roots){"_caller","_version","_apiVersion","ProtocolSupported","Bind","Get"};
        var pending=new Queue<string>(needed);var visited=new HashSet<MemberDeclarationSyntax>();
        while(pending.Count>0)
        {
            string name=pending.Dequeue();if(!byName.TryGetValue(name,out var members))continue;
            foreach(var member in members)
            {
                if(!visited.Add(member)||member is MethodDeclarationSyntax activation&&activation.Identifier.ValueText=="Activate")continue;
                foreach(var simple in member.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var symbol=model.GetSymbolInfo(simple).Symbol;
                    if(symbol?.ContainingType?.Equals(apiSymbol)!=true)continue;
                    if(byName.ContainsKey(symbol.Name)&&needed.Add(symbol.Name))pending.Enqueue(symbol.Name);
                }
            }
        }
        var selected=new List<MemberDeclarationSyntax>();
        foreach(var member in cls.Members)
        {
            if(!Names(member).Any(needed.Contains))continue;
            if(member is MethodDeclarationSyntax method&&method.Identifier.ValueText=="Activate")selected.Add(Activation(method,needed));
            else if(member is FieldDeclarationSyntax field)selected.Add(field.WithDeclaration(field.Declaration.WithVariables(SyntaxFactory.SeparatedList(field.Declaration.Variables.Where(v=>needed.Contains(v.Identifier.ValueText))))));
            else selected.Add(member);
        }
        return "// Generated typed API profile: methods used by this script and their dependencies.\n"
            +"// Regenerate with Build.ps1 after adding API calls; full authoring API remains in Api/HoloMapApi.cs.\n"
            +cls.WithMembers(SyntaxFactory.List(selected)).ToFullString();
    }
    static MethodDeclarationSyntax Activation(MethodDeclarationSyntax method,HashSet<string> needed)
    {
        var statements=new List<StatementSyntax>();
        foreach(var statement in method.Body.Statements)
        {
            if(statement is ExpressionStatementSyntax expression&&expression.Expression is InvocationExpressionSyntax call&&call.Expression.ToString()=="Bind")
            {if(!needed.Contains(call.ArgumentList.Arguments[0].Expression.ToString()))continue;}
            if(statement is IfStatementSyntax check&&check.Condition.ToString().Contains("_wires == null"))
            {
                string[] required={"_version","_wires","_polygons","_view","_transform","_visible","_remove","_clear"};
                statements.Add(SyntaxFactory.ParseStatement("if ("+string.Join(" || ",required.Where(needed.Contains).Select(n=>n+" == null"))+") return false;").WithLeadingTrivia(check.GetLeadingTrivia()).WithTrailingTrivia(check.GetTrailingTrivia()));continue;
            }
            statements.Add(statement);
        }
        return method.WithBody(method.Body.WithStatements(SyntaxFactory.List(statements)));
    }
    static IEnumerable<string> Names(MemberDeclarationSyntax member)
    {
        if(member is FieldDeclarationSyntax field)return field.Declaration.Variables.Select(v=>v.Identifier.ValueText);
        if(member is MethodDeclarationSyntax method)return new[]{method.Identifier.ValueText};
        if(member is PropertyDeclarationSyntax property)return new[]{property.Identifier.ValueText};
        if(member is ClassDeclarationSyntax cls)return new[]{cls.Identifier.ValueText};
        return Array.Empty<string>();
    }
    static void Check(CSharpCompilation compilation)
    {var errors=compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();if(errors.Length>0)throw new Exception(string.Join(Environment.NewLine,errors.Select(d=>d.ToString())));}
    sealed class RemoveUnusedAnimationTracking:CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if(node.Expression is InvocationExpressionSyntax call&&new[]{"Remember","Forget","ClearAnimations"}.Contains(call.Expression.ToString()))return null;
            return base.VisitExpressionStatement(node);
        }
    }
}
