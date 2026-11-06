using System;
using System.IO;
using System.Threading.Tasks;
namespace Luna {
 // Explicitly invoked only with permission for one brief real TUN test.
 // Reuses the production IPC, elevation, job and core lifecycle unchanged.
 static class NetworkProbe {
  static readonly string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"network-probe-safe.txt");
  [STAThread] static int Main(string[] args) {
   if(args.Length==3 && args[0]=="--worker") { int pid; return int.TryParse(args[2],out pid)?Worker.Run(args[1],pid):1; }
   if(args.Length!=1 || args[0]!="--i-accept-network-change") return 2;
   try { return Run().GetAwaiter().GetResult(); }
   catch { File.AppendAllText(report,"Probe failed; sensitive details suppressed.\r\n"); return 1; }
  }
  static async Task<int> Run() {
   System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
   File.WriteAllText(report,"One brief TUN test. No subscription fetch, no raw core logs.\r\n");
   var settings=Storage.Load(Path.Combine(Storage.Folder,"settings.dat"));
   if(settings.Selected<0 || settings.Selected>=settings.Servers.Count) { File.AppendAllText(report,"No selected server.\r\n"); return 1; }
   using(var connection=new Connection()) {
    bool connected=false;
    connection.Status+=state=> {
     string category=CoreFailure.Category(state);
     lock(report) File.AppendAllText(report,category+CoreFailure.ExitSuffix(state)+"\r\n");
     if(category=="CONNECTED") { connected=true; connection.Stop(); }
    };
    Task session=connection.Start(settings.Servers[settings.Selected],false);
    if(await Task.WhenAny(session,Task.Delay(15000))!=session) { File.AppendAllText(report,"Test time limit reached; stopping.\r\n"); connection.Stop(); }
    try { await session; } catch { File.AppendAllText(report,"Session failed; sensitive details suppressed.\r\n"); }
    File.AppendAllText(report,"Helper exited; test finished.\r\n");
    return connected?0:1;
   }
  }
 }
}
