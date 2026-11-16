using System.Runtime.InteropServices;
namespace AetherOnePC;
public sealed partial class BrowserForm
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    private static IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
    private async Task TestInteractions(string output,BrowserTab original)
    {
        SelectPage(original);Activate();bookmarkButton!.PerformClick();await Task.Delay(150);if(bookmarkPopup?.Visible!=true||bookmarkButton.Glyph!="bookmark-filled")throw new Exception("Bookmark save feedback missing");CaptureWindow(Path.Combine(output,"AetherOne-Bookmark.png"));
        var popupBody=((ToolStripControlHost)bookmarkPopup.Items[0]).Control;popupBody.Controls.OfType<TextBox>().Single().Text="My saved fixture";popupBody.Controls.OfType<Button>().Single(x=>x.Text=="Done").PerformClick();if(!state.Bookmarks.Any(x=>x.Url==original.Url&&x.Title=="My saved fixture"))throw new Exception("Bookmark rename failed");
        OpenInternal("bookmarks");var manager=Current!;var open=Descendants(manager.NativePage!).OfType<Button>().First(x=>x.Text=="Open");int count=pages.Count;open.PerformClick();await WaitFor(async()=>pages.Count==count+1&&Current?.Web?.CoreWebView2!=null&&!Current.Loading);if(Current!.Url!=original.Url)throw new Exception("Bookmark Open action failed");CloseTab(Current);CloseTab(manager);SelectPage(original);
        ShowBrowserMenu();await Task.Delay(150);if(!openPopups.Any(x=>x.Visible))throw new Exception("Browser menu did not open");var point=original.Web!.PointToScreen(new Point(100,200));SetCursorPos(point.X,point.Y);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);await WaitFor(()=>Task.FromResult(!openPopups.Any(x=>x.Visible))); 
        OpenInternal("settings/appearance");var appearance=Current!;var toggle=Descendants(appearance.NativePage!).OfType<ToggleSwitch>().First();bool old=state.ShowFavorites;toggle.TestToggle();if(state.ShowFavorites==old||favorites!.Visible==old)throw new Exception("Bookmarks visibility switch did not work");toggle.TestToggle();var theme=Descendants(appearance.NativePage!).OfType<ComboBox>().First();theme.SelectedItem="Light";if(state.DarkTheme)throw new Exception("Theme selection failed");Descendants(appearance.NativePage!).OfType<ComboBox>().First().SelectedItem="Dark";CloseTab(appearance);SelectPage(original);
        var made=new List<BrowserTab>();foreach(var title in new[]{"A","B","C","D"}){NewTab();Current!.Title=title;made.Add(Current);}
        SelectPage(made[1]);horizontalTabs!.Refresh();var r=horizontalTabs.hits.First(x=>x.Page==made[1]).Rect;horizontalTabs.TestClick(new Point(r.Right-4,r.Top+1));if(!pages.Contains(made[1]))throw new Exception("Tab closed outside close control");horizontalTabs.Refresh();r=horizontalTabs.hits.First(x=>x.Page==made[1]).Rect;horizontalTabs.TestClick(new Point(r.Right-15,22));if(pages.Contains(made[1])||Current!=made[2])throw new Exception("Close did not prefer the right-hand neighbor");CloseRight(made[2]);if(pages.Contains(made[3])||!pages.Contains(made[0]))throw new Exception("Close tabs right removed wrong tabs");CloseOthers(original);if(pages.Any(x=>x!=original&&!x.Pinned))throw new Exception("Close other tabs failed");SelectPage(original);
        File.WriteAllText(Path.Combine(output,"interaction-tests.txt"),"PASS: bookmark icon and confirmation popup, rename, actual bookmark Open button, outside click on WebView2 dismisses menu, tab edge outside close target cannot close, right-neighbor activation, close-right and close-other actions.\n");
    }
}
