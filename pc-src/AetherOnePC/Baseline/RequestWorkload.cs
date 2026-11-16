using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace AetherOnePC;
public sealed partial class BrowserForm
{
    // Set only during --smoke-test; production HTTP upgrades/certificates unchanged.
    private string requestFixtureOrigin="";
    private sealed class RequestFixtureServer:IAsyncDisposable
    {
        private readonly TcpListener listener=new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource stop=new();
        private readonly List<Task> responses=[];
        private readonly Task loop;
        internal string Origin {get;}
        internal RequestFixtureServer(){listener.Start();Origin="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;loop=Task.Run(AcceptLoop);}
        private async Task AcceptLoop(){try{while(!stop.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(stop.Token);responses.Add(Respond(client));}}catch(OperationCanceledException){}catch(SocketException) when(stop.IsCancellationRequested){}}
        private async Task Respond(TcpClient client){using(client)try{
            client.NoDelay=true;using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
            string first=await reader.ReadLineAsync(stop.Token)??"";for(int i=0;i<64;i++){string? line=await reader.ReadLineAsync(stop.Token);if(string.IsNullOrEmpty(line))break;}
            string path=first.Split(' ').ElementAtOrDefault(1)??"/";bool page=path.StartsWith("/index.html?"),asset=path.StartsWith("/asset.txt?");
            string body=page?"""
                <!doctype html><title>Loopback request workload</title><h1>Request workload</h1><script>
                window.ready=false;window.started=performance.now();
                Promise.all(Array.from({length:64},(_,i)=>fetch('/asset.txt?item='+i+'&run='+location.search,{cache:'no-store'}).then(r=>{if(!r.ok)throw Error(r.status);return r.text()}))).then(items=>{window.completed=items.length;window.duration=performance.now()-window.started;window.ready=true});
                </script>
                """:asset?"Aether resource fixture":"Not found";
            byte[] data=Encoding.UTF8.GetBytes(body);string code=page||asset?"200 OK":"404 Not Found";
            byte[] headers=Encoding.ASCII.GetBytes($"HTTP/1.1 {code}\r\nContent-Type: {(page?"text/html":"text/plain")}; charset=utf-8\r\nContent-Length: {data.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers,stop.Token);await stream.WriteAsync(data,stop.Token);
        }catch(OperationCanceledException){}catch(IOException){} }
        public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await loop;await Task.WhenAll(responses);stop.Dispose();}
    }
    private async Task RunRequestWorkload(string output)
    {
        if(!smoke)throw new InvalidOperationException("Fixture is test-only");
        await using var server=new RequestFixtureServer();requestFixtureOrigin=server.Origin;BrowserTab? p=null;
        try{NewTab();p=Current!;await Ensure(p);var samples=new List<object>();
            bool selective=typeof(BrowserForm).Assembly.GetName().Version!.Minor>=6;
            for(int i=0;i<5;i++){
                await Navigate(requestFixtureOrigin+"/index.html?run="+i,p);
                await WaitFor(async()=>!p.Loading&&await p.Web!.CoreWebView2.ExecuteScriptAsync("window.ready")=="true");
                if(await p.Web!.CoreWebView2.ExecuteScriptAsync("window.completed")!="64")throw new Exception("Request fixture did not finish all resources");
                if(selective?p.Observed!=0:p.Observed<64)throw new Exception("Request fixture did not exercise expected interception path");
                samples.Add(new{Iteration=i,AppInterceptedResources=p.Observed,NavigationCompletedMs=p.Finished,ResourceCompletionMs=JsonSerializer.Deserialize<double>(await p.Web.CoreWebView2.ExecuteScriptAsync("window.duration"))});
            }
            File.WriteAllText(Path.Combine(output,"request-workload.json"),JsonSerializer.Serialize(new{ResourcesPerIteration=64,Transport="Real HTTP loopback TCP server; no virtual-host mapping",Samples=samples,Note="Five sequential local navigations, not internet page-load timings. Includes loopback-server work in app process. Test-only origin exemption, no production HTTP or certificate change. Observed counts app interception, not total engine work."},new JsonSerializerOptions{WriteIndented=true}));
        }finally{if(p!=null&&pages.Contains(p))CloseTab(p);requestFixtureOrigin="";}
    }
}
