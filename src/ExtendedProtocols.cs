using System;
using System.Linq;
using System.Collections.Generic;
namespace Luna {
 internal static class ExtendedProtocols {
  static readonly string[] MuxOptions={"maxConcurrency","maxConnections","cMaxReuseTimes","hMaxRequestTimes","hMaxReusableSecs","hKeepAlivePeriod"};
  internal static void Validate(Server s) {
   if(s.Transport=="xhttp" && s.Protocol!="vless") throw new SafeException("XHTTP поддерживается только для VLESS.");
   if(s.XhttpMode==null || !new[]{"auto","packet-up","stream-up","stream-one"}.Contains(s.XhttpMode) || (s.Transport!="xhttp" && s.XhttpMode!="auto")) throw new SafeException("Неподдерживаемый режим XHTTP.");
   if(!ValidRange(s.XhttpPadding,16384) || !ValidRange(s.XhttpPostBytes,16777216) || !ValidRange(s.XhttpPostInterval,60000) ||
      s.XhttpMux==null || s.XhttpMux.Any(p=>!MuxOptions.Contains(p.Key) || p.Value=="" || !ValidRange(p.Value,1000000) || (p.Key=="hKeepAlivePeriod" && p.Value.Contains('-'))) ||
      (s.Transport!="xhttp" && (s.XhttpPadding!="" || s.XhttpPostBytes!="" || s.XhttpPostInterval!="" || s.XhttpNoGrpcHeader || s.XhttpMux.Count!=0)))
    throw new SafeException("Неверный диапазон XHTTP.");
   if(s.GrpcIdleSeconds<0 || s.GrpcIdleSeconds>3600 || s.GrpcPingSeconds<0 || s.GrpcPingSeconds>3600 ||
      (s.Transport!="grpc" && (s.GrpcIdleSeconds!=0 || s.GrpcPingSeconds!=0 || s.GrpcPermitWithoutStream)))
    throw new SafeException("Неверные интервалы gRPC.");
   if(s.Protocol=="hysteria2") {
    if(s.Security!="tls" || s.Transport!="tcp" || s.Fingerprint!="" || s.Flow!="") throw new SafeException("Hysteria2 требует TLS и собственный транспорт QUIC.");
    if(s.Obfs!="" && s.Obfs!="salamander") throw new SafeException("Поддерживается только обфускация salamander.");
    if(s.ObfsPassword==null || s.ObfsPassword.Length>2048 || (s.Obfs=="" ? s.ObfsPassword!="" : s.ObfsPassword.Length==0)) throw new SafeException("Неверный пароль обфускации Hysteria2.");
   } else if(s.Obfs!="" || s.ObfsPassword!="") throw new SafeException("Обфускация Hysteria2 без Hysteria2.");
  }
  static bool ValidRange(string value,int limit) {
   if(value=="") return true;
   if(value==null || value.Length>20 || !System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[0-9]+(?:-[0-9]+)?\z")) return false;
   string[] parts=value.Split('-'); int first,last;
   return int.TryParse(parts[0],out first) && int.TryParse(parts[parts.Length-1],out last) && first<=last && last<=limit;
  }
 }
 internal static class XrayConfig {
  static Dictionary<string,object> O(params object[] pairs) { return Configuration.Obj(pairs); }
  static object NumberOrRange(string value) {
   int number;
   return int.TryParse(value,out number)?(object)number:value;
  }
  internal static string Build(Server s,int port,string username,string password) {
   Subscriptions.Validate(s); Subscriptions.CheckProviderNotice(s.Name);
   if(s.Transport!="xhttp" || s.Protocol!="vless") throw new SafeException("Мост Xray предназначен для VLESS XHTTP.");
   if(port<1 || port>65535 || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || username.Length>255 || password.Length>255) throw new SafeException("Неверные параметры локального моста.");
   var xhttp=O("host",s.TransportHost,"path",s.Path==""?"/":s.Path,"mode",s.XhttpMode);
   if(s.XhttpPadding!="") xhttp["xPaddingBytes"]=NumberOrRange(s.XhttpPadding);
   if(s.XhttpPostBytes!="") xhttp["scMaxEachPostBytes"]=NumberOrRange(s.XhttpPostBytes);
   if(s.XhttpPostInterval!="") xhttp["scMinPostsIntervalMs"]=NumberOrRange(s.XhttpPostInterval);
   if(s.XhttpNoGrpcHeader) xhttp["noGRPCHeader"]=true;
   if(s.XhttpMux.Count>0) xhttp["xmux"]=s.XhttpMux.ToDictionary(p=>p.Key,p=>NumberOrRange(p.Value));
   var stream=O("network","xhttp","security",s.Security==""?"none":s.Security,"xhttpSettings",xhttp,"sockopt",O("domainStrategy","UseIPv4"));
   if(s.Security=="tls") { var tls=O("serverName",s.Sni==""?s.Host:s.Sni,"allowInsecure",false); if(s.Fingerprint!="") tls["fingerprint"]=s.Fingerprint; if(s.Alpn.Length>0) tls["alpn"]=s.Alpn; stream["tlsSettings"]=tls; }
   if(s.Security=="reality") stream["realitySettings"]=O("serverName",s.Sni,"fingerprint",s.Fingerprint==""?"chrome":s.Fingerprint,"publicKey",s.PublicKey,"shortId",s.ShortId);
   return Json.Write(O("log",O("loglevel","none"),"dns",O("servers",new[]{"https+local://1.1.1.1/dns-query"},"queryStrategy","UseIPv4"),
    "inbounds",new object[]{O("listen","127.0.0.1","port",port,"protocol","socks","settings",O("auth","password","accounts",new object[]{O("user",username,"pass",password)},"udp",true,"ip","127.0.0.1"))},
    "outbounds",new object[]{O("protocol","vless","settings",O("vnext",new object[]{O("address",s.Host,"port",s.Port,"users",new object[]{O("id",s.Uuid,"encryption","none")})}),"streamSettings",stream)}));
  }
 }
}
