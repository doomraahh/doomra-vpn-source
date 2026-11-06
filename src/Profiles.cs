using System;
using System.Collections.Generic;
using System.Linq;
namespace Luna {
 public sealed class SubscriptionProfile {
  public string Id=Guid.NewGuid().ToString("N"),Name="Основной",Subscription="";
  public SubscriptionInfo Info;
  public List<Server> Servers=new List<Server>();
  public Dictionary<string,SavedServerCheck> Checks=new Dictionary<string,SavedServerCheck>();
  public int Selected;
 }
 internal static class ProfileStore {
  internal const int MaximumProfiles=24,MaximumServers=10000;
  internal static string CleanName(string name) {
   if(name==null || name.Any(char.IsControl)) throw new SafeException("Название не должно содержать управляющие символы.");
   name=name.Trim(); if(name.Length<1 || name.Length>48) throw new SafeException("Название профиля: от 1 до 48 символов.");
   return name;
  }
  internal static void Initialize(Settings settings) {
   if(settings.Profiles==null) settings.Profiles=new List<SubscriptionProfile>();
   if(settings.Profiles.Count==0 && !settings.ProfilesInitialized) {
    var profile=new SubscriptionProfile { Subscription=settings.Subscription??"",Info=settings.SubscriptionInfo,Servers=settings.Servers??new List<Server>(),Selected=settings.Selected };
    settings.Profiles.Add(profile); settings.ActiveProfile=profile.Id;
   }
   settings.ProfilesInitialized=true;
   if(settings.Profiles.Count>MaximumProfiles || settings.Profiles.Any(p=>p==null || p.Servers==null || p.Subscription==null || p.Id==null || !System.Text.RegularExpressions.Regex.IsMatch(p.Id,@"^[a-f0-9]{32}$")) || settings.Profiles.Select(p=>p.Id).Distinct().Count()!=settings.Profiles.Count || settings.Profiles.Sum(p=>(long)p.Servers.Count)>MaximumServers) throw new SafeException("Повреждён список профилей.");
   foreach(var profile in settings.Profiles) {
    profile.Name=CleanName(profile.Name);
    if(profile.Checks==null || profile.Checks.Count>MaximumServers) profile.Checks=new Dictionary<string,SavedServerCheck>();
    var selected=profile.Selected>=0 && profile.Selected<profile.Servers.Count?profile.Servers[profile.Selected]:null;
    if(profile.Servers.RemoveAll(ServerNames.IsAutoSelection)>0)
     profile.Selected=selected==null?0:Math.Max(0,profile.Servers.IndexOf(selected));
   }
   var current=Current(settings)??settings.Profiles.FirstOrDefault(); if(current==null) Clear(settings); else Apply(settings,current);
  }
  internal static SubscriptionProfile Current(Settings settings) { return settings.Profiles.FirstOrDefault(p=>p.Id==settings.ActiveProfile); }
  static void Clear(Settings settings) { settings.ActiveProfile=""; settings.Subscription=""; settings.SubscriptionInfo=null; settings.Servers=new List<Server>(); settings.Selected=0; }
  static void Apply(Settings settings,SubscriptionProfile profile) {
   settings.ActiveProfile=profile.Id; settings.Subscription=profile.Subscription; settings.SubscriptionInfo=profile.Info; settings.Servers=profile.Servers; settings.Selected=profile.Selected;
  }
  internal static void Capture(Settings settings) {
   if(settings.Profiles==null || settings.Profiles.Count==0) { if(!settings.ProfilesInitialized) Initialize(settings); else Clear(settings); return; }
   var current=Current(settings); if(current==null) throw new SafeException("Текущий профиль не найден.");
   current.Subscription=settings.Subscription; current.Info=settings.SubscriptionInfo; current.Servers=settings.Servers; current.Selected=settings.Selected;
  }
  internal static void Switch(Settings settings,string id) {
   var profile=settings.Profiles.FirstOrDefault(p=>p.Id==id); if(profile==null) throw new SafeException("Профиль не найден.");
   Capture(settings); Apply(settings,profile);
  }
  internal static void Remove(Settings settings,string id) {
   var target=settings.Profiles.FirstOrDefault(p=>p.Id==id); if(target==null) throw new SafeException("Профиль не найден.");
   Capture(settings); int index=settings.Profiles.IndexOf(target); bool active=settings.ActiveProfile==id;
   settings.Profiles.Remove(target); settings.ProfilesInitialized=true;
   if(settings.Profiles.Count==0) Clear(settings); else if(active) Apply(settings,settings.Profiles[Math.Min(index,settings.Profiles.Count-1)]);
  }
  internal static void Rename(Settings settings,string id,string name) {
   string clean=CleanName(name); var profile=settings.Profiles.FirstOrDefault(p=>p.Id==id); if(profile==null) throw new SafeException("Профиль не найден.");
   profile.Name=clean;
  }
  internal static void Upsert(Settings settings,string id,string name,string url,SubscriptionInfo info,List<Server> servers) {
   string clean=CleanName(name); Capture(settings);
   var profile=id==null?null:settings.Profiles.FirstOrDefault(p=>p.Id==id);
   if(id!=null && profile==null) throw new SafeException("Профиль не найден.");
   if(profile==null && settings.Profiles.Count>=MaximumProfiles) throw new SafeException("Можно добавить до 24 профилей.");
   if(servers==null || settings.Profiles.Where(p=>p!=profile).Sum(p=>(long)p.Servers.Count)+servers.Count>MaximumServers) throw new SafeException("Слишком много серверов во всех профилях.");
   if(profile==null) { profile=new SubscriptionProfile(); settings.Profiles.Add(profile); }
   if(profile.Checks.Count>0) {
    var keys=new HashSet<string>(servers.Select(s=>s.CheckKey()));
    foreach(string key in profile.Checks.Keys.Where(k=>!keys.Contains(k)).ToArray()) profile.Checks.Remove(key);
   }
   profile.Name=clean; profile.Subscription=url; profile.Info=info; profile.Servers=servers; profile.Selected=0; Apply(settings,profile);
  }
 }
}
