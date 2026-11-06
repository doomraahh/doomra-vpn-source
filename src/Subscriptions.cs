using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Luna {
 public class SafeException : Exception { public SafeException(string message):base(message){} }
 public class Server {
  public string Name="",Protocol="",Host="",Password="",Uuid="",Method="",Security="",Sni="",Fingerprint="",PublicKey="",ShortId="",Flow="",Transport="tcp",Path="",TransportHost="",ServiceName="",Cipher="auto";
  public string XhttpMode="auto",Obfs="",ObfsPassword="";
  public string XhttpPadding="",XhttpPostBytes="",XhttpPostInterval="";
  public Dictionary<string,string> XhttpMux=new Dictionary<string,string>();
  public bool XhttpNoGrpcHeader,GrpcPermitWithoutStream;
  public int GrpcIdleSeconds,GrpcPingSeconds;
  public string[] Alpn=new string[0];
  public int Port;
  public override string ToString() { return Name; }
  internal string CheckKey() {
   var copy=(Server)MemberwiseClone();
   copy.Name="";
   using(var sha=System.Security.Cryptography.SHA256.Create())
    return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Write(copy))));
  }
 }
 public class ImportResult { public int DuplicateCount; public List<Server> Servers=new List<Server>(); public List<string> Errors=new List<string>(); public List<string> Notes=new List<string>(); }
 public static class Json {
  public static JavaScriptSerializer Codec() { return new JavaScriptSerializer { MaxJsonLength=4194304, RecursionLimit=32 }; }
  public static string Write(object value) { return Codec().Serialize(value); }
  public static T Read<T>(string value) { return Codec().Deserialize<T>(value); }
 }
 public static class Subscriptions {
  public const int Limit=4194304;
  static string Decode(string s) { s=s.Replace('-','+').Replace('_','/'); s=s.PadRight((s.Length+3)/4*4,'='); return new UTF8Encoding(false,true).GetString(Convert.FromBase64String(s)); }
  internal static string CleanName(string s) { s=new string(s.Where(c=>!char.IsControl(c) && char.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.Format).ToArray()); return s.Length>100?s.Substring(0,100):s; }
  static string Get(Dictionary<string,string> q,string key,string fallback) { string value; return q.TryGetValue(key,out value)?value:fallback; }
  static void Require(bool value,string message) { if(!value) throw new SafeException(message); }
  static Dictionary<string,string> Query(string query) {
   var q=new Dictionary<string,string>(StringComparer.Ordinal);
   foreach(string pair in query.TrimStart('?').Split('&')) { if(pair.Length==0) continue; string[] p=pair.Split(new[]{'='},2); string k=Uri.UnescapeDataString(p[0]); Require(!q.ContainsKey(k),"Повторяющийся параметр."); q.Add(k,Uri.UnescapeDataString(p.Length==2?p[1]:"")); }
   return q;
  }
  static void Keys(IEnumerable<string> keys,string allowed) { var set=new HashSet<string>(allowed.Split(',')); foreach(string k in keys) Require(set.Contains(k),"Неподдерживаемый параметр URI/JSON (имя и значение скрыты)."); }
  static string Text(Dictionary<string,object> d,string k,string fallback) { object x; return d.TryGetValue(k,out x)?Convert.ToString(x,System.Globalization.CultureInfo.InvariantCulture):fallback; }
  public static ImportResult Parse(string text) {
   Require(text!=null && Encoding.UTF8.GetByteCount(text)<=Limit,"Подписка превышает 4 МиБ.");
   text=text.Trim().TrimStart('\uFEFF'); Require(text.Length>0,"Пустая подписка.");
   if(text.StartsWith("{") || text.StartsWith("[")) return XrayImport.Parse(text);
   if(!text.Contains("://")) { try { text=Decode(text).Trim().TrimStart('\uFEFF'); } catch { throw new SafeException("Ожидается список URI, Base64 или Xray JSON. YAML-подписки не поддерживаются."); } }
   if(text.StartsWith("{") || text.StartsWith("[")) return XrayImport.Parse(text);
   var result=new ImportResult(); var unique=new HashSet<string>(); int line=0;
   foreach(string raw in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
    line++; string value=raw.Trim(); if(value.StartsWith("#")) continue;
    if(line>10000) { result.Errors.Add("Превышено ограничение: 10000 строк."); break; }
    try { Require(value.Length<=16384,"Строка слишком длинная."); Server server=ParseOne(value);
     CheckProviderNotice(server.Name);
     if(ServerNames.IsAutoSelection(server)) continue;
     string name=server.Name; server.Name=""; string key=Json.Write(server); server.Name=name; if(unique.Add(key)) result.Servers.Add(server); else result.DuplicateCount++; }
    catch(SafeException e) { result.Errors.Add("Строка "+line+": "+e.Message); }
    catch { result.Errors.Add("Строка "+line+": неверный формат или значение (содержимое скрыто)."); }
   }
   return result;
  }
  static Server ParseOne(string value) {
   Server s=new Server();
   if(value.StartsWith("vmess://",StringComparison.OrdinalIgnoreCase)) {
    var d=Json.Read<Dictionary<string,object>>(Decode(value.Substring(8)));
    Keys(d.Keys,"v,ps,add,port,id,aid,scy,net,type,host,path,tls,sni,alpn,fp");
    Require(Text(d,"v","2")=="2","Поддерживается VMess JSON v2.");
    Require(Text(d,"aid","0")=="0","VMess alterId > 0 не поддерживается.");
    Require(Text(d,"type","none")=="none" || Text(d,"type","")=="","Заголовок VMess не поддерживается.");
    s.Protocol="vmess"; s.Host=Text(d,"add",""); s.Port=int.Parse(Text(d,"port","0")); s.Uuid=Text(d,"id",""); s.Name=Text(d,"ps","VMess"); s.Cipher=Text(d,"scy","auto");
    Require(new[]{"auto","aes-128-gcm","chacha20-poly1305","none","zero"}.Contains(s.Cipher),"Неподдерживаемый шифр VMess.");
    s.Transport=Text(d,"net","tcp"); s.Security=Text(d,"tls",""); s.Sni=Text(d,"sni",""); s.Path=Text(d,"path",""); s.TransportHost=Text(d,"host",""); s.Fingerprint=Text(d,"fp",""); s.Alpn=Split(Text(d,"alpn",""));
   } else {
    if(value.StartsWith("ss://",StringComparison.OrdinalIgnoreCase)) {
     string body=value.Substring(5); string suffix=""; int hash=body.IndexOf('#'); if(hash>=0) { suffix=body.Substring(hash); body=body.Substring(0,hash); }
     if(!body.Contains("@")) value="ss://"+Decode(body)+suffix;
    }
    Uri uri; Require(Uri.TryCreate(value,UriKind.Absolute,out uri),"Неверный URI.");
    s.Protocol=uri.Scheme.ToLowerInvariant(); if(s.Protocol=="hy2") s.Protocol="hysteria2"; Require(new[]{"vless","trojan","ss","hysteria2"}.Contains(s.Protocol),"Формат не поддерживается (нужен VLESS, VMess, Trojan Shadowsocks или Hysteria2).");
    Require(uri.AbsolutePath=="" || uri.AbsolutePath=="/","Путь в URI должен быть параметром path.");
    s.Host=uri.IdnHost.Trim('[',']'); s.Port=uri.Port; if(s.Protocol=="hysteria2" && s.Port==-1) s.Port=443; s.Name=Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
    var q=Query(uri.Query);
    Keys(q.Keys,s.Protocol=="hysteria2"?"sni,insecure,obfs,obfs-password":"mode,security,sni,peer,fp,pbk,sid,flow,type,path,host,serviceName,alpn,encryption,allowInsecure,insecure,headerType");
    Require(Get(q,"allowInsecure","0")=="0" && Get(q,"insecure","0")=="0","Отключение проверки TLS запрещено.");
    Require(Get(q,"headerType","none")=="none","Маскировка TCP не поддерживается.");
    Require(Get(q,"encryption","none")=="none","Неподдерживаемый encryption.");
    string auth=Uri.UnescapeDataString(uri.UserInfo);
    if(s.Protocol=="hysteria2") { s.Password=auth; s.Security="tls"; s.Sni=Get(q,"sni",""); s.Obfs=Get(q,"obfs",""); s.ObfsPassword=Get(q,"obfs-password",""); } else if(s.Protocol=="ss") {
     Require(q.Count==0,"Параметры Shadowsocks и плагины не поддерживаются.");
     if(!auth.Contains(":")) auth=Decode(auth);
     int colon=auth.IndexOf(':'); Require(colon>0,"Неверный Shadowsocks userinfo."); s.Method=auth.Substring(0,colon); s.Password=auth.Substring(colon+1);
     Require(new[]{"aes-128-gcm","aes-256-gcm","chacha20-ietf-poly1305","2022-blake3-aes-128-gcm","2022-blake3-aes-256-gcm","2022-blake3-chacha20-poly1305"}.Contains(s.Method),"Шифр Shadowsocks не поддерживается.");
    } else {
     if(s.Protocol=="vless") s.Uuid=auth; else s.Password=auth;
     s.Security=Get(q,"security",s.Protocol=="trojan"?"tls":"none"); s.Sni=Get(q,"sni",Get(q,"peer","")); s.Fingerprint=Get(q,"fp",""); s.PublicKey=Get(q,"pbk",""); s.ShortId=Get(q,"sid",""); s.Flow=Get(q,"flow",""); s.Transport=Get(q,"type","tcp"); if(s.Transport=="raw") s.Transport="tcp"; if(s.Transport=="splithttp") s.Transport="xhttp"; s.XhttpMode=Get(q,"mode","auto"); Require(s.Transport=="xhttp" || !q.ContainsKey("mode"),"mode без XHTTP."); s.Path=Get(q,"path",""); s.TransportHost=Get(q,"host",""); s.ServiceName=Get(q,"serviceName",""); s.Alpn=Split(Get(q,"alpn",""));
    }
   }
   Validate(s); s.Name=CleanName(s.Name); if(s.Name.Length==0) s.Name=s.Protocol.ToUpperInvariant()+" · "+s.Host; return s;
  }
  static string[] Split(string text) { return text.Length==0?new string[0]:text.Split(','); }
  internal static void CheckProviderNotice(string name) {
   string notice=Regex.Replace((name??"").ToLowerInvariant(),@"\s+"," ");
   Require(!notice.Contains("приложение не поддерживается") && !Regex.IsMatch(notice,@"попробуйте\b.{0,60}\bрекомендован+ое\b") && !notice.Contains("unsupported application") && !notice.Contains("unsupported client") && !notice.Contains("app is not supported"),"Сервис подписки вернул служебный отказ вместо серверов. Проверьте формат Happ и требование HWID. Если отказ повторяется, уточните у провайдера поддержку сторонних клиентов.");
  }
  public static void Validate(Server s) {
   Require(s!=null,"Сервер не выбран.");
   Require(new[]{"vless","vmess","trojan","ss","hysteria2"}.Contains(s.Protocol),"Неизвестный протокол.");
   Require(!string.IsNullOrEmpty(s.Host) && Uri.CheckHostName(s.Host)!=UriHostNameType.Unknown && s.Host.Length<=253,"Неверное имя сервера.");
   Require(s.Port>0 && s.Port<=65535,"Неверный порт.");
   Require(new[]{"tcp","ws","grpc","xhttp"}.Contains(s.Transport),"Транспорт не поддерживается (TCP, WebSocket, gRPC, XHTTP).");
   Require(new[]{"","none","tls","reality"}.Contains(s.Security),"Неподдерживаемый TLS.");
   if(s.Protocol=="trojan") Require(s.Security=="tls","Trojan требует TLS.");
   if(s.Protocol=="vless" || s.Protocol=="vmess") { Guid id; Require(Guid.TryParse(s.Uuid,out id),"Неверный UUID."); }
   else Require(!string.IsNullOrEmpty(s.Password) && s.Password.Length<=2048,"Пустой или слишком длинный пароль.");
   Require(s.Sni.Length<=253 && (s.Sni.Length==0 || Uri.CheckHostName(s.Sni)!=UriHostNameType.Unknown),"Неверный SNI.");
   Require(s.TransportHost.Length<=253 && !s.TransportHost.Any(char.IsControl),"Неверный Host.");
   Require(s.Path.Length<=2048 && !s.Path.Any(char.IsControl) && (s.Path.Length==0 || s.Path.StartsWith("/")),"Неверный путь транспорта.");
   Require(s.ServiceName.Length<=256 && !s.ServiceName.Any(char.IsControl),"Неверный serviceName.");
   Require(s.Fingerprint=="" || new[]{"chrome","firefox","edge","safari","ios","android","random","randomized"}.Contains(s.Fingerprint),"TLS fingerprint не поддерживается.");
   Require(s.Alpn.Length<=8 && s.Alpn.All(x=>Regex.IsMatch(x,@"^[a-zA-Z0-9./-]{1,32}$")),"Неверный ALPN.");
   Require(s.Flow=="" || (s.Protocol=="vless" && s.Flow=="xtls-rprx-vision" && s.Transport=="tcp" && (s.Security=="tls" || s.Security=="reality")),"Неподдерживаемый flow.");
   if(s.Security=="reality") {
    Require(s.Protocol=="vless" && s.Sni.Length>0,"Reality требует VLESS и SNI.");
    Require(Regex.IsMatch(s.PublicKey,@"^[A-Za-z0-9_-]{43}$"),"Неверный публичный ключ Reality.");
    Require(Regex.IsMatch(s.ShortId,@"^(?:[a-fA-F0-9]{2}){0,8}$"),"Неверный shortId Reality.");
   } else Require(s.PublicKey=="" && s.ShortId=="","Параметры Reality без Reality.");
   Require(s.Security!="none" && s.Security!="" || (s.Fingerprint=="" && s.Sni=="" && s.Alpn.Length==0),"TLS-параметры без TLS.");
   Require(s.Transport=="ws" || s.Transport=="xhttp" || (s.Path=="" && s.TransportHost==""),"Параметры WebSocket без WebSocket.");
   Require(s.Transport=="grpc" || s.ServiceName=="","serviceName без gRPC.");
   ExtendedProtocols.Validate(s);
  }
  public static Task<string> Download(string address) { return Download(address,"Happ"); }
  internal static HttpRequestMessage CreateRequest(Uri uri,string format,string deviceId) {
   var request=new HttpRequestMessage(HttpMethod.Get,uri);
   request.Headers.UserAgent.ParseAdd(RequestAgent(format));
   if(!string.IsNullOrEmpty(deviceId)) {
    Require(Regex.IsMatch(deviceId,@"^[a-zA-Z0-9=-]{10,64}$"),"Неверный формат ID установки (HWID).");
    Require(uri.Scheme=="https","HWID передаётся только по HTTPS.");
    request.Headers.Add("x-hwid",deviceId); request.Headers.Add("x-device-os","Windows"); request.Headers.Add("x-device-model","doomra VPN");
   }
   return request;
  }
  internal static void ValidateResponseHeaders(HttpResponseMessage response) {
   IEnumerable<string> values;
   if(response.Headers.TryGetValues("x-hwid-max-devices-reached",out values) && values.Any(x=>x.Equals("true",StringComparison.OrdinalIgnoreCase))) throw new SafeException("Достигнут лимит устройств у провайдера. Удалите неиспользуемое устройство в личном кабинете или обратитесь в поддержку. HWID не будет автоматически меняться.");
   if(response.Headers.TryGetValues("x-hwid-not-supported",out values) && values.Any(x=>x.Equals("true",StringComparison.OrdinalIgnoreCase))) throw new SafeException("Сервис требует ID устройства (HWID). Включите «Передавать ID установки (HWID)» и обновите подписку. Это может занять слот устройства у провайдера.");
  }
  internal static void ValidateDeviceRedirect(Uri original,Uri next,string deviceId) {
   if(!string.IsNullOrEmpty(deviceId) && !next.GetLeftPart(UriPartial.Authority).Equals(original.GetLeftPart(UriPartial.Authority),StringComparison.OrdinalIgnoreCase)) throw new SafeException("Редирект на другой адрес отклонён, чтобы не передавать HWID другому серверу.");
  }
  internal static string RequestAgent(string format) {
   if(format=="v2rayN") return "v2rayN/7.0";
   if(format=="Happ") return "Happ/1.0";
   if(format=="doomra" || format=="Luna") return "doomraVPN/0.31";
   throw new SafeException("Неизвестный формат запроса подписки.");
  }
  public static Task<string> Download(string address,string format) { return Download(address,format,""); }
  public static async Task<string> Download(string address,string format,string deviceId) { return (await DownloadDetails(address,format,deviceId)).Body; }
  public static async Task<SubscriptionDownload> DownloadDetails(string address,string format,string deviceId) {
   Uri uri; if(!Uri.TryCreate(address,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.UserInfo!="" || uri.Fragment!="") throw new SafeException("Нужна HTTPS-ссылка без имени пользователя и фрагмента.");
   try {
    using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
    using(var handler=new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false, UseDefaultCredentials=false, AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate })
    using(var client=new HttpClient(handler)) {
     // Providers often select the subscription representation by User-Agent.
     // Import only the server fields supported by this client.
     Uri originalUri=uri;
     for(int redirects=0;redirects<=3;redirects++) {
      using(var request=CreateRequest(uri,format,deviceId))
      using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token)) {
       int status=(int)response.StatusCode;
       if(status>=300 && status<400) { Uri next=response.Headers.Location; if(next==null) throw new SafeException("Редирект без адреса."); if(!next.IsAbsoluteUri) next=new Uri(uri,next); if(next.Scheme!="https" || next.UserInfo!="" || next.Fragment!="") throw new SafeException("Небезопасный редирект отклонён."); ValidateDeviceRedirect(originalUri,next,deviceId); uri=next; continue; }
       ValidateResponseHeaders(response);
       if(!response.IsSuccessStatusCode) throw new SafeException("Сервер подписки вернул HTTP "+status+".");
       if(response.Content.Headers.ContentLength>Limit) throw new SafeException("Подписка превышает 4 МиБ.");
       using(var stream=await response.Content.ReadAsStreamAsync()) using(var output=new MemoryStream()) {
        byte[] buffer=new byte[8192]; int count;
        while((count=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0) { if(output.Length+count>Limit) throw new SafeException("Подписка превышает 4 МиБ."); output.Write(buffer,0,count); }
        IEnumerable<string> metadata;
        string header=response.Headers.TryGetValues("subscription-userinfo",out metadata)?string.Join(";",metadata):null;
        return new SubscriptionDownload { Body=new UTF8Encoding(false,true).GetString(output.ToArray()),Info=SubscriptionInfo.Parse(header) };
       }
      }
     }
     throw new SafeException("Слишком много перенаправлений.");
    }
   } catch(SafeException) { throw; } catch { throw new SafeException("Не удалось загрузить подписку: проверьте HTTPS-ссылку и доступ к сети (тайм-аут 20 с)."); }
  }
 }
}

