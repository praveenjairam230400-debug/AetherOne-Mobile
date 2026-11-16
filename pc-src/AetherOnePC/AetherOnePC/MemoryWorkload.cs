using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text.Json;
namespace AetherOnePC;
public sealed partial class BrowserForm
{
    private object MemorySnapshot(string stage){var ids=new Dictionary<int,string>{{Environment.ProcessId,"Shell"}};try{if(environment?.IsCompletedSuccessfully==true)foreach(var info in environment.Result.GetProcessInfos())ids[info.ProcessId]=info.Kind.ToString();}catch{}var rows=new List<object>();long total=0,working=0;foreach(var pair in ids)try{using var process=Process.GetProcessById(pair.Key);total+=process.PrivateMemorySize64;working+=process.WorkingSet64;rows.Add(new{Pid=pair.Key,Kind=pair.Value,PrivateBytes=process.PrivateMemorySize64,WorkingSetBytes=process.WorkingSet64});}catch{}return new{Stage=stage,KnownPrivateBytes=total,SumWorkingSetBytes=working,LiveViews=pages.Count(p=>p.Web!=null),TabCount=pages.Count,Processes=rows};}
    private async Task RunMemoryWorkload(string output)
    {
        state.MemorySaver=false;var folder=Path.Combine(profilePath,"MemoryFixture");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"index.html"),"""
        <!doctype html><title>Memory workload</title><style>body{min-height:6000px}</style><textarea id="draft"></textarea><canvas width="1600" height="900"></canvas><script>window.payload=new Uint8Array(32*1024*1024);window.payload.fill(123);let g=document.querySelector('canvas').getContext('2d');g.fillStyle='#9573cc';g.fillRect(0,0,1600,900);window.ready=true</script>
        """);var workload=new List<BrowserTab>();
        for(int i=0;i<8;i++){NewTab();var p=Current!;await Ensure(p);string host="aether-memory-"+i+".test";p.Web!.CoreWebView2.SetVirtualHostNameToFolderMapping(host,folder,CoreWebView2HostResourceAccessKind.DenyCors);await Navigate("https://"+host+"/index.html",p);await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("window.ready")=="true");workload.Add(p);}
        await workload[2].Web!.CoreWebView2.ExecuteScriptAsync("scrollTo(0,1234)");workload[2].Web!.ZoomFactor=1.25;workload[1].Pinned=true;await workload[0].Web!.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value='KEEP THIS UNSAVED DRAFT'");SelectPage(workload[^1]);await Task.Delay(1500);var before=MemorySnapshot("8 loaded controlled pages; 32 MiB touched JS buffer and canvas per page");state.MemorySaver=true;
        foreach(var p in workload)p.LastActive=DateTime.UtcNow-TimeSpan.FromMinutes(3);
        await ReclaimInactive(true);
        await Task.Delay(2000);var reclaimed=MemorySnapshot("After automatic reclamation; inactivity timestamps advanced for the test");
        if(workload[0].Web==null||workload[1].Web==null||workload[^1].Web==null)throw new Exception("Memory saver discarded protected draft, pinned or active page");
        RestoreMemoryTarget(workload[0]);if(await workload[0].Web!.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value")!="\"KEEP THIS UNSAVED DRAFT\"")throw new Exception("Memory saver lost the draft");
        bool dormant=workload[2].Web==null;if(!dormant)throw new Exception("Controlled pressure did not unload eligible fixture");SelectPage(workload[2]);await WaitFor(async()=>workload[2].Web?.CoreWebView2!=null&&!workload[2].Loading&&await workload[2].Web.CoreWebView2.ExecuteScriptAsync("Math.abs(scrollY-1234)<3")=="true");if(Math.Abs(workload[2].Web!.ZoomFactor-1.25)>.001)throw new Exception("Unloaded page zoom was not restored");foreach(var p in workload.ToArray())CloseTab(p);await Task.Delay(2000);var closed=MemorySnapshot("After closing all 8 workload tabs");
        File.WriteAllText(Path.Combine(output,"memory-workload.json"),JsonSerializer.Serialize(new{Version="0.6.0",Before=before,Reclaimed=reclaimed,Closed=closed,ScrollAndZoomRestored=true,TestBudgetMiB=256,Note="Controlled 8-tab workload, not authenticated ChatGPT/Gmail. Includes all known engine processes. Active/pinned/draft tabs checked. Inactivity timestamps advanced instead of waiting 2 minutes; 256 MiB soft budget injected for test. Working sets double count shared pages; engine caching can remain after closing tabs."},new JsonSerializerOptions{WriteIndented=true}));
    }
}
