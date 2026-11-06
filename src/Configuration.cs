using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace Luna {
 public static class Configuration {
  public const string CoreVersion="1.14.4";
  public const string CoreHash="8f30682ee198855440d2a7fe070d5d3653793b2d01bb8c85eea88f93bbe7c8b9";
  public const string InterfaceName="LunaVPN-TUN";
  public static Dictionary<string,object> Obj(params object[] pairs) { var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2) d.Add((string)pairs[i],pairs[i+1]); return d; }
  public static string Build(Server s) { return Build(s,null); }
  public static string Build(Server s,Dictionary<string,object> proxyOverride) {
   Subscriptions.Validate(s);
   Subscriptions.CheckProviderNotice(s.Name);
   var proxy=proxyOverride??BuildOutbound(s);
   var dns=Obj("servers",new object[]{
    Obj("type","https","tag","remote","server","1.1.1.1","path","/dns-query","tls",Obj("enabled",true,"server_name","cloudflare-dns.com"),"detour","proxy"),
    Obj("type","https","tag","bootstrap","server","1.1.1.1","path","/dns-query","tls",Obj("enabled",true,"server_name","cloudflare-dns.com"))
   },"final","remote","strategy","prefer_ipv4");
   return Json.Write(Obj("log",Obj("level","warn","timestamp",false),"dns",dns,
    "inbounds",new object[]{Obj("type","tun","tag","tun-in","interface_name",InterfaceName,"address",new[]{"172.30.87.1/30","fd87:6c75:6e61::1/126"},"mtu",1500,"auto_route",true,"strict_route",true,"dns_mode","hijack","stack","mixed")},
    "outbounds",new object[]{proxy,Obj("type","direct","tag","direct")},
    "route",Obj("auto_detect_interface",true,"default_domain_resolver","remote","rules",new object[]{Obj("port",53,"action","hijack-dns")},"final","proxy")));
  }
  public static Dictionary<string,object> BuildOutbound(Server s) {
   Subscriptions.Validate(s); if(s.Transport=="xhttp") throw new SafeException("XHTTP требует локальный мост Xray.");
   var proxy=Obj("type",s.Protocol=="ss"?"shadowsocks":s.Protocol,"tag","proxy","server",s.Host,"server_port",s.Port,"domain_resolver",Obj("server","bootstrap","strategy","prefer_ipv4"));
   if(s.Protocol=="vless" || s.Protocol=="vmess") proxy["uuid"]=s.Uuid; else proxy["password"]=s.Password;
   if(s.Protocol=="vmess") { proxy["security"]=s.Cipher; proxy["alter_id"]=0; }
   if(s.Protocol=="ss") proxy["method"]=s.Method;
   if(s.Flow!="") proxy["flow"]=s.Flow;
   if(s.Security=="tls" || s.Security=="reality") {
    var tls=Obj("enabled",true,"server_name",s.Sni==""?s.Host:s.Sni);
    if(s.Alpn.Length>0) tls["alpn"]=s.Alpn;
    if(s.Fingerprint!="" || s.Security=="reality") tls["utls"]=Obj("enabled",true,"fingerprint",s.Fingerprint==""?"chrome":s.Fingerprint);
    if(s.Security=="reality") tls["reality"]=Obj("enabled",true,"public_key",s.PublicKey,"short_id",s.ShortId);
    proxy["tls"]=tls;
   }
   if(s.Transport=="ws") {
    var transport=Obj("type","ws","path",s.Path==""?"/":s.Path);
    if(s.TransportHost!="") transport["headers"]=Obj("Host",s.TransportHost);
    proxy["transport"]=transport;
   }
   if(s.Transport=="grpc") {
    var grpc=Obj("type","grpc","service_name",s.ServiceName);
    if(s.GrpcIdleSeconds>0) grpc["idle_timeout"]=Math.Max(10,s.GrpcIdleSeconds)+"s";
    if(s.GrpcPingSeconds>0) grpc["ping_timeout"]=s.GrpcPingSeconds+"s";
    if(s.GrpcPermitWithoutStream) grpc["permit_without_stream"]=true;
    proxy["transport"]=grpc;
   }
   if(s.Protocol=="hysteria2" && s.Obfs!="") proxy["obfs"]=Obj("type",s.Obfs,"password",s.ObfsPassword);
   return proxy;
  }
  public static void VerifyCore(string path) {
   if(!File.Exists(path)) throw new SafeException("Не найден sing-box.exe рядом с приложением. Запустите tools/fetch-core.ps1.");
   using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create()) {
    string hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
    if(hash!=CoreHash) throw new SafeException("Контрольная сумма sing-box.exe не совпадает. Подключение отменено.");
   }
  }
  // No plaintext configuration files: sing-box accepts a JSON stream as -c stdin.
  public static ProcessStartInfo StartInfo(string path,string verb) {
   return new ProcessStartInfo(path,verb+" -c stdin") { UseShellExecute=false,RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(Path.GetFullPath(path)) };
  }
  public static void Check(string path,string config) {
   VerifyCore(path);
   using(var p=new Process { StartInfo=StartInfo(path,"check") }) {
    p.Start(); var stderr=p.StandardError.ReadToEndAsync(); var stdout=p.StandardOutput.ReadToEndAsync();
    using(var input=new StreamWriter(p.StandardInput.BaseStream,new UTF8Encoding(false))) input.Write(config);
    if(!p.WaitForExit(10000)) { p.Kill(); throw new SafeException("Проверка конфигурации превысила 10 с."); }
    if(p.ExitCode!=0) throw new SafeException("sing-box отклонил конфигурацию. Проверьте поддержку параметров сервера. Код: "+p.ExitCode+".");
    System.Threading.Tasks.Task.WaitAll(stderr,stdout);
   }
  }
 }
}

