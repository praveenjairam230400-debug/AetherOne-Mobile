using System.Runtime.InteropServices;
namespace AetherOnePC;
public sealed partial class BrowserForm
{
    private readonly List<ToolStripDropDown> openPopups=[];
    private IntPtr mouseHook;
    private MouseHookProc? mouseHookProc;
    private delegate IntPtr MouseHookProc(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)]private struct MousePoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern IntPtr SetWindowsHookEx(int id,MouseHookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string? name);
    private void WatchPopup(ToolStripDropDown popup){popup.AutoClose=true;popup.Opened+=(_,_)=>{if(!openPopups.Contains(popup))openPopups.Add(popup);UpdatePopupHook();};popup.Closed+=(_,_)=>{openPopups.Remove(popup);if(bookmarkPopup==popup)bookmarkPopup=null;UpdatePopupHook();if(!closing)BeginInvoke((Action)(()=>popup.Dispose()));};}
    private void UpdatePopupHook(){bool needed=openPopups.Any(p=>p.Visible)||suggestions?.Visible==true;if(needed&&mouseHook==IntPtr.Zero){mouseHookProc=OnPopupMouse;mouseHook=SetWindowsHookEx(14,mouseHookProc,GetModuleHandle(null),0);}else if(!needed&&mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}}
    private IntPtr OnPopupMouse(int code,IntPtr message,IntPtr data){if(code>=0&&(message==(IntPtr)0x201||message==(IntPtr)0x204||message==(IntPtr)0x207)){var p=Marshal.PtrToStructure<MousePoint>(data);var point=new Point(p.X,p.Y);bool inside=openPopups.Any(x=>x.Visible&&x.Bounds.Contains(point))||suggestions?.Visible==true&&(suggestions.Bounds.Contains(point)||address.RectangleToScreen(address.ClientRectangle).Contains(point));if(!inside&&!closing)BeginInvoke((Action)DismissPopups);}return CallNextHookEx(mouseHook,code,message,data);}
    private void DismissPopups(){foreach(var p in openPopups.ToArray())p.Close(ToolStripDropDownCloseReason.AppClicked);suggestTimer.Stop();predictCancellation?.Cancel();suggestions?.Hide();UpdatePopupHook();}
    private ChromeButton? bookmarkButton;
    private ToolStripDropDown? bookmarkPopup;
    private bool IsBookmarked=>Current is {AtHome:false} p&&state.Bookmarks.Any(x=>x.Url==p.Url);
    private void UpdateBookmarkIcon(){if(bookmarkButton==null)return;bookmarkButton.Glyph=IsBookmarked?"bookmark-filled":"bookmark";bookmarkButton.ForeColor=IsBookmarked?Visual.Lilac:Visual.Text;bookmarkButton.Invalidate();favorites?.Invalidate();}
    private void BookmarkWithFeedback(){var p=Current;if(p==null||p.AtHome||!IsWeb(p.Url)){OpenInternal("bookmarks");return;}Bookmark();ShowBookmarkPopup(p);}
    private void ShowBookmarkPopup(BrowserTab p){bookmarkPopup?.Close();var popup=new ToolStripDropDown{Padding=new Padding(1),BackColor=Visual.Surface,DropShadowEnabled=true};bookmarkPopup=popup;var body=new Panel{Size=new Size(332,180),BackColor=Visual.Surface,Padding=new Padding(18)};var title=new Label{Text="Bookmark saved",ForeColor=Visual.Lilac,Font=new Font("Segoe UI",13,FontStyle.Bold),Location=new Point(17,14),Size=new Size(295,30)};var name=new TextBox{Text=state.Bookmarks.First(x=>x.Url==p.Url).Title,Location=new Point(18,59),Width=294,BackColor=Visual.Card,ForeColor=Visual.Text,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Segoe UI",10)};var done=ActionButton("Done",()=>{var item=state.Bookmarks.FirstOrDefault(x=>x.Url==p.Url);if(item!=null)item.Title=string.IsNullOrWhiteSpace(name.Text)?p.Title:name.Text.Trim();QueueSave();UpdateBookmarkIcon();popup.Close();},95);done.Location=new Point(217,112);var remove=ActionButton("Remove",()=>{state.Bookmarks.RemoveAll(x=>x.Url==p.Url);QueueSave();UpdateBookmarkIcon();popup.Close();},95);remove.Location=new Point(18,112);body.Controls.AddRange([title,name,done,remove]);var host=new ToolStripControlHost(body){AutoSize=false,Size=body.Size,Margin=Padding.Empty,Padding=Padding.Empty};popup.Items.Add(host);WatchPopup(popup);popup.Show((Control?)bookmarkButton??address,new Point(-290,36));name.SelectAll();}
    private BrowserTab[] VisualTabs()=>pages.OrderByDescending(x=>x.Pinned).ToArray();
    private void CloseRight(BrowserTab p){var all=VisualTabs();int index=Array.IndexOf(all,p);if(index<0)return;foreach(var tab in all.Skip(index+1).Where(x=>!x.Pinned).Reverse().ToArray())CloseTab(tab);}
    private void CloseOthers(BrowserTab p){foreach(var tab in pages.Where(x=>x!=p&&!x.Pinned).Reverse().ToArray())CloseTab(tab);SelectPage(p);}
}
