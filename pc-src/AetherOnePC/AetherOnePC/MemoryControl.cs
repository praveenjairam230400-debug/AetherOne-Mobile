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
    private DateTime lastActivation = DateTime.UtcNow;
    private long lastCpuSample;
    private Dictionary<int, TimeSpan> previousProcessTimes = [];
    private double browserCpuFraction;
    private int optimizationScans, optimizationReleased;
    private async Task<(double Cpu, long PrivateBytes)> SampleBrowserResources()
    {
        var ids = new HashSet<int> { Environment.ProcessId };
        try
        {
            if (environment?.IsCompletedSuccessfully == true)
                foreach (var item in environment.Result.GetProcessInfos()) ids.Add(item.ProcessId);
        }
        catch (COMException) { }

        var sample = await Task.Run(() =>
        {
            var times = new Dictionary<int, TimeSpan>();
            long bytes = 0;
            foreach (int id in ids)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    times[id] = process.TotalProcessorTime;
                    bytes += process.PrivateMemorySize64;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            return (Times: times, PrivateBytes: bytes, Timestamp: Stopwatch.GetTimestamp());
        });
        if (lastCpuSample != 0)
        {
            double elapsed = Stopwatch.GetElapsedTime(lastCpuSample, sample.Timestamp).TotalSeconds;
            double cpuSeconds = 0;
            foreach (var pair in sample.Times)
                if (previousProcessTimes.TryGetValue(pair.Key, out var previous))
                    cpuSeconds += Math.Max(0, (pair.Value - previous).TotalSeconds);
            if (elapsed > 0.25)
                browserCpuFraction = Math.Clamp(cpuSeconds / (elapsed * Environment.ProcessorCount), 0, 1);
        }
        previousProcessTimes = sample.Times;
        lastCpuSample = sample.Timestamp;
        return (browserCpuFraction, sample.PrivateBytes);
    }
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
    private async Task RestoreCheckpoint(BrowserTab p){var web=p.Web;string url=p.CheckpointUrl;if(!p.MemoryCheckpointPending||web?.CoreWebView2==null)return;p.MemoryCheckpointPending=false;if(url.Length==0||p.Url!=url){p.CheckpointUrl="";return;}double x=p.SavedScrollX,y=p.SavedScrollY,zoom=p.SavedZoom;p.CheckpointUrl="";try{web.ZoomFactor=zoom;string script="if(location.href==="+JsonSerializer.Serialize(url)+")scrollTo({left:"+JsonSerializer.Serialize(x)+",top:"+JsonSerializer.Serialize(y)+",behavior:\"instant\"})";await web.CoreWebView2.ExecuteScriptAsync(script);}catch{}}

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
        if(reclaiming||!state.MemorySaver||closing||DownloadActive)return;
        reclaiming=true;
        try
        {
            optimizationScans++;
            var mode=ResourcePolicy.Mode(state.OptimizationMode);
            var system=new MemoryStatus();
            bool available=GlobalMemoryStatusEx(system);
            bool pressure=available&&ResourcePolicy.Pressure((long)system.AvailablePhysical,(long)system.TotalPhysical);
            memoryBudget=measurement?256*ResourcePolicy.MiB:
                ResourcePolicy.Budget(available?(long)system.TotalPhysical:8L*1024*ResourcePolicy.MiB,pressure);
            var (cpu, usage) = await SampleBrowserResources();
            if (closing || !state.MemorySaver || DownloadActive) return;

            // Prioritize visible interaction, animation, and navigation before reclamation.
            // Never change OS process priority or trim a live renderer's working set.
            if(!measurement&&!ResourcePolicy.CanReclaimNow(
                pressure,Current?.Loading==true,DateTime.UtcNow-lastActivation,cpu))
            {
                memoryDecision=$"Responsive mode · foreground busy or recently selected · CPU {cpu:P0} · deferring background inspection";
                return;
            }

            foreach(var p in pages.ToArray())
                if(p.Web!=null&&p.Initialization?.IsCompleted!=false&&(p.AtHome||p.InternalKey.Length>0)
                    &&!(p==Current&&p.InternalKey=="extensions")&&!installingExtension)
                    ReleaseView(p);

            bool over=ResourcePolicy.NeedsReclaim(usage,memoryBudget);
            var now=DateTime.UtcNow;
            var candidates=pages.Where(EligibleForRelease)
                .Where(p=>now-p.LastActive>=ResourcePolicy.MinimumIdle(pressure,mode))
                .OrderByDescending(p=>ResourcePolicy.DiscardPriority(now-p.LastActive,p.ReloadCostMs,p.Visits))
                .ToArray();

            int released=0,retained=0;
            int maxRelease=ResourcePolicy.MaxReleasesPerScan(pressure,mode);
            foreach(var p in candidates)
            {
                if(released>=maxRelease)break;
                bool longIdle=now-p.LastActive>=ResourcePolicy.LongIdle(mode)
                    &&pages.Count(x=>x.Web!=null)>2;
                if(!measurement&&!(over&&usage>memoryBudget*9/10)&&!longIdle)continue;

                await Task.Yield();
                if (closing || !state.MemorySaver || (!measurement &&
                    !ResourcePolicy.CanReclaimNow(pressure, Current?.Loading == true, DateTime.UtcNow - lastActivation, cpu))) break;
                if (!EligibleForRelease(p)) continue;
                var inspectedView = p.Web;
                var inspectedNavigation = p.ActiveNavigationId;
                p.MemoryReason="Checking safe background state";
                if(await HasUnsavedInput(p))
                {
                    retained++;p.MemoryReason="Retained: input, frames or media";
                    _=SuspendHiddenAsync(p);
                    continue;
                }
                if(!EligibleForRelease(p)||!await SaveCheckpoint(p))continue;
                if(await HasUnsavedInput(p))
                {
                    retained++;p.MemoryReason="Retained: content changed during inspection";
                    _=SuspendHiddenAsync(p);
                    continue;
                }
                if (!state.MemorySaver || !EligibleForRelease(p) || p.Web != inspectedView ||
                    p.ActiveNavigationId != inspectedNavigation ||
                    DateTime.UtcNow - p.LastActive < ResourcePolicy.MinimumIdle(pressure, mode))
                    continue;
                p.MemoryCheckpointPending=true;
                ReleaseView(p);
                p.MemoryReason="Unloaded: reloads on selection";
                released++;optimizationReleased++;
                // Renderer exit is asynchronous. Resample on the next scan.
                if (!measurement && !pressure) break;
            }
            memoryDecision=$"{mode} · browser CPU {cpu:P0} · known private {usage/ResourcePolicy.MiB} MiB · soft budget {memoryBudget/ResourcePolicy.MiB} MiB · released {released}, protected {retained} (total {optimizationReleased})";
            RefreshSurfaces();
        }
        catch(Exception) when(!measurement){memoryDecision="Optimization scan deferred; state changed";}
        finally{reclaiming=false;}
    }
}
