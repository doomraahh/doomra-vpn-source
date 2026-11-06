using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
namespace Luna {
 public sealed class SubscriptionInfo {
  public long? Upload,Download,Total,Expire;
  public long UpdatedAt;
  public long? Used { get { if(!Upload.HasValue || !Download.HasValue || Upload.Value>long.MaxValue-Download.Value) return null; return Upload.Value+Download.Value; } }
  public static SubscriptionInfo Parse(string header) {
   var info=new SubscriptionInfo { UpdatedAt=(long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds };
   if(string.IsNullOrEmpty(header) || header.Length>4096) return info;
   var fields=new Dictionary<string,long?>(StringComparer.OrdinalIgnoreCase);
   foreach(string part in header.Split(';')) {
    int separator=part.IndexOf('='); if(separator<1) continue;
    string key=part.Substring(0,separator).Trim(); long number;
    long? value=long.TryParse(part.Substring(separator+1).Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out number) && number>=0?(long?)number:null;
    if(fields.ContainsKey(key)) fields[key]=null; else fields[key]=value;
   }
   fields.TryGetValue("upload",out info.Upload); fields.TryGetValue("download",out info.Download);
   fields.TryGetValue("total",out info.Total); fields.TryGetValue("expire",out info.Expire);
   if(info.Expire>253402300799L) info.Expire=null;
   return info;
  }
 }
 public sealed class SubscriptionDownload { public string Body; public SubscriptionInfo Info; }
 internal sealed class TrafficAccumulator {
  internal long Received,Sent; internal bool Available;
  internal double? ReceivedPerSecond,SentPerSecond;
  string adapter; long lastReceived,lastSent; double lastSample;
  internal void Reset() { Received=Sent=0; Available=false; Gap(); }
  internal void Sample(string id,long received,long sent,double elapsedSeconds) {
   if(received<0 || sent<0) { Gap(); return; }
   ReceivedPerSecond=SentPerSecond=null;
   if(adapter==id) {
    if(received>=lastReceived) Received+=Math.Min(long.MaxValue-Received,received-lastReceived);
    if(sent>=lastSent) Sent+=Math.Min(long.MaxValue-Sent,sent-lastSent);
    double interval=elapsedSeconds-lastSample;
    if(interval>0 && received>=lastReceived && sent>=lastSent) {
     ReceivedPerSecond=(received-lastReceived)/interval;
     SentPerSecond=(sent-lastSent)/interval;
    }
   }
   adapter=id; lastReceived=received; lastSent=sent; lastSample=elapsedSeconds; Available=true;
  }
  internal void Gap() { adapter=null; ReceivedPerSecond=SentPerSecond=null; }
 }
 internal sealed class SessionMeter {
  internal readonly TrafficAccumulator Traffic=new TrafficAccumulator();
  internal readonly Stopwatch Clock=new Stopwatch();
  internal bool Active;
  internal void Reset() { Traffic.Reset(); Clock.Reset(); Active=false; }
  internal void SetActive(bool active) { Active=active; if(active) Clock.Start(); else { Clock.Stop(); Traffic.Gap(); } }
  internal void Poll() {
   if(!Active) return;
   try {
    var adapter=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>n.Name==Configuration.InterfaceName && n.OperationalStatus==OperationalStatus.Up && n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.ToString()=="172.30.87.1"));
    if(adapter==null) { Traffic.Gap(); return; }
    var stats=adapter.GetIPStatistics(); Traffic.Sample(adapter.Id,stats.BytesReceived,stats.BytesSent,Clock.Elapsed.TotalSeconds);
   } catch { Traffic.Gap(); }
  }
  internal string Elapsed { get { return string.Format(CultureInfo.InvariantCulture,"{0:00}:{1:00}:{2:00}",(int)Clock.Elapsed.TotalHours,Clock.Elapsed.Minutes,Clock.Elapsed.Seconds); } }
 }
 internal static class LogoMotion {
  internal const double Duration=.8;
  internal static double Angle(double seconds) { double t=Math.Max(0,Math.Min(1,seconds/Duration)); return 360*(t*t*t*(t*(t*6-15)+10)); }
  internal static bool IsPending(string state) { return state=="CONNECTING" || state=="VERIFYING" || state=="SWITCHING" || state=="RETRYING"; }
  internal static float ArcAngle(double seconds) { return (float)(((Math.Max(0,seconds-Duration)/1.8)%1)*360-90); }
  internal static float SuccessOpacity(double seconds,double confirmedAt) {
   double elapsed=seconds-Math.Max(Duration,confirmedAt);
   return elapsed<0?0:(float)Math.Max(0,1-elapsed/.42);
  }
 }
 internal static class MetricText {
  internal static string Expiry(SubscriptionInfo info) {
   if(info==null || !info.Expire.HasValue) return "Нет данных";
   if(info.Expire==0) return "Бессрочно";
   try { return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(info.Expire.Value).ToLocalTime().ToString("dd.MM.yyyy"); } catch { return "Нет данных"; }
  }
  static readonly CultureInfo Russian=CultureInfo.GetCultureInfo("ru-RU");
  internal static string Used(SubscriptionInfo info) { return info!=null && info.Used.HasValue?(info.Used.Value/1000000000d).ToString("0.00",Russian)+" ГБ":"Нет данных"; }
  internal static string Plan(SubscriptionInfo info) { return info==null || !info.Total.HasValue?"Лимит не указан":(info.Total==0?"Безлимит":"Лимит "+(info.Total.Value/1000000000d).ToString("0.##",Russian)+" ГБ"); }
  internal static string Updated(SubscriptionInfo info) {
   if(info==null || info.UpdatedAt<=0) return "Обновите подписку";
   try { return "Обновлено "+new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(info.UpdatedAt).ToLocalTime().ToString("dd.MM, HH:mm",Russian); } catch { return "Обновите подписку"; }
  }
  internal static string Usage(SubscriptionInfo info) {
   if(info==null || !info.Used.HasValue) return "Нет данных";
   string used=(info.Used.Value/1000000000d).ToString("0.00",Russian);
   return used+" / "+(info.Total.HasValue?(info.Total==0?"∞":(info.Total.Value/1000000000d).ToString("0.##",Russian)):"—")+" ГБ";
  }
  internal static string Bytes(long bytes) {
   if(bytes>=1000000000) return (bytes/1000000000d).ToString("0.00",Russian)+" ГБ";
   if(bytes>=1000000) return (bytes/1000000d).ToString("0.0",Russian)+" МБ";
   if(bytes>=1000) return (bytes/1000d).ToString("0.0",Russian)+" КБ";
   return bytes+" Б";
  }
  internal static string Speed(double? received,double? sent) {
   return "↓ "+Rate(received)+"  ↑ "+Rate(sent)+" МБ/с";
  }
  static string Rate(double? bytesPerSecond) {
   return bytesPerSecond.HasValue?(bytesPerSecond.Value/1000000d).ToString("0.00",Russian):"—";
  }
 }
 internal sealed class ServerCheckResult {
  internal readonly string Text;
  internal readonly DateTimeOffset? CheckedAt;
  internal readonly int? LatencyMilliseconds;
  internal ServerCheckResult(string text,DateTimeOffset? checkedAt=null,int? latencyMilliseconds=null) { Text=text; CheckedAt=checkedAt; LatencyMilliseconds=latencyMilliseconds; }
  internal string LatencyLabel { get { return LatencyMilliseconds.HasValue?LatencyMilliseconds.Value.ToString(CultureInfo.InvariantCulture)+" мс":"—"; } }
  internal string TimeLabel { get { return CheckedAt.HasValue?CheckedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture):""; } }
  internal string CompactTimeLabel { get { return CheckedAt.HasValue?CheckedAt.Value.ToLocalTime().ToString("dd.MM · HH:mm",CultureInfo.InvariantCulture):""; } }
 }
 public sealed class SavedServerCheck {
  public string Status;
  public long UnixSeconds;
  public int? LatencyMilliseconds;
  internal ServerCheckResult Read() {
   if(Status!="Доступен" && Status!="Нет ответа" && Status!="Ошибка проверки") return null;
   try { return new ServerCheckResult(Status,DateTimeOffset.FromUnixTimeSeconds(UnixSeconds),Status=="Доступен" && LatencyMilliseconds>0?LatencyMilliseconds:null); }
   catch(ArgumentOutOfRangeException) { return null; }
  }
 }
 internal static class ProbeQueue {
  internal static Task Run(IList<Server> servers,Func<Server,CancellationToken,Task<int?>> probe,Action<int,ServerCheckResult> progress,CancellationToken token,int concurrency=1) {
   int next=-1;
   var lanes=new List<Task>();
   for(int lane=0;lane<Math.Min(concurrency,servers.Count);lane++)
    lanes.Add(RunLane(servers,probe,progress,()=>Interlocked.Increment(ref next),token));
   return Task.WhenAll(lanes);
  }
  static async Task RunLane(IList<Server> servers,Func<Server,CancellationToken,Task<int?>> probe,Action<int,ServerCheckResult> progress,Func<int> next,CancellationToken token) {
   for(int i=next();i<servers.Count;i=next()) {
    token.ThrowIfCancellationRequested(); progress(i,new ServerCheckResult("Проверка…")); string result;
    int? latency=null;
    try { latency=await probe(servers[i],token); result=latency.HasValue?"Доступен":"Нет ответа"; }
    catch(OperationCanceledException) { token.ThrowIfCancellationRequested(); result="Нет ответа"; }
    catch { result="Ошибка проверки"; }
    token.ThrowIfCancellationRequested(); progress(i,new ServerCheckResult(result,DateTimeOffset.Now,latency));
   }
  }
 }
}
