using System;
using System.Collections.Generic;
using System.Linq;
using Hdr.Mods;
using Hdr.Html;
using VRage;
using VRageMath;

static partial class HostTests
{
    const string MutationMarkup=SourceMarkup+"<p id='label'>Original</p><p id='status'>{{status}}</p>";
    const string MutationCss=SourceCss+" p {height:24px;font-size:8px;}";
    static MyTuple<string,object[]> Mutation(string operation,params object[] args){return new MyTuple<string,object[]>(operation,args);}
    static string Artwork(Core core){return string.Join("\n",core.Surfaces.Values.Single().ArtworkSources.Values);}
    static void MutationBatchCommitAndRollback()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("atomic-batch"))
            {
                long document=helper.CreateSurface(MutationMarkup,MutationCss,400,240,MatrixD.Identity,.005,"plane",new object[0],AnchorSettings(SourceAnchor()));
                Check(helper.AttachSource(document,"camera","camera-panorama","101"),"Initial source is published before batch");string slot=core.Surfaces.Values.Single().Slots.Single().Key;
                helper.Pointer(document,20,82,true);helper.PollEvents(document);
                Check(helper.SetText(document,"label","Queued"),"Standalone text edit is staged");Check(helper.SetData(document,"status","QueuedData"),"Standalone data edit is staged");var before=helper.Status(document);string original=Artwork(core);
                core.FailSlotOnce=true;
                var changes=new[]{Mutation("text","label","Transient"),Mutation("data","status","CandidateData"),Mutation("attach-source","camera","camera-panorama","102")};
                Check(!helper.Mutate(document,changes),"Injected source failure rejects the complete batch");var after=helper.Status(document);
                Equal(before.Item2,after.Item2,"Failed batch preserves desired revision");Equal(before.Item3,after.Item3,"Confirmed rollback preserves published revision");Equal(before.Item5,after.Item5,"Failed candidate does not consume committed layout count");Equal(original,Artwork(core),"Confirmed rollback restores every published artwork source");Equal("101",core.Surfaces.Values.Single().Slots.Single().Value.Source,"Confirmed rollback restores prior source declaration");
                helper.Pointer(document,80,82,true);Check(helper.PollEvents(document).Any(e=>e.Item1=="change"&&e.Item2=="range"),"Held range survives confirmed batch restoration");
                session.UpdateAfterSimulation();Check(Artwork(core).Contains("Queued")&&Artwork(core).Contains("QueuedData")&&!Artwork(core).Contains("Transient")&&!Artwork(core).Contains("CandidateData"),"Failed batch leaves standalone queued text/data intact for ordinary update");
                before=helper.Status(document);changes=new[]{Mutation("text","label","Accepted"),Mutation("data","status","AcceptedData"),Mutation("attach-source","camera","camera-panorama","102")};
                Check(helper.Mutate(document,changes),"Mixed text/data/source batch commits successfully");after=helper.Status(document);
                Equal(before.Item2+1,after.Item2,"Successful text/data batch consumes one desired revision");Equal(before.Item5+1,after.Item5,"Successful batch performs one candidate layout");Equal(after.Item2,after.Item3,"Batch returns only after publication");Check(Artwork(core).Contains("Accepted")&&Artwork(core).Contains("AcceptedData"),"Every successful candidate text/data edit is published");Equal("102",core.Surfaces.Values.Single().Slots.Single().Value.Source,"Successful mixed batch publishes selected source");Equal(slot,core.Surfaces.Values.Single().Slots.Single().Key,"Mixed source replacement keeps prior slot identity");
                helper.Pointer(document,60,82,true);Check(helper.PollEvents(document).Any(e=>e.Item1=="change"),"Unchanged hit geometry retains the active range through successful batch");
                before=helper.Status(document);int calls=core.ArtworkCalls;Check(helper.Mutate(document,new[]{Mutation("attach-source","camera","camera-panorama","103")}),"Source-only batch commits");after=helper.Status(document);Equal(before.Item2,after.Item2,"Source-only batch does not reparse the document");Equal(before.Item5,after.Item5,"Source-only batch does not relayout");Equal(calls,core.ArtworkCalls,"Source-only batch does not mutate retained artwork");
                Check(helper.Mutate(document,new[]{Mutation("detach-source","camera")}),"Batch detach commits");Equal("Detached",helper.SourceStatus(document,"camera").Item2,"Detach batch retires only the named desired source");
                Check(helper.Mutate(document,new MyTuple<string,object[]>[0]),"Empty valid batch succeeds without a partial-flush signal");
            }
        }
    }
    static void MutationBatchPreflight()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("batch-preflight"))
            {
                long document=helper.CreateHud(MutationMarkup,MutationCss,400,240,"svg");helper.SetSourceAnchor(document,SourceAnchor());var before=helper.Status(document);string original=Artwork(core);int calls=core.ArtworkCalls,sourceCalls=core.SourceCalls;
                Reject<ArgumentException>(()=>helper.Mutate(document,new[]{Mutation("text","label","Unpublished"),Mutation("data","status",new string('x',16385))}),"limit");
                Equal(before.Item2,helper.Status(document).Item2,"Invalid trailing data preserves desired state");Equal(original,Artwork(core),"Invalid trailing data preserves published artwork");Equal(calls,core.ArtworkCalls,"Invalid candidate causes no artwork writes");
                Reject<ArgumentException>(()=>helper.Mutate(document,new[]{Mutation("text","label","Unpublished"),Mutation("attach-source","missing","camera-panorama","101")}),"final candidate");Equal(sourceCalls,core.SourceCalls,"Missing final attachment node causes no source writes");
                Reject<ArgumentException>(()=>helper.Mutate(document,new[]{Mutation("text","label","Unpublished"),Mutation("attach-source","camera","bad?","101")}),"provider");Equal(calls,core.ArtworkCalls,"Invalid provider causes no preceding text publication");
                Reject<ArgumentException>(()=>helper.Mutate(document,Enumerable.Range(0,65).Select(i=>Mutation("data","key","value")).ToArray()),"64");
                Reject<ArgumentException>(()=>helper.Mutate(document,new[]{Mutation("replace","markup")}),"Unsupported");
                Reject<ArgumentException>(()=>helper.Mutate(document,new[]{Mutation("data",12,"value")}),"string");
                Reject<ArgumentException>(()=>helper.Mutate(document,Enumerable.Range(0,9).Select(i=>Mutation("data","bounded-"+i,new string('x',16000))).ToArray()),"batch");
                core.SourceSupported=false;Reject<ArgumentException>(()=>helper.Mutate(document,new[]{Mutation("text","label","Unpublished"),Mutation("attach-source","camera","camera-panorama","101")}),"Unsupported");core.SourceSupported=true;Equal(sourceCalls,core.SourceCalls,"Unsupported consumer batch mutates no source before rejecting");
                Equal(original,Artwork(core),"Every preflight rejection leaves prior output unchanged");
                var nativeSurface=new Surface();long native=helper.CreateNativeLcd(ButtonMarkup,ButtonCss,nativeSurface.Api,true);int nativeCommits=nativeSurface.Commits;Reject<ArgumentException>(()=>helper.Mutate(native,new[]{Mutation("text","button","No"),Mutation("data","x","y")}),"physical native LCD");Equal(nativeCommits,nativeSurface.Commits,"Unsupported native batch touches no physical frame");
                var foreign=bus.Open("batch-foreign");Reject<ArgumentException>(()=>foreign("mutate",new object[]{document,new[]{Mutation("text","label","Foreign")}}),"belong");
                Check(helper.Mutate(document,new[]{Mutation("text","label","Original")}),"All-noop valid text batch is accepted");
                long nested=helper.CreateHud("<div id='parent'><div id='child'></div></div>","div {width:80px;height:40px;}",400,240,"svg");helper.SetSourceAnchor(nested,SourceAnchor());int nestedCalls=core.ArtworkCalls;
                Reject<ArgumentException>(()=>helper.Mutate(nested,new[]{Mutation("text","parent","Replacement"),Mutation("attach-source","child","camera-panorama","101")}),"final candidate");Equal(nestedCalls,core.ArtworkCalls,"Attachment cannot survive removal of its node in the same candidate");helper.Destroy(nested);
                Check(((string[])bus.HtmlService("capabilities",new object[0])).Contains("atomic-retained-mutations"),"Service declares the exact retained mutation capability");
            }
        }
    }
    static void MutationBatchRetirement()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("batch-retirement"))
            {
                long document=helper.CreateHud(ButtonMarkup,ButtonCss,400,200,"svg");helper.Pointer(document,20,15,true);helper.Pointer(document,20,15,false);var before=helper.Status(document);core.FailAllArtwork=true;
                Check(!helper.Mutate(document,new[]{Mutation("text","button","Changed")}),"Unrestorable artwork rejects the batch");var after=helper.Status(document);Equal(before.Item2,after.Item2,"Uncertain publication still commits no candidate desired revision");Equal(before.Item5,after.Item5,"Uncertain publication commits no candidate layout count");Equal(0L,after.Item3,"Uncertain renderer retires visible input revision");Check(after.Item4.Item3.Contains("input is retired"),"Status explains the hard renderer retirement boundary");Equal(0,helper.PollEvents(document).Length,"Retirement discards queued clicks from the retired publication");Equal(0,core.Surfaces.Count,"Retirement destroys the owned renderer context");
                Reject<ArgumentException>(()=>helper.Mutate(document,new[]{Mutation("text","button","Retry")}),"inactive");core.FailAllArtwork=false;session.UpdateAfterSimulation();Check(helper.Status(document).Item3==0,"Retirement keeps the existing bounded retry cadence");
            }
        }
    }
    static void ScriptClaims()
    {
        using(var bus=new Bus())
        {
            var core=new Core();bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();var endpoint=bus.Open("claim-owner");
            long document=(long)endpoint("create-hud",new object[]{SourceMarkup,SourceCss,400d,240d,"svg",0});var alias=new Func<string,object[],object>((operation,args)=>endpoint(operation,args));object first=new object(),other=new object();int calls=core.ArtworkCalls;
            endpoint("pointer",new object[]{document,20d,82d,true});endpoint("poll-events",new object[]{document});
            Check((bool)endpoint("script-claim",new object[]{document,first}),"First realm claims the actual document");Check((bool)alias("script-claim",new object[]{document,first}),"Same opaque token is idempotent across delegate aliases");Check(!(bool)alias("script-claim",new object[]{document,other}),"A wrapped alias cannot claim a second realm");Check((bool)alias("script-valid",new object[]{document,first}),"Actual claim validity follows the document rather than delegate identity");Check(!(bool)endpoint("script-release",new object[]{document,other}),"Foreign release cannot retire the first claim");Equal(calls,core.ArtworkCalls,"Claims perform no repaint");
            var foreign=bus.Open("claim-foreign");Check(!(bool)foreign("script-claim",new object[]{document,other}),"Foreign owner cannot claim another owner's handle");Check(!(bool)foreign("script-valid",new object[]{document,first}),"Foreign owner cannot validate another owner's token");
            Check((bool)endpoint("script-release",new object[]{document,first}),"Matching release retires the claim");Check(!(bool)endpoint("script-valid",new object[]{document,first}),"Released token is stale");Check((bool)alias("script-claim",new object[]{document,other}),"Released document can admit its next realm");
            endpoint("pointer",new object[]{document,80d,82d,true});Check(((MyTuple<string,string,string,MyTuple<double,long>>[])endpoint("poll-events",new object[]{document})).Any(e=>e.Item1=="change"),"Claim/release metadata never cancels an active input grab");
            core.OnArtworkCall=delegate{core.OnArtworkCall=null;Check((bool)alias("script-valid",new object[]{document,other}),"Read-only claim validation is available during a renderer callback");Reject<ArgumentException>(()=>alias("script-claim",new object[]{document,first}),"outside");};
            Check((bool)alias("mutate",new object[]{document,new[]{Mutation("text","button","Updated")}}),"Callback claim reads preserve the enclosing atomic mutation");
            var before=(MyTuple<string,long,long,MyTuple<bool,bool,string>,long>)alias("status",new object[]{document});string previous=Artwork(core);
            core.OnArtworkCall=delegate
            {
                core.OnArtworkCall=null;
                Check(!(bool)alias("script-release",new object[]{document,first}),"Wrong-token callback release cannot affect the actual claim");
                Check(!(bool)foreign("script-release",new object[]{document,other}),"Foreign owner cannot release a claim during paint");
                Check((bool)alias("script-release",new object[]{document,other}),"Matching metadata release is allowed during paint cleanup");
                Check(!(bool)alias("script-valid",new object[]{document,other}),"Released callback claim becomes invalid immediately");
            };
            Check(!(bool)alias("mutate",new object[]{document,new[]{Mutation("text","button","Aborted")}}),"Script disposal during paint rejects its captured-claim batch");
            var after=(MyTuple<string,long,long,MyTuple<bool,bool,string>,long>)alias("status",new object[]{document});Equal(before.Item2,after.Item2,"Disposed-claim batch commits no desired revision");Equal(before.Item3,after.Item3,"Disposed-claim batch restores the prior visible revision");Equal(previous,Artwork(core),"Disposed-claim batch restores prior renderer sources");Check(after.Item4.Item3.Contains("claim was released"),"Aborted-claim restoration has a precise status reason");Check((bool)alias("script-claim",new object[]{document,first}),"Cleanup leaves the actual document available for the next realm");
            Reject<ArgumentException>(()=>endpoint("script-claim",new object[]{document,null}),"nonnull");
            endpoint("destroy",new object[]{document});Check(!(bool)alias("script-valid",new object[]{document,other}),"Destroyed document revokes its claim");Check(!(bool)alias("script-release",new object[]{document,other}),"Destroyed claim cleanup is harmless");
            document=(long)endpoint("create-hud",new object[]{ButtonMarkup,ButtonCss,400d,200d,"svg",0});Check((bool)endpoint("script-claim",new object[]{document,first}),"Fresh document claims independently");bus.Open("claim-owner");Check(!(bool)alias("script-valid",new object[]{document,first}),"Owner replacement revokes captured claim endpoint");Check(!(bool)alias("script-release",new object[]{document,first}),"Stale owner release cannot affect its successor");
        }
    }
    static void ProviderSourceCheck()
    {
        using(var bus=new Bus())
        {
            var core=new Core{NativeSourceSupported=false};bus.CoreService=core.Service;var session=new HtmlFrontendSession();session.BeforeStart();
            using(var helper=new HdrHtmlApi("source-check"))
            {
                long document=helper.CreateHud(SourceMarkup,SourceCss,400,240,"svg");helper.SetSourceAnchor(document,SourceAnchor());var before=helper.Status(document);int artwork=core.ArtworkCalls,sources=core.SourceCalls;
                var custom=helper.SourceCheck(document,"custom-feed","feed-A");Check(custom.Item1,"Negotiated custom mod-native feed is supported without the native plugin");
                var native=helper.SourceCheck(document,"camera-panorama","101");Check(!native.Item1&&native.Item2.Contains("Requires plugin"),"Absent native consumer capability has its precise provider-specific reason");
                var invalid=helper.SourceCheck(document,"bad?","feed-A");Check(!invalid.Item1&&invalid.Item2.Contains("provider"),"Invalid source declaration produces a bounded readonly reason");
                Equal(artwork,core.ArtworkCalls,"Provider checks make no renderer artwork writes");Equal(sources,core.SourceCalls,"Provider checks make no source declarations");Equal(before.Item2,helper.Status(document).Item2,"Provider checks do not edit desired revision");Equal(before.Item3,helper.Status(document).Item3,"Provider checks do not edit published revision");Check(core.Surfaces.Values.Single().Slots.Count==0,"Readonly checks acquire no slot ownership");
                core.OnArtworkCall=delegate{core.OnArtworkCall=null;Check(helper.SourceCheck(document,"custom-feed","feed-A").Item1,"Provider check remains readonly during a renderer callback");};
                Check(helper.Mutate(document,new[]{Mutation("text","button","Updated")}),"Readonly provider callback preserves enclosing batch publication");
                Check(helper.Mutate(document,new[]{Mutation("attach-source","camera","custom-feed","feed-A")}),"Provider-aware validated custom feed uses the same atomic attachment path");
            }
        }
    }
}
