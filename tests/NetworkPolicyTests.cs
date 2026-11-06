using System;
using System.Collections.Generic;
namespace Luna {
 internal static class NetworkPolicyTests {
  internal static void Run(Action<string,Action> test,Action<bool> assert) {
   test("Routing policy snapshots and exact app decisions",delegate {
    var p=new NetworkPolicy {Mode="include",KillSwitch=true,Applications=new List<string>{@"C:\Apps\Browser.exe"}};
    var copy=p.Snapshot(); p.Applications.Clear(); assert(copy.Applications.Count==1 && copy.KillSwitch);
    assert(copy.UsesVpn(@"c:\apps\browser.EXE") && !copy.UsesVpn(@"C:\Other\Browser.exe"));
    copy.Mode="exclude"; assert(!copy.UsesVpn(@"C:\Apps\Browser.exe") && copy.UsesVpn(@"C:\Other\Browser.exe"));
   });
   test("Routing policy rejects relative network wildcard and invalid modes",delegate {
    foreach(string path in new[]{"app.exe",@"\\server\app.exe",@"C:\Apps\*.exe",@"C:\Apps\app.dll"}) {
     bool rejected=false; try { NetworkPolicy.NormalizePath(path); } catch(SafeException) { rejected=true; } assert(rejected);
    }
    bool invalid=false; try { new NetworkPolicy { Mode="bad" }.Snapshot(); } catch(SafeException) { invalid=true; } assert(invalid);
   });
   test("Split routing keeps health checks on proxy and defaults correctly",delegate {
    var baseline=Json.Write(Configuration.Obj("route",Configuration.Obj()));
    foreach(string mode in new[]{"all","include","exclude"}) {
     var p=new NetworkPolicy {Mode=mode,Applications=new List<string>{@"C:\Apps\Browser.exe"}};
     string config=NetworkPolicy.Apply(baseline,p,@"C:\doomra\doomraVPN.exe");
     var route=(Dictionary<string,object>)Json.Read<Dictionary<string,object>>(config)["route"];
     assert((string)route["final"]==(mode=="include"?"direct":"proxy")); assert(config.Contains("doomraVPN.exe") && config.Contains("hijack-dns"));
    }
   });
  }
 }
}
