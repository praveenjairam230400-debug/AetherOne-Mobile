using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;

namespace AetherOnePC;

public sealed partial class BrowserForm : Form
{
    private Color bg = Color.FromArgb(32, 32, 32), panel = Color.FromArgb(43, 43, 43), ink = Color.FromArgb(242, 242, 242), mint = Color.FromArgb(120, 180, 255);
    private readonly TabDeck tabs = new TabDeck { Dock = DockStyle.Fill };
    private readonly TextBox address = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 12), PlaceholderText = "Search or enter an address" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Bottom, Height = 3, Visible = false };
    private readonly ToolTip hints=new();
    private readonly System.Windows.Forms.Timer saver = new() { Interval = 600 };
    private readonly string profilePath;
    private readonly string statePath;
    private BrowserState state = new();
    private Task<CoreWebView2Environment>? environment;
    private bool closing;
    private readonly bool smoke;
    private double shownMs;
    private readonly NavigationDiagnostics navigationDiagnostics = new();
    private static readonly HashSet<Keys> BrowserShortcuts = new() { Keys.Control|Keys.K, Keys.Control|Keys.N, Keys.Control|Keys.L, Keys.Control|Keys.T, Keys.Control|Keys.W, Keys.Control|Keys.D, Keys.Control|Keys.B, Keys.Control|Keys.H, Keys.Control|Keys.J, Keys.Control|Keys.F, Keys.Control|Keys.R, Keys.Control|Keys.P, Keys.Control|Keys.Shift|Keys.T, Keys.Control|Keys.Shift|Keys.N, Keys.Control|Keys.Shift|Keys.Escape, Keys.Control|Keys.Tab, Keys.Control|Keys.Shift|Keys.Tab, Keys.Alt|Keys.Left, Keys.Alt|Keys.Right, Keys.F5 };
    private readonly List<BrowserTab> pages = [];
    private Button back = null!, forward = null!, reload = null!, shield = null!;
    private BrowserTab? Current => tabs.SelectedTab?.Tag as BrowserTab;
    private sealed class BrowserTab
    {
        public Panel Container = null!;
        private Panel? home;
        public Panel Home => home ??= BuildHome();
        public Func<Panel> BuildHome = null!;
        public void HideHome() { if (home != null) home.Visible = false; }
        public WebView2? Web;
        public Task? Initialization;
        public int NavigationIntent;
        public DateTime NextMemoryProbe;
        public ulong? ActiveNavigationId;
        public string Url = "";
        public string Title = "New tab";
        public Bitmap? Favicon;
        public bool Audible;
        public DateTime LastActive=DateTime.UtcNow;
        public CancellationTokenSource? CancelMemory;
        public bool Suspended,SuspendUnavailable;
        public string ExtensionOrigin = "";
        public string InternalKey = "";
        public Control? NativePage;
        public string Workspace = "Personal";
        public bool Pinned;
        public bool Sleeping;
        public string FilterHost = "";
        public SiteRule AppliedRule = new();
        public bool AtHome = true;
        public bool Loading;
        public int Blocked, Observed;
        public string FilterSignature = "";
        public int Visits;
        public long ReloadCostMs;
        public string MemoryReason = "Ready";
        public double SavedScrollX, SavedScrollY, SavedZoom=1;
        public string CheckpointUrl = "";
        public bool MemoryCheckpointPending,ArticleStyleRequested;
        public Stopwatch Clock = new();
        public long Finished;
        public string LastError = "";
    }
    public BrowserForm(bool smokeTest = false,string profileId="default",string? initialUrl=null)
    {
        smoke = smokeTest;
        activeProfileId=profileId;
        profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherOnePC", smoke ? (Environment.GetEnvironmentVariable("AETHER_BENCHMARK")=="1"?"Benchmark-0.6":"SmokeTest-0.6") : (profileId=="default"?"Profile":Path.Combine("Profiles",profileId)));
        Directory.CreateDirectory(profilePath); statePath = Path.Combine(profilePath, "browser-state.json");
        try { if (!smoke && File.Exists(statePath)) state = JsonSerializer.Deserialize<BrowserState>(File.ReadAllText(statePath)) ?? new(); }
        catch { if (File.Exists(statePath)) File.Copy(statePath, statePath + ".corrupt-" + DateTime.UtcNow.Ticks, true); }
        state.Bookmarks ??= []; state.History ??= []; state.Tabs ??= []; state.Sites ??= [];
        state.ReadLater ??= []; state.Sessions ??= []; state.Workspaces ??= ["Personal", "Work", "Research"]; state.WorkspaceNotes ??= [];
        foreach(var entry in state.Sessions)entry.Workspace="Personal";state.Workspaces=["Personal"];state.ActiveWorkspace="Personal";
        if (!state.Workspaces.Contains(state.ActiveWorkspace)) state.ActiveWorkspace = state.Workspaces.FirstOrDefault() ?? "Personal";
        Text = "Aether One"; Width = 1320; Height = 860; MinimumSize = new Size(840, 560);
        Size = new Size(Math.Min(1320, Screen.PrimaryScreen!.WorkingArea.Width - 24), Math.Min(860, Screen.PrimaryScreen.WorkingArea.Height - 24));
        StartPosition = FormStartPosition.CenterScreen; BackColor = bg; ForeColor = ink; Font = new Font("Segoe UI", 10);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        HandleCreated += (_, _) => AetherWindowChrome.Apply(Handle,state.DarkTheme);
        BuildWorkspaceUi();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyData == (Keys.Control | Keys.Shift | Keys.Escape)) { e.Handled = true; e.SuppressKeyPress = true; ShowAetherTaskManager(); } };
        memoryTimer.Tick+=async(_,_)=>await ReclaimInactive();if(!smoke)memoryTimer.Start();
        tabs.BeforeSelectionChanged+=(_,_)=>{if(Current?.Web is {IsDisposed:false} w)w.Visible=false;DismissPopups();};
        Deactivate+=(_,_)=>DismissPopups();
        tabs.SelectedIndexChanged += (_, _) => {
            var previous=lastSelected;if(previous!=null)previous.LastActive=DateTime.UtcNow;lastSelected=Current;if(Current!=null){Current.LastActive=DateTime.UtcNow;Current.Visits++;}horizontalTabs?.RevealSelection();address.Text=Current?.AtHome==true?"":Current?.Url??"";if(previous?.Web is {IsDisposed:false}&&previous!=Current)previous.Web.Visible=false;if(Current?.Web!=null){Current.Web.Visible=!Current.AtHome&&Current.InternalKey.Length==0;RestoreMemoryTarget(Current);}if(previous!=null&&previous!=Current)_=TrimHidden(previous);
            lastActivation = DateTime.UtcNow;
            if (Current != null) { state.ActiveWorkspace = Current.Workspace; if (!Current.AtHome && Current.InternalKey.Length==0 && Current.Web == null) _ = Navigate(Current.Url, Current); }
            Sync(); QueueSave();
        };
        saver.Tick += (_, _) => { saver.Stop(); Save(); };
        Shown += async (_, _) =>
        {
            shownMs = Program.Startup.Elapsed.TotalMilliseconds;
            if (smoke) { await SmokeTest(); return; }
            // Show the dashboard and accept input before rebuilding the saved tab strip.
            NewTab(initialUrl);
            if (initialUrl != null || !state.RestoreTabs) return;
            BeginInvoke((Action)RestoreSavedTabs);
        };
        FormClosing += (_, _) => {memoryTimer.Stop();DismissPopups();if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}hints.Dispose();predictCancellation?.Cancel();suggestions?.Close();suggestTimer.Stop(); Save(); closing = true; saver.Stop(); foreach (var p in pages) p.Web?.Dispose(); };
    }

    private void RestoreSavedTabs()
    {
        if (closing) return;
        tabs.SuspendLayout();
        try
        {
            if (state.Sessions.Count > 0) {
                foreach (var entry in state.Sessions.Where(x => IsWeb(x.Url)).ToArray()) {
                    AddTab(entry.Url, entry.Workspace, entry.Pinned, entry.Title);
                }
            } else foreach (var url in state.Tabs.Where(IsWeb)) AddTab(url, state.ActiveWorkspace);
        }
        finally { tabs.ResumeLayout(); }
        horizontalTabs?.RevealSelection();
        Sync();
    }
    private Button Button(string label, string help, Action action)
    {
        var b = new ChromeButton { Text = label, AccessibleName = help, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = panel, ForeColor = ink, Cursor = Cursors.Hand, Margin = new Padding(2), TabStop = true };
        b.FlatAppearance.BorderSize = 0; hints.SetToolTip(b, help); b.Click += (_, _) => action(); return b;
    }
    private Label Label(string text, float size, Color color) => new() { Text = text, Font = new Font("Segoe UI", size), ForeColor = color, AutoSize = true, Margin = new Padding(0, 8, 0, 8), MaximumSize = new Size(760, 0) };
    private void NewTab(string? url = null, bool select = true, string? workspace = null)
    {
        suggestions?.Hide();

        var previous = Current;
        var p = AddTab(url, workspace ?? state.ActiveWorkspace);
        if (select && previous != null) tabs.SelectedTab = p.Container;
        if (!select) Sync();
        QueueSave();
        if (select && url == null) { address.Text = ""; address.Focus(); }
    }

    private BrowserTab AddTab(string? url, string workspace, bool pinned = false, string? title = null)
    {
        var p = new BrowserTab { Workspace = workspace, Pinned = pinned };
        p.BuildHome = () =>
        {
            var dashboard = CreateDashboard(p);
            p.Container.Controls.Add(dashboard);
            return dashboard;
        };
        p.Container = new Panel { BackColor = bg, Tag = p };
        if (url != null)
        {
            p.Url = url;
            p.Title = string.IsNullOrWhiteSpace(title) ? Policy.Host(url) : title;
            p.AtHome = false;
        }
        p.Container.Text = p.Title;
        if (p.AtHome) _ = p.Home;
        pages.Add(p);
        tabs.TabPages.Add(p.Container);
        return p;
    }
    private void ReorderTab(BrowserTab moving,BrowserTab target)
    {
        if(moving==target||moving.Pinned!=target.Pinned||!pages.Contains(moving)||!pages.Contains(target))return;
        bool forward=pages.IndexOf(moving)<pages.IndexOf(target);pages.Remove(moving);int index=pages.IndexOf(target);pages.Insert(Math.Clamp(index+(forward?1:0),0,pages.Count),moving);tabs.TabPages.MovePage(moving.Container,pages.IndexOf(moving));
        horizontalTabs?.Invalidate();QueueSave();
    }
    private void OpenUrlInWindow(string url)
    {
        if(!IsWeb(url))return;
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true};
        info.ArgumentList.Add("--profile");info.ArgumentList.Add(activeProfileId);
        info.ArgumentList.Add("--open-url");info.ArgumentList.Add(url);
        try{Process.Start(info);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not open window");}
    }
    private void DetachTab(BrowserTab p)
    {
        if(!pages.Contains(p))return;
        if(!IsWeb(p.Url)){status.Text="Only website tabs can be detached";return;}
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true};
        info.ArgumentList.Add("--profile");info.ArgumentList.Add(activeProfileId);
        info.ArgumentList.Add("--open-url");info.ArgumentList.Add(p.Url);
        try{if(Process.Start(info)!=null)CloseTab(p);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not detach tab");}
    }
    private async Task Ensure(BrowserTab p)
    {
        if (p.Initialization != null) { await p.Initialization; return; }
        p.Initialization = Initialize(p); await p.Initialization;
    }
    private async Task Initialize(BrowserTab p)
    {
        var web = new PageWebView { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White, Visible = false };
        p.Web = web; p.Sleeping = false;p.FilterSignature="";p.ArticleStyleRequested=false;p.MemoryReason="Live page"; p.Container.Controls.Add(web);
        try
        {
            if (environment == null || environment.IsFaulted || environment.IsCanceled)
                environment = CreateEnvironment();
            var engine = await environment;
            if (closing || web.IsDisposed) return;
            await web.EnsureCoreWebView2Async(engine);
            if (closing || web.IsDisposed) return;
            var core = web.CoreWebView2;
            if(smoke&&Directory.Exists(Path.Combine(profilePath,"MemoryFixture")))for(int i=0;i<8;i++)core.SetVirtualHostNameToFolderMapping("aether-memory-"+i+".test",Path.Combine(profilePath,"MemoryFixture"),CoreWebView2HostResourceAccessKind.DenyCors);
            await LoadExtensionCatalog(core);
            if (closing || web.IsDisposed) return;
            if (smoke && Directory.Exists(Path.Combine(profilePath, "Fixtures"))) core.SetVirtualHostNameToFolderMapping("aether.test", Path.Combine(profilePath, "Fixtures"), CoreWebView2HostResourceAccessKind.DenyCors);
            web.KeyDown += (_, e) => {
                var key = e.KeyData;
                if (key == (Keys.Control | Keys.Shift | Keys.Escape)) { e.Handled = true; e.SuppressKeyPress = true; BeginInvoke((Action)ShowAetherTaskManager); return; }
                if (BrowserShortcuts.Contains(key)) {
                    e.Handled = true; e.SuppressKeyPress = true; BeginInvoke((Action)(() => { if (!closing) HandleShortcut(key); }));
                }
            };
            core.Settings.IsWebMessageEnabled = false; core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;

            core.NavigationStarting += (_, e) =>
            {
                if (closing) return;
                var extensionPage = IsAllowedExtensionPage(p,e.Uri);
                if (!extensionPage && !IsWeb(e.Uri)) { e.Cancel = true; status.Text = "Only HTTPS web pages are supported"; return; }
                if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !(smoke&&requestFixtureOrigin.Length>0&&e.Uri.StartsWith(requestFixtureOrigin+"/",StringComparison.Ordinal))) { e.Cancel = true; _ = Navigate(e.Uri, p); return; }
                p.ActiveNavigationId = e.NavigationId;
                p.NextMemoryProbe = default;
                p.Url = e.Uri; p.AtHome = false; p.Blocked = 0; p.Observed = 0; p.Finished = 0; p.LastError = ""; p.Loading = true; p.Clock.Restart();
                navigationDiagnostics.Start(p.Url);
                ApplyPolicy(p); Sync();
            };
            core.WebResourceRequested += (_, e) =>
            {
                if (closing) return;
                var resource = e.Request.Uri; if (!Uri.TryCreate(resource,UriKind.Absolute,out var requestUri) || requestUri.Scheme is not ("https" or "http")) return;
                var h = p.FilterHost;
                if (resource == p.Url && e.ResourceContext == CoreWebView2WebResourceContext.Document) return;
                p.Observed++;
                var rule = p.AppliedRule;
                if ((state.BlockAds && rule.BlockAds && Policy.ShouldBlock(requestUri.IdnHost, h)) || (!rule.Images && e.ResourceContext == CoreWebView2WebResourceContext.Image))
                {
                    if (e.ResourceContext != CoreWebView2WebResourceContext.Image || rule.Images) p.Blocked++;
                    e.Response = core.Environment.CreateWebResourceResponse(null, 204, "No Content", "Cache-Control: no-store\r\n");
                }
            };
            core.FaviconChanged += async (_, _) => { try { using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png); using var decoded = Image.FromStream(stream); var icon = new Bitmap(decoded,16,16); if(closing || !pages.Contains(p) || p.Web != web){icon.Dispose();return;} p.Favicon?.Dispose();p.Favicon=icon;RefreshSurfaces(); } catch { } };
            core.IsDocumentPlayingAudioChanged += (_,_) => {p.Audible=core.IsDocumentPlayingAudio;RefreshSurfaces();};
            core.DocumentTitleChanged += (_, _) => { p.Title = core.DocumentTitle; p.Container.Text = Short(p.Title); RefreshSurfaces(); if (p == Current) Text = p.Title + " — Aether One"; QueueSave(); };
            core.SourceChanged += (_, _) => { if (IsWeb(core.Source)) p.Url = core.Source; Sync(); };
            core.HistoryChanged += (_, _) => Sync();
            core.DOMContentLoaded+=(_,_)=>_=ApplyArticleRendering(p);
            core.NavigationCompleted += (_, e) =>
            {
                if (p.ActiveNavigationId != e.NavigationId) return;
                p.Loading = false; p.Finished = p.Clock.ElapsedMilliseconds;
                navigationDiagnostics.Complete(p.Url, e.IsSuccess, p.Finished);
                if(e.IsSuccess){p.ReloadCostMs=p.ReloadCostMs==0?p.Finished:(p.ReloadCostMs*3+p.Finished)/4;_=RestoreCheckpoint(p);}
                if (!e.IsSuccess) p.LastError = e.WebErrorStatus.ToString();
                if (e.IsSuccess && IsWeb(p.Url)) { state.History.RemoveAll(x => x.Url == p.Url); state.History.Insert(0, new SavedLink { Url = p.Url, Title = p.Title }); state.History = state.History.Take(300).ToList(); }
                QueueSave(); Sync();if(p!=Current)_=TrimHidden(p);
            };
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.ContextMenuRequested += (_, e) =>
            {
                var target=e.ContextMenuTarget;
                var link=target?.LinkUri;
                if(string.IsNullOrWhiteSpace(link)||!IsWeb(link))return;
                e.Handled=true;
                BeginInvoke((Action)(()=>{
                    if(closing)return;
                    var menu=BrowserContextMenu();
                    menu.Items.Add("Open link in new tab",null,(_,_)=>NewTab(link));
                    menu.Items.Add("Open link in new window",null,(_,_)=>OpenUrlInWindow(link));
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Copy link address",null,(_,_)=>Clipboard.SetText(link));
                    menu.Show(Cursor.Position);
                }));
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; if (e.IsUserInitiated && IsWeb(e.Uri)){bool fromExtension=e.OriginalSourceFrameInfo?.Source?.StartsWith("chrome-extension://",StringComparison.OrdinalIgnoreCase)==true;NewTab(e.Uri,!fromExtension);} else status.Text = "Automatic popup blocked"; };
            core.PermissionRequested += (_, e) =>
            {
                e.State = CoreWebView2PermissionState.Deny;
                if (smoke || !e.IsUserInitiated) return;
                var result = MessageBox.Show(this, $"{Policy.Host(e.Uri)} requests {e.PermissionKind}.\n\nAllow for this request?", "Site permission", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                e.State = result == DialogResult.Yes ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                e.SavesInProfile = false;
            };
            core.ServerCertificateErrorDetected += (_, e) => { e.Action = CoreWebView2ServerCertificateErrorAction.Cancel; status.Text = "Connection blocked: certificate validation failed"; };
            core.ProcessFailed += (_, _) => { p.Loading = false; p.LastError = "Page engine stopped. Close this tab and open a new one."; Sync(); };
            core.DownloadStarting += (_, e) => TrackDownload(e);
        }
        catch { p.Initialization = null; p.Web = null; web.Dispose(); throw; }
    }
    private static bool IsWeb(string? s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http") && !string.IsNullOrEmpty(u.Host);
    private SiteRule Rule(string h) => state.Sites.TryGetValue(h, out var r) ? r : new SiteRule();
    private void ApplyPolicy(BrowserTab p)
    {
        if (p.Web?.CoreWebView2 == null) return;
        var r = Rule(Policy.Host(p.Url)); p.FilterHost = Policy.Host(p.Url); p.AppliedRule = r; p.Web.CoreWebView2.Settings.IsScriptEnabled = r.JavaScript; p.Web.ZoomFactor = Math.Clamp(r.Zoom, .5, 2.0);
        ConfigureRequestRoute(p);
        _=ApplyArticleRendering(p);
    }
    private async Task Navigate(string raw, BrowserTab? target = null)
    {
        suggestions?.Hide();predictCancellation?.Cancel();
        if(raw.StartsWith("aether://",StringComparison.OrdinalIgnoreCase)){OpenInternal(raw[9..],target);return;}
        var url = smoke&&requestFixtureOrigin.Length>0&&raw.StartsWith(requestFixtureOrigin+"/",StringComparison.Ordinal)?raw:Policy.Resolve(raw,state.SearchEngine); if (url == null) { status.Text = "Enter an HTTPS address or search phrase"; return; }
        var p = target ?? Current; if (p == null) { NewTab(); p = Current!; }
        int intent = ++p.NavigationIntent;
        try
        {
            await Ensure(p);
            if (closing || !pages.Contains(p) || intent != p.NavigationIntent || p.Web?.CoreWebView2 == null) return;
            p.InternalKey = "";
            p.NativePage?.Dispose();
            p.NativePage = null;
            p.Url = url;
            var web = p.Web;p.Title=web.CoreWebView2.DocumentTitle; p.AtHome = false; p.HideHome(); web.Visible = p==Current;if(p==Current)web.BringToFront();
            if (web.Source?.AbsoluteUri == url && !p.Loading && p.Finished > 0) { if(p==Current)web.Focus(); Sync(); status.Text = "Live page reused · no navigation request"; return; }
            p.Loading = true; web.CoreWebView2.Navigate(url); if(p==Current)web.Focus(); Sync(); QueueSave();
        }
        catch (Exception e) { if (!closing && p == Current && intent == p.NavigationIntent) EngineError(e); }
    }
    private void EngineError(Exception e)
    {
        if (closing || smoke) return;
        MessageBox.Show(this, "The browser engine could not start. Install or repair Microsoft Edge WebView2 Evergreen Runtime from Microsoft's website, then restart Aether One.\n\n" + e.GetType().Name,
            "WebView2 engine unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private void Home()
    {
        var p = Current; if (p == null) return;
        p.NavigationIntent++;
        p.ActiveNavigationId = null;
        p.Web?.CoreWebView2?.Stop(); p.Loading = false; p.AtHome = true;p.InternalKey="";p.NativePage?.Dispose();p.NativePage=null; if (p.Web != null) p.Web.Visible = false; p.Home.Visible = true; p.Home.BringToFront(); p.Container.Text = "New tab"; Sync(); QueueSave();
    }
    private void Reload()
    {
        var p = Current;if(p==null)return;if(p.InternalKey.Length>0){OpenInternal(p.InternalKey,p);return;}if(p.AtHome){p.Home.Invalidate(true);return;}if(p.Web?.CoreWebView2==null){_=Navigate(p.Url,p);return;}
        if (p.Loading) { p.ActiveNavigationId = null; p.Web.CoreWebView2.Stop(); p.Loading = false; Sync(); } else { ApplyPolicy(p); p.Loading = true; p.Web.Reload(); }
    }
    private void CloseTab(BrowserTab? p)
    {
        if (p == null||!pages.Contains(p)) return;
        if(p==Current){var visible=VisualTabs();int i=Array.IndexOf(visible,p);var next=i+1<visible.Length?visible[i+1]:i>0?visible[i-1]:null;if(next!=null)SelectPage(next);}
        if (IsWeb(p.Url)) { closedTabs.Push(new TabSnapshot { Url = p.Url, Workspace = p.Workspace, Pinned = p.Pinned }); }
        p.CancelMemory?.Cancel();p.Favicon?.Dispose(); pages.Remove(p); tabs.TabPages.Remove(p.Container); p.Web?.Dispose(); p.Container.Dispose();horizontalTabs?.Refresh(); if (pages.Count == 0 && !closing) NewTab(); Sync(); QueueSave();
    }
    private static string Short(string s) => s.Length > 24 ? s[..23] + "…" : string.IsNullOrWhiteSpace(s) ? "Tab" : s;
    private void Sync()
    {
        if (closing) return;
        var p = Current; var web = p?.Web;
        RefreshSurfaces();UpdateBookmarkIcon();
        if(storeButton!=null){storeButton.Visible=p!=null&&CrxPackage.StoreId(p.Url)!=null;if(storeButton.Visible)storeButton.BringToFront();}
        if (!address.Focused) address.Text = p == null || p.AtHome ? "" : p.Url;
        back.Enabled = web?.CanGoBack == true; forward.Enabled = web?.CanGoForward == true;
        reload.Text = p?.Loading == true ? "×" : "↻"; shield.ForeColor = state.BlockAds ? mint : ink;
        progress.Visible = p?.Loading == true; if (progress.Visible) progress.Style = ProgressBarStyle.Marquee;
        Text = p == null || p.AtHome ? "Aether One" : p.Title + " — Aether One";
        status.Text = p == null || p.AtHome ? "AETHER ONE  /  Workspace ready · Ctrl+K for commands" :
            p.LastError.Length > 0 ? "Page error: " + p.LastError : p.Loading ? "Loading " + Policy.Host(p.Url) : $"{Policy.Host(p.Url)}  ·  navigation completed {p.Finished} ms  ·  {p.Blocked} requests blocked";
    }
    private void QueueSave() { if (!closing) { saver.Stop(); saver.Start(); } }
    private void Save()
    {
        if (smoke) return;
        state.Tabs = pages.Where(x => !x.AtHome && IsWeb(x.Url)).Select(x => x.Url).ToList();
        state.Sessions = pages.Where(x => !x.AtHome && IsWeb(x.Url)).Select(x => new TabSnapshot { Url = x.Url, Workspace = x.Workspace, Pinned = x.Pinned, Title=x.Title }).ToList();
        try { var temp = statePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, statePath, true); }
        catch (Exception) { if (!closing) status.Text = "Could not save browser state · check free disk space and folder permissions"; }
    }
    private void Bookmark()
    {
        var p = Current; if (p == null || p.AtHome || !IsWeb(p.Url)) return;
        state.Bookmarks.RemoveAll(x => x.Url == p.Url); state.Bookmarks.Insert(0, new SavedLink { Url = p.Url, Title = p.Title }); QueueSave();UpdateBookmarkIcon(); status.Text = "Page saved to bookmarks";
    }
    private Form Dialog(string title, int width = 620, int height = 450)
    {
        return new Form { Text = title, Width = width, Height = height, StartPosition = FormStartPosition.CenterParent, BackColor = bg, ForeColor = ink, Font = Font, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false };
    }
    private void BlockerDialog()=>OpenInternal("settings/privacy");
    private void SiteDialog()=>OpenInternal("settings/site");
    private void ShowLinks(bool history)=>OpenInternal(history?"history":"bookmarks");
    private async Task Inspector()
    {
        var p = Current; if (p?.Web?.CoreWebView2 == null || p.AtHome) { status.Text = "Open a website first"; return; }
        var url = p.Url;
        var report = $"{Policy.Host(url)}\r\n\r\nNavigation completed callback: {p.Finished} ms\r\nApp-intercepted resource requests: {p.Observed}\r\nBlocked ad/tracker requests: {p.Blocked}\r\n\r\n";
        try
        {
            if (!Rule(Policy.Host(url)).JavaScript) report += "JavaScript disabled: page timing unavailable.";
            else
            {
                var raw = await p.Web.CoreWebView2.ExecuteScriptAsync("""
                    (()=>{try{const n=performance.getEntriesByType('navigation')[0];if(!n)return null;
                    const r=performance.getEntriesByType('resource'),f=performance.getEntriesByName('first-contentful-paint')[0];
                    return {dns:n.domainLookupEnd-n.domainLookupStart,connection:n.connectEnd-n.connectStart,
                    server:n.responseStart-n.requestStart,download:n.responseEnd-n.responseStart,dom:n.domContentLoadedEventEnd,
                    load:n.loadEventEnd,fcp:f?f.startTime:null,count:r.length,bytes:r.reduce((s,x)=>s+(x.transferSize||0),0),
                    slow:r.slice().sort((a,b)=>b.duration-a.duration).slice(0,5).map(x=>({host:new URL(x.name).hostname,ms:x.duration}))};
                    }catch{return null;}})()
                    """);
                if (p.Url != url) { status.Text = "Page changed. Reopen Loading Inspector."; return; }
                using var doc = JsonDocument.Parse(raw.Length < 50000 ? raw : "null"); var j = doc.RootElement;
                if (j.ValueKind != JsonValueKind.Object) report += "Page timing unavailable.";
                else
                {
                    foreach (var pair in new[] { ("DNS", "dns"), ("Connection including TLS", "connection"), ("Request to first byte", "server"), ("Document download", "download"), ("DOM ready", "dom"), ("Load event", "load"), ("First contentful paint", "fcp") })
                        report += pair.Item1 + ": " + (j.GetProperty(pair.Item2).ValueKind == JsonValueKind.Number ? Math.Round(j.GetProperty(pair.Item2).GetDouble()) + " ms" : "unavailable") + "\r\n";
                    report += "\r\nResource timing entries: " + j.GetProperty("count") + "\r\nReported resource transfer bytes: " + j.GetProperty("bytes") + "\r\n\r\nSlowest reported resources\r\n";
                    foreach (var x in j.GetProperty("slow").EnumerateArray()) report += x.GetProperty("host").GetString() + " — " + Math.Round(x.GetProperty("ms").GetDouble()) + " ms\r\n";
                }
            }
        }
        catch { report += "Page timing unavailable."; }
        report += "\r\n\r\nPage timings are supplied by the website and can be altered by it. Cross-origin sizes can be hidden. Zero can mean cache, reused connections or a pending event. Dynamic content can keep loading after completion. Counts do not capture every worker or redirected request. No time-saved estimate or instant-load claim.";
        NewTab();var inspect=Current!;inspect.AtHome=false;inspect.InternalKey="inspector";inspect.Url="aether://inspector";inspect.Title="Loading inspector";inspect.Home.Visible=false;
        var box=new SoftPanel{Dock=DockStyle.Fill,BackColor=Visual.Base,Padding=new Padding(40),Radius=12};box.Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Visual.Base,ForeColor=Visual.Text,Font=new Font("Segoe UI",11),Text=report});inspect.NativePage=box;inspect.Container.Controls.Add(box);box.BringToFront();Sync();
    }
    private void Downloads()=>OpenInternal("downloads");
    private SoftPanel? findBar;
    private TextBox? findInput;
    private Task Find(){var p=Current;if(p?.Web?.CoreWebView2==null||p.AtHome||p.InternalKey.Length>0)return Task.CompletedTask;if(findBar!=null){findBar.Visible=true;findInput!.Focus();return Task.CompletedTask;}findBar=new SoftPanel{Dock=DockStyle.Top,Height=48,BackColor=Visual.Surface,Padding=new Padding(10,8,10,8)};findInput=new TextBox{Dock=DockStyle.Fill,PlaceholderText="Find in page",BackColor=Visual.Card,ForeColor=Visual.Text,BorderStyle=BorderStyle.None,Font=new Font("Segoe UI",11)};
        var close=ActionButton("×",()=>findBar.Visible=false,34);close.Dock=DockStyle.Right;var next=ActionButton("Next",async()=>{try{if(Current?.Web?.CoreWebView2!=null)await Current.Web.CoreWebView2.ExecuteScriptAsync("window.find("+JsonSerializer.Serialize(findInput.Text)+",false,false,true)");}catch{}},72);next.Dock=DockStyle.Right;findInput.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;next.PerformClick();}else if(e.KeyCode==Keys.Escape)findBar.Visible=false;};findBar.Controls.Add(findInput);findBar.Controls.Add(next);findBar.Controls.Add(close);pageHost.Controls.Add(findBar);findBar.BringToFront();findInput.Focus();return Task.CompletedTask;}
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        return HandleShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }
    private bool HandleShortcut(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.K: address.Focus(); address.SelectAll(); return true;
            case Keys.Control | Keys.Shift | Keys.T: ReopenClosed(); return true;
            case Keys.Control | Keys.Shift | Keys.N: ProfileMenu(); return true;
            case Keys.Control | Keys.L: address.Focus(); address.SelectAll(); ShowSuggestions(); return true;
            case Keys.Control | Keys.N: LaunchProfile(activeProfileId); return true;
            case Keys.Control | Keys.T: NewTab(); return true;
            case Keys.Control | Keys.W: CloseTab(Current); return true;
            case Keys.Control | Keys.D: BookmarkWithFeedback(); return true;
            case Keys.Control | Keys.B: OpenInternal("bookmarks"); return true;
            case Keys.Control | Keys.H: OpenInternal("history"); return true;
            case Keys.Control | Keys.J: Downloads(); return true;
            case Keys.Control | Keys.F: _ = Find(); return true;
            case Keys.Control | Keys.R: case Keys.F5: Reload(); return true;
            case Keys.Control | Keys.P: Current?.Web?.CoreWebView2?.ShowPrintUI(); return true;
            case Keys.Alt | Keys.Left: if (Current?.Web?.CanGoBack == true) Current.Web.GoBack(); return true;
            case Keys.Alt | Keys.Right: if (Current?.Web?.CanGoForward == true) Current.Web.GoForward(); return true;
            case Keys.Control | Keys.Tab: if (tabs.TabCount > 0) tabs.SelectedIndex = (tabs.SelectedIndex + 1) % tabs.TabCount; return true;
            case Keys.Control | Keys.Shift | Keys.Tab: if (tabs.TabCount > 0) tabs.SelectedIndex = (tabs.SelectedIndex + tabs.TabCount - 1) % tabs.TabCount; return true;
        }
        return false;
    }
    private async Task SmokeTest()
    {
        string output=Environment.GetEnvironmentVariable("AETHER_SMOKE_OUTPUT")??Path.Combine(profilePath,"Results");Directory.CreateDirectory(output);
        try {
            NewTab();
            var p = Current!;
            if (p.Web != null || environment != null) throw new Exception("New tab started WebView2 before navigation");
            await Ensure(p);
            if(Environment.GetEnvironmentVariable("AETHER_BENCHMARK")=="1"){await RunBenchmark(output,p);await RunRequestWorkload(output);await RunMemoryWorkload(output);Close();return;}
            await Task.Delay(300);CaptureWindow(Path.Combine(output,"AetherOne-Home.png"));
            string fixtures=Path.Combine(profilePath,"Fixtures");Directory.CreateDirectory(fixtures);
            using(var icon=new Bitmap(32,32)){using var g=Graphics.FromImage(icon);g.Clear(Color.Coral);icon.Save(Path.Combine(fixtures,"favicon.png"));}
            File.WriteAllText(Path.Combine(fixtures,"index.html"),"""
                <!doctype html><meta charset="utf-8"><title>Aether browser integration — a deliberately long page title for tab ellipsis</title><link rel="icon" href="/favicon.png"><style>body{margin:0;padding:70px;background:#f6f4fa;color:#292437;font:20px Segoe UI}h1{font-size:48px}p{line-height:1.7;max-width:650px}button{padding:12px}</style><h1>A cleaner way to browse.</h1><p>This local HTTPS fixture verifies the actual rendering engine, script execution, filtering, browser tabs and extension behavior.</p><script>window.aetherReady=42</script><script src="https://secure.doubleclick.net/aether-check.js"></script>
                """);
            File.WriteAllBytes(Path.Combine(fixtures,"payload.bin"),new byte[4096]);
            p.Web!.CoreWebView2.SetVirtualHostNameToFolderMapping("aether.test",fixtures,CoreWebView2HostResourceAccessKind.DenyCors);
            p.Web.CoreWebView2.AddWebResourceRequestedFilter("https://secure.doubleclick.net/aether-check.js*",CoreWebView2WebResourceContext.All);bool allowed=false;p.Web.CoreWebView2.WebResourceRequested+=(_,e)=>{if(e.Request.Uri.StartsWith("https://secure.doubleclick.net/aether-check.js")&&e.Response==null){allowed=true;e.Response=p.Web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream("/* fixture */"u8.ToArray()),200,"OK","Content-Type: application/javascript\r\n");}};
            await Navigate("https://aether.test/index.html",p);await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("window.aetherReady")=="42");if(p.Blocked<1)throw new Exception("Ad filtering failed");await WaitFor(()=>Task.FromResult(p.Favicon!=null));
            state.BlockAds=false;p.Loading=true;p.Web.Reload();await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("window.aetherReady")=="42");if(!allowed||p.Blocked!=0)throw new Exception("Blocker off failed");state.BlockAds=true;
            state.Sites["aether.test"]=new SiteRule{JavaScript=false};ApplyPolicy(p);p.Loading=true;p.Web.Reload();await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("typeof window.aetherReady")=="\"undefined\"");state.Sites.Remove("aether.test");ApplyPolicy(p);p.Loading=true;p.Web.Reload();await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("window.aetherReady")=="42");
            Bookmark();if(!state.Bookmarks.Any(x=>x.Url==p.Url))throw new Exception("Bookmark failed");
            NewTab();var second=Current!;await Ensure(second);await Navigate("https://aether.test/index.html",second);await WaitFor(async()=>!second.Loading&&await second.Web!.CoreWebView2.ExecuteScriptAsync("window.aetherReady")=="42");
            horizontalTabs!.Refresh();foreach(var offset in new[]{new Point(5,5),new Point(55,22),new Point(75,30)}){SelectPage(second);horizontalTabs.Refresh();var r=horizontalTabs.hits.First(x=>x.Page==p).Rect;horizontalTabs.TestClick(new Point(r.X+offset.X,r.Y+offset.Y));if(Current!=p)throw new Exception("Tab body hit target failed");}
            await SuspendHiddenAsync(second);if(second.Web!.CoreWebView2.MemoryUsageTargetLevel!=CoreWebView2MemoryUsageTargetLevel.Low)throw new Exception("Hidden memory target failed");SelectPage(second);if(second.Web.CoreWebView2.MemoryUsageTargetLevel!=CoreWebView2MemoryUsageTargetLevel.Normal||await second.Web.CoreWebView2.ExecuteScriptAsync("window.aetherReady")!="42")throw new Exception("Memory restore failed");SelectPage(p);
            var cookie=p.Web.CoreWebView2.CookieManager.CreateCookie("aether_isolation","original","aether.test","/");p.Web.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
            using(var isolated=new WebView2{Visible=false}){Controls.Add(isolated);var isolatedEnvironment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(profilePath,"IsolatedProfile"),new CoreWebView2EnvironmentOptions{AreBrowserExtensionsEnabled=true});await isolated.EnsureCoreWebView2Async(isolatedEnvironment);if((await isolated.CoreWebView2.CookieManager.GetCookiesAsync("https://aether.test")).Any(c=>c.Name=="aether_isolation"))throw new Exception("Profile cookies were not isolated");Controls.Remove(isolated);}
            if(!(await p.Web.CoreWebView2.CookieManager.GetCookiesAsync("https://aether.test")).Any(c=>c.Name=="aether_isolation"))throw new Exception("Original profile cookie missing");
            address.Focus();address.Text="weather";await FetchPredictions(true);if(remotePredictions.Count==0)throw new Exception("Live prediction provider returned no predictions");ShowSuggestions();await Task.Delay(200);if(!suggestions!.Visible||!address.Focused||!suggestionItems.Any(x=>x.Detail.Contains("prediction")))throw new Exception("Live suggestion popup failed");CaptureWindow(Path.Combine(output,"AetherOne-Suggestions.png"));suggestions.Hide();p.Web.Focus();
            var extdir=Path.Combine(profilePath,"FixtureExtension");Directory.CreateDirectory(extdir);File.WriteAllText(Path.Combine(extdir,"manifest.json"),"""{"manifest_version":3,"name":"Integration Fixture","version":"1.0","content_scripts":[{"matches":["https://aether.test/*"],"js":["content.js"]}]}""");File.WriteAllText(Path.Combine(extdir,"content.js"),"document.documentElement.dataset.aetherExtension='active'");
            var ext=await ImportExtension(p.Web.CoreWebView2,extdir,false)??throw new Exception("Extension import failed");p.Loading=true;p.Web.Reload();await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("document.documentElement.dataset.aetherExtension")=="\"active\"");await ext.EnableAsync(false);p.Loading=true;p.Web.Reload();await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("document.documentElement.dataset.aetherExtension")=="null");await ext.EnableAsync(true);
            // Exercise a real signed store package through the same verification/extraction/engine path.
            string storeId="eimadpbcbfnmbkopoojfekhnkhdbieeh";var package=await DownloadStorePackage(storeId,p.Web.CoreWebView2.Environment.BrowserVersionString.Split(' ')[0]);await InstallCrxBytes(package,storeId,p,false);if(!loadedExtensions.TryGetValue(storeId,out var storeExtension)||!storeExtension.IsEnabled)throw new Exception("Store package install failed");
            var ultimateBytes=await DownloadStorePackage("ohahllgiabjaoigichmmfljhkcfikeof",p.Web.CoreWebView2.Environment.BrowserVersionString.Split(' ')[0]);await InstallCrxBytes(ultimateBytes,"ohahllgiabjaoigichmmfljhkcfikeof",p,false);if(!loadedExtensions.ContainsKey("ohahllgiabjaoigichmmfljhkcfikeof"))throw new Exception("AdBlocker Ultimate installation failed");await loadedExtensions["ohahllgiabjaoigichmmfljhkcfikeof"].EnableAsync(false);
            File.WriteAllText(Path.Combine(output,"store-test.txt"),"PASS: Downloaded official Chrome Web Store CRX3 for Dark Reader, verified developer signature and expected ID, extracted safely and loaded with the same ID in WebView2. AdBlocker Ultimate also downloaded, verified, extracted and installed.\n");
            SelectPage(p);Activate();OpenInternal("extensions",p);await Task.Delay(800);SelectPage(p);if(Current!=p||p.NativePage?.Visible!=true)throw new Exception("Extensions page is not active");await Task.Delay(250);Refresh();p.NativePage.Refresh();horizontalTabs!.Refresh();CaptureWindow(Path.Combine(output,"AetherOne-Extensions.png"));await storeExtension.EnableAsync(false);await ext.RemoveAsync();loadedExtensions.Remove(ext.Id);state.ExtensionFolders.Remove(ext.Id);if((await p.Web.CoreWebView2.Profile.GetBrowserExtensionsAsync()).Any(x=>x.Id==ext.Id))throw new Exception("Extension removal failed");
            OpenInternal("settings/appearance",p);await Task.Delay(200);CaptureWindow(Path.Combine(output,"AetherOne-Settings.png"));if(Current!=p||p.NativePage==null||!p.NativePage.Visible||p.Web?.Visible==true)throw new Exception("Native settings page failed");OpenInternal("settings/search",p);await Task.Delay(200);CaptureWindow(Path.Combine(output,"AetherOne-Search-Settings.png"));
            state.SearchEngine="Brave";if(!Policy.Resolve("test search",state.SearchEngine)!.StartsWith("https://search.brave.com"))throw new Exception("Engine switch failed");state.SearchEngine="Google";
            Home();await Navigate("https://aether.test/index.html",p);await WaitFor(async()=>!p.Loading&&await p.Web.CoreWebView2.ExecuteScriptAsync("window.aetherReady")=="42");state.AskDownloadLocation=false;state.DownloadFolder=Path.Combine(profilePath,"DownloadFixture");await p.Web.CoreWebView2.ExecuteScriptAsync("(()=>{let a=document.createElement('a');a.href='/payload.bin';a.download='payload.bin';a.click()})()");await WaitFor(()=>Task.FromResult(downloadEntries.Any(x=>x.Operation.State==CoreWebView2DownloadState.Completed&&File.Exists(x.Path))));if(new FileInfo(downloadEntries[0].Path).Length!=4096)throw new Exception("Download byte count failed");
            SelectPage(p);SleepInactive(false);if(second.Web!=null||!second.Sleeping)throw new Exception("Manual sleep did not release inactive view");SelectPage(second);await WaitFor(async()=>second.Web?.CoreWebView2!=null&&!second.Loading&&await second.Web.CoreWebView2.ExecuteScriptAsync("window.aetherReady")=="42");SelectPage(p);
            CloseTab(second);ReopenClosed();if(Current==p||!IsWeb(Current?.Url))throw new Exception("Reopen tab failed");CloseTab(Current);SelectPage(p);NewTab();var preview=Current!;horizontalTabs.Refresh();Home();await Task.Delay(200);CaptureWindow(Path.Combine(output,"AetherOne-Dashboard.png"));
            ShowBrowserMenu();await Task.Delay(150);CaptureWindow(Path.Combine(output,"AetherOne-Menu.png"));System.Windows.Forms.SendKeys.SendWait("{ESC}");
            SelectPage(p);Activate();await Task.Delay(100);p.Web.Focus();System.Windows.Forms.SendKeys.SendWait("^l");await WaitFor(()=>Task.FromResult(address.Focused));if(!address.Focused)throw new Exception("Page-focused Ctrl+L failed");suggestions?.Hide();p.Web.Focus();CaptureWindow(Path.Combine(output,"AetherOne-Engine.png"));
            await TestInteractions(output,p);await TestArticleRendering(output,p);
            File.WriteAllText(Path.Combine(output,"smoke-test.txt"),"PASS: real engine HTTPS/JavaScript, ad blocker on/off, site script setting, bookmark, favicon, full tab-body click targets, Low/Normal memory targets with DOM preservation, live Google search predictions and popup focus, unpacked extension execution/disable, real signed store package installation, extension removal, native settings tabs, search engine switching, completed file download bytes, close/reopen, isolated profile cookies, manual sleep/reload, page-focused Ctrl+L.\nRuntime: "+p.Web.CoreWebView2.Environment.BrowserVersionString);Close();
        }catch(Exception e){File.WriteAllText(Path.Combine(output,"smoke-test.txt"),"FAIL: "+e);Environment.ExitCode=1;Close();}
    }
    private static async Task WaitFor(Func<Task<bool>> ready)
    {
        var clock = Stopwatch.StartNew(); while (clock.Elapsed < TimeSpan.FromSeconds(25)) { if (await ready()) return; await Task.Delay(100); } throw new TimeoutException("WebView2 fixture did not reach expected state");
    }
    private void CaptureWindow(string path)
    {
        using var bmp = new Bitmap(Width, Height); using var g = Graphics.FromImage(bmp); g.CopyFromScreen(Location, Point.Empty, Size); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
