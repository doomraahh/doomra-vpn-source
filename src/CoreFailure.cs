using System;
using System.Linq;
namespace Luna {
 internal sealed class CoreFailure {
  readonly object gate=new object(); string category="ERROR_CORE";
  internal void Observe(string line) {
   if(string.IsNullOrEmpty(line)) return;
   string text=line.Length>4096?line.Substring(0,4096).ToLowerInvariant():line.ToLowerInvariant();
   if(!text.Contains("fatal") && !text.Contains("panic:")) return;
   string code="ERROR_CORE";
   if(text.Contains("access is denied") || text.Contains("permission denied") || text.Contains("requires elevation")) code="ERROR_PERMISSION";
   else if(text.Contains("address already in use") || text.Contains("only one usage")) code="ERROR_BIND";
   else if(text.Contains("tun") || text.Contains("wfp") || text.Contains("wintun")) code="ERROR_TUN";
   else if(text.Contains("resolve") || text.Contains("lookup") || text.Contains("dns")) code="ERROR_DNS";
   else if(text.Contains("invalid") || text.Contains("decode config") || text.Contains("initialize outbound")) code="ERROR_CONFIG";
   lock(gate) { category=code; }
  }
  internal string Status(int exitCode) { lock(gate) { return category+":"+exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture); } }
  internal static string Category(string status) {
   string[] parts=status.Split(':'); int number;
   if(parts.Length>2 || (parts.Length==2 && !int.TryParse(parts[1],out number))) return "ERROR_WORKER";
   return new[]{"CONNECTING","SWITCHING","VERIFYING","CONNECTED","RETRYING","BLOCKED","STOPPED","ERROR_CORE","ERROR_CONFIG","ERROR_NETWORK","ERROR_WORKER","ERROR_TUN","ERROR_PERMISSION","ERROR_BIND","ERROR_DNS","ERROR_STOP"}.Contains(parts[0])?parts[0]:"ERROR_WORKER";
  }
  internal static string ExitSuffix(string status) { string[] p=status.Split(':'); int n; return p.Length==2 && int.TryParse(p[1],out n)?" Код выхода: "+n+".":""; }
 }
}
