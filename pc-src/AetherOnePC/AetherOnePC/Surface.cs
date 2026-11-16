using System.Drawing.Drawing2D;
using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text.Json;

namespace AetherOnePC;
internal static class Visual
{
    public static Color Base = Color.FromArgb(27, 29, 33), Surface = Color.FromArgb(32, 34, 39), Card = Color.FromArgb(42, 44, 50), Stroke = Color.FromArgb(62, 65, 72), Text = Color.FromArgb(237, 242, 248), Muted = Color.FromArgb(165, 171, 182), Mint = Color.FromArgb(132, 229, 204), Lilac = Color.FromArgb(186, 173, 245);
    public static Color Hover=Color.FromArgb(61,59,72);
    public static void SetTheme(bool dark){Base=dark?Color.FromArgb(33,32,39):Color.FromArgb(226,224,234);Surface=dark?Color.FromArgb(45,43,52):Color.FromArgb(248,247,252);Card=dark?Color.FromArgb(55,53,65):Color.White;Stroke=dark?Color.FromArgb(75,72,88):Color.FromArgb(219,215,230);Text=dark?Color.FromArgb(242,239,250):Color.FromArgb(39,35,52);Muted=dark?Color.FromArgb(179,174,193):Color.FromArgb(107,101,122);Mint=Color.FromArgb(245,116,68);Lilac=dark?Color.FromArgb(188,171,255):Color.FromArgb(101,65,204);Hover=dark?Color.FromArgb(65,61,80):Color.FromArgb(231,226,245);}
    public static GraphicsPath Shape(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90); p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    public static void Round(Graphics g, RectangleF r, Color fill, float radius=12, bool stroke=false)
    {
        using var p = Shape(r, radius); using var b = new SolidBrush(fill); g.FillPath(b,p); if(stroke){using var pen=new Pen(Stroke);g.DrawPath(pen,p);}
    }
    public static void TextAt(Graphics g, string value, Rectangle r, float size, Color color, bool bold=false)
    {
        using var f = new Font("Segoe UI", size, bold?FontStyle.Bold:FontStyle.Regular);
        TextRenderer.DrawText(g,value,f,r,color,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
    }
    public static void Icon(Graphics g, string name, Rectangle r, Color color)
    {
        var save=g.Save();g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/24f,r.Height/24f);
        using var p = new Pen(color,1.65f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        void Line(float x,float y,float a,float b)=>g.DrawLine(p,x,y,a,b);
        void Path(params PointF[] points)=>g.DrawLines(p,points);
        switch(name){
            case "back":Path(new(15,5),new(8,12),new(15,19));break;
            case "forward":Path(new(9,5),new(16,12),new(9,19));break;
            case "down":Path(new(6,9),new(12,15),new(18,9));break;
            case "reload":g.DrawArc(p,5,5,14,14,30,290);Path(new(16,3),new(20,6),new(16,9));break;
            case "home":Path(new(3,11),new(12,4),new(21,11));Path(new(6,10),new(6,20),new(18,20),new(18,10));break;
            case "search":g.DrawEllipse(p,4,4,12,12);Line(15,15,21,21);break;
            case "plus":Line(12,5,12,19);Line(5,12,19,12);break;
            case "close":Line(6,6,18,18);Line(6,18,18,6);break;
            case "shield":Path(new(12,3),new(20,6),new(19,14),new(16,19),new(12,22),new(8,19),new(5,14),new(4,6),new(12,3));Path(new(8,12),new(11,15),new(16,9));break;
            case "sidebar":g.DrawRectangle(p,3,4,18,16);Line(9,4,9,20);break;
            case "split":g.DrawRectangle(p,3,4,18,16);Line(12,4,12,20);break;
            case "download":Path(new(12,3),new(12,15),new(7,10));Path(new(12,15),new(17,10));Path(new(4,17),new(4,21),new(20,21),new(20,17));break;
            case "extensions":Path(new(4,5),new(9,5),new(9,2),new(14,2),new(14,5),new(20,5),new(20,11),new(23,11),new(23,16),new(20,16),new(20,21),new(4,21),new(4,16),new(1,16),new(1,11),new(4,11),new(4,5));break;
            case "notes":Path(new(5,3),new(16,3),new(20,7),new(20,21),new(5,21),new(5,3));Line(9,10,16,10);Line(9,14,16,14);break;
            case "bookmark-filled":using(var fill=new SolidBrush(color)){g.FillPolygon(fill,[new PointF(6,3),new PointF(18,3),new PointF(18,21),new PointF(12,17),new PointF(6,21)]);}break;
            case "bookmark":Path(new(6,3),new(18,3),new(18,21),new(12,17),new(6,21),new(6,3));break;
            case "sleep":Path(new(9,6),new(17,6),new(9,16),new(17,16));break;
            case "clock":g.DrawEllipse(p,3,3,18,18);Path(new(12,7),new(12,12),new(16,14));break;
            case "menu":Line(5,6,19,6);Line(5,12,19,12);Line(5,18,19,18);break;
            case "profile":g.DrawEllipse(p,4,4,16,16);g.DrawEllipse(p,9,7,6,6);g.DrawArc(p,7,14,10,8,190,160);break;
            case "settings":g.DrawEllipse(p,4,4,16,16);g.DrawEllipse(p,9,9,6,6);break;
            case "brand":Path(new(4,20),new(12,3),new(20,20));Line(8,14,16,14);g.DrawArc(p,2,8,20,15,10,150);break;
            case "inspect":Line(5,19,5,12);Line(12,19,12,5);Line(19,19,19,9);break;
            default:g.DrawEllipse(p,5,5,14,14);Line(12,8,12,16);Line(8,12,16,12);break;
        }g.Restore(save);
    }
}
internal sealed class ChromeButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = "";
    private bool hover;
    public ChromeButton(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent?.BackColor??Visual.Base);
        Visual.Round(g,new Rectangle(1,1,Width-3,Height-3),hover?Visual.Hover:BackColor,9);
        var c=Enabled?ForeColor:Visual.Muted;
        if(Glyph.Length>0)Visual.Icon(g,Glyph,new Rectangle((Width-19)/2,(Height-19)/2,19,19),c);
        else TextRenderer.DrawText(g,Text,Font,ClientRectangle,c,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        if(Focused){using var p=new Pen(Visual.Mint);g.DrawRectangle(p,3,3,Width-7,Height-7);}
    }
}
// A page host has no native tab headers underneath the painted strip.
internal sealed class TabDeck : Panel
{
    internal sealed class PageCollection(TabDeck owner):System.Collections.ObjectModel.Collection<Panel>
    {
        protected override void InsertItem(int index,Panel item){base.InsertItem(index,item);item.Dock=DockStyle.Fill;item.Visible=false;owner.Controls.Add(item);if(owner.SelectedTab==null)owner.SelectedTab=item;}
        protected override void RemoveItem(int index){var item=this[index];bool active=owner.SelectedTab==item;Panel? next=active&&Count>1?this[index+1<Count?index+1:index-1]:null;if(active)owner.SelectedTab=next;owner.Controls.Remove(item);base.RemoveItem(index);}
    }
    internal PageCollection TabPages {get;}
    private Panel? selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Panel? SelectedTab {get=>selected;set{if(selected==value)return;BeforeSelectionChanged?.Invoke(this,EventArgs.Empty);if(selected!=null)selected.Visible=false;selected=value;if(value!=null){value.Visible=true;value.BringToFront();}SelectedIndexChanged?.Invoke(this,EventArgs.Empty);}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex {get=>selected==null?-1:TabPages.IndexOf(selected);set=>SelectedTab=value>=0&&value<TabPages.Count?TabPages[value]:null;}
    internal int TabCount=>TabPages.Count;
    internal event EventHandler? SelectedIndexChanged;
    internal event EventHandler? BeforeSelectionChanged;
    public TabDeck(){TabPages=new PageCollection(this);DoubleBuffered=true;}
}
public sealed partial class BrowserForm
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);if(OperatingSystem.IsWindowsVersionAtLeast(10,0,17763)){int dark=1;DwmSetWindowAttribute(Handle,20,ref dark,sizeof(int));}}
    private TableLayoutPanel shell=null!;
    private Panel pageHost=null!;
    private bool refreshing;
    private readonly Stack<TabSnapshot> closedTabs=[];
    private void BuildWorkspaceUi()=>BuildBrowserChrome();
    private Panel CreateDashboard(BrowserTab owner){var p=new Panel{Dock=DockStyle.Fill,BackColor=Visual.Base};p.Controls.Add(new StartPage(this,owner){Dock=DockStyle.Fill});return p;}
    private void RefreshSurfaces(){if(horizontalTabs==null||refreshing||closing)return;refreshing=true;horizontalTabs.Invalidate();favorites?.Invalidate();RefreshExtensionPins();if(Current?.AtHome==true)foreach(Control c in Current.Home.Controls)c.Invalidate();if(reload is ChromeButton b)b.Glyph=Current?.Loading==true?"close":"reload";refreshing=false;}
    private void SelectPage(BrowserTab p){tabs.SelectedTab=p.Container;Sync();QueueSave();}
    private void TabMenu(BrowserTab p){var m=BrowserContextMenu();m.Items.Add(p.Pinned?"Unpin tab":"Pin tab",null,(_,_)=>{p.Pinned=!p.Pinned;RefreshSurfaces();QueueSave();});m.Items.Add("Reload",null,(_,_)=>{SelectPage(p);Reload();});m.Items.Add("Duplicate",null,(_,_)=>NewTab(p.Url));m.Items.Add("Close tab",null,(_,_)=>CloseTab(p));m.Items.Add("Close tabs to the right",null,(_,_)=>CloseRight(p));m.Items.Add("Close other tabs",null,(_,_)=>CloseOthers(p));m.Items.Add("Reopen closed tab",null,(_,_)=>ReopenClosed());m.Show(Cursor.Position);}
    private void ReopenClosed(){if(closedTabs.TryPop(out var x)){NewTab(x.Url);Current!.Pinned=x.Pinned;RefreshSurfaces();}}
    private void SleepInactive(bool confirm=true){var targets=pages.Where(p=>p!=Current&&!p.Pinned&&!p.Audible&&!p.Loading&&p.Web!=null&&p.InternalKey.Length==0).ToArray();if(targets.Length==0)return;if(confirm&&MessageBox.Show(this,$"Release {targets.Length} inactive page views? Tabs reload when selected. Unsaved forms and scroll positions can be lost.","Sleep inactive tabs",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;foreach(var p in targets){ReleaseView(p);}RefreshSurfaces();}
    private async Task RunBenchmark(string output,BrowserTab page)
    {
        var ready=Program.Startup.Elapsed.TotalMilliseconds;await Task.Delay(3000);GC.Collect();GC.WaitForPendingFinalizers();
        Dictionary<int,(long Private,long Working,double Cpu,string Kind)> Snapshot(){
            var ids=new Dictionary<int,string>{{Environment.ProcessId,"Shell"}};
            foreach(var info in page.Web!.CoreWebView2.Environment.GetProcessInfos())ids[info.ProcessId]=info.Kind.ToString();
            var result=new Dictionary<int,(long,long,double,string)>();foreach(var pair in ids)try{using var p=Process.GetProcessById(pair.Key);result[pair.Key]=(p.PrivateMemorySize64,p.WorkingSet64,p.TotalProcessorTime.TotalMilliseconds,pair.Value);}catch{}return result;
        }
        var before=Snapshot();var timer=Stopwatch.StartNew();await Task.Delay(3000);var after=Snapshot();using var root=Process.GetCurrentProcess();root.Refresh();
        File.WriteAllText(Path.Combine(output,"performance.json"),JsonSerializer.Serialize(new{Version="0.6.0",NativeWindowShownMs=shownMs,EngineReadyMs=ready,Scenario="Native new-tab UI, one warmed blank WebView2; default bundled extensions where present",RootPrivateBytes=root.PrivateMemorySize64,RootWorkingSetBytes=root.WorkingSet64,TotalPrivateBytes=after.Values.Sum(x=>x.Private),SumWorkingSetBytes=after.Values.Sum(x=>x.Working),KnownProcessCount=after.Count,ManagedBytes=GC.GetTotalMemory(false),IdleRootCpuMs=after.GetValueOrDefault(Environment.ProcessId).Cpu-before.GetValueOrDefault(Environment.ProcessId).Cpu,IdleKnownProcessCpuMs=after.Sum(x=>Math.Max(0,x.Value.Cpu-(before.TryGetValue(x.Key,out var previous)?previous.Cpu:0))),SampleMs=timer.ElapsedMilliseconds,PageViews=pages.Count(x=>x.Web!=null),NativeControls=CountControls(this),Processes=after.Select(x=>new{Pid=x.Key,Kind=x.Value.Kind,PrivateBytes=x.Value.Private,WorkingSetBytes=x.Value.Working}),Note="One same-runner sample. Main-entry timers exclude EXE extraction. Known WebView2 process snapshots included; exited-process CPU is not traced. Summed working sets can count shared pages twice. Not an internet-wide browser ranking."},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static int CountControls(Control c)=>1+c.Controls.Cast<Control>().Sum(CountControls);
}
