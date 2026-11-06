using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace Luna {
 internal sealed class XrayBridge : IDisposable {
  internal const string Version="26.10.31";
  internal const string Hash="d5b558191cccb1e92a5772b68b8cab67717b26f634b0f15153215f65ae982a7e";
  Process process; FileStream coreLock; IDisposable job;
  internal readonly int Port;
  internal readonly string Username=Guid.NewGuid().ToString("N"),Password=Guid.NewGuid().ToString("N");
  internal bool Alive { get { try { return process!=null && !process.HasExited; } catch { return false; } } }
  internal static string CorePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"xray.exe"); } }
  static void Verify(string path) {
   if(!File.Exists(path)) throw new SafeException("Для XHTTP требуется xray.exe из полного архива VPN.");
   using(var input=File.OpenRead(path)) using(var sha=SHA256.Create()) if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=Hash) throw new SafeException("Контрольная сумма Xray не совпадает. Подключение отменено.");
  }
  static ProcessStartInfo StartInfo(string path) { return new ProcessStartInfo(path,"run -format json -config stdin:") {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path)}; }
  internal XrayBridge(Server server,CancellationToken token) {
   var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); Port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
   try {
    coreLock=new FileStream(CorePath,FileMode.Open,FileAccess.Read,FileShare.Read); Verify(CorePath);
    process=new Process {StartInfo=StartInfo(CorePath)}; process.OutputDataReceived+=delegate{}; process.ErrorDataReceived+=delegate{};
    process.Start(); job=Native.GuardChild(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
    using(var input=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false))) input.Write(XrayConfig.Build(server,Port,Username,Password));
    bool ready=false;
    for(int i=0;i<60 && Alive;i++) {
     token.ThrowIfCancellationRequested();
     using(var client=new TcpClient()) { try { var pending=client.ConnectAsync(IPAddress.Loopback,Port); if(pending.Wait(100) && client.Connected) { ready=true; break; } } catch {} }
     token.WaitHandle.WaitOne(50);
    }
    if(!ready || !Alive) throw new SafeException("Не удалось запустить локальный мост XHTTP.");
   } catch {Dispose(); throw;}
  }
  internal Dictionary<string,object> Outbound { get { return Socks(Port,Username,Password); } }
  internal static Dictionary<string,object> Socks(int port,string username,string password) { return Configuration.Obj("type","socks","tag","proxy","server","127.0.0.1","server_port",port,"version","5","username",username,"password",password); }
  public void Dispose() {
   if(process!=null) {try {if(!process.HasExited) {process.Kill(); process.WaitForExit(3000);}} catch {} process.Dispose();process=null;}
   if(job!=null) {job.Dispose();job=null;} if(coreLock!=null) {coreLock.Dispose();coreLock=null;}
  }
 }
}
