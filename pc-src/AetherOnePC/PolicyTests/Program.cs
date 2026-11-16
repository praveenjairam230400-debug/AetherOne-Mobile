using AetherOnePC;
var count = 0;
void Check(bool condition, string title) { if (!condition) throw new Exception(title); count++; }
Check(Policy.Resolve("example.org/path") == "https://example.org/path", "bare host");
Check(Policy.Resolve("http://example.org/") == "https://example.org/", "HTTPS upgrade");
Check(Policy.Resolve("https://example.org:8443/") == "https://example.org:8443/", "custom port");
Check(Policy.Resolve("javascript:alert(1)") is null, "script scheme rejected");
Check(Policy.Resolve("file:///C:/secret") is null, "local files rejected");
Check(Policy.Resolve("data:text/html,test") is null, "data scheme rejected");
Check(Policy.Resolve("about:blank") is null, "about scheme rejected");
Check(Policy.Resolve("AML case notes")!.StartsWith("https://www.google.com/search?q="), "search routing");
Check(Policy.ShouldBlock("secure.doubleclick.net", "example.org"), "ad subdomain");
Check(Policy.ShouldBlock("DOUBLECLICK.NET.", "example.org"), "normalization");
Check(!Policy.ShouldBlock("notdoubleclick.net", "example.org"), "suffix boundary");
Check(!Policy.ShouldBlock("doubleclick.net.example.org", "example.com"), "suffix spoof");
Check(!Policy.ShouldBlock("doubleclick.net", "doubleclick.net"), "direct visit");
Check(!Policy.ShouldBlock("secure.doubleclick.net", "doubleclick.net"), "first party");
Check(!Policy.ShouldBlock(null, "example.org"), "null host");
Check(Policy.Resolve("test search","Brave")!.StartsWith("https://search.brave.com/search?q="),"Brave search");
Check(Policy.Resolve("test search","Bing")!.StartsWith("https://www.bing.com/search?q="),"Bing search");
Check(Policy.Resolve("test search","DuckDuckGo")!.StartsWith("https://duckduckgo.com/?q="),"DuckDuckGo search");
Check(PredictionParser.Parse("[\"tea\",[\"tea recipe\",\"tea shop\",\"tea recipe\"]]").Length==2,"prediction parsing and deduplication");
Check(CrxPackage.StoreId("https://evil.test/detail/eimadpbcbfnmbkopoojfekhnkhdbieeh")==null,"store origin restriction");
Check(CrxPackage.StoreId("https://chromewebstore.google.com/detail/dark-reader/eimadpbcbfnmbkopoojfekhnkhdbieeh")=="eimadpbcbfnmbkopoojfekhnkhdbieeh","store listing identity");
using var rsa=System.Security.Cryptography.RSA.Create(2048);var key=rsa.ExportSubjectPublicKeyInfo();string id=CrxPackage.IdForKey(key);
byte[] Field(int tag,byte[] bytes){var b=new List<byte>();void V(ulong n){while(n>127){b.Add((byte)((n&127)|128));n>>=7;}b.Add((byte)n);}V((ulong)((tag<<3)|2));V((ulong)bytes.Length);b.AddRange(bytes);return b.ToArray();}
byte[] zip;using(var memory=new MemoryStream()){using(var archive=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Create,true)){using var w=new StreamWriter(archive.CreateEntry("manifest.json").Open());w.Write("{\"manifest_version\":3,\"name\":\"Fixture\",\"version\":\"1.0\"}");}zip=memory.ToArray();}
var signed=Field(1,System.Security.Cryptography.SHA256.HashData(key).Take(16).ToArray());byte[] data=System.Text.Encoding.ASCII.GetBytes("CRX3 SignedData\0").Concat(BitConverter.GetBytes(signed.Length)).Concat(signed).Concat(zip).ToArray();var sig=rsa.SignData(data,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1);var header=Field(2,Field(1,key).Concat(Field(2,sig)).ToArray()).Concat(Field(10000,signed)).ToArray();var crx=System.Text.Encoding.ASCII.GetBytes("Cr24").Concat(BitConverter.GetBytes(3)).Concat(BitConverter.GetBytes(header.Length)).Concat(header).Concat(zip).ToArray();
Check(CrxPackage.Verify(crx,id).Id==id,"signed CRX3 verifies");bool rejected=false;var changed=(byte[])crx.Clone();changed[^1]^=1;try{CrxPackage.Verify(changed,id);}catch(InvalidDataException){rejected=true;}Check(rejected,"tampered CRX rejected");rejected=false;try{CrxPackage.Verify(crx,new string('a',32));}catch(InvalidDataException){rejected=true;}Check(rejected,"wrong listing identity rejected");
string temp=Path.Combine(Path.GetTempPath(),"Aether-Policy-"+Guid.NewGuid().ToString("N"));try{CrxPackage.Extract(CrxPackage.Verify(crx,id),temp);Check(System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(temp,"manifest.json"))).RootElement.GetProperty("key").GetString()==Convert.ToBase64String(key),"verified key retained for stable extension ID");}finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}

Check(ResourcePolicy.Budget(8L*1024*ResourcePolicy.MiB,false)==256*ResourcePolicy.MiB,"8 GiB soft budget");
Check(ResourcePolicy.Budget(64L*1024*ResourcePolicy.MiB,false)==512*ResourcePolicy.MiB,"soft budget upper bound");
Check(ResourcePolicy.Budget(1024*ResourcePolicy.MiB,false)==256*ResourcePolicy.MiB,"soft budget lower bound");
Check(ResourcePolicy.Budget(8L*1024*ResourcePolicy.MiB,true)==192*ResourcePolicy.MiB,"pressure reduces budget");
Check(ResourcePolicy.Pressure(500*ResourcePolicy.MiB,8L*1024*ResourcePolicy.MiB),"low available memory detected");
Check(!ResourcePolicy.Pressure(4L*1024*ResourcePolicy.MiB,8L*1024*ResourcePolicy.MiB),"healthy available memory");
Check(!ResourcePolicy.NeedsReclaim(550*ResourcePolicy.MiB,512*ResourcePolicy.MiB),"hysteresis avoids borderline churn");
Check(ResourcePolicy.NeedsReclaim(600*ResourcePolicy.MiB,512*ResourcePolicy.MiB),"over-budget reclaim trigger");
Check(!ResourcePolicy.Eligible(TimeSpan.FromSeconds(10),true),"recent background tabs protected even under pressure");
Check(ResourcePolicy.Eligible(TimeSpan.FromSeconds(30),true),"pressure permits older candidate");
Check(!ResourcePolicy.Eligible(TimeSpan.FromSeconds(30),false),"normal pressure retains recent tab");
Check(ResourcePolicy.DiscardPriority(TimeSpan.FromMinutes(5),500,1)>ResourcePolicy.DiscardPriority(TimeSpan.FromMinutes(5),10000,10),"costly frequently revisited page retained longer");
Console.WriteLine($"PASS: {count} policy, prediction and signed-package checks");
