using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
namespace AetherOnePC;
public sealed partial class BrowserForm
{
    private string FaviconPath(string url)=>Path.Combine(profilePath,"Favicons",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Policy.Host(url))))+".png");
    private Bitmap? LoadCachedFavicon(string url){try{using var original=Image.FromFile(FaviconPath(url));return new Bitmap(original,16,16);}catch{return null;}}
    private void CacheFavicon(string url,Bitmap icon){if(!IsWeb(url))return;try{using var memory=new MemoryStream();icon.Save(memory,System.Drawing.Imaging.ImageFormat.Png);var bytes=memory.ToArray();var path=FaviconPath(url);_=Task.Run(()=>{try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);if(File.Exists(path)||Directory.EnumerateFiles(Path.GetDirectoryName(path)!,"*.png").Take(257).Count()<256)File.WriteAllBytes(path,bytes);}catch{}});}catch{}}
    private HorizontalTabs? horizontalTabs;
    private FavoritesBar? favorites;
    private FlowLayoutPanel? extensionPins;
    private TableLayoutPanel? browserToolbar;
    private Button? homeButton,storeButton;
    private string extensionPinKey="";
    private SuggestionWindow? suggestions;
    private readonly System.Windows.Forms.Timer suggestTimer=new(){Interval=220};
    private readonly List<Suggestion> suggestionItems=[];
    private CancellationTokenSource? predictCancellation;
    private readonly List<string> remotePredictions=[];
    private string predictionQuery="";
    private static readonly HttpClient network=new(){Timeout=Timeout.InfiniteTimeSpan};
    private sealed record Suggestion(string Title,string Detail,string Value,BrowserTab? Tab=null);
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
    protected override void WndProc(ref Message m){if(m.Msg==0x84&&WindowState!=FormWindowState.Maximized){long v=m.LParam.ToInt64();var p=PointToClient(new Point((short)v,(short)(v>>16)));bool l=p.X<6,r=p.X>=ClientSize.Width-6,t=p.Y<6,b=p.Y>=ClientSize.Height-6;if(l||r||t||b){m.Result=(IntPtr)(t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:11);return;}}base.WndProc(ref m);}
    private void BuildBrowserChrome()
    {
        Visual.SetTheme(state.DarkTheme);bg=Visual.Base;panel=Visual.Surface;ink=Visual.Text;mint=Visual.Mint;
        FormBorderStyle=FormBorderStyle.None;MaximizedBounds=Screen.FromControl(this).WorkingArea;BackColor=Visual.Base;MinimumSize=new Size(860,560);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,BackColor=Visual.Base,Padding=Padding.Empty,Margin=Padding.Empty};
        foreach(var height in new[]{40,48,state.ShowFavorites?30:0})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,2));Controls.Add(root);
        horizontalTabs=new HorizontalTabs(this){Dock=DockStyle.Fill,Margin=Padding.Empty};root.Controls.Add(horizontalTabs,0,0);
        var bar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=11,RowCount=1,Padding=new Padding(8,5,8,5),BackColor=Visual.Surface,Margin=Padding.Empty};browserToolbar=bar;bar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        for(int i=0;i<11;i++)bar.ColumnStyles.Add(new ColumnStyle(i==4?SizeType.Percent:SizeType.Absolute,i==4?100:i==8?0:36));
        Button Tool(string icon,string name,Action action){var b=(ChromeButton)Button("",name,action);b.Glyph=icon;b.Margin=Padding.Empty;b.BackColor=Visual.Surface;return b;}
        back=Tool("back","Back · Alt+Left",()=>{if(Current?.Web?.CanGoBack==true)Current.Web.GoBack();});forward=Tool("forward","Forward · Alt+Right",()=>{if(Current?.Web?.CanGoForward==true)Current.Web.GoForward();});reload=Tool("reload","Reload · Ctrl+R",Reload);homeButton=Tool("home","Home",Home);
        bar.Controls.Add(back,0,0);bar.Controls.Add(forward,1,0);bar.Controls.Add(reload,2,0);bar.Controls.Add(homeButton,3,0);bar.ColumnStyles[3].Width=state.ShowHome?36:0;
        var omnibox=new SoftPanel{Dock=DockStyle.Fill,BackColor=Visual.Card,Radius=19,Padding=new Padding(15,10,48,7),Margin=new Padding(6,0,9,0)};
        address.Font=new Font("Segoe UI",10);address.BackColor=Visual.Card;address.ForeColor=Visual.Text;address.PlaceholderText="Search "+state.SearchEngine+" or enter address";omnibox.Controls.Add(address);omnibox.Click+=(_,_)=>{address.Focus();address.SelectAll();};
        var save=Tool("bookmark","Bookmark this tab · Ctrl+D",BookmarkWithFeedback);bookmarkButton=(ChromeButton)save;save.Dock=DockStyle.None;save.Size=new Size(32,32);save.Anchor=AnchorStyles.Right|AnchorStyles.Top;save.BackColor=Visual.Card;omnibox.Controls.Add(save);omnibox.Resize+=(_,_)=>save.Location=new Point(omnibox.Width-38,3);bar.Controls.Add(omnibox,4,0);
        shield=Tool("shield","Shields · site controls",()=>OpenInternal("settings/site"));shield.ForeColor=Visual.Mint;bar.Controls.Add(shield,5,0);
        bar.Controls.Add(Tool("extensions","Extensions",()=>OpenInternal("extensions")),6,0);bar.Controls.Add(Tool("download","Downloads · Ctrl+J",()=>OpenInternal("downloads")),7,0);
        extensionPins=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,BackColor=Visual.Surface};bar.Controls.Add(extensionPins,8,0);
        bar.Controls.Add(Tool("profile","Profiles",ProfileMenu),9,0);bar.Controls.Add(Tool("menu","Browser menu",ShowBrowserMenu),10,0);root.Controls.Add(bar,0,1);
        favorites=new FavoritesBar(this){Dock=DockStyle.Fill,Visible=state.ShowFavorites,Margin=Padding.Empty};root.Controls.Add(favorites,0,2);
        shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=1,Margin=Padding.Empty};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        pageHost=new Panel{Dock=DockStyle.Fill,BackColor=Visual.Base,Margin=Padding.Empty};pageHost.Controls.Add(tabs);shell.Controls.Add(pageHost,0,0);root.Controls.Add(shell,0,3);
        progress.Dock=DockStyle.Fill;root.Controls.Add(progress,0,4);
        storeButton=Button("Install extension","Install this Chrome Web Store extension",()=>_=InstallStore(Current?.Url??""));storeButton.Dock=DockStyle.Top;storeButton.Height=36;storeButton.Visible=false;pageHost.Controls.Add(storeButton);
        address.TextChanged+=(_,_)=>{predictCancellation?.Cancel();suggestTimer.Stop();remotePredictions.Clear();predictionQuery="";if(address.Focused){ShowSuggestions();suggestTimer.Start();}else suggestions?.Hide();};
        suggestTimer.Tick+=async(_,_)=>{suggestTimer.Stop();await FetchPredictions();};address.GotFocus+=(_,_)=>{ShowSuggestions();suggestTimer.Start();};address.LostFocus+=(_,_)=>{predictCancellation?.Cancel();BeginInvoke((Action)(()=>{if(suggestions?.ContainsFocus!=true)suggestions?.Hide();}));};
        address.KeyDown+=(_,e)=>{if(e.KeyCode is Keys.Down or Keys.Up){e.SuppressKeyPress=true;if(suggestions?.Visible!=true)ShowSuggestions();suggestions?.MoveSelection(e.KeyCode==Keys.Down?1:-1);}else if(e.KeyCode==Keys.Escape){predictCancellation?.Cancel();suggestions?.Hide();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;if(suggestions?.Visible==true&&suggestions.SelectedIndex>=0)ChooseSuggestion(suggestions.SelectedIndex);else _=Navigate(address.Text);}};
        LocationChanged+=(_,_)=>suggestions?.Hide();ResizeBegin+=(_,_)=>suggestions?.Hide();
    }
    private void RefreshExtensionPins()
    {
        if(extensionPins==null||browserToolbar==null)return;
        var extensions=state.PinnedExtensions.Where(loadedExtensions.ContainsKey).Select(id=>loadedExtensions[id]).Where(x=>x.IsEnabled).Take(3).ToArray();string key=string.Join('|',extensions.Select(x=>x.Id));if(key==extensionPinKey)return;extensionPinKey=key;
        foreach(Control c in extensionPins.Controls.Cast<Control>().ToArray())c.Dispose();extensionPins.Controls.Clear();browserToolbar.ColumnStyles[8].Width=extensions.Length*34;
        foreach(var ext in extensions){var e=ext;var b=(ChromeButton)Button(e.Name[..1],e.Name,()=>_=OpenExtensionPage(e));b.Glyph="";b.Dock=DockStyle.None;b.Size=new Size(32,36);b.Margin=Padding.Empty;b.BackColor=Visual.Surface;extensionPins.Controls.Add(b);}
    }
    private ContextMenuStrip BrowserContextMenu(){var menu=new ContextMenuStrip{Renderer=new CleanMenuRenderer(),BackColor=Visual.Surface,ForeColor=Visual.Text,Font=new Font("Segoe UI",10),ShowImageMargin=false,Padding=new Padding(7),DropShadowEnabled=true};WatchPopup(menu);return menu;}
    private void ShowBrowserMenu()
    {
        suggestions?.Hide();suggestTimer.Stop();predictCancellation?.Cancel();
        var m=BrowserContextMenu();void Item(string name,Action run,string key=""){var i=new ToolStripMenuItem(name){ShortcutKeyDisplayString=key,AutoSize=false,Width=282,Height=34};i.Click+=(_,_)=>run();m.Items.Add(i);}
        Item("New tab",()=>NewTab(),"Ctrl+T");Item("New window",()=>LaunchProfile(activeProfileId),"Ctrl+N");Item("Reopen closed tab",ReopenClosed,"Ctrl+Shift+T");m.Items.Add(new ToolStripSeparator());
        Item("Profiles",ProfileMenu);Item("Bookmarks",()=>OpenInternal("bookmarks"),"Ctrl+B");Item("History",()=>OpenInternal("history"),"Ctrl+H");Item("Downloads",()=>OpenInternal("downloads"),"Ctrl+J");Item("Extensions",()=>OpenInternal("extensions"));m.Items.Add(new ToolStripSeparator());
        Item("Zoom",ZoomMenu);Item("Find in page",()=>_=Find(),"Ctrl+F");Item("Print",()=>Current?.Web?.CoreWebView2?.ShowPrintUI(),"Ctrl+P");Item("Save page as PDF",()=>Current?.Web?.CoreWebView2?.ShowPrintUI());m.Items.Add(new ToolStripSeparator());
        Item("Shields & privacy",()=>OpenInternal("settings/privacy"));Item("Site settings",()=>OpenInternal("settings/site"));Item("Loading inspector",()=>_=Inspector());Item("Sleep inactive tabs",()=>SleepInactive());Item("Settings",()=>OpenInternal("settings"));Item("About Aether One",()=>OpenInternal("settings/about"));Item("Exit",Close);bool web=Current is {AtHome:false,InternalKey:"",Web:not null};foreach(ToolStripItem item in m.Items)if(item.Text is "Zoom" or "Find in page" or "Print" or "Save page as PDF" or "Loading inspector")item.Enabled=web;m.Show(this,new Point(Math.Max(0,ClientSize.Width-300),85));
    }
    private void ZoomMenu(){var m=BrowserContextMenu();foreach(int n in new[]{75,90,100,110,125,150}){int value=n;m.Items.Add(n+"%",null,(_,_)=>{if(Current?.Web!=null)Current.Web.ZoomFactor=value/100.0;});}m.Show(Cursor.Position);}
    private void SettingsDialog()=>OpenInternal("settings");
    private void ShowSuggestions()
    {
        if(!address.Focused||closing)return;string q=address.Text.Trim();suggestionItems.Clear();
        if(q.Length>0){bool url=IsWeb(q)||(!q.Any(char.IsWhiteSpace)&&q.Contains('.'));suggestionItems.Add(new(url?"Open "+q:q,url?"Web address":"Search with "+state.SearchEngine,q));}
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){q};
        if(predictionQuery==q)foreach(var x in remotePredictions)if(seen.Add(x))suggestionItems.Add(new(x,"Search prediction · Google",x));
        bool Match(string t,string u)=>q.Length==0||t.Contains(q,StringComparison.OrdinalIgnoreCase)||u.Contains(q,StringComparison.OrdinalIgnoreCase);
        foreach(var p in pages.Where(p=>!p.AtHome&&p.InternalKey.Length==0&&Match(p.Title,p.Url)).Take(2))if(seen.Add(p.Url))suggestionItems.Add(new(p.Title,"Switch to tab · "+p.Url,p.Url,p));
        foreach(var x in state.Bookmarks.Concat(state.History).Where(x=>Match(x.Title,x.Url))){if(suggestionItems.Count>=8)break;if(seen.Add(x.Url))suggestionItems.Add(new(x.Title,x.Url,x.Url));}
        if(suggestionItems.Count==0){suggestions?.Hide();return;}if(suggestions==null){suggestions=new SuggestionWindow(this);suggestions.VisibleChanged+=(_,_)=>UpdatePopupHook();}suggestions.Items=suggestionItems.Take(8).Select(x=>(x.Title,x.Detail)).ToList();suggestions.SelectedIndex=-1;var box=address.Parent!;suggestions.Bounds=new Rectangle(box.PointToScreen(new Point(0,box.Height-2)),new Size(box.Width,suggestions.Items.Count*48+12));if(!suggestions.Visible)suggestions.Show(this);suggestions.Invalidate();
    }
    private static bool CanPredict(string q)=>q.Length is >=2 and <=150&&!q.Contains("://")&&!q.Contains('@')&&!q.Contains('\\')&&!(q.Contains('.')&&!q.Any(char.IsWhiteSpace));
    private async Task FetchPredictions(bool allowTest=false)
    {
        string q=address.Text.Trim();if((smoke&&!allowTest)||!state.SearchPredictions||!address.Focused||!CanPredict(q))return;predictCancellation?.Cancel();var c=new CancellationTokenSource(TimeSpan.FromSeconds(3));predictCancellation=c;
        try{var data=await network.GetStringAsync("https://suggestqueries.google.com/complete/search?client=firefox&q="+Uri.EscapeDataString(q),c.Token);var items=PredictionParser.Parse(data);if(c.IsCancellationRequested||closing||!address.Focused||q!=address.Text.Trim())return;remotePredictions.Clear();remotePredictions.AddRange(items);predictionQuery=q;ShowSuggestions();}catch(OperationCanceledException){}catch(HttpRequestException){}catch(System.Text.Json.JsonException){}
    }
    private void ChooseSuggestion(int i){if(i<0||i>=suggestionItems.Count)return;var x=suggestionItems[i];predictCancellation?.Cancel();suggestions?.Hide();if(x.Tab!=null){SelectPage(x.Tab);x.Tab.Web?.Focus();Sync();}else _=Navigate(x.Value);}
    private sealed class SuggestionWindow:Form
    {
        private readonly BrowserForm owner;public List<(string Title,string Detail)> Items=[];public int SelectedIndex=-1;
        protected override bool ShowWithoutActivation=>true;protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
        public SuggestionWindow(BrowserForm o){owner=o;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;DoubleBuffered=true;BackColor=Visual.Card;}
        public void MoveSelection(int by){SelectedIndex=Math.Clamp(SelectedIndex+by,0,Items.Count-1);Invalidate();}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Visual.Card);for(int i=0;i<Items.Count;i++){var r=new Rectangle(5,6+i*48,Width-10,46);if(i==SelectedIndex)Visual.Round(e.Graphics,r,Visual.Hover,8);Visual.Icon(e.Graphics,"search",new Rectangle(17,r.Y+14,17,17),Visual.Muted);Visual.TextAt(e.Graphics,Items[i].Title,new Rectangle(45,r.Y+2,Width-60,24),10,Visual.Text);Visual.TextAt(e.Graphics,Items[i].Detail,new Rectangle(45,r.Y+25,Width-60,18),8,Visual.Muted);}}
        protected override void OnMouseMove(MouseEventArgs e){int i=(e.Y-6)/48;if(i>=0&&i<Items.Count){SelectedIndex=i;Invalidate();}base.OnMouseMove(e);}
        protected override void OnMouseDown(MouseEventArgs e){int i=(e.Y-6)/48;if(i>=0&&i<Items.Count)owner.ChooseSuggestion(i);base.OnMouseDown(e);}
    }
    private sealed class HorizontalTabs:Control
    {
        private readonly BrowserForm owner;internal readonly List<(Rectangle Rect,BrowserTab Page)> hits=[];private readonly Dictionary<BrowserTab,Rectangle> closeHits=[];private BrowserTab? hotClose;private int first;private int? closeWidth;private int currentWidth;private Rectangle plus;private readonly ToolTip tip=new();private BrowserTab? hovered;
        public HorizontalTabs(BrowserForm o){owner=o;DoubleBuffered=true;BackColor=Visual.Base;AccessibleName="Browser tabs";}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Visual.Base);hits.Clear();closeHits.Clear();Visual.Icon(g,"down",new Rectangle(12,14,16,16),Visual.Muted);
            var all=owner.VisualTabs();int available=Math.Max(120,Width-230),tw=closeWidth??Math.Clamp(available/Math.Max(1,all.Length),112,236),slots=Math.Max(1,available/tw),active=Array.IndexOf(all,owner.Current);if(active<first)first=Math.Max(0,active);if(active>=first+slots)first=active-slots+1;first=Math.Clamp(first,0,Math.Max(0,all.Length-slots));
            currentWidth=tw;int x=43;foreach(var p in all.Skip(first).Take(slots)){var r=new Rectangle(x,5,tw-4,35);bool current=p==owner.Current;if(current||p==hovered)Visual.Round(g,r,current?Visual.Surface:Visual.Hover,10);
                if(p.Favicon!=null&&!p.AtHome&&p.InternalKey.Length==0)g.DrawImage(p.Favicon,new Rectangle(x+12,15,16,16));else Visual.Icon(g,p.AtHome?"brand":p.InternalKey.Length>0?"settings":"clock",new Rectangle(x+12,14,17,17),Visual.Muted);
                Visual.TextAt(g,p.AtHome?"New tab":p.Title,new Rectangle(x+37,8,tw-72,30),9,Visual.Text);if(p.Audible)Visual.TextAt(g,"♪",new Rectangle(r.Right-40,9,16,28),10,Visual.Mint);
                var close=new Rectangle(r.Right-27,10,24,24);closeHits[p]=close;if(p==hotClose)Visual.Round(g,close,Visual.Hover,12);Visual.Icon(g,"close",new Rectangle(close.X+7,close.Y+7,10,10),p==hotClose?Visual.Text:Visual.Muted);hits.Add((r,p));x+=tw;}
            plus=new Rectangle(Math.Min(x+3,Width-178),7,30,30);Visual.Icon(g,"plus",new Rectangle(plus.X+7,plus.Y+7,16,16),Visual.Muted);
            using var pen=new Pen(Visual.Text);g.DrawLine(pen,Width-120,20,Width-110,20);g.DrawRectangle(pen,Width-78,15,9,9);Visual.Icon(g,"close",new Rectangle(Width-39,12,16,16),Visual.Text);
        }
        internal void TestClick(Point point)=>OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));
        protected override void OnMouseDown(MouseEventArgs e){Refresh();base.OnMouseDown(e);if(e.X>Width-48){owner.Close();return;}if(e.X>Width-94){owner.WindowState=owner.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;return;}if(e.X>Width-140){owner.WindowState=FormWindowState.Minimized;return;}if(e.X<38){owner.TabSearchMenu();return;}foreach(var(r,p)in hits)if(r.Contains(e.Location)){if(e.Button==MouseButtons.Right)owner.TabMenu(p);else if(e.Button==MouseButtons.Middle||e.Button==MouseButtons.Left&&closeHits.TryGetValue(p,out var close)&&close.Contains(e.Location)){closeWidth=currentWidth;owner.CloseTab(p);}else owner.SelectPage(p);return;}if(plus.Contains(e.Location)){owner.NewTab();return;}if(e.Clicks==2){owner.WindowState=owner.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;return;}ReleaseCapture();SendMessage(owner.Handle,0xA1,(IntPtr)2,IntPtr.Zero);}
        protected override void OnMouseMove(MouseEventArgs e){var p=hits.FirstOrDefault(h=>h.Rect.Contains(e.Location)).Page;var close=p!=null&&closeHits.TryGetValue(p,out var rect)&&rect.Contains(e.Location)?p:null;if(close!=hotClose){hotClose=close;Invalidate();}if(p!=hovered){hovered=p;tip.SetToolTip(this,p==null?"":p.Title+"\n"+p.Url+(p.Sleeping?"\nSleeping · reloads on selection":p.Suspended?"\nSuspended · page state retained":""));Invalidate();}base.OnMouseMove(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=null;hotClose=null;closeWidth=null;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseWheel(MouseEventArgs e){first=Math.Max(0,first-(e.Delta>0?1:-1));Invalidate();}
        protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
    }
    private void TabSearchMenu(){var m=BrowserContextMenu();foreach(var p in pages){var tab=p;m.Items.Add(p.AtHome?"New tab":p.Title,null,(_,_)=>SelectPage(tab));}m.Show(this,new Point(5,40));}
    private sealed class FavoritesBar:Control
    {
        private readonly BrowserForm owner;private readonly List<(Rectangle R,string Url)> hits=[];
        public FavoritesBar(BrowserForm o){owner=o;DoubleBuffered=true;AccessibleName="Bookmarks bar";Cursor=Cursors.Hand;}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Visual.Surface);hits.Clear();int x=17;foreach(var p in owner.state.Bookmarks.Take(10)){int w=Math.Min(165,TextRenderer.MeasureText(p.Title,Font).Width+32);if(x+w>Width-25)break;var icon=owner.pages.FirstOrDefault(t=>Policy.Host(t.Url)==Policy.Host(p.Url)&&t.Favicon!=null)?.Favicon;if(icon!=null)g.DrawImage(icon,new Rectangle(x,7,16,16));else Visual.Icon(g,"bookmark",new Rectangle(x,7,16,16),Visual.Muted);Visual.TextAt(g,p.Title,new Rectangle(x+23,2,w-26,26),8,Visual.Text);hits.Add((new Rectangle(x,0,w,Height),p.Url));x+=w+12;}if(owner.state.Bookmarks.Count==0)Visual.TextAt(g,"Your bookmarks appear here.  Bookmark a page with Ctrl+D.",new Rectangle(17,0,Width-35,Height),9,Visual.Muted);}
        protected override void OnMouseDown(MouseEventArgs e){foreach(var(r,url)in hits)if(r.Contains(e.Location)){if(e.Button==MouseButtons.Middle)owner.NewTab(url);else _=owner.Navigate(url);return;}}
    }
    private sealed class StartPage:Control
    {
        private readonly BrowserForm owner;private readonly BrowserTab page;private Rectangle search;private readonly List<(Rectangle R,string Url)> links=[];
        public StartPage(BrowserForm o,BrowserTab p){owner=o;page=p;DoubleBuffered=true;AccessibleName="Aether new tab";Cursor=Cursors.Default;}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using var gradient=new LinearGradientBrush(ClientRectangle,owner.state.DarkTheme?Color.FromArgb(33,27,57):Color.FromArgb(240,235,254),owner.state.DarkTheme?Color.FromArgb(16,19,33):Color.FromArgb(222,231,249),32);g.FillRectangle(gradient,ClientRectangle);links.Clear();int left=Math.Max(35,(Width-680)/2),w=Math.Min(680,Width-70),y=Math.Max(100,Height/4);
            Visual.TextAt(g,"Aether One",new Rectangle(left,y-14,w,65),35,Visual.Text,true);Visual.TextAt(g,"A quieter way to explore.",new Rectangle(left+3,y+54,w,30),12,Visual.Muted);
            search=new Rectangle(left,y+118,w,58);Visual.Round(g,search,Visual.Card,28);Visual.Icon(g,"search",new Rectangle(left+21,y+138,19,19),Visual.Muted);Visual.TextAt(g,"Search "+owner.state.SearchEngine+" or enter a web address",new Rectangle(left+56,y+126,w-80,42),11,Visual.Muted);
            var sites=new[]{("Google","G","https://www.google.com/"),("YouTube","Y","https://www.youtube.com/"),("Wikipedia","W","https://www.wikipedia.org/"),("GitHub","GH","https://github.com/")};int start=left+(w-352)/2;
            for(int i=0;i<4;i++){int x=start+i*88;var r=new Rectangle(x,y+207,80,87);Visual.Round(g,new Rectangle(x+15,y+208,48,48),Visual.Card,24);Visual.TextAt(g,sites[i].Item2,new Rectangle(x+17,y+215,44,33),15,Visual.Text,true);Visual.TextAt(g,sites[i].Item1,new Rectangle(x,y+265,80,20),9,Visual.Muted);links.Add((r,sites[i].Item3));}
            Visual.TextAt(g,DateTime.Now.ToString("HH:mm"),new Rectangle(Width-170,35,140,60),32,Visual.Text);Visual.TextAt(g,"Make it yours  ·  Settings",new Rectangle(Width-230,Height-43,210,24),9,Visual.Muted);
        }
        protected override void OnMouseDown(MouseEventArgs e){if(search.Contains(e.Location)){owner.address.Focus();owner.address.SelectAll();return;}foreach(var(r,url)in links)if(r.Contains(e.Location)){_=owner.Navigate(url,page);return;}if(e.Y>Height-50&&e.X>Width-250)owner.OpenInternal("settings/appearance");}
    }
}
internal sealed class SoftPanel:Panel
{
    public int Radius=14;
    public SoftPanel(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent?.BackColor??Visual.Base);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;if(Width>2&&Height>2)Visual.Round(e.Graphics,new Rectangle(0,0,Width-1,Height-1),BackColor,Radius);}
}
internal sealed class CleanMenuRenderer:ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Visual.Surface);}
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(e.Item.Selected)Visual.Round(e.Graphics,new Rectangle(2,1,e.Item.Width-4,e.Item.Height-2),Visual.Hover,7);}
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=Visual.Text;base.OnRenderItemText(e);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using var p=new Pen(Visual.Stroke);e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
}
