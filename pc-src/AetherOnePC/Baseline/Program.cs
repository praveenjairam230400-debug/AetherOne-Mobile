namespace AetherOnePC;
internal static class Program
{
    internal static readonly System.Diagnostics.Stopwatch Startup = System.Diagnostics.Stopwatch.StartNew();
    [STAThread]
    static void Main(string[] args)
    {
        Startup.Restart();
        ApplicationConfiguration.Initialize();
        int profileIndex=Array.IndexOf(args,"--profile");string id=profileIndex>=0&&profileIndex+1<args.Length?args[profileIndex+1]:"default";if(id!="default"&&!Guid.TryParseExact(id,"N",out _))id="default";
        Application.Run(new BrowserForm(args.Contains("--smoke-test"),id));
    }
}
