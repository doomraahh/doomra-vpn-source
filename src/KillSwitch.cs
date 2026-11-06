using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Luna {
 // The worker must serialize VPN sessions and stop both trusted cores before Release.
 // Closing the WFP handle deliberately does NOT remove persistent filters. Only an
 // explicit user disconnect/recovery removes them. This protects against process
 // crashes, not an administrator disabling BFE or rebooting without our own service.
 public sealed class KillSwitchSession : IDisposable {
  readonly IKillSwitchStore store;
  readonly NetworkPolicy policy;
  string[] cores;
  bool disposed, released;
  KillSwitchSession(NetworkPolicy p,string[] c,IKillSwitchStore s) { policy=p;cores=c;store=s; }
  public static KillSwitchSession Arm(NetworkPolicy policy,params string[] trustedCorePaths) {
   return ArmWithStore(policy,trustedCorePaths,()=>new WfpKillSwitchStore());
  }
  internal static KillSwitchSession ArmWithStore(NetworkPolicy policy,string[] paths,Func<IKillSwitchStore> factory) {
   if(policy==null) throw new ArgumentNullException("policy");
   var p=policy.Snapshot();
   if(!p.KillSwitch) throw new InvalidOperationException("Kill switch is not enabled.");
   if(paths==null || paths.Length==0) throw new ArgumentException("Trusted core paths are required.");
   var c=paths.Select(NetworkPolicy.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
   var s=factory();
   try { s.Replace(KillSwitchPlan.Build(p,c,0),true); return new KillSwitchSession(p,c,s); }
   catch { s.Dispose(); throw; }
  }
  // Call with zero BEFORE stopping/restarting the core; call with the verified
  // interface LUID only after the new tunnel exists. No adapter-name wildcard.
  public void SetTunnelLuid(ulong luid) { Check(); store.Replace(KillSwitchPlan.Build(policy,cores,luid),false); }
  internal void PrepareSwitch(string[] paths) {
   Check();
   var next=paths.Select(NetworkPolicy.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
   store.Replace(KillSwitchPlan.Build(policy,next,0),false);
   cores=next;
  }
  public void Release() { Check(); store.Remove(false); released=true; }
  void Check() { if(disposed) throw new ObjectDisposedException("KillSwitchSession"); if(released) throw new InvalidOperationException("Kill switch was released."); }
  public void Dispose() { if(!disposed) { disposed=true;store.Dispose(); } }
  public static void RecoverOwned() { RecoverOwned(null); }
  internal static void RecoverOwned(string ownerSid) { using(var s=new WfpKillSwitchStore(ownerSid)) s.Remove(true); }
 }
 internal interface IKillSwitchStore : IDisposable {
  void Replace(IList<KillSwitchRule> rules,bool initial);
  void Remove(bool allSessions);
 }
 internal sealed class KillSwitchRule {
  internal int Layer;
  internal bool Permit;
  internal ulong Weight;
  internal string Application;
  internal ulong Interface;
  internal bool Loopback;
  internal ushort Port;
  internal byte Protocol;
 }
 internal static class KillSwitchPlan {
  // One sublayer: first matching terminating action wins. Never use hard permits
  // (CLEAR_ACTION_RIGHT): unrelated security products may still block traffic.
  internal static List<KillSwitchRule> Build(NetworkPolicy p,string[] cores,ulong luid) {
   var rules=new List<KillSwitchRule>();
   for(int layer=0;layer<4;layer++) {
    rules.Add(new KillSwitchRule { Layer=layer,Permit=true,Weight=100,Loopback=true });
    if(luid!=0) rules.Add(new KillSwitchRule { Layer=layer,Permit=true,Weight=100,Interface=luid });
    // Core exceptions only authorize outbound establishment. Replies belong to
    // the authorized ALE flow; physical inbound listeners get no exemption.
    if(layer<2) foreach(string core in cores) rules.Add(new KillSwitchRule { Layer=layer,Permit=true,Weight=90,Application=core });
    foreach(ushort port in new ushort[]{53,853}) foreach(byte protocol in new byte[]{6,17})
     rules.Add(new KillSwitchRule { Layer=layer,Weight=80,Port=port,Protocol=protocol });
    // Windows DNS Client may use encrypted DNS. Block only shared-host HTTPS,
    // not DHCP/NTP or arbitrary svchost traffic. This also affects other Windows
    // service HTTPS when the tunnel is unavailable; the UI must disclose it.
    if(layer<2) foreach(byte protocol in new byte[]{6,17})
     rules.Add(new KillSwitchRule { Layer=layer,Weight=80,Application=System.IO.Path.Combine(Environment.SystemDirectory,"svchost.exe"),Port=443,Protocol=protocol });
    if(!p.ByDomain && p.Mode=="exclude") foreach(string app in p.Applications)
     rules.Add(new KillSwitchRule { Layer=layer,Permit=true,Weight=60,Application=app });
    if(!p.ByDomain && p.Mode=="include") foreach(string app in p.Applications)
     rules.Add(new KillSwitchRule { Layer=layer,Weight=20,Application=app });
    else rules.Add(new KillSwitchRule { Layer=layer,Weight=20 });
   }
   return rules;
  }
 }
 internal sealed class WfpKillSwitchStore : IKillSwitchStore {
  IntPtr engine;
  readonly Guid provider, sublayer, session=Guid.NewGuid();
  const string Marker="doomraVPN.kill-switch.v1";
  const uint NotFoundProvider=0x80320005, NotFoundSublayer=0x80320007;
  static readonly Guid[] Layers={new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82"),new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4"),new Guid("e1cd9fe7-f4b5-4273-96c0-592e487b8650"),new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f")};
  static readonly Guid AppId=new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971"), LocalInterface=new Guid("4cd62a49-59c3-4969-b7f3-bda5d32890a4"), Flags=new Guid("632ce23b-5167-435c-86d7-e903684aa80c"), RemotePort=new Guid("c35a604d-d22b-4e1a-91b4-68f674ee674b"), Protocol=new Guid("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
  internal WfpKillSwitchStore(string ownerSid=null) {
   if(IntPtr.Size!=8) throw new PlatformNotSupportedException("Kill switch requires the x64 application.");
   string sid=ownerSid;
   if(sid==null) using(var identity=WindowsIdentity.GetCurrent()) { if(identity.User==null) throw new InvalidOperationException("No Windows user SID.");sid=identity.User.Value; }
   provider=OwnedKey(sid,"provider");sublayer=OwnedKey(sid,"sublayer");
   // Null session => non-dynamic session. RPC_C_AUTHN_WINNT=10.
   Check(Native.FwpmEngineOpen0(null,10,IntPtr.Zero,IntPtr.Zero,out engine));
  }
  internal static Guid OwnedKey(string sid,string kind) { using(var hash=SHA256.Create()) { return new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes(Marker+"|"+sid+"|"+kind)).Take(16).ToArray()); } }
  public void Replace(IList<KillSwitchRule> rules,bool initial) {
   Transaction(delegate {
    EnsureObjects();
    var owned=OwnedFilters();
    if(!initial && !owned.Any(f=>f.Session==session)) throw new InvalidOperationException("Kill switch ownership changed; refusing to modify another session.");
    foreach(var f in owned) { if(initial || f.Session==session) Check(Native.FwpmFilterDeleteById0(engine,f.Id)); }
    foreach(var rule in rules) Add(rule);
    var installed=OwnedFilters().Where(f=>f.Session==session).ToList();
    if(installed.Count!=rules.Count || installed.Any(f=>f.Disabled)) throw new InvalidOperationException("WFP did not activate all kill switch filters.");
   });
  }
  public void Remove(bool allSessions) {
   Transaction(delegate {
    if(!ValidateObjects(false)) return;
    foreach(var f in OwnedFilters()) if(allSessions || f.Session==session) Check(Native.FwpmFilterDeleteById0(engine,f.Id));
    // Explicit recovery also drops only our verified metadata, so a disabled
    // provider left after a BFE restart can be recreated on the next connection.
    if(allSessions) { var key=sublayer;Check(Native.FwpmSubLayerDeleteByKey0(engine,ref key));key=provider;Check(Native.FwpmProviderDeleteByKey0(engine,ref key)); }
   });
  }
  void Transaction(Action body) {
   Check(Native.FwpmTransactionBegin0(engine,0)); bool committed=false;
   try { body();Check(Native.FwpmTransactionCommit0(engine));committed=true; }
   finally { if(!committed) Native.FwpmTransactionAbort0(engine); }
  }
  void EnsureObjects() {
   if(ValidateObjects(false)) return;
   using(var mem=new NativeMemory()) {
    var p=new Provider { Key=provider,Display=mem.Display("VPN kill switch"),Flags=1,Data=mem.Blob(Encoding.UTF8.GetBytes(Marker)) };
    uint code=Native.FwpmProviderAdd0(engine,ref p,IntPtr.Zero); if(code!=0 && code!=0x80320009) Check(code);
    var s=new Sublayer { Key=sublayer,Display=mem.Display("VPN application isolation"),Flags=1,Provider=mem.Struct(provider),Data=mem.Blob(Encoding.UTF8.GetBytes(Marker)),Weight=0x7fff };
    code=Native.FwpmSubLayerAdd0(engine,ref s,IntPtr.Zero);if(code!=0 && code!=0x80320009) Check(code);
   }
   if(!ValidateObjects(true)) throw new InvalidOperationException("Cannot establish kill switch ownership.");
  }
  bool ValidateObjects(bool required) {
   IntPtr ptr=IntPtr.Zero; var key=provider;
   uint result=Native.FwpmProviderGetByKey0(engine,ref key,out ptr);
   if(result==NotFoundProvider && !required) return false;
   Check(result);
   try { var p=(Provider)Marshal.PtrToStructure(ptr,typeof(Provider)); if(!Matches(p.Data,Encoding.UTF8.GetBytes(Marker)) || (p.Flags&1)==0) throw new InvalidOperationException("WFP provider ownership mismatch."); }
   finally { Native.FwpmFreeMemory0(ref ptr); }
   key=sublayer;result=Native.FwpmSubLayerGetByKey0(engine,ref key,out ptr);
   if(result==NotFoundSublayer && !required) return false;
   Check(result);
   try { var s=(Sublayer)Marshal.PtrToStructure(ptr,typeof(Sublayer));if(s.Provider==IntPtr.Zero || (Guid)Marshal.PtrToStructure(s.Provider,typeof(Guid))!=provider || !Matches(s.Data,Encoding.UTF8.GetBytes(Marker)) || (s.Flags&1)==0) throw new InvalidOperationException("WFP sublayer ownership mismatch."); }
   finally { Native.FwpmFreeMemory0(ref ptr); }
   return true;
  }
  sealed class OwnedFilter { internal ulong Id;internal Guid Session;internal bool Disabled; }
  List<OwnedFilter> OwnedFilters() {
   var found=new List<OwnedFilter>();IntPtr enumeration;
   Check(Native.FwpmFilterCreateEnumHandle0(engine,IntPtr.Zero,out enumeration));
   try {
    while(true) {
     IntPtr entries=IntPtr.Zero;uint count;
     Check(Native.FwpmFilterEnum0(engine,enumeration,128,out entries,out count));
     try {
      for(int i=0;i<count;i++) {
       var f=(Filter)Marshal.PtrToStructure(Marshal.ReadIntPtr(entries,i*IntPtr.Size),typeof(Filter));
       if(f.Provider==IntPtr.Zero || (Guid)Marshal.PtrToStructure(f.Provider,typeof(Guid))!=provider || f.Sublayer!=sublayer) continue;
       if(f.Data.Size!=16 || f.Data.Data==IntPtr.Zero) throw new InvalidOperationException("Unrecognized filter under owned provider; recovery stopped.");
       var bytes=new byte[16];Marshal.Copy(f.Data.Data,bytes,0,16);found.Add(new OwnedFilter { Id=f.Id,Session=new Guid(bytes),Disabled=(f.Flags&0x20)!=0 });
      }
     } finally { if(entries!=IntPtr.Zero) Native.FwpmFreeMemory0(ref entries); }
     if(count<128) break;
    }
   } finally { Native.FwpmFilterDestroyEnumHandle0(engine,enumeration); }
   return found;
  }
  void Add(KillSwitchRule r) {
   using(var mem=new NativeMemory()) {
    var conditions=new List<Condition>();
    if(r.Application!=null) conditions.Add(new Condition { Key=AppId,Value=new Value { Type=12,Data=mem.App(r.Application) } });
    if(r.Interface!=0) conditions.Add(new Condition { Key=LocalInterface,Value=new Value { Type=4,Data=mem.Struct(r.Interface) } });
    if(r.Loopback) conditions.Add(new Condition { Key=Flags,Match=6,Value=new Value { Type=3,Data=new IntPtr(1) } });
    if(r.Port!=0) conditions.Add(new Condition { Key=RemotePort,Value=new Value { Type=2,Data=new IntPtr(r.Port) } });
    if(r.Protocol!=0) conditions.Add(new Condition { Key=Protocol,Value=new Value { Type=1,Data=new IntPtr(r.Protocol) } });
    var f=new Filter { Key=Guid.NewGuid(),Display=mem.Display("VPN "+(r.Permit?"exception":"protection")),Flags=1,Provider=mem.Struct(provider),Data=mem.Blob(session.ToByteArray()),Layer=Layers[r.Layer],Sublayer=sublayer,Weight=new Value { Type=4,Data=mem.Struct(r.Weight) },Count=(uint)conditions.Count,Conditions=mem.Array(conditions),Action=new FilterAction { Type=r.Permit?0x1002u:0x1001u } };
    ulong id;Check(Native.FwpmFilterAdd0(engine,ref f,IntPtr.Zero,out id));
   }
  }
  static bool Matches(Blob b,byte[] expected) { if(b.Size!=expected.Length || b.Data==IntPtr.Zero) return false;var actual=new byte[expected.Length];Marshal.Copy(b.Data,actual,0,actual.Length);return actual.SequenceEqual(expected); }
  static void Check(uint result) { if(result!=0) throw new SafeException("Windows не смог применить Kill switch (WFP 0x"+result.ToString("X8")+"). Подключение остановлено; используйте восстановление сети при необходимости."); }
  public void Dispose() { if(engine!=IntPtr.Zero) { Native.FwpmEngineClose0(engine);engine=IntPtr.Zero; } }

  // SDK layouts (fwpmtypes.h/fwptypes.h). The context union is 16 bytes,
  // aligned to 8, because it contains BOTH UINT64 and GUID members.
  [StructLayout(LayoutKind.Sequential)] internal struct Display { internal IntPtr Name,Description; }
  [StructLayout(LayoutKind.Sequential)] internal struct Blob { internal uint Size;internal IntPtr Data; }
  [StructLayout(LayoutKind.Sequential)] internal struct Value { internal uint Type;internal IntPtr Data; }
  [StructLayout(LayoutKind.Sequential)] internal struct Condition { internal Guid Key;internal uint Match;internal Value Value; }
  [StructLayout(LayoutKind.Sequential)] internal struct FilterAction { internal uint Type;internal Guid Key; }
  [StructLayout(LayoutKind.Explicit,Size=16)] internal struct Context { [FieldOffset(0)] internal ulong Raw;[FieldOffset(0)] internal Guid Provider; }
  [StructLayout(LayoutKind.Sequential)] internal struct Filter { internal Guid Key;internal Display Display;internal uint Flags;internal IntPtr Provider;internal Blob Data;internal Guid Layer,Sublayer;internal Value Weight;internal uint Count;internal IntPtr Conditions;internal FilterAction Action;internal Context Context;internal IntPtr Reserved;internal ulong Id;internal Value EffectiveWeight; }
  [StructLayout(LayoutKind.Sequential)] internal struct Provider { internal Guid Key;internal Display Display;internal uint Flags;internal Blob Data;internal IntPtr Service; }
  [StructLayout(LayoutKind.Sequential)] internal struct Sublayer { internal Guid Key;internal Display Display;internal uint Flags;internal IntPtr Provider;internal Blob Data;internal ushort Weight; }
  sealed class NativeMemory : IDisposable {
   readonly List<IntPtr> heap=new List<IntPtr>(),wfp=new List<IntPtr>();
   IntPtr Alloc(int size) { var p=Marshal.AllocHGlobal(size);heap.Add(p);return p; }
   internal IntPtr Struct<T>(T value) where T:struct { var p=Alloc(Marshal.SizeOf(typeof(T)));Marshal.StructureToPtr(value,p,false);return p; }
   internal Display Display(string name) { var p=Marshal.StringToHGlobalUni(name);heap.Add(p);return new Display { Name=p }; }
   internal Blob Blob(byte[] bytes) { var p=Alloc(bytes.Length);Marshal.Copy(bytes,0,p,bytes.Length);return new Blob { Size=(uint)bytes.Length,Data=p }; }
   internal IntPtr App(string path) { IntPtr p;Check(Native.FwpmGetAppIdFromFileName0(path,out p));wfp.Add(p);return p; }
   internal IntPtr Array(List<Condition> values) { if(values.Count==0) return IntPtr.Zero;int size=Marshal.SizeOf(typeof(Condition));var p=Alloc(size*values.Count);for(int i=0;i<values.Count;i++) Marshal.StructureToPtr(values[i],IntPtr.Add(p,size*i),false);return p; }
   public void Dispose() { foreach(var p in heap) Marshal.FreeHGlobal(p);foreach(var p in wfp) { var copy=p;Native.FwpmFreeMemory0(ref copy); } }
  }
  static class Native {
   const string Dll="fwpuclnt.dll";
   [DllImport(Dll,CharSet=CharSet.Unicode,ExactSpelling=true)] internal static extern uint FwpmEngineOpen0(string server,uint auth,IntPtr identity,IntPtr session,out IntPtr engine);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmEngineClose0(IntPtr engine);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmTransactionBegin0(IntPtr engine,uint flags);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmTransactionCommit0(IntPtr engine);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmTransactionAbort0(IntPtr engine);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmProviderAdd0(IntPtr engine,ref Provider value,IntPtr security);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmProviderGetByKey0(IntPtr engine,ref Guid key,out IntPtr value);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmProviderDeleteByKey0(IntPtr engine,ref Guid key);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmSubLayerAdd0(IntPtr engine,ref Sublayer value,IntPtr security);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmSubLayerGetByKey0(IntPtr engine,ref Guid key,out IntPtr value);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmSubLayerDeleteByKey0(IntPtr engine,ref Guid key);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmFilterAdd0(IntPtr engine,ref Filter value,IntPtr security,out ulong id);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmFilterDeleteById0(IntPtr engine,ulong id);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmFilterCreateEnumHandle0(IntPtr engine,IntPtr template,out IntPtr enumeration);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmFilterEnum0(IntPtr engine,IntPtr enumeration,uint requested,out IntPtr entries,out uint count);
   [DllImport(Dll,ExactSpelling=true)] internal static extern uint FwpmFilterDestroyEnumHandle0(IntPtr engine,IntPtr enumeration);
   [DllImport(Dll,ExactSpelling=true)] internal static extern void FwpmFreeMemory0(ref IntPtr memory);
   [DllImport(Dll,CharSet=CharSet.Unicode,ExactSpelling=true)] internal static extern uint FwpmGetAppIdFromFileName0(string filename,out IntPtr appId);
  }
 }
}
