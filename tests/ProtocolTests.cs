using System;
namespace Luna {
 internal static class ProtocolTests {
  static Server Xhttp() { return Subscriptions.Parse("vless://11111111-1111-4111-8111-111111111111@example.com:443?security=tls&type=xhttp&mode=packet-up&path=%2Ftest").Servers[0]; }
  internal static void Run(Action<string,Action> test,Action<bool> assert) {
   test("Xray XHTTP imports bounded padding and POST tuning from extra",delegate {
    const string prefix="{\"outbounds\":[{\"protocol\":\"vless\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"id\":\"11111111-1111-4111-8111-111111111111\"},\"streamSettings\":{\"network\":\"xhttp\",\"security\":\"tls\",\"xhttpSettings\":{\"mode\":\"packet-up\",\"extra\":";
    const string suffix="}}}]}";
    var r=Subscriptions.Parse(prefix+"{\"xPaddingBytes\":\"100-1000\",\"noGRPCHeader\":true,\"scMaxEachPostBytes\":1000000,\"scMinPostsIntervalMs\":30,\"headers\":{},\"noSSEHeader\":false,\"xmux\":{\"maxConcurrency\":\"16-32\",\"hKeepAlivePeriod\":0}}"+suffix);
    assert(r.Errors.Count==0 && r.Servers.Count==1);
    string config=XrayConfig.Build(r.Servers[0],10808,"user","pass");
    assert(config.Contains("\"xPaddingBytes\":\"100-1000\"") && config.Contains("\"scMaxEachPostBytes\":1000000") && config.Contains("\"maxConcurrency\":\"16-32\""));
    using(var core=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"xray.exe"),"run -test -format json -config stdin:") { UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true })) {
     using(var input=core.StandardInput) input.Write(config);
     var output=core.StandardOutput.ReadToEndAsync(); var errors=core.StandardError.ReadToEndAsync();
     if(!core.WaitForExit(5000)) { core.Kill(); throw new Exception("Xray configuration check timed out"); }
     System.Threading.Tasks.Task.WaitAll(output,errors); assert(core.ExitCode==0);
    }
    var rejected=Subscriptions.Parse(prefix+"{\"downloadSettings\":{\"address\":\"secret.invalid\"}}"+suffix);
    assert(rejected.Servers.Count==0 && rejected.Errors[0].Contains("downloadSettings") && !rejected.Errors[0].Contains("secret.invalid"));
   });
   test("Xray JSON gRPC maps keepalive options to the pinned sing-box core",delegate {
    var r=Subscriptions.Parse("{\"outbounds\":[{\"protocol\":\"vless\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"id\":\"11111111-1111-4111-8111-111111111111\"},\"streamSettings\":{\"network\":\"grpc\",\"security\":\"tls\",\"grpcSettings\":{\"authority\":\"\",\"serviceName\":\"service\",\"idle_timeout\":60,\"health_check_timeout\":20,\"permit_without_stream\":true,\"initial_windows_size\":0}}}]}");
    assert(r.Errors.Count==0 && r.Servers.Count==1);
    string config=Configuration.Build(r.Servers[0]);
    assert(config.Contains("\"idle_timeout\":\"60s\"") && config.Contains("\"ping_timeout\":\"20s\""));
    Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),config);
   });
   test("Xray JSON Hysteria v2 imports with TLS and transport authentication",delegate {
    var r=Subscriptions.Parse("{\"outbounds\":[{\"protocol\":\"hysteria\",\"settings\":{\"version\":2,\"address\":\"example.com\",\"port\":443},\"streamSettings\":{\"network\":\"hysteria\",\"security\":\"tls\",\"tlsSettings\":{\"serverName\":\"example.com\"},\"hysteriaSettings\":{\"version\":2,\"auth\":\"synthetic\",\"udpIdleTimeout\":60}}}]}");
    assert(r.Errors.Count==0 && r.Servers.Count==1 && r.Servers[0].Protocol=="hysteria2" && r.Servers[0].Password=="synthetic");
    Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),Configuration.Build(r.Servers[0]));
   });
   test("Xray JSON Reality password maps to the public key",delegate {
    const string key="abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
    var r=Subscriptions.Parse("{\"outbounds\":[{\"protocol\":\"vless\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"id\":\"11111111-1111-4111-8111-111111111111\",\"encryption\":\"none\"},\"streamSettings\":{\"network\":\"raw\",\"security\":\"reality\",\"realitySettings\":{\"serverName\":\"example.com\",\"fingerprint\":\"chrome\",\"password\":\""+key+"\",\"shortId\":\"abcd\"}}}]}");
    assert(r.Errors.Count==0 && r.Servers.Count==1 && r.Servers[0].PublicKey==key);
   });
   test("XHTTP generated bridge restricts listener and bootstrap",delegate { string json=XrayConfig.Build(Xhttp(),10808,"test-user","test-password"); var d=Json.Read<System.Collections.Generic.Dictionary<string,object>>(json); assert(json.Contains("127.0.0.1") && json.Contains("https+local://1.1.1.1/dns-query") && json.Contains("UseIPv4") && json.Contains("accounts") && json.Contains("test-password")); });
   test("XHTTP JSON roundtrip typed fields",delegate { var r=Subscriptions.Parse(XrayConfig.Build(Xhttp(),10808,"test-user","test-password").Replace("\"sockopt\":{\"domainStrategy\":\"UseIPv4\"},", "").Replace(",\"sockopt\":{\"domainStrategy\":\"UseIPv4\"}", "")); assert(r.Servers.Count==1 && r.Servers[0].XhttpMode=="packet-up" && r.Servers[0].Path=="/test"); });
   test("Hysteria2 generated auth obfs and TLS",delegate { var r=Subscriptions.Parse("hysteria2://user:pass@example.com:8443?obfs=salamander&obfs-password=synthetic"); var p=Configuration.BuildOutbound(r.Servers[0]); assert((string)p["type"]=="hysteria2" && (string)p["password"]=="user:pass" && p.ContainsKey("obfs") && p.ContainsKey("tls") && !p.ContainsKey("transport")); });
   test("Duplicate imports counted without exposing credentials",delegate { string uri="hy2://secret@example.com"; var r=Subscriptions.Parse(uri+"\n"+uri); assert(r.Servers.Count==1 && r.DuplicateCount==1); });
   test("Unsupported Hysteria2 options fail closed",delegate { foreach(string q in new[]{"obfs=gecko","pinSHA256=secret","sni=example.com&fp=chrome","obfs=salamander","obfs-password=secret"}) assert(Subscriptions.Parse("hy2://secret@example.com?"+q).Servers.Count==0); });
   test("Invalid XHTTP modes fail closed",delegate { assert(Subscriptions.Parse("vless://11111111-1111-4111-8111-111111111111@example.com:443?type=xhttp&mode=unknown").Servers.Count==0); });
   test("Hysteria2 auth and default TLS port",delegate { var r=Subscriptions.Parse("hy2://user:pass@example.com/?obfs=salamander&obfs-password=synthetic#test"); assert(r.Servers.Count==1); var s=r.Servers[0]; assert(s.Protocol=="hysteria2" && s.Port==443 && s.Password=="user:pass" && s.Security=="tls"); });
   test("XHTTP URI import",delegate { var r=Subscriptions.Parse("vless://11111111-1111-4111-8111-111111111111@example.com:443?security=tls&type=xhttp&mode=packet-up&path=%2Ftest&host=cdn.example.com"); assert(r.Servers.Count==1); assert(r.Servers[0].Transport=="xhttp" && r.Servers[0].Path=="/test"); });
   test("Hysteria2 refuses disabled TLS verification",delegate { var r=Subscriptions.Parse("hy2://secret@example.com:443?insecure=1"); assert(r.Servers.Count==0 && !string.Join(" ",r.Errors).Contains("secret")); });
   test("XHTTP refuses arbitrary extra configuration",delegate { var r=Subscriptions.Parse("vless://11111111-1111-4111-8111-111111111111@example.com:443?type=xhttp&extra=%7B%22downloadSettings%22%3A%7B%7D%7D"); assert(r.Servers.Count==0); });
  }
 }
}

