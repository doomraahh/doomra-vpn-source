using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Luna {
 internal static class ServerCheck {
  internal static string Build(Server server,int port) { return Build(server,port,null); }
  internal static string Build(Server server,int port,Dictionary<string,object> proxyOverride,NetworkCredential credentials=null) {
   if(port<1 || port>65535) throw new SafeException("Неверный порт проверки.");
   var config=Json.Read<Dictionary<string,object>>(Configuration.Build(server,proxyOverride??(server.Transport=="xhttp"?XrayBridge.Socks(10808,"check","synthetic-check"):null)));
   var inbound=Configuration.Obj("type","mixed","tag","check-in","listen","127.0.0.1","listen_port",port);
   if(credentials!=null) inbound["users"]=new[]{Configuration.Obj("username",credentials.UserName,"password",credentials.Password)};
   config["inbounds"]=new object[]{inbound};
   return Json.Write(config);
  }
  internal static async Task<int?> Run(Server server,CancellationToken token) {
   string corePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe");
   if(!File.Exists(corePath)) throw new SafeException("Рядом с приложением не найдено сетевое ядро.");
   var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
   XrayBridge bridge=null;
   var credentials=new NetworkCredential(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));
   using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
   using(var coreLock=new FileStream(corePath,FileMode.Open,FileAccess.Read,FileShare.Read))
   using(var process=new Process { StartInfo=Configuration.StartInfo(corePath,"run") }) {
    Configuration.VerifyCore(corePath);
    timeout.CancelAfter(12000);
    IDisposable job=null;
    try {
     if(server.Transport=="xhttp") bridge=new XrayBridge(server,timeout.Token);
     string config=Build(server,port,bridge==null?null:bridge.Outbound,credentials);
     process.OutputDataReceived+=delegate { }; process.ErrorDataReceived+=delegate { };
     process.Start(); job=Native.GuardChild(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
     using(var input=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false))) input.Write(config);
     bool ready=false;
     for(int i=0;i<40 && !process.HasExited;i++) {
      timeout.Token.ThrowIfCancellationRequested();
      using(var client=new TcpClient()) {
       Task connecting=client.ConnectAsync(IPAddress.Loopback,port);
       try { if(await Task.WhenAny(connecting,Task.Delay(150,timeout.Token))==connecting) { await connecting; ready=true; break; } }
       catch(SocketException) {}
       finally { ObserveFailure(connecting); }
      }
      await Task.Delay(100,timeout.Token);
     }
     if(process.HasExited) throw new SafeException("Ядро проверки завершилось до подключения.");
     if(!ready) return null;
     using(var handler=new HttpClientHandler { UseProxy=true,Proxy=new WebProxy("http://127.0.0.1:"+port) { Credentials=credentials },UseCookies=false,AllowAutoRedirect=false })
     using(var http=new HttpClient(handler)) {
      int? latency=await Worker.ProbeLatency(http,false,timeout.Token);
      if(process.HasExited) throw new SafeException("Ядро проверки завершилось во время запроса.");
      return latency;
     }
    } catch(OperationCanceledException) { return null; }
    finally {
     try { if(!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch {}
     if(job!=null) job.Dispose(); if(bridge!=null) bridge.Dispose();
    }
   }
  }
  static void ObserveFailure(Task task) { task.ContinueWith(t=> { var ignored=t.Exception; },TaskContinuationOptions.OnlyOnFaulted); }
 }
}
