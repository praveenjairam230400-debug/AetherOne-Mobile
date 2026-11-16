using Microsoft.Web.WebView2.Core;
namespace AetherOnePC;
public sealed partial class BrowserForm
{
    // Allowed requests stay inside Chromium. Native filters route only possible
    // blocker matches to C#; Policy still checks the parsed hostname boundary.
    private void ConfigureRequestRoute(BrowserTab p)
    {
        if(p.Web?.CoreWebView2 is not {} core)return;
        bool ads=state.BlockAds&&p.AppliedRule.BlockAds, images=!p.AppliedRule.Images;
        string signature=$"{ads}:{images}";if(p.FilterSignature==signature)return;
        if(p.FilterSignature.Length>0){
            if(p.FilterSignature.StartsWith("True:"))foreach(var pattern in RequestPatterns)core.RemoveWebResourceRequestedFilter(pattern,CoreWebView2WebResourceContext.All);
            if(p.FilterSignature.EndsWith(":True"))core.RemoveWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.Image);
        }
        if(ads)foreach(var pattern in RequestPatterns)core.AddWebResourceRequestedFilter(pattern,CoreWebView2WebResourceContext.All);
        if(images)core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.Image);
        p.FilterSignature=signature;
    }
    // A conservative substring superset covers ports and trailing-dot hosts.
    // False positives pass the existing hostname check; no URL is blocked by a glob alone.
    private static readonly string[] RequestPatterns=Policy.BlockedDomains.Select(x=>"*://*"+x+"*").ToArray();
}
