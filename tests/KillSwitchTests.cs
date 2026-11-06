using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
namespace Luna {
 internal static class KillSwitchTests {
  const string Core=@"C:\doomra\sing-box.exe", App=@"C:\apps\protected.exe", Other=@"C:\apps\other.exe";
  internal static void Run(Action<string,Action> test,Action<bool> assert) {
   test("Kill switch blocks protected IPv4/IPv6 outbound and inbound before tunnel exists",delegate {
    foreach(string mode in new[]{"all","include","exclude"}) {
     var policy=new NetworkPolicy { Mode=mode,KillSwitch=true,Applications=new List<string>{mode=="exclude"?Other:App} };
     var rules=KillSwitchPlan.Build(policy,new[]{Core},0);
     for(int layer=0;layer<4;layer++) { assert(!Allowed(rules,layer,App,42,false,443,6));assert(Allowed(rules,layer,App,42,true,443,6)); }
    }
   });
   test("Kill switch exact application selection preserves direct apps and denies renamed protected bypass in exclude",delegate {
    foreach(string mode in new[]{"include","exclude"}) {
     var rules=KillSwitchPlan.Build(new NetworkPolicy { Mode=mode,Applications=new List<string>{mode=="include"?App:Other} },new[]{Core},0);
     for(int layer=0;layer<4;layer++) { assert(Allowed(rules,layer,Other.ToUpperInvariant(),42,false,443,6));assert(!Allowed(rules,layer,App,42,false,443,6)); }
     if(mode=="exclude") assert(!Allowed(rules,0,Other+".renamed",42,false,443,6));
    }
   });
   test("Kill switch permits exact TUN LUID and trusted core outbound only",delegate {
    var rules=KillSwitchPlan.Build(new NetworkPolicy(),new[]{Core},99);
    for(int layer=0;layer<4;layer++) { assert(Allowed(rules,layer,App,99,false,443,6));assert(!Allowed(rules,layer,App,100,false,443,6));assert(Allowed(rules,layer,Core,100,false,443,6)==(layer<2)); }
   });
   test("Kill switch DNS denies outrank direct application exceptions; tunnel DNS still works",delegate {
    var host=System.IO.Path.Combine(Environment.SystemDirectory,"svchost.exe");
    var rules=KillSwitchPlan.Build(new NetworkPolicy { Mode="exclude",Applications=new List<string>{Other,host} },new[]{Core},99);
    foreach(ushort port in new ushort[]{53,853}) foreach(byte protocol in new byte[]{6,17}) for(int layer=0;layer<4;layer++) {
     assert(!Allowed(rules,layer,Other,42,false,port,protocol));assert(Allowed(rules,layer,Other,99,false,port,protocol));
    }
    foreach(byte protocol in new byte[]{6,17}) { assert(!Allowed(rules,0,host,42,false,443,protocol));assert(Allowed(rules,0,host,99,false,443,protocol)); }
    assert(Allowed(rules,0,host,42,false,123,17));assert(Allowed(rules,0,Other,42,false,443,6));
   });
   test("Kill switch disposal preserves protection and only explicit Release removes it",delegate {
    var store=new FakeStore();var policy=new NetworkPolicy { KillSwitch=true };
    using(var session=KillSwitchSession.ArmWithStore(policy,new[]{Core},()=>store)) { assert(store.Rules.Count>0);session.SetTunnelLuid(99);session.SetTunnelLuid(0);assert(!store.Rules.Any(r=>r.Interface!=0)); }
    assert(store.Closed && !store.Removed && store.Rules.Count>0);
    store=new FakeStore();using(var session=KillSwitchSession.ArmWithStore(policy,new[]{Core},()=>store)) session.Release();assert(store.Removed);
   });
   test("Kill switch failed policy update retains prior committed protection",delegate {
    var store=new FakeStore();using(var session=KillSwitchSession.ArmWithStore(new NetworkPolicy { KillSwitch=true },new[]{Core},()=>store)) {
     store.Fail=true;bool failed=false;try { session.SetTunnelLuid(99); } catch(InvalidOperationException) { failed=true; }
     assert(failed && !store.Rules.Any(r=>r.Interface!=0) && !store.Removed);
    }
   });
   test("Server switch keeps protection armed while replacing core exceptions",delegate {
    const string xray=@"C:\doomra\xray.exe";
    var store=new FakeStore();
    using(var session=KillSwitchSession.ArmWithStore(new NetworkPolicy { KillSwitch=true },new[]{Core,xray},()=>store)) {
     session.SetTunnelLuid(99);
     session.SetTunnelLuid(0);
     session.PrepareSwitch(new[]{Core});
     assert(!store.Removed && !store.Rules.Any(r=>r.Interface!=0 || r.Application==xray));
     assert(!Allowed(store.Rules,0,App,42,false,443,6));
     session.SetTunnelLuid(100);
     assert(Allowed(store.Rules,0,App,100,false,443,6) && !Allowed(store.Rules,0,App,99,false,443,6));
    }
   });
   test("Kill switch native x64 SDK structures retain union alignment",delegate {
    assert(IntPtr.Size==8);assert(Marshal.SizeOf(typeof(WfpKillSwitchStore.Filter))==200);
    assert(Marshal.SizeOf(typeof(WfpKillSwitchStore.Condition))==40);
    assert(Marshal.SizeOf(typeof(WfpKillSwitchStore.Provider))==64);
    assert(Marshal.SizeOf(typeof(WfpKillSwitchStore.Sublayer))==72);
    assert(Marshal.OffsetOf(typeof(WfpKillSwitchStore.Filter),"Context").ToInt32()==152);
    assert(Marshal.OffsetOf(typeof(WfpKillSwitchStore.Filter),"Id").ToInt32()==176);
    assert(Marshal.OffsetOf(typeof(WfpKillSwitchStore.Filter),"EffectiveWeight").ToInt32()==184);
    assert(WfpKillSwitchStore.OwnedKey("user1","provider")==WfpKillSwitchStore.OwnedKey("user1","provider"));
    assert(WfpKillSwitchStore.OwnedKey("user1","provider")!=WfpKillSwitchStore.OwnedKey("user2","provider"));
   });
  }
  static bool Allowed(IEnumerable<KillSwitchRule> rules,int layer,string app,ulong luid,bool loopback,ushort port,byte protocol) {
   var match=rules.Where(r=>r.Layer==layer && (r.Application==null || string.Equals(r.Application,app,StringComparison.OrdinalIgnoreCase)) && (r.Interface==0 || r.Interface==luid) && (!r.Loopback || loopback) && (r.Port==0 || r.Port==port) && (r.Protocol==0 || r.Protocol==protocol)).OrderByDescending(r=>r.Weight).FirstOrDefault();
   return match==null || match.Permit;
  }
  sealed class FakeStore : IKillSwitchStore {
   internal List<KillSwitchRule> Rules=new List<KillSwitchRule>();internal bool Removed,Closed,Fail;
   public void Replace(IList<KillSwitchRule> rules,bool initial) { if(Fail) throw new InvalidOperationException("Simulated atomic update failure");Rules=rules.ToList(); }
   public void Remove(bool all) { Removed=true;Rules.Clear(); }
   public void Dispose() { Closed=true; }
  }
 }
}
