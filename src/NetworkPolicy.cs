using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
namespace Luna {
 public sealed class NetworkPolicy {
  public string Mode="all";
  public List<string> Applications=new List<string>();
  public List<string> Domains=new List<string>();
  public bool ByDomain;
  public bool KillSwitch;
  internal List<string> RoutingEntries { get { return ByDomain?Domains:Applications; } }
  public NetworkPolicy Snapshot() {
   if(Mode!="all" && Mode!="include" && Mode!="exclude") throw new SafeException("Неизвестный режим маршрутизации.");
   if(Applications==null || Applications.Count>64) throw new SafeException("Можно выбрать до 64 приложений.");
   if(Domains==null || Domains.Count>64) throw new SafeException("Можно выбрать до 64 сайтов.");
   var copy=new NetworkPolicy { Mode=Mode,KillSwitch=KillSwitch,ByDomain=ByDomain };
   foreach(string path in Applications) {
    string normalized=NormalizePath(path);
    if(!copy.Applications.Contains(normalized,StringComparer.OrdinalIgnoreCase)) copy.Applications.Add(normalized);
   }
   foreach(string domain in Domains) {
    string normalized=NormalizeDomain(domain);
    if(!copy.Domains.Contains(normalized,StringComparer.OrdinalIgnoreCase)) copy.Domains.Add(normalized);
   }
   return copy;
  }
  internal static string NormalizeDomain(string value) {
   const string message="Введите домен сайта или ссылку HTTP/HTTPS.";
   string input=(value??"").Trim();
   if(input.Length==0 || input.Length>2048 || input.Any(char.IsWhiteSpace) || input.Any(char.IsControl))
    throw new SafeException(message);
   Uri uri;
   if(!Uri.TryCreate(input.Contains("://")?input:"https://"+input,UriKind.Absolute,out uri) ||
      (uri.Scheme!="http" && uri.Scheme!="https") || uri.HostNameType!=UriHostNameType.Dns || uri.UserInfo.Length!=0 ||
      input.IndexOfAny(new[]{'*','\\','@'})>=0) throw new SafeException(message);
   string domain;
   try { domain=new IdnMapping { UseStd3AsciiRules=true }.GetAscii(uri.Host.TrimEnd('.')).ToLowerInvariant(); }
   catch(ArgumentException) { throw new SafeException(message); }
   IPAddress address;
   if(domain.Length>253 || !domain.Contains(".") || IPAddress.TryParse(domain,out address) ||
      domain.Split('.').Any(label=>label.Length==0 || label.Length>63)) throw new SafeException(message);
   return domain;
  }
  public static string NormalizePath(string path) {
   if(string.IsNullOrWhiteSpace(path) || path.Length>260 || path.Any(char.IsControl) || path.IndexOfAny(new[]{'*','?','"'})>=0 || path.StartsWith(@"\\") || path.Length<4 || !char.IsLetter(path[0]) || path.IndexOf(':',2)>=0 || path[1]!=':' || (path[2]!='\\' && path[2]!='/') || !path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) throw new SafeException("Выберите локальный исполняемый файл .exe по полному пути.");
   try { return Path.GetFullPath(path); } catch { throw new SafeException("Некорректный путь приложения."); }
  }
  public bool UsesVpn(string path) {
   bool selected=Applications.Contains(path,StringComparer.OrdinalIgnoreCase);
   return Mode=="all" || (Mode=="include"?selected:!selected);
  }
  internal static string Apply(string json,NetworkPolicy policy,string workerPath) {
   var p=(policy??new NetworkPolicy()).Snapshot();
   var config=Json.Read<Dictionary<string,object>>(json);
   var route=(Dictionary<string,object>)config["route"];
   var rules=new List<object>();
   rules.Add(Configuration.Obj("process_path",new[]{XrayBridge.CorePath},"action","route","outbound","direct"));
   rules.Add(Configuration.Obj("port",53,"action","hijack-dns"));
   // Health probe belongs to our elevated worker and must never take the direct fallback.
   if(!string.IsNullOrEmpty(workerPath)) rules.Add(Configuration.Obj("process_path",new[]{Path.GetFullPath(workerPath)},"action","route","outbound","proxy"));
   if(p.Mode!="all" && p.RoutingEntries.Count>0) {
    string outbound=p.Mode=="include"?"proxy":"direct";
    if(p.ByDomain) {
     rules.Add(Configuration.Obj("action","sniff","sniffer",new[]{"http","tls","quic"}));
     rules.Add(Configuration.Obj("domain_suffix",p.Domains.ToArray(),"action","route","outbound",outbound));
     ConfigureDomainDns(config,p.Domains.ToArray());
    } else rules.Add(Configuration.Obj("process_path",p.Applications.ToArray(),"action","route","outbound",outbound));
   }
   route["rules"]=rules.ToArray(); route["final"]=p.Mode=="include"?"direct":"proxy";
   return Json.Write(config);
  }
  static void ConfigureDomainDns(Dictionary<string,object> config,string[] domains) {
   var dns=(Dictionary<string,object>)config["dns"];
   var servers=((System.Collections.IEnumerable)dns["servers"]).Cast<object>().ToList();
   servers.Add(Configuration.Obj("type","fakeip","tag","site-dns","inet4_range","198.18.0.0/15","inet6_range","fd87:6c75:6e62::/48"));
   dns["servers"]=servers.ToArray();
   dns["rules"]=new object[]{
    Configuration.Obj("domain_suffix",domains,"query_type",new[]{"A","AAAA"},"action","route","server","site-dns","rewrite_ttl",1)
   };
  }
 }
}
