using System;
using System.Text.RegularExpressions;
namespace Luna {
 internal static class ServerNames {
  internal static bool IsAutoSelection(Server server) {
   if(server==null || string.IsNullOrEmpty(server.Name)) return false;
   string name=Regex.Replace(server.Name,@"[^\p{L}\p{N}\s·/]","").Trim();
   return Regex.IsMatch(name,@"^(?:EU\s+)?автовыбор(?:\s*·\s*(?:VLESS|VMESS|TROJAN|SS|HYSTERIA2)\s*/.*)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
  }
  static bool Generic(string name) { return string.IsNullOrWhiteSpace(name) || Regex.IsMatch(name.Trim(),@"^(proxy|outbound|node)([-_ ]?\d+)?$",RegexOptions.IgnoreCase); }
  internal static string FromJson(string remarks,string tag,Server server) {
   string name=!Generic(remarks)?remarks:(!Generic(tag)?tag:HostLabel(server.Host));
   return Subscriptions.CleanName(name+" · "+server.Protocol.ToUpperInvariant()+" / "+server.Transport.ToUpperInvariant()+" :"+server.Port);
  }
  internal static string Display(Server server) { return Generic(server.Name)?FromJson("","",server):server.Name; }
  static string HostLabel(string host) {
   string[,] hints={{"germany|deutschland","Германия"},{"netherlands|holland","Нидерланды"},{"finland","Финляндия"},{"spain","Испания"},{"france","Франция"},{"sweden","Швеция"},{"poland","Польша"},{"turkey","Турция"},{"japan","Япония"},{"singapore","Сингапур"},{"usa|unitedstates","США"}};
   for(int i=0;i<hints.GetLength(0);i++) if(Regex.IsMatch(host??"", @"^(?:"+hints[i,0]+@")\d*(?:\.|-)",RegexOptions.IgnoreCase)) return hints[i,1]+" (имя хоста)";
   return host??"Сервер";
  }
 }
}
