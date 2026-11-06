using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace Luna {
 internal sealed class NetworkSettingsLayout {
  internal RectangleF Theme,LaunchAtLogin,ConnectAtLogin,Kill,Add,Previous,Next,Recovery;
  internal RectangleF General,Protection,Routing,RouteHint,SessionNotice,ScopeApps,ScopeSites;
  internal RectangleF[] Modes=new RectangleF[3],Rows=new RectangleF[3],Remove=new RectangleF[3];
  internal NetworkSettingsLayout(DashboardLayout d) {
   float x=d.SettingsPage.X+24,w=d.SettingsPage.Width-48,y=d.SettingsPage.Y;
   float left=Math.Min(370,w*.40f),rx=x+left+16,rw=w-left-16;
   SessionNotice=new RectangleF(d.SettingsPage.X+160,y+64,d.SettingsPage.Width-184,20);
   General=new RectangleF(x,SessionNotice.Bottom+16,left,168);
   Protection=new RectangleF(x,General.Bottom+16,left,204);
   Routing=RectangleF.FromLTRB(rx,General.Top,rx+rw,Protection.Bottom);
   ScopeApps=new RectangleF(Routing.Right-224,Routing.Top+8,126,31);
   ScopeSites=new RectangleF(Routing.Right-94,Routing.Top+8,80,31);
   Theme=new RectangleF(x+14,General.Y+44,left-28,36);
   LaunchAtLogin=new RectangleF(x+14,General.Y+84,left-28,36);
   ConnectAtLogin=new RectangleF(x+14,General.Y+124,left-28,36);
   Kill=new RectangleF(x+14,Protection.Y+46,left-28,40);
   Recovery=new RectangleF(x+14,Protection.Y+146,left-28,38);
   RouteHint=new RectangleF(rx+16,Routing.Y+179,rw-32,40);
   for(int i=0;i<3;i++) Modes[i]=new RectangleF(rx+14,Routing.Y+46+i*44,rw-28,38);
   for(int i=0;i<3;i++) {
    Rows[i]=new RectangleF(rx+14,Routing.Y+224+i*35,rw-28,30);
    Remove[i]=new RectangleF(Rows[i].Right-36,Rows[i].Y,36,30);
   }
   Add=new RectangleF(rx+14,Routing.Bottom-52,216,38);
   Previous=new RectangleF(Routing.Right-96,Add.Y,36,38); Next=new RectangleF(Routing.Right-50,Add.Y,36,38);
  }
 }
 internal static class NetworkSettingsView {
  internal static void Draw(Graphics g,DashboardLayout d,DashboardDetails details,string hot,string pressed,int focus) {
   var p=details.Network??new NetworkPolicy(); var l=new NetworkSettingsLayout(d); var page=d.SettingsPage;
   Dashboard.Card(g,page);
   Dashboard.Button(g,d.Back,new UiAction("← Назад") {Enabled=true},"back",hot,pressed,focus,false);
   Dashboard.Label(g,"Настройки",new RectangleF(page.X+160,page.Y+24,260,40),24,Palette.Text);
   if(details.NetworkSessionActive)
    Dashboard.Label(g,"Сетевые настройки вступят в силу после переподключения",l.SessionNotice,13,Palette.Muted);
   Section(g,l.General,"Приложение");
   Section(g,l.Protection,"Защита соединения");
   Section(g,l.Routing,"Раздельный VPN");
   Dashboard.Button(g,l.ScopeApps,new UiAction("Приложения") {Enabled=details.NetworkEditAllowed},"split-apps",hot,pressed,-1,!p.ByDomain);
   Dashboard.Button(g,l.ScopeSites,new UiAction("Сайты") {Enabled=details.NetworkEditAllowed},"split-sites",hot,pressed,-1,p.ByDomain);
   if(focus==38 || focus==39) Dashboard.Line(g,RectangleF.Inflate(focus==38?l.ScopeApps:l.ScopeSites,2,2),Palette.Muted,10);
   Toggle(g,l.Theme,"Светлая тема",details.Light,hot=="theme",focus==15);
   if(details.Installed) {
    Toggle(g,l.LaunchAtLogin,"Запуск при входе в Windows",details.LaunchAtLogin,hot=="launch-at-login",focus==27);
    Toggle(g,l.ConnectAtLogin,"VPN при автозапуске",details.ConnectAtLogin,hot=="connect-at-login",focus==28);
   } else Dashboard.Label(g,"Автозапуск доступен после установки",l.LaunchAtLogin,13,Palette.Muted,false,false,true);
   string[] titles={p.ByDomain?"Весь трафик через VPN":"Все приложения","Только выбранные","Все, кроме выбранных"}; string[] modes={"all","include","exclude"};
   for(int i=0;i<3;i++) RouteOption(g,l.Modes[i],titles[i],p.Mode==modes[i],details.NetworkEditAllowed,hot=="route-"+modes[i],focus==30+i);
   string routeHint=p.Mode=="all"?"Список ниже применяется в двух выборочных режимах.":
    (p.ByDomain?(p.Mode=="include"?"Сайты из списка и их поддомены идут через VPN.":"Сайты из списка и их поддомены идут напрямую."):
     (p.Mode=="include"?"Только приложения из списка используют VPN.":"Приложения из списка выходят в интернет напрямую."));
   Dashboard.Label(g,routeHint,l.RouteHint,13,Palette.Muted,false,false,true);
   var entries=p.RoutingEntries;
   if(entries.Count==0) Dashboard.Label(g,p.ByDomain?"Сайтов в списке пока нет":"Приложений в списке пока нет",new RectangleF(l.Rows[0].X,l.Rows[0].Y+20,l.Rows[0].Width,40),14,Palette.Muted,false,true);
   for(int i=0;i<3;i++) {
    int index=details.AppScroll+i; if(index>=entries.Count) break;
    var r=l.Rows[i]; Dashboard.Fill(g,r,Palette.Input,7);
    string entry=entries[index];
    if(p.ByDomain) Dashboard.Label(g,entry,new RectangleF(r.X+12,r.Y,r.Width-56,r.Height),14,Palette.Text);
    else {
     Dashboard.Label(g,Path.GetFileName(entry),new RectangleF(r.X+12,r.Y,r.Width*.32f,r.Height),14,Palette.Text);
     Dashboard.Label(g,Path.GetDirectoryName(entry),new RectangleF(r.X+r.Width*.34f,r.Y,r.Width*.66f-48,r.Height),13,Palette.Muted);
    }
    Dashboard.Icon(g,l.Remove[i],"trash",details.NetworkEditAllowed?Palette.Text:Palette.Muted);
    if(focus==40+i) Dashboard.Line(g,l.Remove[i],Palette.Muted,7);
   }
   Dashboard.Button(g,l.Add,new UiAction(p.ByDomain?"+ Добавить сайт":"+ Добавить приложение") {Enabled=details.NetworkEditAllowed},"app-add",hot,pressed,-1,false);
   if(focus==33) Dashboard.Line(g,RectangleF.Inflate(l.Add,2,2),Palette.Muted,10);
   Dashboard.Button(g,l.Previous,new UiAction("←") {Enabled=details.AppScroll>0},"apps-prev",hot,pressed,-1,false);
   Dashboard.Button(g,l.Next,new UiAction("→") {Enabled=details.AppScroll+3<entries.Count},"apps-next",hot,pressed,-1,false);
   if(focus==34) Dashboard.Line(g,l.Previous,Palette.Muted,10); if(focus==35) Dashboard.Line(g,l.Next,Palette.Muted,10);
   Dashboard.Label(g,entries.Count+" / 64",new RectangleF(l.Add.Right+10,l.Add.Y,64,36),13,Palette.Muted);
   Toggle(g,l.Kill,"Kill switch",p.KillSwitch,hot=="kill",focus==36);
   Dashboard.Label(g,p.ByDomain?"При обрыве блокирует интернет целиком в режиме по сайтам.":"При обрыве блокирует трафик приложений, которым назначен VPN.",new RectangleF(l.Kill.X+2,l.Kill.Bottom+5,l.Kill.Width-4,48),13,Palette.Muted,false,false,true);
   Dashboard.Button(g,l.Recovery,new UiAction("Восстановить интернет") {Enabled=details.NetworkEditAllowed && !details.NetworkSessionActive},"network-recover",hot,pressed,-1,false);
   if(focus==37) Dashboard.Line(g,RectangleF.Inflate(l.Recovery,2,2),Palette.Muted,10);
  }
  static void Section(Graphics g,RectangleF r,string title) {
   Dashboard.Fill(g,r,Palette.SurfaceBottom,13);
   Dashboard.Line(g,r,Palette.Border,13);
   Dashboard.Label(g,title,new RectangleF(r.X+16,r.Y+9,r.Width-32,30),16,Palette.Text,true);
  }
  static void RouteOption(Graphics g,RectangleF r,string title,bool selected,bool enabled,bool hover,bool focus) {
   Dashboard.Fill(g,r,selected?Palette.Selection:(hover&&enabled?Palette.Hover:Palette.Input),8);
   if(selected || focus) Dashboard.Line(g,r,focus?Palette.Muted:Palette.Border,8);
   var dot=new RectangleF(r.X+13,r.Y+(r.Height-14)/2,14,14);
   using(var pen=new Pen(selected?Palette.Text:Palette.Muted)) g.DrawEllipse(pen,dot);
   if(selected) using(var brush=new SolidBrush(Palette.Text)) g.FillEllipse(brush,RectangleF.Inflate(dot,-4,-4));
   Dashboard.Label(g,title,new RectangleF(r.X+37,r.Y,r.Width-50,r.Height),14,enabled||selected?Palette.Text:Palette.Muted);
  }
  static void Toggle(Graphics g,RectangleF r,string title,bool value,bool hover,bool focus) {
   Dashboard.Fill(g,r,hover?Palette.Hover:Palette.SurfaceBottom,10); Dashboard.Line(g,r,Palette.Border,10);
   Dashboard.Label(g,title,new RectangleF(r.X+12,r.Y,r.Width-71,r.Height),14,Palette.Text);
   var t=new RectangleF(r.Right-53,r.Y+(r.Height-22)/2,42,22); Dashboard.Fill(g,t,value?Palette.Accent:Palette.Selection,11);
   using(var b=new SolidBrush(value?Palette.OnAccent:Palette.Text)) g.FillEllipse(b,t.X+(value?23:3),t.Y+3,16,16);
   if(focus) Dashboard.Line(g,RectangleF.Inflate(t,3,3),Palette.Muted,16);
  }
 }
 public sealed partial class MainForm {
  bool IsNetworkAction(string id) { return id.StartsWith("route-") || id.StartsWith("app-remove:") || id=="split-apps" || id=="split-sites" || id=="app-add" || id=="apps-prev" || id=="apps-next" || id=="kill" || id=="network-recover" || id=="launch-at-login" || id=="connect-at-login"; }
  string NetworkSettingsHit(PointF point) {
   var l=new NetworkSettingsLayout(LayoutNow);
   if(l.Theme.Contains(point)) return "theme";
   if(installed && l.LaunchAtLogin.Contains(point)) return "launch-at-login";
   if(installed && l.ConnectAtLogin.Contains(point)) return "connect-at-login";
   if(l.ScopeApps.Contains(point)) return "split-apps";
   if(l.ScopeSites.Contains(point)) return "split-sites";
   string[] modes={"all","include","exclude"}; for(int i=0;i<3;i++) if(l.Modes[i].Contains(point)) return "route-"+modes[i];
   for(int i=0;i<3;i++) if(details.AppScroll+i<settings.Network.RoutingEntries.Count && l.Remove[i].Contains(point)) return "app-remove:"+i;
   if(l.Add.Contains(point)) return "app-add"; if(l.Previous.Contains(point)) return "apps-prev"; if(l.Next.Contains(point)) return "apps-next";
   if(l.Kill.Contains(point)) return "kill"; if(l.Recovery.Contains(point)) return "network-recover"; return "";
  }
  bool NetworkActionEnabled(string id) {
   if(!details.SettingsPage) return false;
   if(id=="launch-at-login" || id=="connect-at-login") return installed && !closing && storageReady;
   if(id=="apps-prev") return details.AppScroll>0;
   if(id=="apps-next") return details.AppScroll+3<settings.Network.RoutingEntries.Count;
   if(id=="network-recover") return connection==null && !busy && !closing;
   return !busy && !closing && storageReady;
  }
  async Task NetworkSettingsAction(string id) {
   if(!NetworkActionEnabled(id)) return;
   if(id=="launch-at-login") { LoginStartup.Set(!launchAtLogin); launchAtLogin=LoginStartup.Enabled; }
   else if(id=="connect-at-login") settings.ConnectAtLogin=!settings.ConnectAtLogin;
   else if(id=="apps-prev") details.AppScroll=Math.Max(0,details.AppScroll-3);
   else if(id=="apps-next") details.AppScroll+=3;
   else if(id=="split-apps" || id=="split-sites") { settings.Network.ByDomain=id=="split-sites"; details.AppScroll=0; }
   else if(id.StartsWith("route-")) settings.Network.Mode=id.Substring(6);
   else if(id=="kill") settings.Network.KillSwitch=!settings.Network.KillSwitch;
   else if(id.StartsWith("app-remove:")) {
    var entries=settings.Network.RoutingEntries;
    int index;
    if(int.TryParse(id.Substring(11),out index)) {
     index+=details.AppScroll;
     if(index>=0 && index<entries.Count) entries.RemoveAt(index);
    }
    details.AppScroll=Math.Min(details.AppScroll,Math.Max(0,entries.Count-1)/3*3);
   }
   else if(id=="app-add" && settings.Network.ByDomain) {
    if(!AddSite()) return;
   }
   else if(id=="app-add") using(var picker=new RoutingPicker(settings.Network.Applications)) {
    if(picker.ShowDialog(this)!=DialogResult.OK || !NetworkActionEnabled(id)) return;
    try {
     var copy=settings.Network.Snapshot();
     foreach(string path in picker.SelectedPaths) {
      string normalized=NetworkPolicy.NormalizePath(path);
      if(!copy.Applications.Contains(normalized,StringComparer.OrdinalIgnoreCase)) copy.Applications.Add(normalized);
     }
     settings.Network=copy.Snapshot();
    } catch(SafeException e) { Error(e.Message); return; }
   }
   else if(id=="network-recover") { await RecoverNetwork(); return; }
   settings.Network=settings.Network.Snapshot(); Save(); UpdateControls(); NotifyAccess();
  }
  bool AddSite() {
   if(settings.Network.Domains.Count>=64) { Error("Можно выбрать до 64 сайтов."); return false; }
   using(var dialog=new RoutingPicker(settings.Network.Domains,sites:true)) {
    if(dialog.ShowDialog(this)!=DialogResult.OK || !NetworkActionEnabled("app-add")) return false;
    settings.Network.Domains.Add(dialog.SelectedDomain);
    details.AppScroll=(settings.Network.Domains.Count-1)/3*3;
    return true;
   }
  }
  async Task RecoverNetwork() {
   if(connection!=null || busy) return;
   busy=true; UpdateControls();
   try {
    int result=await Task.Run(()=> {
     if(installed) return VpnService.Recover();
     using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--recover-network") {UseShellExecute=true,Verb="runas",WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden})) {process.WaitForExit(); return process.ExitCode;}
    });
    if(result!=0) throw new SafeException("Не удалось снять правила VPN. Повторите восстановление с правами администратора.");
    Error("Правила блокировки VPN сняты. Остальные правила Windows не изменены.");
   } catch(System.ComponentModel.Win32Exception) { Error("Восстановление отменено: Windows не предоставила права администратора."); }
   finally {busy=false;UpdateControls();}
  }
  List<Node> NetworkSettingsNodes() {
   var l=new NetworkSettingsLayout(LayoutNow); var nodes=new List<Node>();
   Action<string,string,RectangleF,AccessibleRole> add=(id,name,r,role)=>nodes.Add(new Node {Id=id,Name=name,Bounds=r,Role=role,Enabled=id=="theme" || NetworkActionEnabled(id)});
   add("theme","Светлая тема",l.Theme,AccessibleRole.CheckButton);
   if(installed) {
    add("launch-at-login","Запускать при входе в Windows",l.LaunchAtLogin,AccessibleRole.CheckButton);
    add("connect-at-login","Подключать VPN при автозапуске",l.ConnectAtLogin,AccessibleRole.CheckButton);
   }
   bool sites=settings.Network.ByDomain;
   add("split-apps","По приложениям",l.ScopeApps,AccessibleRole.CheckButton);
   add("split-sites","По сайтам",l.ScopeSites,AccessibleRole.CheckButton);
   string[] modes={"all","include","exclude"},names={sites?"Весь трафик через VPN":"Все приложения","VPN только для выбранных","VPN для всех, кроме выбранных"};
   for(int i=0;i<3;i++) add("route-"+modes[i],names[i],l.Modes[i],AccessibleRole.CheckButton);
   var entries=settings.Network.RoutingEntries;
   for(int i=0;i<3;i++) if(details.AppScroll+i<entries.Count) add("app-remove:"+i,"Удалить "+(sites?entries[details.AppScroll+i]:Path.GetFileName(entries[details.AppScroll+i])),l.Remove[i],AccessibleRole.PushButton);
   add("app-add",sites?"Добавить сайт":"Добавить приложение",l.Add,AccessibleRole.PushButton); add("apps-prev","Предыдущие записи",l.Previous,AccessibleRole.PushButton); add("apps-next","Следующие записи",l.Next,AccessibleRole.PushButton);
   add("kill","Kill switch",l.Kill,AccessibleRole.CheckButton); add("network-recover","Восстановить интернет",l.Recovery,AccessibleRole.PushButton); return nodes;
  }
 }
}
