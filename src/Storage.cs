using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace Luna {
 public class Settings {
  public List<SubscriptionProfile> Profiles=new List<SubscriptionProfile>();
  public string ActiveProfile="";
  public bool ProfilesInitialized;
  public bool LightTheme;
  public NetworkPolicy Network=new NetworkPolicy();
  public string Subscription="";
  public SubscriptionInfo SubscriptionInfo;
  public bool SendDeviceId=true;
  public string DeviceId="";
  public List<Server> Servers=new List<Server>();
  public int Selected=0;
  public bool AutoReconnect=true;
  public bool ConnectAtLogin;
 }
 public static class Storage {
  internal static void EnsureDeviceId(Settings settings) {
   if(string.IsNullOrEmpty(settings.DeviceId)) settings.DeviceId="Luna-"+Guid.NewGuid().ToString("N");
   if(!System.Text.RegularExpressions.Regex.IsMatch(settings.DeviceId,@"^[a-zA-Z0-9=-]{10,64}$")) throw new SafeException("Сохранённый ID установки повреждён. Автоматическая смена HWID отключена, чтобы не создавать новые устройства у провайдера.");
  }
  public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LunaVPN"); } }
  public static void PrivateDirectory(string path) {
   Directory.CreateDirectory(path);
   var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
   acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   Directory.SetAccessControl(path,acl);
  }
  public static void Save(string path,Settings settings) {
   ProfileStore.Capture(settings);
   PrivateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
   byte[] plain=Encoding.UTF8.GetBytes(Json.Write(settings)); byte[] encrypted;
   if(plain.Length>16777216) { Array.Clear(plain,0,plain.Length); throw new SafeException("Слишком большой файл профилей."); }
   try { encrypted=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser); } finally { Array.Clear(plain,0,plain.Length); }
   if(encrypted.Length>16777216) throw new SafeException("Слишком большой файл профилей.");
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try { File.WriteAllBytes(temp,encrypted); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
   finally { if(File.Exists(temp)) File.Delete(temp); }
  }
  public static Settings Load(string path) {
   if(!File.Exists(path)) return new Settings();
   try {
    if(new FileInfo(path).Length>16777216) throw new Exception();
    byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
    try { var settings=Json.Read<Settings>(Encoding.UTF8.GetString(plain)); if(settings==null || settings.Servers==null || settings.Servers.Count>10000) throw new Exception(); settings.Network=(settings.Network??new NetworkPolicy()).Snapshot(); ProfileStore.Initialize(settings); foreach(var profile in settings.Profiles) foreach(var s in profile.Servers) Subscriptions.Validate(s); return settings; }
    finally { Array.Clear(plain,0,plain.Length); }
   } catch { throw new SafeException("Не удалось прочитать защищённые настройки. Файл сохранён; восстановите резервную копию или переименуйте settings.dat."); }
  }
 }
 public class Diagnostics {
  readonly Queue<string> entries=new Queue<string>();
  public void Add(string safeMessage) { lock(entries) { entries.Enqueue(DateTime.Now.ToString("HH:mm:ss")+"  "+safeMessage); while(entries.Count>200) entries.Dequeue(); } }
  public string Report() { lock(entries) { return "VPN 0.54 | sing-box "+Configuration.CoreVersion+" | Windows "+Environment.OSVersion.Version+"\r\nБез URI, имён серверов, адресов, паролей и ключей. Сырые логи ядра не сохраняются.\r\n\r\n"+string.Join("\r\n",entries.ToArray()); } }
 }
}
