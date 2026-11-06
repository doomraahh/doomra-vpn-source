using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Luna {
 internal sealed class VpnService : ServiceBase {
  internal const string Name="DoomraVpn";
  const string PipeName="DoomraVpn.Service.v1";
  const string RegistryPath=@"SOFTWARE\doomra VPN";
  static readonly SecurityIdentifier SystemSid=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
  readonly object gate=new object();
  NamedPipeServerStream listener;
  Process worker;
  Thread thread;
  bool stopping;
  SecurityIdentifier owner;

  internal VpnService() { ServiceName=Name; CanStop=true; CanShutdown=true; AutoLog=false; }

  internal static bool IsInstalled {
   get {
    using(var key=Registry.LocalMachine.OpenSubKey(RegistryPath)) {
     string path=key==null?null:key.GetValue("Executable") as string;
     return string.Equals(path,Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase);
    }
   }
  }
  internal static int RegisterOwner() {
   try {
    using(var identity=WindowsIdentity.GetCurrent()) {
     if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) || identity.IsSystem) return 1;
     using(var key=Registry.LocalMachine.CreateSubKey(RegistryPath)) {
      if(key.GetValue("Owner")==null) key.SetValue("Owner",ShellOwner()??identity.User.Value);
      key.SetValue("Executable",Assembly.GetExecutingAssembly().Location);
     }
    }
    return 0;
   } catch { return 1; }
  }
  internal static SecurityIdentifier ReadOwner() {
   using(var key=Registry.LocalMachine.OpenSubKey(RegistryPath))
    return new SecurityIdentifier((string)key.GetValue("Owner"));
  }
  static string ShellOwner() {
   IntPtr window=GetShellWindow();
   if(window==IntPtr.Zero) return null;
   uint pid;
   if(GetWindowThreadProcessId(window,out pid)==0) return null;
   using(var process=Process.GetProcessById((int)pid)) {
    IntPtr token;
    if(process.SessionId!=Process.GetCurrentProcess().SessionId || !OpenProcessToken(process.Handle,8,out token)) return null;
    try { using(var user=new WindowsIdentity(token)) return user.IsSystem?null:user.User.Value; }
    finally { CloseHandle(token); }
   }
  }
  internal static int SetRunning(bool running) {
   try {
    using(var service=new ServiceController(Name)) {
     if(running) {
      if(service.Status==ServiceControllerStatus.Stopped) service.Start();
      service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(25));
     } else {
      if(service.Status!=ServiceControllerStatus.Stopped && service.Status!=ServiceControllerStatus.StopPending) service.Stop();
      service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(25));
     }
    }
    return 0;
   } catch { return 1; }
  }
  internal static int RemoveOwnerSettings() {
   try {
    using(var key=Registry.LocalMachine.OpenSubKey(RegistryPath))
     if(key==null || key.GetValue("Owner")==null) return 0;
    var owner=ReadOwner();
    KillSwitchSession.RecoverOwned(owner.Value);
    LoginStartup.RemoveFor(owner.Value);
    return 0;
   } catch { return 1; }
  }
  internal static bool IsSessionPipe(string value) {
   return value!=null && Regex.IsMatch(value,@"\ALunaVPN-[a-f0-9]{32}\z");
  }
  protected override void OnStart(string[] args) {
   if(!IsInstalled) throw new InvalidOperationException("Invalid service installation.");
   owner=ReadOwner();
   listener=CreateListener(owner);
   thread=new Thread(Listen) { IsBackground=true,Name="VPN IPC" };
   thread.Start();
  }
  internal static NamedPipeServerStream CreateListener(SecurityIdentifier owner,string name=PipeName) {
   var acl=new PipeSecurity();
   acl.SetAccessRuleProtection(true,false);
   acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
   acl.AddAccessRule(new PipeAccessRule(SystemSid,PipeAccessRights.FullControl,AccessControlType.Allow));
   acl.AddAccessRule(new PipeAccessRule(owner,PipeAccessRights.ReadWrite,AccessControlType.Allow));
   return new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,acl);
  }
  void Listen() {
   while(true) {
    NamedPipeServerStream pipe;
    lock(gate) {
     if(stopping) return;
     pipe=listener;
    }
    try {
     pipe.WaitForConnection();
     Handle(pipe);
    } catch {
     try { if(pipe.IsConnected) Wire.Send(pipe,"ERROR"); } catch {}
    } finally {
     lock(gate) {
      if(!stopping && pipe.IsConnected) {
       try { pipe.Disconnect(); } catch { pipe.Dispose(); listener=CreateListener(owner); }
      } else if(!stopping) {
       pipe.Dispose(); listener=CreateListener(owner);
      }
     }
    }
   }
  }
  void Handle(NamedPipeServerStream pipe) {
   var read=Task.Run(()=>Wire.Read(pipe));
   if(!read.Wait(5000)) { pipe.Dispose(); return; }
   string request=read.Result;
   bool allowed=false;
   pipe.RunAsClient(delegate {
    using(var identity=WindowsIdentity.GetCurrent(true))
     allowed=identity!=null && owner.Equals(identity.User);
   });
   uint pid;
   if(!allowed || !Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle,out pid) || pid==0)
    throw new UnauthorizedAccessException();
   using(var ui=Process.GetProcessById((int)pid)) {
    if(ui.SessionId==0 || !string.Equals(ui.MainModule.FileName,Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))
     throw new UnauthorizedAccessException();
   }
   if(request=="RECOVER") { KillSwitchSession.RecoverOwned(owner.Value); Wire.Send(pipe,"OK"); return; }
   if(!IsSessionPipe(request)) throw new InvalidOperationException();
   var path=Assembly.GetExecutingAssembly().Location;
   Process child;
   lock(gate) {
    if(stopping) return;
    child=Process.Start(new ProcessStartInfo(path,"--service-worker "+request+" "+pid) {
     UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path)
    });
    worker=child;
   }
   try {
    using(Native.GuardChild(child)) {
     Wire.Send(pipe,child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
     child.WaitForExit();
    }
   } finally {
    lock(gate) {
     try { if(!child.HasExited) { child.Kill(); child.WaitForExit(); } } catch {}
     worker=null;
     child.Dispose();
    }
   }
  }
  protected override void OnStop() {
   lock(gate) {
    stopping=true;
    if(listener!=null) listener.Dispose();
    try { if(worker!=null && !worker.HasExited) worker.Kill(); } catch {}
   }
   if(thread!=null && !thread.Join(15000)) throw new InvalidOperationException("VPN worker did not stop.");
   if(owner!=null) KillSwitchSession.RecoverOwned(owner.Value);
  }
  protected override void OnShutdown() { OnStop(); }

  internal static NamedPipeClientStream Connect() {
   var pipe=CreateClient(PipeName);
   try {
    pipe.Connect(4000);
    uint pid;
    if(!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle,out pid) || pid==0 || pid!=ServicePid()) throw new UnauthorizedAccessException();
    return pipe;
   } catch {
    pipe.Dispose();
    throw new SafeException("Служба VPN недоступна. Запустите восстановление через установщик.");
   }
  }
  internal static NamedPipeClientStream CreateClient(string name) {
   return new NamedPipeClientStream(".",name,PipeAccessRights.ReadWrite,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification,HandleInheritability.None);
  }
  internal static int Recover() {
   using(var pipe=Connect()) {
    Wire.Send(pipe,"RECOVER");
    return Wire.Read(pipe)=="OK"?0:1;
   }
  }

  [StructLayout(LayoutKind.Sequential)]
  struct ServiceStatus {
   internal uint Type,State,Controls,Exit,SpecificExit,Checkpoint,WaitHint,Pid,Flags;
  }
  static uint ServicePid() {
   IntPtr manager=OpenSCManager(null,null,1);
   if(manager==IntPtr.Zero) return 0;
   try {
    IntPtr service=OpenService(manager,Name,4);
    if(service==IntPtr.Zero) return 0;
    try {
     ServiceStatus status; uint needed;
     return QueryServiceStatusEx(service,0,out status,(uint)Marshal.SizeOf(typeof(ServiceStatus)),out needed) && status.State==4?status.Pid:0;
    } finally { CloseServiceHandle(service); }
   } finally { CloseServiceHandle(manager); }
  }
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
  [DllImport("advapi32.dll")] static extern bool QueryServiceStatusEx(IntPtr service,int level,out ServiceStatus status,uint size,out uint needed);
  [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
  [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 }
}
