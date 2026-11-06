using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Luna {
 public sealed class NetworkPolicy {
  public string Mode="all";
  public List<string> Applications=new List<string>();
  public bool KillSwitch;
  public NetworkPolicy Snapshot() {
   if(Mode!="all" && Mode!="include" && Mode!="exclude") throw new SafeException("Неизвестный режим маршрутизации.");
   if(Applications==null || Applications.Count>64) throw new SafeException("Можно выбрать до 64 приложений.");
   var copy=new NetworkPolicy { Mode=Mode,KillSwitch=KillSwitch };
   foreach(string path in Applications) {
    string normalized=NormalizePath(path);
    if(!copy.Applications.Contains(normalized,StringComparer.OrdinalIgnoreCase)) copy.Applications.Add(normalized);
   }
   return copy;
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
   if(p.Mode!="all" && p.Applications.Count>0) rules.Add(Configuration.Obj("process_path",p.Applications.ToArray(),"action","route","outbound",p.Mode=="include"?"proxy":"direct"));
   route["rules"]=rules.ToArray(); route["final"]=p.Mode=="include"?"direct":"proxy";
   return Json.Write(config);
  }
 }
}
