using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace AetherOnePC;
internal sealed class PageWebView:WebView2 {internal void SynchronizeVisibility()=>base.OnVisibleChanged(EventArgs.Empty);}
public sealed partial class BrowserForm
{
    private readonly System.Windows.Forms.Timer memoryTimer=new(){Interval=10000};
    private bool reclaiming;
    private long memoryBudget=512*ResourcePolicy.MiB;
    private string memoryDecision="Waiting for first memory sample";
    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatus {public uint Length=(uint)Marshal.SizeOf<MemoryStatus>();public uint Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;}
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In,Out] MemoryStatus status);
    private long KnownPrivateBytes(){long bytes=0;var ids=new HashSet<int>{Environment.ProcessId};try{if(environment?.IsCompletedSuccessfully==true)foreach(var i in environment.Result.GetProcessInfos())ids.Add(i.ProcessId);}catch{}foreach(int id in ids)try{using var process=Process.GetProcessById(id);bytes+=process.PrivateMemorySize64;}catch{}return bytes;}
    private bool EligibleForRelease(BrowserTab p)=>pages.Contains(p)&&p!=Current&&!p.Pinned&&!p.Loading&&!p.Audible&&p.Web is {IsDisposed:false}&&p.Web.CoreWebView2!=null&&p.InternalKey.Length==0&&IsWeb(p.Url)&&!DownloadActive;
    private static async Task<bool> SaveCheckpoint(BrowserTab p){var web=p.Web;string url=p.Url;if(web?.CoreWebView2==null)return false;try{using var value=JsonDocument.Parse(await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,x:scrollX,y:scrollY})"));using var data=JsonDocument.Parse(value.RootElement.GetString()??"{}");if(p.Web!=web||p.Url!=url||data.RootElement.GetProperty("url").GetString()!=url)return false;p.SavedScrollX=data.RootElement.GetProperty("x").GetDouble();p.SavedScrollY=data.RootElement.GetProperty("y").GetDouble();p.SavedZoom=web.ZoomFactor;p.CheckpointUrl=url;return true;}catch{return false;}}
    private async Task RestoreCheckpoint(BrowserTab p){var web=p.Web;string url=p.CheckpointUrl;if(!p.MemoryCheckpointPending||web?.CoreWebView2==null)return;p.MemoryCheckpointPending=false;if(url.Length==0||p.Url!=url){p.CheckpointUrl="";return;}double x=p.SavedScrollX,y=p.SavedScrollY,zoom=p.SavedZoom;p.CheckpointUrl="";try{web.ZoomFactor=zoom;string script="if(location.href==="+JsonSerializer.Serialize(url)+")scrollTo("+JsonSerializer.Serialize(x)+","+JsonSerializer.Serialize(y)+")";await web.CoreWebView2.ExecuteScriptAsync(script);}catch{}}

    private bool DownloadActive=>downloadEntries.Any(x=>x.Operation.State==CoreWebView2DownloadState.InProgress);
    private async Task SuspendHiddenAsync(BrowserTab p)
    {
        p.CancelMemory?.Cancel();var c=new CancellationTokenSource();p.CancelMemory=c;
        try{await Task.Delay(smoke?30:1000,c.Token);var w=p.Web;if(closing||!state.MemorySaver||p==Current||p.Pinned||p.Loading||p.Audible||DownloadActive||w?.CoreWebView2==null||w.IsDisposed||w.Visible)return;
            if(w is PageWebView native)native.SynchronizeVisibility();
            if(!p.SuspendUnavailable){try{p.Suspended=await w.CoreWebView2.TrySuspendAsync();}catch{p.SuspendUnavailable=true;}}
            if(!p.Suspended){p.SuspendUnavailable=true;w.CoreWebView2.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Low;}
            if(p==Current)RestoreMemoryTarget(p);
        }catch(OperationCanceledException){}catch{}finally{if(p.CancelMemory==c)p.CancelMemory=null;c.Dispose();}
    }
    private static void ReleaseView(BrowserTab p){p.CancelMemory?.Cancel();p.Web?.Dispose();p.Web=null;p.Initialization=null;p.Suspended=false;p.SuspendUnavailable=false;p.FilterSignature="";p.Sleeping=!p.AtHome&&p.InternalKey.Length==0;}
    private async Task<bool> HasUnsavedInput(BrowserTab p)
    {
        if(p.Web?.CoreWebView2==null)return true;
        try{RestoreMemoryTarget(p);string answer=await p.Web.CoreWebView2.ExecuteScriptAsync("""
            (()=>{if(document.querySelector('iframe,frame'))return true;if(document.querySelector('video:not([paused]),audio:not([paused])')){for(const e of document.querySelectorAll('audio,video'))if(!e.paused)return true;}
            for(const e of document.querySelectorAll('input,textarea,select,[contenteditable="true"]')){
            if(e.isContentEditable&&e.textContent.trim())return true;if((e.tagName==='TEXTAREA'||['text','email','password','tel'].includes(e.type))&&e.value.trim())return true;
            if(e.tagName==='SELECT'&&Array.from(e.options).some(o=>o.selected!==o.defaultSelected))return true;
            if(e.type==='checkbox'||e.type==='radio'){if(e.checked!==e.defaultChecked)return true;}else if(e.type!=='hidden'&&e.type!=='button'&&e.type!=='submit'&&e.value!==e.defaultValue)return true;}
            return false;})()
            """);return answer!="false";}catch{return true;}
    }
    private async Task ReclaimInactive(bool measurement=false)
    {
        if(reclaiming||!state.MemorySaver||closing||DownloadActive)return;reclaiming=true;
        try{
            foreach(var p in pages.ToArray())if(p.Web!=null&&p.Initialization?.IsCompleted!=false&&(p.AtHome||p.InternalKey.Length>0)&&!(p==Current&&p.InternalKey=="extensions")&&!installingExtension)ReleaseView(p);
            var system=new MemoryStatus();bool available=GlobalMemoryStatusEx(system);
            bool pressure=available&&ResourcePolicy.Pressure((long)system.AvailablePhysical,(long)system.TotalPhysical);
            memoryBudget=ResourcePolicy.Budget(available?(long)system.TotalPhysical:8L*1024*ResourcePolicy.MiB,pressure);
            // The controlled benchmark injects a budget, not fake process measurements.
            if(measurement)memoryBudget=256*ResourcePolicy.MiB;
            long usage=KnownPrivateBytes();bool over=ResourcePolicy.NeedsReclaim(usage,memoryBudget);
            var now=DateTime.UtcNow;
            var candidates=pages.Where(EligibleForRelease).Where(p=>ResourcePolicy.Eligible(now-p.LastActive,pressure)).OrderByDescending(p=>ResourcePolicy.DiscardPriority(now-p.LastActive,p.ReloadCostMs,p.Visits)).ToArray();
            int released=0;
            foreach(var p in candidates){
                bool veryOld=now-p.LastActive>TimeSpan.FromMinutes(3)&&pages.Count(x=>x.Web!=null)>2;
                if(!(over&&usage>memoryBudget*9/10)&&!veryOld)continue;
                p.MemoryReason="Checking unsaved work";bool protect=await HasUnsavedInput(p);
                if(!EligibleForRelease(p))continue;
                if(protect){p.MemoryReason="Retained: detected input or media";_=SuspendHiddenAsync(p);continue;}
                // Input and active state are checked again after checkpoint capture.
                if(!await SaveCheckpoint(p)||!EligibleForRelease(p))continue;
                if(await HasUnsavedInput(p)){p.MemoryReason="Retained: input changed";_=SuspendHiddenAsync(p);continue;}
                if(!EligibleForRelease(p)||DateTime.UtcNow-p.LastActive<TimeSpan.FromSeconds(pressure?15:45))continue;
                p.MemoryCheckpointPending=true;ReleaseView(p);p.MemoryReason="Unloaded for memory; reloads on selection";released++;usage=KnownPrivateBytes();
            }
            memoryDecision=$"Known engine processes {(environment?.IsCompletedSuccessfully==true ? environment.Result.GetProcessInfos().Count : 0)} · Soft budget {memoryBudget/ResourcePolicy.MiB} MiB · {(pressure?"Windows memory pressure":"normal pressure")} · {released} views released";
            RefreshSurfaces();
        }catch(Exception) when(!measurement){memoryDecision="Memory check deferred; page state changed";}finally{reclaiming=false;}
    }
}
