using System;
using System.Text;
using System.Linq;
namespace Luna {
 public static class Tests {
  static int failed;
  static void Test(string name, Action body) { try { body(); Console.WriteLine("PASS " + name); } catch(Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
  sealed class SlowPrimaryProbe : System.Net.Http.HttpMessageHandler {
   protected override async System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,System.Threading.CancellationToken token) {
    if(request.RequestUri.Host=="cp.cloudflare.com") await System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite,token);
    return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
   }
  }
  static void DashboardFeatures() {
   NetworkPolicyTests.Run(Test,Assert);
   KillSwitchTests.Run(Test,Assert);
   NetworkSettingsTests.Run(Test,Assert);
   ProtocolTests.Run(Test,Assert);
   Test("Startup probe uses the backup without waiting for the primary timeout", delegate {
    using(var client=new System.Net.Http.HttpClient(new SlowPrimaryProbe())) {
     var watch=System.Diagnostics.Stopwatch.StartNew();
     Assert(Worker.Probe(client,true,System.Threading.CancellationToken.None).GetAwaiter().GetResult());
     Assert(watch.ElapsedMilliseconds<2000);
    }
    Assert(!UiState.IsOnline(CoreFailure.Category("VERIFYING"),true,false,false));
   });
   Test("Switch message accepts a validated server and keeps STOP distinct", delegate {
    var server=Subscriptions.Parse("trojan://synthetic@example.com:443").Servers[0];
    Assert(Worker.ReadSwitchServer("SWITCH "+Json.Write(server)).Host=="example.com");
    Assert(Worker.ReadSwitchServer("STOP")==null && Worker.ReadSwitchServer("RECOVER")==null);
    Assert(!UiState.IsOnline(CoreFailure.Category("SWITCHING"),true,false,false));
   });
   Test("Server rows can scroll while switching without accepting another selection", delegate {
    var list=new DashboardList { CanChoose=false,Viewport=128,Selected=0 };
    for(int i=0;i<8;i++) list.Items.Add(new Server());
    int offset=4*DashboardList.RowPitch;
    list.ScrollTo(offset); list.Choose(4);
    Assert(list.Scroll==offset && list.Selected==0);
    int events=0; list.SelectedChanged+=delegate { events++; };
    list.CanChoose=true; list.Choose(4); list.Choose(4);
    Assert(list.Selected==4 && events==1);
   });
   Test("Live resize preserves the connection card geometry and pixels", delegate {
    var list=new DashboardList();
    var action=new UiAction("Подключить") { Enabled=true };
    var details=new DashboardDetails();
    using(var small=new System.Drawing.Bitmap(900,640)) using(var large=new System.Drawing.Bitmap(1280,800)) {
     var first=new DashboardLayout(small.Size); var second=new DashboardLayout(large.Size);
     Assert(first.ConnectCard==second.ConnectCard && first.List.Width<second.List.Width && first.List.Height<second.List.Height);
     using(var g=System.Drawing.Graphics.FromImage(small)) Dashboard.DrawLive(g,first,list,action,action,action,"STOPPED",false,"","",-1,false,details);
     using(var g=System.Drawing.Graphics.FromImage(large)) Dashboard.DrawLive(g,second,list,action,action,action,"STOPPED",false,"","",-1,false,details);
     for(int y=85;y<350;y+=3) for(int x=30;x<300;x+=3) Assert(small.GetPixel(x,y)==large.GetPixel(x,y));
    }
   });
   Test("Title bar logo pixels stay stable during live resize", delegate {
    var list=new DashboardList();
    var action=new UiAction("Подключить");
    foreach(float scale in new[]{1f,1.5f}) foreach(bool light in new[]{false,true}) {
     var details=new DashboardDetails { Light=light };
     using(var normal=new System.Drawing.Bitmap((int)(900*scale),(int)(640*scale)))
     using(var resized=new System.Drawing.Bitmap((int)(1040*scale),(int)(720*scale))) {
      using(var g=System.Drawing.Graphics.FromImage(normal)) {
       g.ScaleTransform(scale,scale);
       Dashboard.Draw(g,new DashboardLayout(new System.Drawing.Size(900,640)),list,action,action,action,"STOPPED",false,"","",-1,false,details);
      }
      using(var g=System.Drawing.Graphics.FromImage(resized)) {
       g.ScaleTransform(scale,scale);
       Dashboard.DrawLive(g,new DashboardLayout(new System.Drawing.Size(1040,720)),list,action,action,action,"STOPPED",false,"","",-1,false,details);
      }
      for(int y=(int)(12*scale);y<36*scale;y++)
       for(int x=(int)(23*scale);x<47*scale;x++) Assert(normal.GetPixel(x,y)==resized.GetPixel(x,y));
     }
    }
   });
   Test("VPN service control pipe reads the real client identity", delegate {
    using(var identity=System.Security.Principal.WindowsIdentity.GetCurrent()) {
     string name="doomra-test-"+Guid.NewGuid().ToString("N");
     using(var listener=VpnService.CreateListener(identity.User,name)) using(var client=VpnService.CreateClient(name)) {
      var wait=System.Threading.Tasks.Task.Factory.FromAsync(listener.BeginWaitForConnection,listener.EndWaitForConnection,null);
      client.Connect(2000); Assert(wait.Wait(2000));
      Wire.Send(client,"RECOVER"); Assert(Wire.Read(listener)=="RECOVER");
      uint pid;
      Assert(Native.GetNamedPipeClientProcessId(listener.SafePipeHandle,out pid) && pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id);
      listener.RunAsClient(delegate {
       using(var peer=System.Security.Principal.WindowsIdentity.GetCurrent(true)) Assert(peer!=null && identity.User.Equals(peer.User));
      });
     }
    }
   });
   Test("Service accepts only a random session pipe name", delegate {
    var name="LunaVPN-"+Guid.NewGuid().ToString("N");
    Assert(VpnService.IsSessionPipe(name) && !VpnService.IsSessionPipe(name+"\n") && !VpnService.IsSessionPipe(name+" --recover-network") && !VpnService.IsSessionPipe(null));
   });
   Test("Login launch command quotes the executable and VPN auto-connect is opt-in", delegate {
    Assert(LoginStartup.Command.StartsWith("\"") && LoginStartup.Command.EndsWith("\" --autostart"));
    Assert(!new Settings().ConnectAtLogin && Json.Read<Settings>(Json.Write(new Settings { ConnectAtLogin=true })).ConnectAtLogin);
   });
   Test("Partial animation frames match a full dashboard repaint", delegate {
    var layout=new DashboardLayout(new System.Drawing.Size(900,640));
    var list=new DashboardList { Selected=0 }; var action=new UiAction("Подключить") { Enabled=true };
    var server=new Server { Name="Netherlands",Host="example.com",Protocol="vless",Transport="tcp" };
    list.Items.Add(server);
    foreach(bool light in new[]{false,true}) foreach(float angle in new[]{0f,90f,137f,270f,360f}) {
     var details=new DashboardDetails { Moving=true,Angle=angle,Light=light };
     using(var expected=new System.Drawing.Bitmap(900,640)) using(var actual=new System.Drawing.Bitmap(900,640)) {
      using(var g=System.Drawing.Graphics.FromImage(expected)) Dashboard.Draw(g,layout,list,action,action,action,"CONNECTING",false,"","",-1,false,details);
      details.Angle=angle+30;
      using(var g=System.Drawing.Graphics.FromImage(actual)) Dashboard.Draw(g,layout,list,action,action,action,"CONNECTING",false,"","",-1,false,details);
      details.Angle=angle;
      using(var g=System.Drawing.Graphics.FromImage(actual)) { g.SetClip(System.Drawing.RectangleF.Inflate(layout.ConnectCard,2,2)); Dashboard.DrawConnection(g,layout,action,"CONNECTING",false,"","",-1,details); }
      for(int y=0;y<640;y+=3) for(int x=0;x<900;x+=3) Assert(expected.GetPixel(x,y)==actual.GetPixel(x,y));
     }
    }
   });
   Test("Auto-selection servers are skipped on import and removed from saved profiles", delegate {
    const string id="11111111-1111-4111-8111-111111111111";
    var r=Subscriptions.Parse("vless://"+id+"@first.example:443#EU%20АвтоВыбор\nvless://"+id+"@second.example:443#Нидерланды");
    Assert(r.Errors.Count==0 && r.Servers.Count==1 && r.Servers[0].Host=="second.example");
    var json=Subscriptions.Parse("{\"remarks\":\"🇪🇺 Автовыбор ⚡\",\"outbounds\":[{\"protocol\":\"vless\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"id\":\""+id+"\"}}]}");
    Assert(json.Errors.Count==0 && json.Servers.Count==0);
    var settings=new Settings { Selected=1 };
    settings.Servers.Add(new Server { Name="EU АвтоВыбор · VLESS / TCP :443" });
    settings.Servers.Add(r.Servers[0]);
    ProfileStore.Initialize(settings);
    Assert(settings.Servers.Count==1 && settings.Selected==0 && settings.Servers[0]==r.Servers[0]);
   });
   Test("Session footer repaint matches a full repaint after metrics change", delegate {
    var layout=new DashboardLayout(new System.Drawing.Size(900,640));
    var list=new DashboardList();
    var action=new UiAction("Подключить");
    foreach(bool light in new[]{false,true}) {
     var details=new DashboardDetails { Light=light };
     using(var actual=new System.Drawing.Bitmap(900,640)) using(var expected=new System.Drawing.Bitmap(900,640)) {
      using(var g=System.Drawing.Graphics.FromImage(actual)) Dashboard.Draw(g,layout,list,action,action,action,"CONNECTED",true,"","",-1,false,details);
      details.Elapsed="00:02:35"; details.Download="120 МБ"; details.Upload="8 МБ";
      using(var g=System.Drawing.Graphics.FromImage(expected)) Dashboard.Draw(g,layout,list,action,action,action,"CONNECTED",true,"","",-1,false,details);
      using(var g=System.Drawing.Graphics.FromImage(actual)) {
       g.SetClip(System.Drawing.RectangleF.Inflate(layout.Footer,2,2));
       Dashboard.DrawSessionFooter(g,layout,details,true);
      }
      for(int y=(int)layout.Footer.Top;y<layout.Footer.Bottom;y++)
       for(int x=(int)layout.Footer.Left;x<layout.Footer.Right;x++) Assert(actual.GetPixel(x,y)==expected.GetPixel(x,y));
     }
    }
   });
   Test("Server viewport repaint matches a full repaint after scrolling", delegate {
    var layout=new DashboardLayout(new System.Drawing.Size(900,640));
    var list=new DashboardList { Viewport=(int)layout.List.Height,Selected=5 };
    for(int i=0;i<30;i++) list.Items.Add(new Server { Name="Server "+i,Host="example.com",Protocol="vless",Transport="tcp" });
    var action=new UiAction("Подключить");
    foreach(bool light in new[]{false,true}) {
     var details=new DashboardDetails { Light=light };
     using(var expected=new System.Drawing.Bitmap(900,640)) using(var actual=new System.Drawing.Bitmap(900,640)) {
      list.ScrollTo(0);
      using(var g=System.Drawing.Graphics.FromImage(actual)) Dashboard.Draw(g,layout,list,action,action,action,"STOPPED",false,"","",-1,false,details);
      list.ScrollTo(327);
      using(var g=System.Drawing.Graphics.FromImage(expected)) Dashboard.Draw(g,layout,list,action,action,action,"STOPPED",false,"","",-1,false,details);
      using(var g=System.Drawing.Graphics.FromImage(actual)) Dashboard.DrawServerViewport(g,layout,list,light);
      for(int y=0;y<640;y+=3) for(int x=0;x<900;x+=3) Assert(expected.GetPixel(x,y)==actual.GetPixel(x,y));
     }
    }
   });
   Test("Main refresh is accessible and disabled without a profile or while busy", delegate {
    var previous=System.Threading.SynchronizationContext.Current;
    try {
     foreach(bool hasProfile in new[]{false,true}) using(var form=new MainForm(new Settings { Subscription=hasProfile?"https://example.invalid/synthetic":"" })) {
      var handle=form.Handle; var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
      Func<System.Windows.Forms.AccessibleObject> refresh=()=>Enumerable.Range(0,form.AccessibilityObject.GetChildCount()).Select(i=>form.AccessibilityObject.GetChild(i)).FirstOrDefault(n=>n.Name=="Обновить серверы профиля");
      Assert(refresh()!=null);
      Assert(((refresh().State&System.Windows.Forms.AccessibleStates.Unavailable)==0)==hasProfile);
      form.GetType().GetField("busy",flags).SetValue(form,true);
      form.GetType().GetMethod("UpdateControls",flags).Invoke(form,null);
      Assert((refresh().State&System.Windows.Forms.AccessibleStates.Unavailable)!=0);
      if(hasProfile) {
       var task=(System.Threading.Tasks.Task)form.GetType().GetMethod("RefreshCurrentProfile",flags).Invoke(form,null);
       Assert(task.IsCompleted); task.GetAwaiter().GetResult(); Assert(form.Controls.Count==0);
      }
     }
    } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previous); }
   });
   Test("Uploaded portrait flags override five card backgrounds without replacing badges", delegate {
    foreach(string code in new[]{"US","NL","DE","SE","FI"}) { var image=CountryFlags.GetBackground(code); Assert(image!=null && image.Width==840 && image.Height==960); Assert(CountryFlags.Get(code).Width==250); }
    Assert(CountryFlags.GetBackground("CA")!=null);
   });
   Test("Deleting inactive profile preserves current server and device ID", delegate {
    var settings=new Settings { DeviceId="Luna-unchanged-device",Subscription="https://example.invalid/one",Servers=new System.Collections.Generic.List<Server>{new Server {Name="One"}} };
    ProfileStore.Initialize(settings); string first=settings.ActiveProfile;
    ProfileStore.Upsert(settings,null,"Второй","https://example.invalid/two",null,new System.Collections.Generic.List<Server>{new Server {Name="Two"}});
    string second=settings.ActiveProfile; ProfileStore.Switch(settings,first); ProfileStore.Remove(settings,second);
    Assert(settings.Profiles.Count==1 && settings.ActiveProfile==first && settings.Servers[0].Name=="One" && settings.DeviceId=="Luna-unchanged-device");
   });
   Test("Deleting active and final profiles clears legacy credentials on reload", delegate {
    var settings=new Settings { Subscription="https://example.invalid/secret-one",DeviceId="Luna-unchanged-device" };
    ProfileStore.Initialize(settings); string first=settings.ActiveProfile;
    ProfileStore.Upsert(settings,null,"Второй","https://example.invalid/secret-two",null,new System.Collections.Generic.List<Server>{new Server {Name="Two"}});
    ProfileStore.Remove(settings,settings.ActiveProfile); Assert(settings.ActiveProfile==first);
    ProfileStore.Remove(settings,first); ProfileStore.Capture(settings);
    var copy=Json.Read<Settings>(Json.Write(settings)); ProfileStore.Initialize(copy);
    Assert(copy.Profiles.Count==0 && copy.Subscription=="" && copy.SubscriptionInfo==null && copy.Servers.Count==0 && copy.ActiveProfile=="" && copy.DeviceId=="Luna-unchanged-device");
    ProfileStore.Upsert(copy,null,"Снова","https://example.invalid/new",null,new System.Collections.Generic.List<Server>());
    Assert(copy.Profiles.Count==1 && ProfileStore.Current(copy).Name=="Снова");
   });
   Test("Settings is a full page with Back and no server actions", delegate {
    var previous=System.Threading.SynchronizationContext.Current;
    try { using(var form=new MainForm(new Settings())) {
     var handle=form.Handle; var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
     form.GetType().GetMethod("RunAction",flags).Invoke(form,new object[]{"settings"});
     var details=(DashboardDetails)form.GetType().GetField("details",flags).GetValue(form);
     Assert(details.SettingsPage && !details.Dropdown);
     var names=Enumerable.Range(0,form.AccessibilityObject.GetChildCount()).Select(i=>form.AccessibilityObject.GetChild(i).Name).ToList();
     Assert(names.Contains("Назад к серверам") && names.Contains("Светлая тема") && !names.Contains("Список серверов"));
     object[] keys={new System.Windows.Forms.Message(),System.Windows.Forms.Keys.Escape};
     form.GetType().GetMethod("ProcessCmdKey",flags).Invoke(form,keys);
     Assert(!details.SettingsPage && Enumerable.Range(0,form.AccessibilityObject.GetChildCount()).Any(i=>form.AccessibilityObject.GetChild(i).Name=="Список серверов"));
    } } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previous); }
   });
   Test("Profile deletion cancellation keeps draft and confirmation removes credentials", delegate {
    var previous=System.Threading.SynchronizationContext.Current;
    try {
     var settings=new Settings { Subscription="https://example.invalid/synthetic",DeviceId="Luna-stable-delete-id" };
     using(var form=new MainForm(settings)) {
      var handle=form.Handle; var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
      form.GetType().GetMethod("ShowSubscription",flags).Invoke(form,null);
      form.Controls.OfType<System.Windows.Forms.TextBox>().First(t=>t.AccessibleName=="Название профиля").Text="Черновик";
      form.GetType().GetMethod("ConfirmDelete",flags).Invoke(form,null);
      Assert(settings.Profiles.Count==1);
      form.GetType().GetMethod("CancelDelete",flags).Invoke(form,null);
      Assert(form.Controls.OfType<System.Windows.Forms.TextBox>().First(t=>!t.UseSystemPasswordChar).Text=="Черновик");
      form.GetType().GetMethod("ConfirmDelete",flags).Invoke(form,null);
      var task=(System.Threading.Tasks.Task)form.GetType().GetMethod("DeleteProfile",flags).Invoke(form,null);
      Assert(task.IsCompleted); task.GetAwaiter().GetResult();
      Assert(settings.Profiles.Count==0 && settings.Subscription=="" && settings.DeviceId=="Luna-stable-delete-id" && form.Controls.Count==0);
      object[] down={new System.Windows.Forms.Message(),System.Windows.Forms.Keys.Down};
      object[] enter={new System.Windows.Forms.Message(),System.Windows.Forms.Keys.Enter};
      form.GetType().GetMethod("ProcessCmdKey",flags).Invoke(form,down); form.GetType().GetMethod("ProcessCmdKey",flags).Invoke(form,enter);
      Assert(form.Controls.Count==2);
     }
    } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previous); }
   });
   Test("Native text keeps its device clip at 150 percent scale", delegate {
    using(var bitmap=new System.Drawing.Bitmap(400,240)) using(var g=System.Drawing.Graphics.FromImage(bitmap)) {
     g.Clear(System.Drawing.Color.Black); g.ScaleTransform(1.5f,1.5f); g.SetClip(new System.Drawing.RectangleF(10,10,200,100));
     typeof(Dashboard).GetMethod("Label",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{g,"MMMM",new System.Drawing.RectangleF(20,80,100,24),14f,System.Drawing.Color.White,false,false,false});
     int bright=0; for(int y=120;y<156;y++) for(int x=30;x<180;x++) if(bitmap.GetPixel(x,y).R>100) bright++;
     if(bright<=20) { int first=-1,last=-1; for(int y=0;y<240;y++) for(int x=0;x<400;x++) if(bitmap.GetPixel(x,y).R>100) {if(first<0) first=y;last=y;} throw new Exception("Text pixels="+bright+" y="+first+".."+last); } typeof(Dashboard).GetMethod("Label",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{g,"MMMM",new System.Drawing.RectangleF(20,112,100,24),14f,System.Drawing.Color.White,false,false,false});
     int outside=0; for(int y=170;y<220;y++) for(int x=30;x<180;x++) if(bitmap.GetPixel(x,y).R>100) outside++;
     Assert(outside==0);
    }
   });
   Test("Accessibility retained actions follow current text and window geometry", delegate {
    var previous=System.Threading.SynchronizationContext.Current;
    try { using(var form=new MainForm(new Settings())) {
     var handle=form.Handle; var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
     var connect=form.AccessibilityObject.GetChild(0); var check=form.AccessibilityObject.GetChild(2); var before=check.Bounds;
     ((UiAction)form.GetType().GetField("connect",flags).GetValue(form)).Text="Отменить";
     form.ClientSize=new System.Drawing.Size(form.ClientSize.Width+100,form.ClientSize.Height);
     Assert(connect.Name=="Отменить" && check.Bounds.X>before.X);
    } } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previous); }
   });
   Test("Profile editor renames unchanged URL without starting download", delegate {
    var previous=System.Threading.SynchronizationContext.Current;
    try {
     var settings=new Settings { Subscription="https://example.invalid/synthetic" };
     using(var form=new MainForm(settings)) {
      var handle=form.Handle; var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
      form.GetType().GetMethod("ShowSubscription",flags).Invoke(form,null);
      form.Controls.OfType<System.Windows.Forms.TextBox>().First(t=>!t.UseSystemPasswordChar).Text="Дом";
      var task=(System.Threading.Tasks.Task)form.GetType().GetMethod("AddSubscription",flags).Invoke(form,null);
      Assert(task.IsCompleted); task.GetAwaiter().GetResult();
      Assert(ProfileStore.Current(settings).Name=="Дом" && settings.Profiles.Count==1 && form.Controls.Count==0);
     }
    } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previous); }
   });
   Test("Selecting current profile preserves the session display", delegate {
    var previous=System.Threading.SynchronizationContext.Current;
    try { using(var form=new MainForm(new Settings())) {
     var handle=form.Handle; var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
     var details=(DashboardDetails)form.GetType().GetField("details",flags).GetValue(form); details.Elapsed="01:02:03";
     form.GetType().GetMethod("ShowProfiles",flags).Invoke(form,null);
     form.GetType().GetMethod("RunAction",flags).Invoke(form,new object[]{"profile:0"});
     Assert(details.Elapsed=="01:02:03" && !details.Dropdown);
    } } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previous); }
   });
   Test("Settings light theme toggle updates palette and stored preference", delegate {
    var previous=System.Threading.SynchronizationContext.Current;
    try {
     var settings=new Settings();
     using(var form=new MainForm(settings)) {
      var handle=form.Handle; var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
      form.GetType().GetMethod("OpenMenu",flags).Invoke(form,new object[]{"settings"});
      form.GetType().GetMethod("RunAction",flags).Invoke(form,new object[]{"theme"});
      Assert(settings.LightTheme && Palette.Light && Palette.Background.R>=210 && Palette.Background.R<=230 && Palette.Text.R<50);
      var child=Enumerable.Range(0,form.AccessibilityObject.GetChildCount()).Select(i=>form.AccessibilityObject.GetChild(i)).First(n=>n.Name=="Светлая тема");
      Assert((child.State&System.Windows.Forms.AccessibleStates.Checked)!=0);
     }
    } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previous); }
   });
   Test("Profiles migrate legacy subscription without changing installation ID", delegate {
    var settings=new Settings { Subscription="https://example.invalid/original",DeviceId="Luna-stable-device",Selected=3,Servers=new System.Collections.Generic.List<Server>{new Server {Name="Legacy"}} };
    ProfileStore.Initialize(settings); Assert(settings.Profiles.Count==1 && ProfileStore.Current(settings).Name=="Основной");
    Assert(settings.Servers[0].Name=="Legacy" && settings.Selected==3 && settings.DeviceId=="Luna-stable-device");
    string id=settings.ActiveProfile; ProfileStore.Initialize(settings); Assert(settings.Profiles.Count==1 && settings.ActiveProfile==id);
   });
   Test("Profiles keep servers selections and metadata isolated across switches", delegate {
    var settings=new Settings { Subscription="https://example.invalid/one",Servers=new System.Collections.Generic.List<Server>{new Server {Name="One"}},Selected=1 };
    ProfileStore.Initialize(settings); string original=settings.ActiveProfile;
    ProfileStore.Upsert(settings,null,"Работа","https://example.invalid/two",SubscriptionInfo.Parse("upload=1;download=2"),new System.Collections.Generic.List<Server>{new Server {Name="Two"}});
    Assert(settings.Profiles.Count==2 && settings.Servers[0].Name=="Two" && settings.SubscriptionInfo.Used==3);
    string second=settings.ActiveProfile; ProfileStore.Switch(settings,original);
    Assert(settings.Servers[0].Name=="One" && settings.Selected==1 && settings.Subscription=="https://example.invalid/one");
    ProfileStore.Rename(settings,second,"Личный"); ProfileStore.Switch(settings,second);
    Assert(ProfileStore.Current(settings).Name=="Личный" && settings.Servers[0].Name=="Two");
    settings.LightTheme=true; ProfileStore.Capture(settings);
    var copy=Json.Read<Settings>(Json.Write(settings)); ProfileStore.Initialize(copy); Assert(copy.LightTheme && copy.Profiles.Count==2 && copy.Servers[0].Name=="Two");
   });
   Test("Profile names reject empty and control characters", delegate {
    foreach(string name in new[]{"","  ","a\nb",new string('a',49)}) { bool caught=false; try { ProfileStore.CleanName(name); } catch(SafeException) {caught=true;} Assert(caught); }
   });
   Test("Dashboard country display names keep custom remarks and original source", delegate {
    var server=new Server { Name="germany #3 DE",Host="example.invalid",Protocol="vless" };
    Assert(Dashboard.ServerTitle(server)=="Германия · #3"); Assert(server.Name=="germany #3 DE");
    Assert(Dashboard.ServerTitle(new Server { Name="usa #3 US",Protocol="vless" })=="США · #3");
    Assert(Dashboard.ServerTitle(new Server { Name="business custom gateway",Protocol="vless" })=="business custom gateway");
    Assert(Dashboard.ServerTitle(new Server { Name="No limits",Protocol="vless" })=="No limits");
    Assert(Dashboard.ServerTitle(new Server { Name="At home",Protocol="vless" })=="At home");
    Assert(CountryFlags.Resolve("No limits","example.invalid")=="");
    Assert(CountryFlags.Resolve("At home","example.invalid")=="");
   });
   Test("Dashboard compact list scroll exposes last selected row", delegate {
    var list=new DashboardList { Viewport=128 }; for(int i=0;i<10;i++) list.Items.Add(new Server());
    int lastPage=9*DashboardList.RowPitch+DashboardList.RowHeight-list.Viewport;
    list.Choose(9); Assert(list.Scroll==lastPage && list.MaxScroll==lastPage);
    list.Choose(0); Assert(list.Scroll==0);
   });
   Test("Dashboard global check belongs to server list at both window sizes", delegate {
    foreach(var size in new[]{new System.Drawing.Size(1040,720),new System.Drawing.Size(900,640)}) {
     var layout=new DashboardLayout(size); Assert(layout.ServersCard.Contains(layout.Check) && !layout.Check.IntersectsWith(layout.Add));
     Assert(layout.Hit(new System.Drawing.PointF(layout.Check.X+5,layout.Check.Y+5))=="check");
     Assert(layout.List.Bottom<layout.Footer.Top && !layout.Popup.IntersectsWith(layout.Add));
    }
   });
   Test("Dashboard subscription header supports missing fields and unlimited", delegate {
    var m=SubscriptionInfo.Parse("upload=1024; download=2048; total=0; expire=0"); Assert(m.Used==3072 && m.Total==0 && m.Expire==0);
    Assert(SubscriptionInfo.Parse(null).Used==null); Assert(SubscriptionInfo.Parse("download=5").Used==null);
   });
   Test("Dashboard subscription header rejects invalid, duplicate and overflow values", delegate {
    var m=SubscriptionInfo.Parse("upload=-1; download=12; expire=999999999999999; total=abc"); Assert(m.Upload==null && m.Total==null && m.Expire==null);
    Assert(SubscriptionInfo.Parse("upload=1; upload=2; download=3").Used==null);
    Assert(SubscriptionInfo.Parse("upload=9223372036854775807; download=1").Used==null);
   });
   Test("Dashboard traffic counts only deltas and rebases on adapter reset", delegate {
    var t=new TrafficAccumulator(); t.Sample("tun",1000,2000,1); Assert(t.Available && t.Received==0 && t.Sent==0);
    t.Sample("tun",1300,2100,2); Assert(t.Received==300 && t.Sent==100);
    t.Sample("tun",20,30,3); Assert(t.Received==300 && t.Sent==100);
    t.Sample("tun",40,60,4); Assert(t.Received==320 && t.Sent==130);
    t.Gap(); t.Sample("new",9000,9000,5); Assert(t.Received==320);
    t.Reset(); Assert(!t.Available && t.Received==0);
   });
   Test("Connection speed uses the sample interval and clears on loss or counter reset", delegate {
    var t=new TrafficAccumulator();
    t.Sample("tun",1000,2000,1); Assert(!t.ReceivedPerSecond.HasValue);
    t.Sample("tun",3001000,1002000,2.5);
    Assert(t.ReceivedPerSecond==2000000 && Math.Abs(t.SentPerSecond.Value-1000000d/1.5)<.001);
    Assert(MetricText.Speed(t.ReceivedPerSecond,t.SentPerSecond)=="↓ 2,00  ↑ 0,67 МБ/с");
    t.Sample("tun",3001000,1002000,4); Assert(t.ReceivedPerSecond==0 && t.SentPerSecond==0);
    t.Sample("tun",10,20,5); Assert(!t.ReceivedPerSecond.HasValue && !t.SentPerSecond.HasValue);
    t.Gap(); t.Sample("tun",5000010,1000020,7); Assert(!t.ReceivedPerSecond.HasValue);
    t.Sample("tun",6000010,1000020,8); Assert(t.ReceivedPerSecond==1000000 && t.SentPerSecond==0);
    t.Reset(); Assert(!t.ReceivedPerSecond.HasValue && !t.SentPerSecond.HasValue);
   });
   Test("Dashboard logo completes a smooth full turn in 0.8 seconds", delegate {
    Assert(LogoMotion.Angle(0)==0 && LogoMotion.Angle(.8)==360 && LogoMotion.Angle(1)==360);
    Assert(Math.Abs(LogoMotion.Angle(.4)-180)<.001);
    Assert(LogoMotion.Angle(.02)<LogoMotion.Angle(.4)-LogoMotion.Angle(.38));
    Assert(LogoMotion.Angle(.5)<LogoMotion.Angle(.7));
   });
   Test("Dashboard probe queue is sequential and continues failed endpoints", delegate {
    var list=new System.Collections.Generic.List<Server> {new Server(),new Server(),new Server()};
    int active=0,max=0,done=0;
    ProbeQueue.Run(list,async (server,token)=>{ active++; max=Math.Max(max,active); await System.Threading.Tasks.Task.Delay(5); active--; return done!=1?(int?)42:null; },
     (i,status)=>{ if(status.Text!="Проверка…") { Assert(i==done && status.CheckedAt.HasValue && status.LatencyMilliseconds==(i==1?(int?)null:42)); done++; } else Assert(!status.CheckedAt.HasValue); },System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    Assert(max==1 && done==3);
   });
   Test("Batch probes start three at a time and report each server once", delegate {
    var list=Enumerable.Range(0,8).Select(i=>new Server()).ToList();
    var release=new System.Threading.Tasks.TaskCompletionSource<int?>(); int started=0,done=0;
    var run=ProbeQueue.Run(list,(server,token)=>{System.Threading.Interlocked.Increment(ref started); return release.Task;},
     (i,status)=>{if(status.CheckedAt.HasValue) System.Threading.Interlocked.Increment(ref done);},System.Threading.CancellationToken.None,3);
    Assert(started==3 && done==0);
    release.SetResult(42); run.GetAwaiter().GetResult();
    Assert(started==8 && done==8);
   });
   Test("Server checks persist in encrypted profiles and survive a rename during refresh", delegate {
    string folder=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N"));
    string path=System.IO.Path.Combine(folder,"settings.dat");
    var settings=new Settings { Servers=Subscriptions.Parse("trojan://synthetic@example.com:443#Before").Servers };
    ProfileStore.Initialize(settings);
    var server=settings.Servers[0]; var profile=ProfileStore.Current(settings);
    Assert(Json.Read<SavedServerCheck>("{\"Status\":\"Доступен\",\"UnixSeconds\":1794000000}").Read().LatencyMilliseconds==null);
    profile.Checks[server.CheckKey()]=new SavedServerCheck { Status="Доступен",UnixSeconds=1794000000,LatencyMilliseconds=42 };
    try {
     Storage.Save(path,settings); settings=Storage.Load(path);
     profile=ProfileStore.Current(settings);
     var renamed=Subscriptions.Parse("trojan://synthetic@example.com:443#After").Servers;
     ProfileStore.Upsert(settings,profile.Id,profile.Name,"",null,renamed);
     var record=profile.Checks[renamed[0].CheckKey()].Read();
     Assert(record.Text=="Доступен" && record.CheckedAt.Value.ToUnixTimeSeconds()==1794000000 && record.LatencyMilliseconds==42);
     var other=Subscriptions.Parse("trojan://synthetic@elsewhere.example:443").Servers;
     ProfileStore.Upsert(settings,profile.Id,profile.Name,"",null,other);
     Assert(profile.Checks.Count==0);
    } finally { if(System.IO.File.Exists(path)) System.IO.File.Delete(path); if(System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder); }
   });
   Test("Dashboard subscription metadata survives serialization", delegate {
    var settings=new Settings { SubscriptionInfo=SubscriptionInfo.Parse("upload=100; download=200; total=1000; expire=1800000000") };
    var copy=Json.Read<Settings>(Json.Write(settings)); Assert(copy.SubscriptionInfo.Used==300 && copy.SubscriptionInfo.Total==1000 && copy.SubscriptionInfo.Expire==1800000000);
   });
   Test("Dashboard inline subscription opens and Escape dismisses without fetching", delegate {
    var previousContext=System.Threading.SynchronizationContext.Current;
    try { using(var form=new MainForm(new Settings())) {
     var handle=form.Handle;
     var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
     form.GetType().GetMethod("ShowSubscription",flags).Invoke(form,null);
     Assert(form.Controls.Count==2 && form.Controls[0] is System.Windows.Forms.TextBox);
     Assert(Enumerable.Range(0,form.AccessibilityObject.GetChildCount()).Any(i=>form.AccessibilityObject.GetChild(i).Name=="Сохранить профиль"));
     form.Controls.OfType<System.Windows.Forms.TextBox>().First(t=>t.AccessibleName=="HTTPS-ссылка подписки").Text="invalid";
     form.GetType().GetMethod("AddSubscription",flags).Invoke(form,null);
     Assert(form.Controls.Count==2);
     form.GetType().GetField("focus",flags).SetValue(form,11);
     typeof(System.Windows.Forms.Control).GetMethod("OnGotFocus",flags).Invoke(form.Controls[0],new object[]{EventArgs.Empty});
     object[] enter={new System.Windows.Forms.Message(),System.Windows.Forms.Keys.Enter};
     form.GetType().GetMethod("ProcessCmdKey",flags).Invoke(form,enter);
     Assert(form.Controls.Count==2);
     object[] keys={new System.Windows.Forms.Message(),System.Windows.Forms.Keys.Escape};
     form.GetType().GetMethod("ProcessCmdKey",flags).Invoke(form,keys);
     Assert(form.Controls.Count==0 && form.AccessibilityObject.GetChildCount()==11);
    } } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext); }
   });
   Test("Dashboard probe queue continues after a broken configuration", delegate {
    int count=0,done=0;
    ProbeQueue.Run(new System.Collections.Generic.List<Server>{new Server(),new Server()},(server,token)=>{count++; if(count==1) throw new SafeException("synthetic");return System.Threading.Tasks.Task.FromResult<int?>(42);},(i,result)=>{if(result.Text!="Проверка…") { Assert(result.Text==(i==0?"Ошибка проверки":"Доступен") && result.CheckedAt.HasValue && result.TimeLabel.Length==16); done++; }},System.Threading.CancellationToken.None).GetAwaiter().GetResult();
    Assert(count==2 && done==2);
   });
   Test("Dashboard probe queue stops before next server on cancellation", delegate {
    using(var cancel=new System.Threading.CancellationTokenSource()) {
     int calls=0; bool cancelled=false;
     try { ProbeQueue.Run(new System.Collections.Generic.List<Server>{new Server(),new Server()},(server,token)=>{calls++;cancel.Cancel();return System.Threading.Tasks.Task.FromResult<int?>(null);},(i,status)=>{},cancel.Token).GetAwaiter().GetResult(); }
     catch(OperationCanceledException) { cancelled=true; }
     Assert(cancelled && calls==1);
    }
   });
  }
  static void Assert(bool value) { if(!value) throw new Exception("Assertion failed"); }
  static void Reject(string uri) { var r=Subscriptions.Parse(uri); Assert(r.Servers.Count==0 && r.Errors.Count>0); Assert(!r.Errors[0].Contains("secret")); }
  [STAThread] public static int Main(string[] args) {
   if(args.Length>0 && args[0]=="--sleep") { System.Threading.Thread.Sleep(60000); return 0; }
   if(args.Length==2 && args[0]=="--job-child") { Native.EnrolJob(); var p=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--sleep") { UseShellExecute=false,CreateNoWindow=true }); System.IO.File.WriteAllText(args[1],p.Id.ToString()); System.Threading.Thread.Sleep(60000); return 0; }
   if(args.Length==2 && (args[0]=="--core-stop" || args[0]=="--dns-startup")) {
    Native.EnrolJob(); Native.AllocConsole(); Native.ShowWindow(Native.GetConsoleWindow(),0); Native.ConsoleHandler h=delegate(uint code) { return true; }; Native.SetConsoleCtrlHandler(h,true);
    var info=Configuration.StartInfo(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),"run"); info.CreateNoWindow=false;
    string inputConfig="{\"log\":{\"disabled\":true},\"outbounds\":[{\"type\":\"direct\"}]}";
    if(args[0]=="--dns-startup") {
     var config=Json.Read<System.Collections.Generic.Dictionary<string,object>>(Configuration.Build(Subscriptions.Parse("trojan://synthetic@127.0.0.1:443").Servers[0]));
     config["inbounds"]=new object[0];
     var dns=(System.Collections.Generic.Dictionary<string,object>)config["dns"];
     foreach(System.Collections.Generic.Dictionary<string,object> resolver in (System.Collections.ArrayList)dns["servers"]) resolver["server"]="127.0.0.1";
     inputConfig=Json.Write(config);
    }
    using(var core=new System.Diagnostics.Process { StartInfo=info }) { core.Start(); core.BeginOutputReadLine(); core.BeginErrorReadLine(); using(var input=new System.IO.StreamWriter(core.StandardInput.BaseStream,new UTF8Encoding(false))) input.Write(inputConfig); System.Threading.Thread.Sleep(500); bool alive=!core.HasExited; Worker.StopCore(core); System.IO.File.WriteAllText(args[1],alive?core.ExitCode.ToString():"EARLY_EXIT"); GC.KeepAlive(h); }
    return 0;
   }
   if(args.Length>0 && args[0]=="--dashboard-features") { DashboardFeatures(); return failed; }
   DashboardFeatures();
   string id="11111111-1111-4111-8111-111111111111";
   Test("Bundled JetBrains Mono is loaded without system font installation", delegate { using(var font=UiFonts.Get(14)) using(var bold=UiFonts.Get(14,true)) { Assert(font.FontFamily.Name=="JetBrains Mono"); Assert(bold.FontFamily.Name=="JetBrains Mono" && bold.Bold); } });
   Test("Bundled US and EU flags are usable images", delegate { Assert(CountryFlags.Get("US")!=null && CountryFlags.Get("US").Width>=80); Assert(CountryFlags.Get("EU")!=null); Assert(CountryFlags.Get("CA")!=null); });
   Test("Country flags support USA and regional metadata before hostname", delegate {
    Assert(CountryFlags.Resolve("usa #3 us","netherlands1.example.com")=="US");
    Assert(CountryFlags.Resolve("США #2","example.com")=="US");
    Assert(CountryFlags.Resolve("🇺🇸 Los Angeles","example.com")=="US");
    Assert(CountryFlags.Resolve("EU АвтоВыбор","germany1.example.com")=="EU");
    Assert(CountryFlags.Resolve("NL Нидерланды каскад #1","usa1.example.com")=="NL");
   });
   Test("Country code matching avoids accidental substrings", delegate {
    Assert(CountryFlags.Resolve("Russia #1","example.com")=="RU");
    Assert(CountryFlags.Resolve("business server","example.com")=="");
    Assert(CountryFlags.Resolve("proxy","usa3.example.com")=="US");
    Assert(CountryFlags.Resolve("🇨🇦 Toronto","example.com")=="CA");
   });
   Test("Connection animation keeps a waiting arc until success or a terminal state", delegate {
    foreach(string state in new[]{"CONNECTING","VERIFYING","SWITCHING","RETRYING"}) Assert(LogoMotion.IsPending(state));
    foreach(string state in new[]{"CONNECTED","STOPPING","STOPPED","BLOCKED","ERROR_NETWORK"}) Assert(!LogoMotion.IsPending(state));
    Assert(LogoMotion.ArcAngle(LogoMotion.Duration)==-90 && LogoMotion.ArcAngle(LogoMotion.Duration+.9)>80);
    Assert(LogoMotion.SuccessOpacity(.6,.5)==0 && LogoMotion.SuccessOpacity(.8,.5)==1);
    Assert(LogoMotion.SuccessOpacity(2,2)==1 && LogoMotion.SuccessOpacity(2.2,2)>0 && LogoMotion.SuccessOpacity(2.5,2)==0);
   });
   Test("Windows connection badge renders a green dot with transparent corners", delegate {
    using(var badge=ConnectionBadge.Create())
    using(var bitmap=badge.ToBitmap()) {
     Assert(bitmap.Width==16 && bitmap.Height==16);
     Assert(bitmap.GetPixel(0,0).A==0);
     var center=bitmap.GetPixel(9,9);
     Assert(center.A==255 && center.G>center.R && center.G>center.B);
    }
   });
   Test("Connected tray icon preserves the globe outside the badge", delegate {
    using(var stream=typeof(MainForm).Assembly.GetManifestResourceStream("Doomra.Icon"))
    using(var original=new System.Drawing.Icon(stream,16,16))
    using(var nativeOriginal=System.Drawing.Icon.FromHandle(original.Handle))
    using(var badge=ConnectionBadge.LoadTrayIcon(16))
    using(var source=nativeOriginal.ToBitmap())
    using(var rendered=badge.ToBitmap()) {
     Assert(rendered.Width==16 && rendered.Height==16);
     int matching=0,visible=0;
     for(int y=0;y<8;y++) for(int x=0;x<16;x++) {
      var expected=source.GetPixel(x,y);
      if(expected.A<250) continue;
      visible++;
      var actual=rendered.GetPixel(x,y);
      if(Math.Abs(actual.A-expected.A)<=3 && Math.Abs(actual.R-expected.R)<=6 && Math.Abs(actual.G-expected.G)<=6 && Math.Abs(actual.B-expected.B)<=6) matching++;
     }
     Assert(visible>0 && matching==visible);
     Assert(rendered.GetPixel(0,0).A==0);
     var dot=rendered.GetPixel(11,11);
     Assert(dot.A==255 && dot.G>dot.R && dot.G>dot.B);
    }
   });
   Test("Title indicator turns green only for a live confirmed connection", delegate {
    Assert(UiState.IsOnline("CONNECTED",true,false,false));
    foreach(string state in new[]{"CONNECTING","RETRYING","STOPPING","STOPPED","ERROR_CORE","ERROR_DNS"}) Assert(!UiState.IsOnline(state,true,false,false));
    Assert(!UiState.IsOnline("CONNECTED",false,false,false)); Assert(!UiState.IsOnline("CONNECTED",true,true,false)); Assert(!UiState.IsOnline("CONNECTED",true,false,true));
   });
   Test("Availability check uses loopback proxy only without TUN", delegate {
    var s=Subscriptions.Parse("trojan://synthetic@127.0.0.1:443").Servers[0];
    var config=Json.Read<System.Collections.Generic.Dictionary<string,object>>(ServerCheck.Build(s,18080));
    var inbounds=(System.Collections.ArrayList)config["inbounds"]; Assert(inbounds.Count==1);
    var inbound=(System.Collections.Generic.Dictionary<string,object>)inbounds[0];
    Assert((string)inbound["type"]=="mixed" && (string)inbound["listen"]=="127.0.0.1" && (int)inbound["listen_port"]==18080);
    Assert(!Json.Write(config).Contains("\"type\":\"tun\""));
    Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),Json.Write(config));
   });
   Test("Closed loopback VPN endpoint is not reported available", delegate {
    var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); listener.Start(); int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var s=Subscriptions.Parse("trojan://synthetic@127.0.0.1:"+port).Servers[0];
    Assert(!ServerCheck.Run(s,System.Threading.CancellationToken.None).GetAwaiter().GetResult().HasValue);
   });
   Test("Disposing a probe job kills its child process", delegate {
    using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--sleep") { UseShellExecute=false,CreateNoWindow=true })) {
     try { var job=Native.GuardChild(child); job.Dispose(); Assert(child.WaitForExit(5000)); } finally { if(!child.HasExited) child.Kill(); }
    }
   });
   Test("Unnamed JSON outbound falls back to hostname country hint", delegate { var r=Subscriptions.Parse("{\"outbounds\":[{\"protocol\":\"trojan\",\"settings\":{\"address\":\"germany1.example.com\",\"port\":443,\"password\":\"synthetic\"},\"streamSettings\":{\"security\":\"tls\"}}]}"); Assert(r.Servers.Count==1 && r.Servers[0].Name.Contains("Германия (имя хоста)")); });
   Test("Bootstrap DNS actually initializes without TUN or remote requests", delegate {
    string path=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")+".dns"); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
    using(var harness=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--dns-startup \""+path+"\"") { UseShellExecute=false,CreateNoWindow=true })) {
     try { Assert(harness.WaitForExit(15000)); Assert(harness.ExitCode==0); Assert(System.IO.File.ReadAllText(path)=="0"); }
     finally { if(!harness.HasExited) harness.Kill(); if(System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
   });
   Test("Provider country remarks take priority over technical proxy tag", delegate { var s=new Server { Host="example.com",Protocol="trojan",Transport="tcp",Port=443 }; string name=ServerNames.FromJson("🇩🇪 Германия","proxy-2",s); Assert(name.Contains("Германия") && !name.Contains("proxy-2")); });
   Test("Saved generic names receive honest host-based display fallback", delegate { var s=new Server { Name="proxy",Host="germany1.example.com",Protocol="trojan",Transport="tcp",Port=2096 }; Assert(ServerNames.Display(s).Contains("Германия (имя хоста)")); s.Host="edge.example.com"; Assert(ServerNames.Display(s).Contains("edge.example.com") && !ServerNames.Display(s).Contains("Германия")); });
   Test("Core failure classification never emits raw credentials", delegate { var failure=new CoreFailure(); failure.Observe("FATAL start inbound/tun[tun-in]: Access is denied https://secret-token.example/secret-key"); string status=failure.Status(1); Assert(status=="ERROR_PERMISSION:1" && !status.Contains("secret")); Assert(CoreFailure.Category(status)=="ERROR_PERMISSION"); Assert(CoreFailure.Category("ERROR_CORE:secret")=="ERROR_WORKER"); });
   Test("Core exit captures TUN category and exit code", delegate { var failure=new CoreFailure(); failure.Observe("FATAL start inbound/tun[tun-in]: configure tun interface failed"); Assert(failure.Status(5)=="ERROR_TUN:5"); Assert(CoreFailure.ExitSuffix(failure.Status(5)).Contains("5")); });
   Test("Malformed JSON config does not discard other valid configs", delegate { var r=Subscriptions.Parse("[{\"outbounds\":[{\"protocol\":\"trojan\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"password\":\"secret\"},\"streamSettings\":{\"security\":\"tls\"}}]},null,{\"outbounds\":{}}]"); Assert(r.Servers.Count==1 && r.Errors.Count==2); });
   Test("Conflicting case variants of WebSocket Host rejected", delegate { var r=Subscriptions.Parse("{\"outbounds\":[{\"protocol\":\"trojan\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"password\":\"secret\"},\"streamSettings\":{\"network\":\"ws\",\"security\":\"tls\",\"wsSettings\":{\"headers\":{\"Host\":\"example.com\",\"host\":\"other.example\"}}}}]}"); Assert(r.Servers.Count==0 && r.Errors.Count==1); });
   Test("Large minified flat Xray JSON array and Base64 JSON", delegate { string json="[{\"ignored\":\""+new string('x',20000)+"\",\"outbounds\":[{\"protocol\":\"vless\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"id\":\""+id+"\",\"encryption\":\"none\",\"reverse\":{}},\"streamSettings\":{\"network\":\"grpc\",\"security\":\"tls\",\"grpcSettings\":{\"serviceName\":\"tunnel\"}}}]},{\"outbounds\":[{\"protocol\":\"trojan\",\"settings\":{\"address\":\"example.net\",\"port\":443,\"password\":\"secret\"},\"streamSettings\":{\"network\":\"tcp\",\"security\":\"tls\"}}]}]"; foreach(string data in new[]{json,Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}) { var r=Subscriptions.Parse(data); Assert(r.Servers.Count==2 && r.Errors.Count==0); Assert(r.Servers[0].ServiceName=="tunnel"); } });
   Test("JSON TLS verification bypass rejected without leaking values", delegate { var r=Subscriptions.Parse("{\"outbounds\":[{\"protocol\":\"trojan\",\"settings\":{\"address\":\"example.com\",\"port\":443,\"password\":\"secret\"},\"streamSettings\":{\"security\":\"tls\",\"tlsSettings\":{\"allowInsecure\":true}}}]}"); Assert(r.Servers.Count==0 && r.Errors.Count==1 && !r.Errors[0].Contains("secret")); });
   Test("JSON active proxy chain rejected", delegate { var r=Subscriptions.Parse("{\"outbounds\":[{\"protocol\":\"trojan\",\"proxySettings\":{\"tag\":\"secret\"},\"settings\":{\"address\":\"example.com\",\"port\":443,\"password\":\"secret\"},\"streamSettings\":{\"security\":\"tls\"}}]}"); Assert(r.Servers.Count==0 && r.Errors.Count==1 && !r.Errors[0].Contains("secret")); });
   Test("Happ JSON extracts Trojan WS without executing remote settings", delegate { string json="{\"remarks\":\"Demo\",\"inbounds\":[{\"protocol\":\"http\",\"port\":9999}],\"routing\":{\"rules\":[{\"outboundTag\":\"direct\"}]},\"outbounds\":[{\"tag\":\"Test\",\"protocol\":\"trojan\",\"settings\":{\"servers\":[{\"address\":\"example.com\",\"port\":443,\"password\":\"secret\"}]},\"streamSettings\":{\"network\":\"ws\",\"security\":\"tls\",\"tlsSettings\":{\"serverName\":\"example.com\",\"allowInsecure\":false},\"wsSettings\":{\"path\":\"/ws\",\"headers\":{\"Host\":\"example.com\"}}}},{\"protocol\":\"freedom\",\"tag\":\"direct\"}]}"; var r=Subscriptions.Parse(json); Assert(r.Servers.Count==1 && r.Errors.Count==0); Assert(r.Servers[0].Transport=="ws" && r.Servers[0].Password=="secret"); string config=Configuration.Build(r.Servers[0]); Assert(!config.Contains("9999")); Assert(!config.Contains("outboundTag")); Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),config); });
   Test("Happ JSON VLESS Reality and incompatible XHTTP flow partial import", delegate { string template="{\"protocol\":\"vless\",\"settings\":{\"vnext\":[{\"address\":\"example.com\",\"port\":443,\"users\":[{\"id\":\""+id+"\",\"encryption\":\"none\",\"flow\":\"xtls-rprx-vision\"}]}]},\"streamSettings\":{\"network\":\"TRANSPORT\",\"security\":\"reality\",\"realitySettings\":{\"serverName\":\"example.com\",\"fingerprint\":\"chrome\",\"publicKey\":\"abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG\",\"shortId\":\"abcd\",\"spiderX\":\"/\"}}}"; var r=Subscriptions.Parse("{\"outbounds\":["+template.Replace("TRANSPORT","tcp")+","+template.Replace("TRANSPORT","xhttp")+"]}"); Assert(r.Servers.Count==1 && r.Errors.Count==1); Assert(r.Errors[0].Contains("flow")); Assert(!r.Errors[0].Contains("abcdefghijklmnopqrstuvwxyz")); Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),Configuration.Build(r.Servers[0])); });
   Test("HWID request uses only explicitly supplied installation ID", delegate { using(var r=Subscriptions.CreateRequest(new Uri("https://example.com/sub"),"Happ","Luna-0123456789abcdef0123456789abcdef")) { Assert(r.Headers.Contains("x-hwid")); Assert(System.Linq.Enumerable.Single(r.Headers.GetValues("x-hwid"))=="Luna-0123456789abcdef0123456789abcdef"); } using(var r=Subscriptions.CreateRequest(new Uri("https://example.com/sub"),"Happ","")) Assert(!r.Headers.Contains("x-hwid")); });
   Test("Provider HWID rejection gives actionable error before URI parsing", delegate { using(var r=new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)) { r.Headers.Add("x-hwid-not-supported","true"); bool caught=false; try { Subscriptions.ValidateResponseHeaders(r); } catch(SafeException e) { caught=true; Assert(e.Message.Contains("HWID")); } Assert(caught); } });
   Test("Installation ID is stable and accepted by provider format", delegate { var s=new Settings(); Storage.EnsureDeviceId(s); string first=s.DeviceId; Assert(System.Text.RegularExpressions.Regex.IsMatch(first,@"^[a-zA-Z0-9=-]{10,64}$")); Storage.EnsureDeviceId(s); Assert(s.DeviceId==first); });
   Test("HWID cannot follow a cross-origin redirect", delegate { var a=new Uri("https://example.com/sub"); Subscriptions.ValidateDeviceRedirect(a,new Uri("https://example.com/next"),"Luna-0123456789abcdef"); foreach(string target in new[]{"https://other.example/sub","https://example.com:8443/sub"}) { bool caught=false; try { Subscriptions.ValidateDeviceRedirect(a,new Uri(target),"Luna-0123456789abcdef"); } catch(SafeException) { caught=true; } Assert(caught); } });
   Test("Device limit response does not suggest rotating HWID", delegate { using(var r=new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)) { r.Headers.Add("x-hwid-max-devices-reached","true"); bool caught=false; try { Subscriptions.ValidateResponseHeaders(r); } catch(SafeException e) { caught=true; Assert(e.Message.Contains("лимит устройств") && e.Message.Contains("не будет автоматически меняться")); } Assert(caught); } });
   Test("Opt-in and installation ID survive encrypted settings reload", delegate { string folder=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")); string path=System.IO.Path.Combine(folder,"settings.dat"); var s=new Settings(); Storage.EnsureDeviceId(s); s.SendDeviceId=true; try { Storage.Save(path,s); var loaded=Storage.Load(path); Assert(loaded.SendDeviceId && loaded.DeviceId==s.DeviceId); Assert(!Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path)).Contains(s.DeviceId)); } finally { if(System.IO.File.Exists(path)) System.IO.File.Delete(path); if(System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder); } });
   Test("Provider instruction with spelling variant is rejected", delegate { var r=Subscriptions.Parse("vless://"+id+"@example.com:443?security=tls#"+Uri.EscapeDataString("↓Попробуйте установить рекомендованое из списка↓")); Assert(r.Servers.Count==0 && r.Errors.Count==1); });
   Test("Both provider instruction rows rejected in Base64 subscription", delegate { string a="vless://"+id+"@example.com:443?security=tls#"; var r=Subscriptions.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(a+Uri.EscapeDataString("Приложение не поддерживается :(")+"\n"+a+Uri.EscapeDataString("↓Попробуйте установить рекомендованное из списка↓")))); Assert(r.Servers.Count==0 && r.Errors.Count==2); });
   Test("Happ compatibility request uses selectable known header", delegate { Assert(Subscriptions.RequestAgent("Happ").StartsWith("Happ/")); });
   Test("doomra subscription identity keeps the existing device ID", delegate {
    const string device="Luna-synthetic-device";
    using(var request=Subscriptions.CreateRequest(new Uri("https://example.com/subscription"),"doomra",device)) {
     Assert(request.Headers.UserAgent.ToString()==Subscriptions.RequestAgent("Luna"));
     Assert(request.Headers.GetValues("x-hwid").Single()==device && request.Headers.GetValues("x-device-model").Single()=="doomra VPN");
    }
   });
   Test("Removed subscription preference is ignored when loading old settings", delegate {
    const string device="Luna-synthetic-device";
    var settings=Json.Read<Settings>("{\"UseDoomraIdentity\":true,\"DeviceId\":\"Luna-synthetic-device\"}");
    Assert(settings.DeviceId==device && !Json.Write(settings).Contains("UseDoomraIdentity"));
    using(var request=Subscriptions.CreateRequest(new Uri("https://example.com/subscription"),"Happ",settings.DeviceId)) {
     Assert(request.Headers.UserAgent.ToString()=="Happ/1.0" && request.Headers.GetValues("x-hwid").Single()==device);
    }
   });
   Test("Provider rejection is not imported as a usable VLESS server", delegate { var r=Subscriptions.Parse("vless://"+id+"@example.com:443?security=tls#"+Uri.EscapeDataString("Приложение не поддерживается :(")); Assert(r.Servers.Count==0 && r.Errors.Count==1); Assert(r.Errors[0].Contains("Сервис подписки")); });
   Test("Previously saved provider rejection cannot start a tunnel", delegate { var s=Subscriptions.Parse("vless://"+id+"@example.com:443?security=tls").Servers[0]; s.Name="Приложение не поддерживается :("; bool caught=false; try { Configuration.Build(s); } catch(SafeException) { caught=true; } Assert(caught); });
   Test("Subscription format chooses known User-Agent and rejects arbitrary header", delegate { Assert(Subscriptions.RequestAgent("v2rayN")=="v2rayN/7.0"); Assert(Subscriptions.RequestAgent("Luna")=="doomraVPN/0.31"); bool caught=false; try { Subscriptions.RequestAgent("secret\r\nX-Injected: value"); } catch(SafeException e) { caught=true; Assert(!e.Message.Contains("secret")); } Assert(caught); });
   Test("VLESS Reality and UTF8 name", delegate { var r=Subscriptions.Parse("vless://"+id+"@example.com:443?security=reality&sni=example.com&fp=chrome&pbk=abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG&sid=abcd&type=tcp&flow=xtls-rprx-vision#%D0%A1%D0%B5%D1%80%D0%B2%D0%B5%D1%80"); Assert(r.Servers.Count==1); Assert(r.Servers[0].Name=="Сервер"); Assert(r.Servers[0].Protocol=="vless"); });
   Test("Base64 subscription", delegate { var r=Subscriptions.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes("trojan://secret@example.com:443#Test\nvless://"+id+"@example.net:443?security=tls"))); Assert(r.Servers.Count==2); });
   Test("Partial import reports unsupported format without secrets", delegate {
    var r=Subscriptions.Parse("trojan://secret@example.com:443\nunsupported://secret@example.net:443");
    Assert(r.Servers.Count==1 && r.Errors.Count==1);
    Assert(r.Errors[0].Contains("Формат не поддерживается") && !r.Errors[0].Contains("secret"));
   });
   Test("Unknown parameters rejected", delegate { Reject("vless://"+id+"@example.com:443?security=tls&execute=secret"); });
   Test("Unknown transport rejected", delegate { Reject("vless://"+id+"@example.com:443?security=tls&type=unsupported"); });
   Test("Insecure TLS rejected", delegate { Reject("trojan://secret@example.com:443?allowInsecure=1"); });
   Test("Invalid uuid rejected", delegate { Reject("vless://secret@example.com:443"); });
   Test("Invalid port rejected", delegate { Reject("trojan://secret@example.com:0"); });
   Test("SS SIP002", delegate { var r=Subscriptions.Parse("ss://"+Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:secret"))+"@example.com:8388#SS"); Assert(r.Servers.Count==1 && r.Servers[0].Method=="aes-128-gcm" && r.Servers[0].Password=="secret"); });
   Test("SS plugin rejected", delegate { Reject("ss://"+Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:secret"))+"@example.com:8388?plugin=secret"); });
   Test("VMess JSON", delegate { string json="{\"v\":\"2\",\"ps\":\"VM\",\"add\":\"example.com\",\"port\":\"443\",\"id\":\""+id+"\",\"aid\":\"0\",\"scy\":\"auto\",\"net\":\"ws\",\"type\":\"none\",\"host\":\"example.com\",\"path\":\"/ws\",\"tls\":\"tls\"}"; var r=Subscriptions.Parse("vmess://"+Convert.ToBase64String(Encoding.UTF8.GetBytes(json))); Assert(r.Servers.Count==1 && r.Servers[0].Transport=="ws"); });
   Test("Oversize rejected", delegate { bool caught=false; try { Subscriptions.Parse(new string('x',4194305)); } catch(SafeException) { caught=true; } Assert(caught); });
   Test("Duplicate server deduplication", delegate { var r=Subscriptions.Parse("trojan://secret@example.com:443\ntrojan://secret@example.com:443"); Assert(r.Servers.Count==1); });
   Test("Core validates four protocols without TUN startup", delegate {
    string[] samples={"vless://"+id+"@example.com:443?security=tls&type=ws&path=%2Fws&host=example.com", "trojan://secret@example.com:443", "ss://"+Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:secret"))+"@example.com:8388", "vmess://"+Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"add\":\"example.com\",\"port\":443,\"id\":\""+id+"\",\"net\":\"tcp\",\"tls\":\"tls\"}"))};
    foreach(string sample in samples) Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),Configuration.Build(Subscriptions.Parse(sample).Servers[0]));
   });
   Test("Configuration captures IPv4 IPv6 and DNS", delegate { var d=Json.Read<System.Collections.Generic.Dictionary<string,object>>(Configuration.Build(Subscriptions.Parse("trojan://secret@example.com:443").Servers[0])); var a=(System.Collections.ArrayList)d["inbounds"]; var tun=(System.Collections.Generic.Dictionary<string,object>)a[0]; Assert((bool)tun["strict_route"] && (bool)tun["auto_route"]); var addresses=(System.Collections.ArrayList)tun["address"]; Assert(addresses.Count==2); var route=(System.Collections.Generic.Dictionary<string,object>)d["route"]; Assert((string)route["final"]=="proxy"); });
   Test("Encrypted profiles preserve separate secrets selections and theme", delegate {
    string folder=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")),path=System.IO.Path.Combine(folder,"settings.dat");
    var settings=new Settings { Subscription="https://example.invalid/secret-one",DeviceId="Luna-stable-profile-id",Servers=Subscriptions.Parse("trojan://secret-one@127.0.0.1:443").Servers };
    ProfileStore.Initialize(settings); string first=settings.ActiveProfile;
    ProfileStore.Upsert(settings,null,"Работа","https://example.invalid/secret-two",null,Subscriptions.Parse("trojan://secret-two@127.0.0.1:443").Servers); settings.LightTheme=true;
    Storage.Save(path,settings); Assert(!Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path)).Contains("secret"));
    var restored=Storage.Load(path); Assert(restored.Profiles.Count==2 && restored.LightTheme && restored.Servers[0].Password=="secret-two");
    ProfileStore.Switch(restored,first); Assert(restored.Servers[0].Password=="secret-one" && restored.DeviceId=="Luna-stable-profile-id");
    ProfileStore.Remove(restored,first); ProfileStore.Remove(restored,restored.ActiveProfile); Storage.Save(path,restored);
    var empty=Storage.Load(path); Assert(empty.Profiles.Count==0 && empty.Servers.Count==0 && empty.Subscription=="" && empty.DeviceId=="Luna-stable-profile-id");
    System.IO.File.Delete(path); System.IO.Directory.Delete(folder);
   });
   Test("Encrypted settings roundtrip and atomic overwrite", delegate { string folder=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")); string path=System.IO.Path.Combine(folder,"settings.dat"); var settings=new Settings { Subscription="https://example.com/secret-token", Servers=Subscriptions.Parse("trojan://secret@example.com:443").Servers }; Storage.Save(path,settings); Assert(!Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path)).Contains("secret")); Assert(Storage.Load(path).Servers[0].Password=="secret"); settings.Selected=7; Storage.Save(path,settings); Assert(Storage.Load(path).Selected==7); System.IO.File.Delete(path); System.IO.Directory.Delete(folder); });
   Test("Damaged encrypted settings fail closed", delegate { string folder=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(folder); string path=System.IO.Path.Combine(folder,"settings.dat"); System.IO.File.WriteAllText(path,"corrupted"); bool caught=false; try { Storage.Load(path); } catch(SafeException) { caught=true; } Assert(caught); System.IO.File.Delete(path); System.IO.Directory.Delete(folder); });
   Test("Missing core rejected", delegate { bool caught=false; try { Configuration.Check("missing-core.exe","{}"); } catch(SafeException) { caught=true; } Assert(caught); });
   Test("Tampered core rejected before execution", delegate { string path=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")+".exe"); System.IO.File.WriteAllText(path,"not an executable"); bool caught=false; try { Configuration.Check(path,"{}"); } catch(SafeException) { caught=true; } finally { System.IO.File.Delete(path); } Assert(caught); });
   Test("Reality config accepted by pinned core", delegate { var s=Subscriptions.Parse("vless://"+id+"@example.com:443?security=reality&sni=example.com&fp=chrome&pbk=abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG&sid=abcd&type=tcp&flow=xtls-rprx-vision").Servers[0]; Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),Configuration.Build(s)); });
   Test("gRPC config accepted by pinned core", delegate { var s=Subscriptions.Parse("trojan://secret@example.com:443?type=grpc&serviceName=tunnel").Servers[0]; Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),Configuration.Build(s)); });
   Test("Plain HTTP subscription rejected without fetching", delegate { bool caught=false; try { Subscriptions.Download("http://example.com/secret").GetAwaiter().GetResult(); } catch(SafeException e) { caught=true; Assert(!e.Message.Contains("secret")); } Assert(caught); });
   Test("Unknown parameter names cannot leak into diagnostic errors", delegate { Reject("trojan://secret@example.com:443?secret_token_123=1"); });
   Test("Core rejects invalid config without printing secrets", delegate { bool caught=false; try { Configuration.Check(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sing-box.exe"),"{\"outbounds\":[{\"type\":\"secret-invalid\"}]}"); } catch(SafeException e) { caught=true; Assert(!e.Message.Contains("secret")); } Assert(caught); });
   Test("Framed IPC roundtrip", delegate { using(var ms=new System.IO.MemoryStream()) { Wire.Send(ms,"Секрет не в логах"); ms.Position=0; Assert(Wire.Read(ms)=="Секрет не в логах"); } });
   Test("IPC oversize frame rejected before allocating payload", delegate { using(var ms=new System.IO.MemoryStream(BitConverter.GetBytes(int.MaxValue))) { bool caught=false; try { Wire.Read(ms); } catch(SafeException) { caught=true; } Assert(caught); } });
   Test("IPC truncated payload fails closed", delegate { using(var ms=new System.IO.MemoryStream(new byte[]{5,0,0,0,1})) { bool caught=false; try { Wire.Read(ms); } catch(System.IO.EndOfStreamException) { caught=true; } Assert(caught); } });
   Test("Job Object kills child when owner crashes", delegate {
    string path=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")+".pid"); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
    using(var owner=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--job-child \""+path+"\"") { UseShellExecute=false,CreateNoWindow=true })) {
     try {
      var wait=System.Diagnostics.Stopwatch.StartNew(); while(!System.IO.File.Exists(path) && wait.ElapsedMilliseconds<5000 && !owner.HasExited) System.Threading.Thread.Sleep(50);
      Assert(System.IO.File.Exists(path)); int pid=int.Parse(System.IO.File.ReadAllText(path)); using(var child=System.Diagnostics.Process.GetProcessById(pid)) { owner.Kill(); owner.WaitForExit(3000); Assert(child.WaitForExit(5000)); }
     } finally { if(!owner.HasExited) owner.Kill(); if(System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
   });
   Test("Core stops gracefully with console signal (no inbounds or network)", delegate {
    string path=System.IO.Path.Combine(Environment.CurrentDirectory,"test-output",Guid.NewGuid().ToString("N")+".exit");
    using(var harness=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--core-stop \""+path+"\"") { UseShellExecute=false,CreateNoWindow=true })) {
     try { Assert(harness.WaitForExit(15000)); Assert(harness.ExitCode==0); Assert(System.IO.File.ReadAllText(path)=="0"); }
     finally { if(!harness.HasExited) harness.Kill(); if(System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
   });
   Console.WriteLine("Failures: "+failed); return failed==0?0:1;
  }
 }
}
