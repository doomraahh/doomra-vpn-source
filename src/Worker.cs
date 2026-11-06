using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
namespace Luna {
 public static class Wire {
  // Bounded framing prevents a peer from forcing an unbounded ReadLine allocation.
  public static string Read(Stream stream) {
   byte[] size=new byte[4]; Exact(stream,size,4); int count=BitConverter.ToInt32(size,0);
   if(count<0 || count>65536) throw new SafeException("Неверный размер сообщения IPC.");
   byte[] data=new byte[count]; Exact(stream,data,count); return new UTF8Encoding(false,true).GetString(data);
  }
  static void Exact(Stream stream,byte[] data,int count) { int offset=0; while(offset<count) { int n=stream.Read(data,offset,count-offset); if(n==0) throw new EndOfStreamException(); offset+=n; } }
  public static void Send(Stream stream,string value) { byte[] data=Encoding.UTF8.GetBytes(value); if(data.Length>65536) throw new SafeException("Слишком большой профиль IPC."); lock(stream) { byte[] size=BitConverter.GetBytes(data.Length); stream.Write(size,0,4); stream.Write(data,0,data.Length); stream.Flush(); } }
 }
 public sealed class Connection : IDisposable {
  NamedPipeServerStream pipe;
  Process worker;
  volatile bool stopping;
  public bool Cancelled { get { return stopping; } }
  public event Action<string> Status;
  void Emit(string status) { var handler=Status; if(handler!=null) handler(status); }
  public Task Start(Server server,bool autoReconnect) { return Start(server,autoReconnect,new NetworkPolicy()); }
  public async Task Start(Server server,bool autoReconnect,NetworkPolicy network) {
   network=(network??new NetworkPolicy()).Snapshot();
   Subscriptions.Validate(server);
   if(stopping) return;
   string name="LunaVPN-"+Guid.NewGuid().ToString("N");
   var security=new PipeSecurity(); security.SetAccessRuleProtection(true,false); security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
   security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
   bool installed=VpnService.IsInstalled;
   if(installed) security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
   pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
   Exception failure=null; NamedPipeClientStream broker=null;
   try {
    uint expected;
    if(installed) {
     broker=await Task.Run(()=>VpnService.Connect());
     var service=broker;
     expected=await Task.Run(()=> {
      Wire.Send(service,name);
      uint id;
      if(!uint.TryParse(Wire.Read(service),out id) || id==0) throw new SafeException("Служба не смогла запустить подключение.");
      return id;
     });
    } else {
     worker=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--worker "+name+" "+Process.GetCurrentProcess().Id) { UseShellExecute=true,Verb="runas",WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,WindowStyle=ProcessWindowStyle.Hidden });
     expected=(uint)worker.Id;
    }
    var waiting=Task.Factory.FromAsync(pipe.BeginWaitForConnection,pipe.EndWaitForConnection,null);
    if(await Task.WhenAny(waiting,Task.Delay(30000))!=waiting) throw new SafeException("Процесс TUN не ответил за 30 с.");
    await waiting;
    uint peer; if(!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle,out peer) || peer!=expected) throw new SafeException("Проверка процесса TUN не пройдена.");
    if(stopping) throw new SafeException("Подключение отменено.");
    Wire.Send(pipe,Json.Write(new ConnectRequest { Server=server,AutoReconnect=autoReconnect,Network=network }));
    await Task.Run(()=>ReadStatuses());
   } catch(System.ComponentModel.Win32Exception) { failure=new SafeException("Повышение прав отменено или процесс TUN не запущен."); }
   catch(Exception e) { failure=e; }
   finally { if(pipe!=null) pipe.Dispose(); }
   if(broker!=null) {
    using(broker) {
     var done=Task.Run(()=>broker.ReadByte());
     if(await Task.WhenAny(done,Task.Delay(25000))==done) { try { await done; } catch(IOException) {} }
     else Emit("ERROR_STOP");
    }
   }
   if(worker!=null) {
    try {
     Task exiting=Task.Run(()=>worker.WaitForExit());
     if(await Task.WhenAny(exiting,Task.Delay(25000))!=exiting) Emit("ERROR_STOP");
     await exiting; // Never release the session while the elevated helper can still own TUN.
    } finally { worker.Dispose(); }
   }
   if(failure!=null) throw failure;
  }
  void ReadStatuses() {
   try { while(true) { string status=Wire.Read(pipe); string category=CoreFailure.Category(status); Emit(category=="ERROR_WORKER"?"ERROR_WORKER":status); if(category=="STOPPED" || category.StartsWith("ERROR")) break; } }
   catch { if(!stopping) Emit("ERROR_WORKER"); }
  }
  public void Stop() { stopping=true; try { if(pipe!=null) { if(pipe.IsConnected) Wire.Send(pipe,"STOP"); else pipe.Dispose(); } } catch {} }
  internal void SwitchServer(Server server) {
   Subscriptions.Validate(server);
   if(stopping || pipe==null || !pipe.IsConnected) throw new SafeException("Соединение уже завершается.");
   Wire.Send(pipe,"SWITCH "+Json.Write(server));
  }
  public void Dispose() { Stop(); if(pipe!=null) pipe.Dispose(); }
 }
 public class ConnectRequest { public Server Server; public bool AutoReconnect; public NetworkPolicy Network=new NetworkPolicy(); }
 public static class Worker {
  static readonly Native.ConsoleHandler handler=delegate(uint code) { return true; };
  static bool OwnTunPresent() {
   try { return NetworkInterface.GetAllNetworkInterfaces().Any(n=>n.Name==Configuration.InterfaceName && n.OperationalStatus==OperationalStatus.Up && n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()=="172.30.87.1")); } catch { return false; }
  }
  static ulong OwnTunLuid() {
   try { var adapter=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>n.Name==Configuration.InterfaceName && n.OperationalStatus==OperationalStatus.Up && n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()=="172.30.87.1")); if(adapter==null) return 0; Guid id; ulong luid; if(!Guid.TryParse(adapter.Id,out id) || Native.ConvertInterfaceGuidToLuid(ref id,out luid)!=0) return 0; return luid; } catch {return 0;}
  }
  public static int RecoverNetwork() { if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 1; try {KillSwitchSession.RecoverOwned();return 0;} catch {return 1;} }
  static async Task<int?> ProbeEndpoint(HttpClient client,string url,CancellationToken token) {
   var clock=Stopwatch.StartNew();
   try { using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false)) return (int)response.StatusCode==204?(int?)Math.Max(1,clock.ElapsedMilliseconds):null; }
   catch(HttpRequestException) { return null; }
   catch(OperationCanceledException) { return null; }
  }
  internal static async Task<bool> Probe(HttpClient client,bool startup,CancellationToken token) {
   return (await ProbeLatency(client,startup,token).ConfigureAwait(false)).HasValue;
  }
  internal static async Task<int?> ProbeLatency(HttpClient client,bool startup,CancellationToken token) {
   using(var attempt=CancellationTokenSource.CreateLinkedTokenSource(token)) {
    attempt.CancelAfter(startup?3000:6000);
    var primary=ProbeEndpoint(client,"https://cp.cloudflare.com/generate_204",attempt.Token);
    if(await Task.WhenAny(primary,Task.Delay(startup?250:750,attempt.Token)).ConfigureAwait(false)==primary) {
     var latency=await primary.ConfigureAwait(false);
     if(latency.HasValue) return latency;
     if(attempt.IsCancellationRequested) return null;
    }
    var backup=ProbeEndpoint(client,"https://www.gstatic.com/generate_204",attempt.Token);
    var first=await Task.WhenAny(primary,backup).ConfigureAwait(false);
    var firstLatency=await first.ConfigureAwait(false);
    if(firstLatency.HasValue) { attempt.Cancel(); return firstLatency; }
    return await (first==primary?backup:primary).ConfigureAwait(false);
   }
  }
  public static int Run(string name,int uiPid) { return Run(name,uiPid,null); }
  internal static int Run(string name,int uiPid,string ownerSid) {
   if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^LunaVPN-[a-f0-9]{32}$")) return 1;
   if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 1;
   using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)) {
    Process core=null; XrayBridge bridge=null; KillSwitchSession protection=null; int deliberateStop=0;
    try {
     pipe.Connect(15000);
     uint peer; if(!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle,out peer) || peer!=(uint)uiPid) return 1;
     using(var ui=Process.GetProcessById(uiPid)) if(!string.Equals(ui.MainModule.FileName,System.Reflection.Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase)) return 1;
     // The helper holds no persistent administrator service or arbitrary command API.
     var first=Task.Run(()=>Wire.Read(pipe)); if(!first.Wait(15000)) return 1;
     var request=Json.Read<ConnectRequest>(first.Result); request.Network=(request.Network??new NetworkPolicy()).Snapshot();
     string corePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe");
     using(var coreLock=new FileStream(corePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
      if(OwnTunPresent()) { Wire.Send(pipe,"ERROR_WORKER"); return 1; }
      // Enrol the helper before spawning the core. Its children inherit the job;
      // OS closes this handle on helper termination, killing orphan core processes.
      Native.EnrolJob();
      if(!Native.AllocConsole()) throw new SafeException("Не удалось создать консоль ядра.");
      Native.ShowWindow(Native.GetConsoleWindow(),0); Native.SetConsoleCtrlHandler(handler,true);
      bool switching=false;
      while(true) using(var stop=new CancellationTokenSource()) {
       var command=Task.Run(()=> {
        try {
         string message=Wire.Read(pipe);
         if(message=="STOP") Interlocked.Exchange(ref deliberateStop,1);
         return ReadSwitchServer(message);
        } catch { return null; }
        finally { try { stop.Cancel(); } catch(ObjectDisposedException) {} }
       });
       if(switching) Wire.Send(pipe,"SWITCHING");
       string config=null;
       try {
        var paths=request.Server.Transport=="xhttp"?new[]{corePath,XrayBridge.CorePath}:new[]{corePath};
        config=NetworkPolicy.Apply(Configuration.Build(request.Server,paths.Length>1?XrayBridge.Socks(10808,"check","synthetic-check"):null),request.Network,System.Reflection.Assembly.GetExecutingAssembly().Location);
        Configuration.Check(corePath,config);
        if(!stop.IsCancellationRequested && request.Network.KillSwitch) {
         if(protection==null) protection=KillSwitchSession.ArmWithStore(request.Network,paths,()=>new WfpKillSwitchStore(ownerSid));
         else protection.PrepareSwitch(paths);
        }
       } catch(SafeException) {
        if(protection==null) throw;
        config=null;
        if(!stop.IsCancellationRequested) { Wire.Send(pipe,"BLOCKED"); stop.Token.WaitHandle.WaitOne(); }
       }
       int retries=0; string failure="ERROR_CORE";
       while(config!=null && !stop.IsCancellationRequested) {
        if(request.Server.Transport=="xhttp") {
         try { bridge=new XrayBridge(request.Server,stop.Token); }
         catch(OperationCanceledException) { if(stop.IsCancellationRequested) break; throw; }
         catch(SafeException) {
          if(protection==null) throw;
          if(!stop.IsCancellationRequested) { Wire.Send(pipe,"BLOCKED"); stop.Token.WaitHandle.WaitOne(); }
          break;
         }
         config=NetworkPolicy.Apply(Configuration.Build(request.Server,bridge.Outbound),request.Network,System.Reflection.Assembly.GetExecutingAssembly().Location);
        }
        core=new Process { StartInfo=Configuration.StartInfo(corePath,"run") }; core.StartInfo.CreateNoWindow=false;
        var coreFailure=new CoreFailure();
        core.OutputDataReceived+=(sender,e)=>coreFailure.Observe(e.Data); core.ErrorDataReceived+=(sender,e)=>coreFailure.Observe(e.Data);
        core.Start(); core.BeginOutputReadLine(); core.BeginErrorReadLine();
        using(var input=new StreamWriter(core.StandardInput.BaseStream,new UTF8Encoding(false))) input.Write(config);
        Wire.Send(pipe,retries==0?"CONNECTING":"RETRYING");
        bool healthy=false,checking=false; int failures=0; var readyClock=Stopwatch.StartNew(); long next=0;
        ulong lastLuid=0;
        using(var probeHandler=new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false,UseCookies=false })
        using(var client=new HttpClient(probeHandler))
        while(!stop.IsCancellationRequested && !core.HasExited && (bridge==null || bridge.Alive)) {
         ulong luid=protection==null?0:OwnTunLuid();
         if(protection!=null && (luid!=0 || lastLuid!=0)) protection.SetTunnelLuid(luid);
         lastLuid=luid;
         if(readyClock.ElapsedMilliseconds>=next) {
          bool tunReady=protection==null?OwnTunPresent():luid!=0;
          if(tunReady && !checking && retries==0) { checking=true; Wire.Send(pipe,"VERIFYING"); }
          bool ok=tunReady && Probe(client,!healthy,stop.Token).GetAwaiter().GetResult();
          if(stop.IsCancellationRequested) break;
          if(ok && !core.HasExited) { if(!healthy) Wire.Send(pipe,"CONNECTED"); healthy=true; failures=0; }
          else {
           if(healthy) { healthy=false; Wire.Send(pipe,"RETRYING"); }
           failures++; if(readyClock.ElapsedMilliseconds>40000 && failures>=3) { failure="ERROR_NETWORK"; break; }
          }
          next=readyClock.ElapsedMilliseconds+(healthy?10000:(tunReady?500:0));
         }
         stop.Token.WaitHandle.WaitOne(150);
        }
        if(core.HasExited) { core.WaitForExit(); failure=coreFailure.Status(core.ExitCode); }
        if(protection!=null) protection.SetTunnelLuid(0);
        StopCore(core); if(!core.HasExited) throw new SafeException("Ядро ещё работает; защитные правила сохранены."); core.Dispose(); core=null; if(bridge!=null) {bridge.Dispose();bridge=null;}
        if(stop.IsCancellationRequested) break;
        if(!request.AutoReconnect || retries>=3) { if(request.Network.KillSwitch) { Wire.Send(pipe,"BLOCKED"); stop.Token.WaitHandle.WaitOne(); } else Wire.Send(pipe,failure); break; }
        retries++; Wire.Send(pipe,"RETRYING"); stop.Token.WaitHandle.WaitOne(2000*(1<<retries));
       }
       if(!stop.IsCancellationRequested) break;
       var replacement=command.GetAwaiter().GetResult();
       if(replacement==null) break;
       request.Server=replacement;
       switching=true;
      }
      if(protection!=null && Interlocked.CompareExchange(ref deliberateStop,0,0)==1) { protection.Release(); protection.Dispose(); protection=null; }
      try { Wire.Send(pipe,"STOPPED"); } catch {}
     }
     return 0;
    } catch { try { Wire.Send(pipe,"ERROR_WORKER"); } catch {} return 1; }
    finally { if(core!=null) { StopCore(core); core.Dispose(); } if(bridge!=null) bridge.Dispose(); if(protection!=null) { try { if(Interlocked.CompareExchange(ref deliberateStop,0,0)==1) protection.Release(); } catch { try {Wire.Send(pipe,"ERROR_WORKER");} catch {} } finally { protection.Dispose(); } } }
   }
  }
  internal static Server ReadSwitchServer(string command) {
   if(!command.StartsWith("SWITCH ",StringComparison.Ordinal)) return null;
   var server=Json.Read<Server>(command.Substring(7));
   Subscriptions.Validate(server);
   return server;
  }
  internal static void StopCore(Process process) {
   try { if(!process.HasExited) { Native.GenerateConsoleCtrlEvent(1,0); if(!process.WaitForExit(8000)) { process.Kill(); process.WaitForExit(3000); } } } catch {}
  }
 }
 internal static class Native {
  [DllImport("iphlpapi.dll")] internal static extern uint ConvertInterfaceGuidToLuid(ref Guid guid,out ulong luid);
  public delegate bool ConsoleHandler(uint code);
  [DllImport("kernel32.dll",SetLastError=true)] public static extern bool GetNamedPipeClientProcessId(SafePipeHandle handle,out uint pid);
  [DllImport("kernel32.dll",SetLastError=true)] public static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle,out uint pid);
  [DllImport("kernel32.dll")] public static extern bool AllocConsole();
  [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle,int command);
  [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(ConsoleHandler callback,bool add);
  [DllImport("kernel32.dll")] public static extern bool GenerateConsoleCtrlEvent(uint code,uint group);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
  [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job,int info,IntPtr data,uint length);
  [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [StructLayout(LayoutKind.Sequential)] struct BasicLimit { public long ProcessTime,JobTime; public uint Flags; public UIntPtr MinWorkingSet,MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority,Scheduling; }
  [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes; }
  [StructLayout(LayoutKind.Sequential)] struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory; }
  static IntPtr jobHandle;
  sealed class ChildJob : IDisposable {
   IntPtr handle;
   internal ChildJob(IntPtr value) { handle=value; }
   public void Dispose() { if(handle!=IntPtr.Zero) { CloseHandle(handle); handle=IntPtr.Zero; } }
  }
  internal static IDisposable GuardChild(Process process) {
   IntPtr job=CreateJobObject(IntPtr.Zero,null); if(job==IntPtr.Zero) throw new SafeException("Не удалось защитить процесс проверки.");
   var info=new ExtendedLimit(); info.Basic.Flags=0x2000; int size=Marshal.SizeOf(info); IntPtr ptr=Marshal.AllocHGlobal(size);
   try {
    Marshal.StructureToPtr(info,ptr,false);
    if(!SetInformationJobObject(job,9,ptr,(uint)size) || !AssignProcessToJobObject(job,process.Handle)) { CloseHandle(job); throw new SafeException("Не удалось защитить процесс проверки."); }
    return new ChildJob(job);
   } finally { Marshal.FreeHGlobal(ptr); }
  }
  public static void EnrolJob() {
   IntPtr job=CreateJobObject(IntPtr.Zero,null); if(job==IntPtr.Zero) throw new SafeException("Job Object недоступен.");
   var info=new ExtendedLimit(); info.Basic.Flags=0x2000; int size=Marshal.SizeOf(info); IntPtr ptr=Marshal.AllocHGlobal(size);
   try { Marshal.StructureToPtr(info,ptr,false); if(!SetInformationJobObject(job,9,ptr,(uint)size) || !AssignProcessToJobObject(job,Process.GetCurrentProcess().Handle)) { CloseHandle(job); throw new SafeException("Не удалось защитить жизненный цикл ядра."); } jobHandle=job; }
   finally { Marshal.FreeHGlobal(ptr); }
  }
 }
}
