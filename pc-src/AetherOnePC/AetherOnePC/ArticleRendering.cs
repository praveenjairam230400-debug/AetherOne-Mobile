using System.Text.Json;
namespace AetherOnePC;
public sealed partial class BrowserForm
{
    // Per-site opt-in CSS. Uses the engine's own content-visibility mechanism;
    // no DOM removal, timer monkey-patching, mutation polling or external service.
    private async Task ApplyArticleRendering(BrowserTab p)
    {
        var web=p.Web;if(web?.CoreWebView2==null||!IsWeb(p.Url)||Policy.Host(web.CoreWebView2.Source)!=p.FilterHost)return;
        bool enabled=p.AppliedRule.EfficientArticles;
        if(!enabled&&!p.ArticleStyleRequested)return;
        p.ArticleStyleRequested=enabled;
        try{await web.CoreWebView2.ExecuteScriptAsync("""
            (()=>{const id='aether-efficient-articles-06';const old=document.getElementById(id);
            if(!ENABLED){old?.remove();return 'off';}
            if(!CSS.supports('content-visibility','auto')||!document.head)return 'unsupported';
            if(!old){const style=document.createElement('style');style.id=id;
            style.textContent='main > article, article[data-testid^="conversation-turn-"] {content-visibility:auto;contain-intrinsic-size:auto 720px}';
            document.head.appendChild(style);}return 'on';})()
            """.Replace("ENABLED",enabled?"true":"false"));}catch{}
    }
    private async Task TestArticleRendering(string output,BrowserTab p)
    {
        string folder=Path.Combine(profilePath,"Fixtures");
        File.WriteAllText(Path.Combine(folder,"articles.html"),"<!doctype html><title>Article rendering test</title><main>"+string.Concat(Enumerable.Range(0,50).Select(i=>$"<article style='height:720px'><h2>Section {i}</h2><textarea></textarea><p>Searchable article text {i}</p></article>"))+"</main>");
        await Navigate("https://aether.test/articles.html",p);await WaitFor(()=>Task.FromResult(!p.Loading));
        if(!await SaveCheckpoint(p))throw new Exception("Checkpoint capture failed");p.SavedScrollY=4000;await RestoreCheckpoint(p);if(await p.Web!.CoreWebView2.ExecuteScriptAsync("scrollY")!="0")throw new Exception("Aborted checkpoint unexpectedly moved a live page");
        var rule=Rule("aether.test");rule.EfficientArticles=true;state.Sites["aether.test"]=rule;ApplyPolicy(p);await ApplyArticleRendering(p);
        string result=await p.Web!.CoreWebView2.ExecuteScriptAsync("(()=>{const a=document.querySelector('article');a.querySelector('textarea').value='KEEP';window.scrollTo(0,6000);return getComputedStyle(a).contentVisibility==='auto'&&document.querySelectorAll('article').length===50&&document.body.textContent.includes('Searchable article text 49')})()");
        if(result!="true")throw new Exception("Article mode lost DOM content or did not activate");
        rule.EfficientArticles=false;ApplyPolicy(p);await ApplyArticleRendering(p);
        if(await p.Web.CoreWebView2.ExecuteScriptAsync("document.getElementById('aether-efficient-articles-06')===null&&document.querySelector('textarea').value==='KEEP'")!="true")throw new Exception("Article mode did not reverse or lost input");
        File.WriteAllText(Path.Combine(output,"article-rendering-tests.txt"),"PASS: aborted checkpoint capture does not move a live page; opt-in article CSS activates; 50 article nodes and searchable text remain; input persists when mode is disabled; style removal reverses mode. No browser memory or speed benefit inferred from this correctness check.\n");
        state.Sites.Remove("aether.test");await Navigate("https://aether.test/index.html",p);await WaitFor(()=>Task.FromResult(!p.Loading));
    }
}
