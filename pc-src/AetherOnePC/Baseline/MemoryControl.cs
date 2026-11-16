using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace AetherOnePC;
internal sealed class PageWebView:WebView2 {internal void SynchronizeVisibility()=>base.OnVisibleChanged(EventArgs.Empty);}
public sealed partial class BrowserForm
{
    private readonly System.Windows.Forms.Timer memoryTimer=new(){Interval=15000};
    private bool reclaiming;
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
    private static void ReleaseView(BrowserTab p){p.CancelMemory?.Cancel();p.Web?.Dispose();p.Web=null;p.Initialization=null;p.Suspended=false;p.SuspendUnavailable=false;p.Sleeping=!p.AtHome&&p.InternalKey.Length==0;}
    private async Task<bool> HasUnsavedInput(BrowserTab p)
    {
        if(p.Web?.CoreWebView2==null)return true;
        try{RestoreMemoryTarget(p);string answer=await p.Web.CoreWebView2.ExecuteScriptAsync("""
            (()=>{if(document.querySelector('video:not([paused]),audio:not([paused])')){for(const e of document.querySelectorAll('audio,video'))if(!e.paused)return true;}
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
        try{foreach(var p in pages.ToArray()){if(p.Web!=null&&p.Initialization?.IsCompleted!=false&&(p.AtHome||p.InternalKey.Length>0)&&!(p==Current&&p.InternalKey=="extensions")&&!installingExtension){ReleaseView(p);}}
            var candidates=pages.Where(p=>p!=Current&&!p.Pinned&&!p.Loading&&!p.Audible&&p.Web?.CoreWebView2!=null&&p.InternalKey.Length==0&&DateTime.UtcNow-p.LastActive>TimeSpan.FromMinutes(2)).OrderBy(p=>p.LastActive).ToArray();
            foreach(var p in candidates){if(pages.Count(x=>x.Web!=null)<=3)break;bool protect=await HasUnsavedInput(p);if(!pages.Contains(p)||p==Current||p.Pinned||p.Audible||p.Loading)continue;if(!protect)ReleaseView(p);else {p.LastActive=DateTime.UtcNow;_=SuspendHiddenAsync(p);}}
            RefreshSurfaces();
        }finally{reclaiming=false;}
    }
}
