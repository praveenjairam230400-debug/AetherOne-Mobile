namespace AetherOnePC;
internal static class Policy
{
    public static string? Resolve(string raw,string searchEngine="Google")
    {
        raw = raw.Trim();
        if (raw.Length == 0) return null;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var u))
        {
            if (u.Scheme == "https") return string.IsNullOrWhiteSpace(u.Host) ? null : u.AbsoluteUri;
            if (u.Scheme == "http") return new UriBuilder(u) { Scheme = "https", Port = u.IsDefaultPort ? -1 : u.Port }.Uri.AbsoluteUri;
            // A hostname followed by a numeric port is still an address.
            if (raw.Contains("://") || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return null;
        }
        if (!raw.Any(char.IsWhiteSpace) && (raw.Contains('.') || raw.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate("https://" + raw, UriKind.Absolute, out var address) && !string.IsNullOrEmpty(address.Host)) return address.AbsoluteUri;
        return SearchUrl(searchEngine,raw);
    }
    public static string SearchUrl(string engine,string query)=>(engine switch {"Brave"=>"https://search.brave.com/search?q=","Bing"=>"https://www.bing.com/search?q=","DuckDuckGo"=>"https://duckduckgo.com/?q=",_=>"https://www.google.com/search?q="})+Uri.EscapeDataString(query);
    public static string Host(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.IdnHost.ToLowerInvariant().TrimEnd('.') : "";
    private static readonly HashSet<string> Blocked = new("""
        doubleclick.net googlesyndication.com googleadservices.com google-analytics.com
        googletagservices.com adservice.google.com amazon-adsystem.com media.net
        adnxs.com adsrvr.org adroll.com adform.net advertising.com criteo.com criteo.net
        taboola.com outbrain.com pubmatic.com rubiconproject.com openx.net casalemedia.com
        indexww.com smartadserver.com sharethrough.com lijit.com sovrn.com triplelift.com
        3lift.com contextweb.com bidr.io bidswitch.net bidvertiser.com yieldmo.com
        yieldlab.net revcontent.com mgid.com propellerads.com popads.net popcash.net
        adsterra.com trafficjunky.net exoclick.com scorecardresearch.com quantserve.com
        hotjar.com hotjar.io mouseflow.com clarity.ms fullstory.com adsafeprotected.com
        doubleverify.com moatads.com demdex.net everesttech.net bluekai.com rlcdn.com
        tapad.com mathtag.com mookie1.com agkn.com dotomi.com serving-sys.com
        adtelligent.com onetag-sys.com admanmedia.com teads.tv seedtag.com gumgum.com
        undertone.com kargo.com inmobi.com adswizz.com spotxchange.com freewheel.tv
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
    internal static IEnumerable<string> BlockedDomains => Blocked;
    public static bool ShouldBlock(string? resource, string page)
    {
        if (string.IsNullOrEmpty(page) || string.IsNullOrEmpty(resource)) return false;
        resource = resource.ToLowerInvariant().TrimEnd('.'); page = page.ToLowerInvariant().TrimEnd('.');
        if (resource == page || resource.EndsWith("." + page, StringComparison.Ordinal)) return false;
        while (resource.Length > 0)
        {
            if (Blocked.Contains(resource)) return true;
            var dot = resource.IndexOf('.'); if (dot < 0) break; resource = resource[(dot + 1)..];
        }
        return false;
    }
}
public sealed class SiteRule
{
    public bool BlockAds { get; set; } = true;
    public bool JavaScript { get; set; } = true;
    public bool Images { get; set; } = true;
    public double Zoom { get; set; } = 1;
    public bool EfficientArticles {get;set;}
}
public sealed class SavedLink
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}
public sealed class TabSnapshot
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Workspace { get; set; } = "Personal";
    public bool Pinned { get; set; }
}
public sealed class BrowserState
{
    public string SearchEngine {get;set;}="Google";
    public bool SearchPredictions {get;set;}=true;
    public bool ShowHome {get;set;}=true;
    public bool RestoreTabs {get;set;}=true;
    public bool DarkTheme {get;set;}=true;
    public string DownloadFolder {get;set;}="";
    public bool AskDownloadLocation {get;set;}=true;
    public bool RemovedReadingTools {get;set;}

    public Dictionary<string,string> ExtensionFolders { get; set; } = [];
    public List<string> PinnedExtensions { get; set; } = [];
    public bool ReadingToolsOffered { get; set; }
    public bool ShowFavorites { get; set; } = true;
    public bool MemorySaver { get; set; } = true;
    public string OptimizationMode { get; set; } = "Adaptive";
    public List<string> Workspaces { get; set; } = ["Personal", "Work", "Research"];
    public string ActiveWorkspace { get; set; } = "Personal";
    public List<TabSnapshot> Sessions { get; set; } = [];
    public List<SavedLink> ReadLater { get; set; } = [];
    public Dictionary<string, string> WorkspaceNotes { get; set; } = [];
    public bool BlockAds { get; set; } = true;
    public List<SavedLink> Bookmarks { get; set; } = [];
    public List<SavedLink> History { get; set; } = [];
    public List<string> Tabs { get; set; } = [];
    public Dictionary<string, SiteRule> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
