using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace Luna {
 public sealed partial class MainForm : Form {
  readonly string settingsPath=Path.Combine(Storage.Folder,"settings.dat");
  readonly bool transient;
  float scale=1; bool headerOnline,dragging,inSizeMove,resizing; string hot="",pressed=""; int focus,dragY,dragScroll;
  Settings settings=new Settings(); readonly Diagnostics log=new Diagnostics();
  readonly Dictionary<Server,ServerCheckResult> checks=new Dictionary<Server,ServerCheckResult>();
  readonly UiAction connect=new UiAction("Подключить"),add=new UiAction("Профили"),check=new UiAction("Проверить все");
  readonly DashboardDetails details=new DashboardDetails();
  readonly SessionMeter meter=new SessionMeter();
  readonly System.Diagnostics.Stopwatch motion=new System.Diagnostics.Stopwatch(),tickClock=System.Diagnostics.Stopwatch.StartNew();
  readonly System.Windows.Forms.Timer uiTimer=new System.Windows.Forms.Timer();
  readonly System.Windows.Forms.Timer scrollTimer=new System.Windows.Forms.Timer { Interval=16 };
  int scrollTarget;
  readonly ToolTip nameTip=new ToolTip { InitialDelay=650,ReshowDelay=150,AutoPopDelay=10000,ShowAlways=true };
  string tipText="",editingId; TextBox subscriptionInput,profileInput; bool dropdown; long lastPoll;
  double logoConfirmedAt=-1;
  readonly DashboardList servers=new DashboardList();
  bool loading,busy,closing,exiting,storageReady=true; string state="STOPPED";
  Connection connection; Task session,checking; CancellationTokenSource checkCancel; bool awaitingSwitch;
  public MainForm() : this(null,false) { }
  internal MainForm(bool atLogin) : this(null,atLogin) { }
  internal MainForm(Settings initial) : this(initial,false) { }
  MainForm(Settings initial,bool atLogin) {
   transient=initial!=null;
   using(var graphics=Graphics.FromHwnd(IntPtr.Zero)) scale=Math.Max(1,graphics.DpiX/96f);
   Text="VPN"; StartPosition=FormStartPosition.CenterScreen; Font=UiFonts.Native(11);
   FormBorderStyle=FormBorderStyle.None; AutoScaleMode=AutoScaleMode.None; MinimumSize=new Size((int)(900*scale),(int)(640*scale)); ClientSize=new Size((int)(1040*scale),(int)(720*scale)); KeyPreview=true;
   SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Doomra.Icon")) if(stream!=null) using(var icon=new Icon(stream)) Icon=(Icon)icon.Clone();
   try { settings=initial??Storage.Load(settingsPath); } catch(SafeException e) { storageReady=false; Shown+=delegate { Error(e.Message); }; }
   ProfileStore.Initialize(settings); settings.Network=(settings.Network??new NetworkPolicy()).Snapshot(); ApplyTheme();
   settings.SendDeviceId=true; settings.AutoReconnect=true;
   servers.SelectedChanged+=SelectServer;
   FillServers(); focus=Selected()==null?1:0;
   uiTimer.Interval=250; uiTimer.Tick+=delegate {
    bool changed=motion.IsRunning,footerChanged=false,speedChanged=false;
    if(changed) UpdateLogoMotion();
    if(meter.Active && tickClock.ElapsedMilliseconds-lastPoll>=1000) {
     lastPoll=tickClock.ElapsedMilliseconds; meter.Poll();
     RefreshSessionMetrics(out footerChanged,out speedChanged);
    }
    if(closing || !Visible || WindowState==FormWindowState.Minimized || details.SettingsPage) return;
    if(footerChanged) Invalidate(FooterPaintBounds);
    if(changed || speedChanged) Invalidate(ConnectionPaintBounds);
   }; uiTimer.Start();
   scrollTimer.Tick+=delegate { AdvanceScroll(); };
   if(!transient) InitializeTray(atLogin);
   FormClosing+=async delegate(object sender,FormClosingEventArgs e) {
    if(exiting || e.CloseReason==CloseReason.WindowsShutDown) return;
    e.Cancel=true; if(closing) return;
    if(tray!=null && !exitRequested && e.CloseReason==CloseReason.UserClosing) { HideSubscription(); Hide(); return; }
    closing=true; HideSubscription(); uiTimer.Stop(); UpdateControls();
    if(checkCancel!=null) checkCancel.Cancel(); if(checking!=null) { try { await checking; } catch {} }
    await Disconnect(); exiting=true; Close();
   };
  }
  DashboardLayout LayoutNow { get { return new DashboardLayout(new Size(Math.Max(1,(int)(ClientSize.Width/scale)),Math.Max(1,(int)(ClientSize.Height/scale)))); } }
  PointF Logical(Point point) { return new PointF(point.X/scale,point.Y/scale); }
  protected override void Dispose(bool disposing) { if(disposing) { uiTimer.Dispose(); scrollTimer.Dispose(); nameTip.Dispose(); DisposeTray(); } base.Dispose(disposing); }
  void ApplyTheme() {
   Palette.Light=settings.LightTheme; details.Light=settings.LightTheme; BackColor=Palette.Background; ForeColor=Palette.Text;
   foreach(Control control in Controls) { control.BackColor=Palette.Input; control.ForeColor=Palette.Text; }
   Invalidate();
  }
  void PositionInput() {
   if(subscriptionInput==null) return;
   PlaceInput(subscriptionInput,LayoutNow.Input); PlaceInput(profileInput,LayoutNow.NameInput);
  }
  void PlaceInput(TextBox input,RectangleF r) { if(input!=null) input.SetBounds((int)((r.X+10)*scale),(int)((r.Y+10)*scale),(int)((r.Width-20)*scale),(int)((r.Height-16)*scale)); }
  TextBox MakeInput(string text,bool secret,int limit,string name,int tabFocus) {
   var input=new TextBox { Text=text,UseSystemPasswordChar=false,PasswordChar=secret?'·':'\0',BorderStyle=BorderStyle.None,BackColor=Palette.Input,ForeColor=Palette.Text,Font=UiFonts.Get(15*scale),MaxLength=limit,AccessibleName=name };
   var ownedFont=input.Font; input.Disposed+=delegate { ownedFont.Dispose(); };
   input.GotFocus+=delegate { focus=tabFocus; NotifyAccess(); Invalidate(); }; Controls.Add(input); return input;
  }
  void OpenMenu(string menu) {
   if(menu=="settings") { ShowSettings(); return; }
   HideSubscription(); dropdown=details.Dropdown=true; details.Menu=menu;
   nameTip.Hide(this); nameTip.SetToolTip(this,null); tipText=""; UpdateControls(); Invalidate();
  }
  void ShowProfiles() { OpenMenu("profiles"); details.ProfileScroll=Math.Max(0,Math.Min(settings.Profiles.Count-4,details.ProfileIndex)); FocusAction(settings.Profiles.Count==0?"new":"profile:"+details.ProfileIndex,false); }
  void ShowSubscription() { var current=ProfileStore.Current(settings); ShowEditor(current==null?null:current.Id); }
  void ShowSettings() { HideSubscription(); details.SettingsPage=true; UpdateControls(); FocusAction("back",false); NotifyAccess(); Invalidate(); }
  void BackToMain() { details.SettingsPage=false; FocusAction("settings",true); NotifyAccess(); Invalidate(); }
  void ConfirmDelete() {
   var profile=settings.Profiles.FirstOrDefault(p=>p.Id==editingId); if(profile==null) return;
   details.DeleteName=profile.Name; details.Menu="delete";
   if(profileInput!=null) profileInput.Visible=false; if(subscriptionInput!=null) subscriptionInput.Visible=false;
   FocusAction("dismiss",false); NotifyAccess(); Invalidate();
  }
  void CancelDelete() {
   details.Menu="editor"; if(profileInput!=null) profileInput.Visible=true; if(subscriptionInput!=null) subscriptionInput.Visible=true;
   PositionInput(); FocusAction("name",true); NotifyAccess(); Invalidate();
  }
  async Task DeleteProfile() {
   if(busy || closing || editingId==null) return;
   string id=editingId; bool active=settings.ActiveProfile==id;
   busy=true; UpdateControls();
   try {
    if(active) await Disconnect(); if(closing) return;
    ProfileStore.Remove(settings,id); if(active) { ResetSession(); checks.Clear(); }
    Save(); HideSubscription(); FillServers(); ShowProfiles();
   } finally { busy=false; if(!closing) UpdateControls(); }
  }
  void ShowEditor(string id) {
   editingId=id; var profile=settings.Profiles.FirstOrDefault(p=>p.Id==id);
   OpenMenu("editor"); details.NewProfile=profile==null; details.Notice=connection!=null?"Смена ссылки или обновление отключат VPN":"HTTPS-ссылка из личного кабинета";
   profileInput=MakeInput(profile==null?"Профиль "+(settings.Profiles.Count+1):profile.Name,false,48,"Название профиля",13);
   subscriptionInput=MakeInput(profile==null?"":profile.Subscription,true,8192,"HTTPS-ссылка подписки",9);
   PositionInput(); FocusAction("name",false);
  }
  void HideSubscription() {
   dropdown=details.Dropdown=false; details.Menu="";
   if(subscriptionInput!=null) { Controls.Remove(subscriptionInput); subscriptionInput.Dispose(); subscriptionInput=null; }
   if(profileInput!=null) { Controls.Remove(profileInput); profileInput.Dispose(); profileInput=null; }
   focus=1; Invalidate();
  }
  string HitAction(PointF point) {
   var layout=LayoutNow;
   if(details.SettingsPage) {
    if(layout.Back.Contains(point)) return "back";
    string networkAction=NetworkSettingsHit(point); if(networkAction!="") return networkAction;
    if(layout.Close.Contains(point)) return "close"; if(layout.Maximize.Contains(point)) return "max"; if(layout.Minimize.Contains(point)) return "min"; return "";
   }
   if(dropdown) {
    if(layout.MenuClose(details.Menu,settings.Profiles.Count).Contains(point)) return "dismiss";
    if(details.Menu=="delete" && layout.MenuSubmit(details.Menu,settings.Profiles.Count).Contains(point)) return "confirm-delete";
    if(details.Menu=="editor" && editingId!=null && layout.Delete.Contains(point)) return "delete";
    if(details.Menu=="editor" && layout.Submit.Contains(point)) return "save";

    if(details.Menu=="profiles") {
     if(layout.MenuSubmit(details.Menu,settings.Profiles.Count).Contains(point)) return "new";
     for(int i=details.ProfileScroll;i<Math.Min(settings.Profiles.Count,details.ProfileScroll+4);i++) {
      if(layout.ProfileEdit(i,details.ProfileScroll).Contains(point)) return "edit:"+i;
      if(layout.ProfileRow(i,details.ProfileScroll).Contains(point)) return "profile:"+i;
     }
    }
    return "";
   }
   return layout.Hit(point);
  }
  bool EnabledAction(string action) {
   if(closing) return false;
   if(IsNetworkAction(action)) return NetworkActionEnabled(action);
   if(details.SettingsPage && action!="back" && action!="theme" && action!="close" && action!="min" && action!="max") return false;
   if(action=="delete" || action=="confirm-delete") return dropdown && !busy;
   if(action=="connect") return connect.Enabled;
   if(action=="add") return add.Enabled;
   if(action=="check") return check.Enabled;
   if(action=="refresh-profile") return !dropdown && !busy && !closing && ProfileStore.Current(settings)!=null && !string.IsNullOrWhiteSpace(settings.Subscription);
   if(action=="settings") return true;
   if(action=="save" || action=="new" || action.StartsWith("profile:") || action.StartsWith("edit:")) return dropdown && !busy;
   return true;
  }
  async void RunAction(string action) {
   if(!EnabledAction(action)) return;
   try {
    if(action=="connect") await Toggle();
    else if(action=="add") ShowProfiles();
    else if(action=="settings") ShowSettings();
    else if(action=="back") BackToMain();
    else if(IsNetworkAction(action)) await NetworkSettingsAction(action);
    else if(action=="delete") ConfirmDelete();
    else if(action=="confirm-delete") await DeleteProfile();
    else if(action=="theme") { settings.LightTheme=!settings.LightTheme; ApplyTheme(); Save(); NotifyAccess(); }
    else if(action=="new") { if(settings.Profiles.Count>=ProfileStore.MaximumProfiles) { Error("Можно добавить до 24 профилей."); return; } ShowEditor(null); }
    else if(action.StartsWith("profile:") || action.StartsWith("edit:")) {
     int index; if(!int.TryParse(action.Substring(action.IndexOf(':')+1),out index) || index<0 || index>=settings.Profiles.Count) return;
     string id=settings.Profiles[index].Id;
     if(action.StartsWith("profile:") && id==settings.ActiveProfile) { HideSubscription(); return; }
     if(action.StartsWith("edit:")) { ShowEditor(id); }
     else { await PrepareProfileChange(); if(closing) return; ProfileStore.Switch(settings,id); ResetSession(); checks.Clear(); HideSubscription(); Save(); FillServers(); }
    }
    else if(action=="save") await AddSubscription();
    else if(action=="refresh-profile") await RefreshCurrentProfile();
    else if(action=="dismiss") { if(details.Menu=="delete") CancelDelete(); else HideSubscription(); }
    else if(action=="check") { if(checkCancel!=null) checkCancel.Cancel(); else await CheckAll(); }
    else if(action=="close") Close();
    else if(action=="min") WindowState=FormWindowState.Minimized;
    else if(action=="max") WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;
   } catch(SafeException e) { if(!closing) Error(e.Message); } catch { if(!closing) Error("Не удалось выполнить действие."); }
  }
  async Task PrepareProfileChange() { busy=true; UpdateControls(); try { await Disconnect(); } finally { busy=false; if(!closing) UpdateControls(); } }
  void ResetSession() { meter.Reset(); details.Elapsed="00:00:00"; details.Upload=details.Download="—"; details.Speed=MetricText.Speed(null,null); }
  void RefreshSessionMetrics(out bool footerChanged,out bool speedChanged) {
   string elapsed=meter.Elapsed;
   string download=meter.Traffic.Available?MetricText.Bytes(meter.Traffic.Received):"—";
   string upload=meter.Traffic.Available?MetricText.Bytes(meter.Traffic.Sent):"—";
   string speed=MetricText.Speed(meter.Traffic.ReceivedPerSecond,meter.Traffic.SentPerSecond);
   footerChanged=details.Elapsed!=elapsed || details.Download!=download || details.Upload!=upload;
   speedChanged=details.Speed!=speed;
   details.Elapsed=elapsed; details.Download=download; details.Upload=upload; details.Speed=speed;
  }
  protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); MaximizedBounds=Screen.FromControl(this).WorkingArea; }
  protected override void OnResizeBegin(EventArgs e) {
   base.OnResizeBegin(e); inSizeMove=true; scrollTimer.Stop();
  }
  protected override void OnResizeEnd(EventArgs e) {
   base.OnResizeEnd(e); inSizeMove=false;
   if(resizing) { resizing=false; UpdateWindowRegion(); Invalidate(); }
  }
  void UpdateWindowRegion() {
   var previous=Region;
   if(resizing || WindowState==FormWindowState.Maximized) {
    if(previous!=null) Region=null;
   } else if(Width>0 && Height>0) {
    using(var path=Dashboard.Round(new RectangleF(0,0,Width,Height),14*scale)) Region=new Region(path);
   } else return;
   if(previous!=null) previous.Dispose();
  }
  protected override void OnResize(EventArgs e) {
   base.OnResize(e); if(servers!=null) { servers.Viewport=(int)LayoutNow.List.Height; servers.ScrollTo(servers.Scroll); }
   if(!resizing) { resizing=inSizeMove; UpdateWindowRegion(); }
   PositionInput(); Invalidate();
  }
  Rectangle ConnectionPaintBounds { get { var r=RectangleF.Inflate(LayoutNow.ConnectCard,2,2); return Rectangle.Ceiling(new RectangleF(r.X*scale,r.Y*scale,r.Width*scale,r.Height*scale)); } }
  Rectangle FooterPaintBounds {
   get {
    var r=RectangleF.Inflate(LayoutNow.Footer,2,2);
    return Rectangle.FromLTRB((int)Math.Floor(r.Left*scale),(int)Math.Floor(r.Top*scale),(int)Math.Ceiling(r.Right*scale),(int)Math.Ceiling(r.Bottom*scale));
   }
  }
  Rectangle ServerPaintBounds {
   get {
    var r=LayoutNow.List;
    return Rectangle.FromLTRB((int)Math.Floor(r.Left*scale),(int)Math.Floor(r.Top*scale),(int)Math.Ceiling(r.Right*scale),(int)Math.Ceiling(r.Bottom*scale));
   }
  }
  protected override void OnPaintBackground(PaintEventArgs e) { }
  protected override void OnPaint(PaintEventArgs e) {
   var saved=e.Graphics.Save(); e.Graphics.ScaleTransform(scale,scale); var layout=LayoutNow; servers.Viewport=(int)layout.List.Height; servers.ScrollTo(servers.Scroll);
   if(resizing) Dashboard.DrawLive(e.Graphics,layout,servers,connect,add,check,state,headerOnline,hot,pressed,focus,WindowState==FormWindowState.Maximized,details);
   else if(!dropdown && !details.SettingsPage && ConnectionPaintBounds.Contains(e.ClipRectangle)) Dashboard.DrawConnection(e.Graphics,layout,connect,state,headerOnline,hot,pressed,focus,details);
   else if(!dropdown && !details.SettingsPage && ServerPaintBounds.Contains(e.ClipRectangle)) Dashboard.DrawServerViewport(e.Graphics,layout,servers,details.Light);
   else if(!dropdown && !details.SettingsPage && FooterPaintBounds.Contains(e.ClipRectangle)) Dashboard.DrawSessionFooter(e.Graphics,layout,details,true);
   else Dashboard.Draw(e.Graphics,layout,servers,connect,add,check,state,headerOnline,hot,pressed,focus,WindowState==FormWindowState.Maximized,details);
   e.Graphics.Restore(saved);
  }
  protected override void OnMouseMove(MouseEventArgs e) {
   base.OnMouseMove(e); var point=Logical(e.Location); var layout=LayoutNow;
   if(dragging) { float thumb=Math.Max(24,layout.List.Height*layout.List.Height/Math.Max(1,servers.Items.Count*DashboardList.RowPitch)); servers.ScrollTo(dragScroll+(int)((point.Y-dragY)*servers.MaxScroll/Math.Max(1,layout.List.Height-thumb))); Invalidate(ServerPaintBounds); return; }
   string next=HitAction(point); int row=!dropdown && !details.SettingsPage && layout.List.Contains(point)?(int)((point.Y-layout.List.Y+servers.Scroll)/DashboardList.RowPitch):-1;
   string tip=next=="refresh-profile"?(connection!=null?"Обновить серверы профиля (отключит VPN)":"Обновить серверы профиля"):(next=="check"?check.Text:(next=="settings"?"Настройки":(next.StartsWith("edit:")?"Редактировать профиль":(!dropdown && row>=0 && row<servers.Items.Count?ServerTooltip(servers.Items[row]):(!dropdown && !details.SettingsPage && layout.SelectedCard.Contains(point) && Selected()!=null?Selected().Name:"")))));
   if(tip!=tipText) { tipText=tip; nameTip.SetToolTip(this,tip.Length==0?null:tip); }
   if(next!=hot || row!=servers.Hover) { hot=next; servers.Hover=row; Cursor=next!="" || (row>=0 && servers.CanChoose)?Cursors.Hand:Cursors.Default; Invalidate(); }
  }
  protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); nameTip.Hide(this); hot=""; servers.Hover=-1; Invalidate(); }
  protected override void OnMouseDown(MouseEventArgs e) {
   scrollTimer.Stop();
   base.OnMouseDown(e); if(e.Button!=MouseButtons.Left) return; Focus(); details.KeyboardFocus=false;
   var point=Logical(e.Location); var layout=LayoutNow;
   if(dropdown && !layout.MenuRect(details.Menu,settings.Profiles.Count).Contains(point)) { HideSubscription(); return; }
   string action=HitAction(point);
   if(action!="" && EnabledAction(action)) { pressed=action; focus=FocusId(action); Capture=true; Invalidate(); return; }
   if(!dropdown && !details.SettingsPage && layout.List.Contains(point) && servers.Enabled) {
    focus=3;
    if(servers.MaxScroll>0 && point.X>=layout.List.Right-14) {
     float thumb=Math.Max(24,layout.List.Height*layout.List.Height/Math.Max(1,servers.Items.Count*DashboardList.RowPitch));
     float top=layout.List.Y+(layout.List.Height-thumb)*servers.Scroll/Math.Max(1,servers.MaxScroll);
     if(point.Y<top || point.Y>top+thumb) servers.ScrollTo((int)((point.Y-layout.List.Y-thumb/2)*servers.MaxScroll/Math.Max(1,layout.List.Height-thumb)));
     dragY=(int)point.Y; dragScroll=servers.Scroll; dragging=true; Capture=true;
    } else servers.Choose((int)((point.Y-layout.List.Y+servers.Scroll)/DashboardList.RowPitch)); Invalidate();
   }
  }
  protected override void OnMouseUp(MouseEventArgs e) {
   base.OnMouseUp(e); string action=pressed; pressed=""; dragging=false; Capture=false; Invalidate();
   if(e.Button==MouseButtons.Left && action!="" && HitAction(Logical(e.Location))==action) RunAction(action);
  }
  protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(!Capture) { pressed=""; dragging=false; Invalidate(); } }
  protected override void OnMouseWheel(MouseEventArgs e) {
   if(dropdown && details.Menu=="profiles") {
    details.ProfileScroll=Math.Max(0,Math.Min(Math.Max(0,settings.Profiles.Count-4),details.ProfileScroll-(e.Delta>0?1:-1)));
    Invalidate();
   } else if(!dropdown && !details.SettingsPage && servers.Enabled && !dragging) {
    int start=scrollTimer.Enabled?scrollTarget:servers.Scroll;
    scrollTarget=Math.Max(0,Math.Min(servers.MaxScroll,start-(int)Math.Round(e.Delta*.45)));
    if(scrollTarget!=servers.Scroll) scrollTimer.Start();
   }
  }
  void AdvanceScroll() {
   if(closing || dropdown || details.SettingsPage || !servers.Enabled) { scrollTimer.Stop(); return; }
   scrollTarget=Math.Min(scrollTarget,servers.MaxScroll);
   int remaining=scrollTarget-servers.Scroll;
   if(remaining!=0) {
    int step=(int)Math.Ceiling(Math.Abs(remaining)*.3);
    servers.ScrollTo(servers.Scroll+Math.Sign(remaining)*step);
    servers.Hover=-1;
    Invalidate(ServerPaintBounds);
   }
   if(servers.Scroll==scrollTarget) scrollTimer.Stop();
  }
  static int FocusId(string action) {
   if(action=="launch-at-login") return 27; if(action=="connect-at-login") return 28;
   if(action=="route-all") return 30; if(action=="route-include") return 31; if(action=="route-exclude") return 32; if(action=="app-add") return 33; if(action=="apps-prev") return 34; if(action=="apps-next") return 35; if(action=="kill") return 36; if(action=="network-recover") return 37; if(action.StartsWith("app-remove:")) { int i; return int.TryParse(action.Substring(11),out i)?40+i:-1; }
   if(action=="connect") return 0; if(action=="add") return 1; if(action=="check") return 2; if(action=="list") return 3;
   if(action=="back") return 17; if(action=="delete") return 18; if(action=="confirm-delete") return 19; if(action=="refresh-profile") return 16; if(action=="url") return 9; if(action=="save") return 10; if(action=="dismiss") return 11; if(action=="settings") return 12; if(action=="name") return 13; if(action=="new") return 14; if(action=="theme") return 15;
   int index; if(action.StartsWith("profile:") && int.TryParse(action.Substring(8),out index)) return 100+index*2;
   if(action.StartsWith("edit:") && int.TryParse(action.Substring(5),out index)) return 101+index*2;
   return -1;
  }
  void FocusAction(string action,bool keyboard) {
   focus=FocusId(action); details.KeyboardFocus=keyboard;
   if(action=="url" && subscriptionInput!=null) subscriptionInput.Focus();
   else if(action=="name" && profileInput!=null) profileInput.Focus();
   else { ActiveControl=null; Focus(); }
   NotifyAccess(); Invalidate();
  }
  protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
   if((keyData&(Keys.Alt|Keys.Control))!=0) return base.ProcessCmdKey(ref msg,keyData);
   Keys key=keyData&Keys.KeyCode;
   if(key==Keys.Escape && details.SettingsPage) { BackToMain(); return true; }
   if(key==Keys.Escape && dropdown && details.Menu=="delete") { CancelDelete(); return true; }
   if(key==Keys.Escape && dropdown) { HideSubscription(); FocusAction("add",true); return true; }
   if(key==Keys.Tab) {
    var actions=Nodes().Where(n=>n.Enabled && (n.Role==AccessibleRole.PushButton || n.Role==AccessibleRole.CheckButton || n.Role==AccessibleRole.List || n.Native!=null)).Where(n=>dropdown?n.Popup:!n.Popup && n.Id!="min" && n.Id!="max" && n.Id!="close").ToList();
    if(actions.Count==0) return true;
    int current=actions.FindIndex(n=>FocusId(n.Id)==focus),step=(keyData&Keys.Shift)!=0?-1:1;
    int next=(current+step+actions.Count)%actions.Count; FocusAction(actions[next].Id,true); return true;
   }
   if(dropdown && details.Menu=="editor" && (subscriptionInput.Focused || profileInput.Focused || focus==9 || focus==13)) {
    if(key==Keys.Enter) { RunAction("save"); return true; } return base.ProcessCmdKey(ref msg,keyData);
   }
   if(key==Keys.Enter || key==Keys.Space) {
    var node=Nodes().FirstOrDefault(n=>FocusId(n.Id)==focus && n.Enabled && n.Role!=AccessibleRole.StaticText);
    if(node!=null && node.Id!="list") RunAction(node.Id); return true;
   }
   if(!dropdown && !details.SettingsPage && focus==3 && servers.Enabled && servers.CanChoose) {
    int next=servers.Selected;
    if(key==Keys.Down) next=Math.Min(servers.Items.Count-1,next+1); else if(key==Keys.Up) next=Math.Max(0,next-1); else if(key==Keys.Home) next=0; else if(key==Keys.End) next=servers.Items.Count-1; else return base.ProcessCmdKey(ref msg,keyData);
    scrollTimer.Stop(); details.KeyboardFocus=true; servers.Choose(next); NotifyAccess(); Invalidate(); return true;
   }
   if(dropdown && details.Menu=="profiles" && (key==Keys.Down || key==Keys.Up)) {
    if(settings.Profiles.Count==0) { FocusAction("new",true); return true; }
    int index=focus>=100?(focus-100)/2:details.ProfileIndex;
    index=Math.Max(0,Math.Min(settings.Profiles.Count-1,index+(key==Keys.Down?1:-1)));
    if(index<details.ProfileScroll) details.ProfileScroll=index; if(index>=details.ProfileScroll+4) details.ProfileScroll=index-3;
    FocusAction("profile:"+index,true); return true;
   }
   return base.ProcessCmdKey(ref msg,keyData);
  }
  protected override void WndProc(ref Message m) {
   if(m.Msg==ShowMessage && ShowMessage!=0 && tray!=null) { ShowFromTray(); return; }
   if(m.Msg==TaskbarButtonCreated && TaskbarButtonCreated!=0) { taskbarButtonReady=true; RefreshTaskbarBadge(); }
   if(m.Msg==0x84) {
    long raw=m.LParam.ToInt64(); var point=Logical(PointToClient(new Point((short)(raw&0xffff),(short)((raw>>16)&0xffff)))); float x=point.X,y=point.Y,w=ClientSize.Width/scale,h=ClientSize.Height/scale;
    if(WindowState==FormWindowState.Normal) { int hit=0; if(y<7) hit=x<7?13:(x>w-8?14:12); else if(y>h-8) hit=x<7?16:(x>w-8?17:15); else if(x<7) hit=10; else if(x>w-8) hit=11; if(hit!=0) { m.Result=(IntPtr)hit; return; } }
    if(y>=0 && y<48 && x<w-144) { m.Result=(IntPtr)2; return; }
   }
   base.WndProc(ref m);
  }
  internal void ScrollPreview(int offset) { servers.ScrollTo(offset); Invalidate(); }
  sealed class Node {
   internal string Id,Name,Description; internal RectangleF Bounds; internal AccessibleRole Role; internal bool Enabled,Popup; internal Control Native;
  }
  List<Node> Nodes() {
   var layout=LayoutNow; var nodes=new List<Node>();
   Action<string,string,RectangleF,AccessibleRole,bool,Control> append=(id,name,bounds,role,popup,native)=>nodes.Add(new Node { Id=id,Name=name,Bounds=bounds,Role=role,Popup=popup,Native=native,Enabled=(popup || !dropdown || id=="close" || id=="min" || id=="max") && (id=="list"?servers.Enabled:EnabledAction(id)) });
   if(details.SettingsPage) {
    append("min","Свернуть",layout.Minimize,AccessibleRole.PushButton,false,null);
    append("max","Развернуть",layout.Maximize,AccessibleRole.PushButton,false,null);
    append("close","Закрыть",layout.Close,AccessibleRole.PushButton,false,null);
    append("back","Назад к серверам",layout.Back,AccessibleRole.PushButton,false,null);
    nodes.AddRange(NetworkSettingsNodes());
    append("page-title","Настройки",layout.SettingsPage,AccessibleRole.StaticText,false,null);
    return nodes;
   }
   append("connect",connect.Text,layout.Connect,AccessibleRole.PushButton,false,null);
   append("add","Профили подписок",layout.Add,AccessibleRole.PushButton,false,null);
   append("check",check.Text,layout.Check,AccessibleRole.PushButton,false,null);
   append("list","Список серверов",layout.List,AccessibleRole.List,false,null);
   append("min","Свернуть",layout.Minimize,AccessibleRole.PushButton,false,null);
   append("max","Развернуть",layout.Maximize,AccessibleRole.PushButton,false,null);
   append("close","Закрыть",layout.Close,AccessibleRole.PushButton,false,null);
   append("status",Dashboard.ConnectionText(state,headerOnline)+(headerOnline?". Скорость приёма и отправки: "+details.Speed:""),layout.ConnectCard,AccessibleRole.StaticText,false,null);
   append("stats","Профиль "+details.ProfileName+". Срок: "+MetricText.Expiry(settings.SubscriptionInfo)+". Использовано: "+MetricText.Usage(settings.SubscriptionInfo)+". Сеанс: "+details.Elapsed+". Получено: "+details.Download+". Отправлено: "+details.Upload,layout.Footer,AccessibleRole.StaticText,false,null);
   append("settings","Настройки",layout.Settings,AccessibleRole.PushButton,false,null);
   append("refresh-profile","Обновить серверы профиля",layout.RefreshProfile,AccessibleRole.PushButton,false,null);
   if(dropdown) {
    if(details.Menu=="editor") {
     append("name","Название профиля",layout.NameInput,AccessibleRole.Text,true,profileInput);
     append("url","HTTPS-ссылка подписки",layout.Input,AccessibleRole.Text,true,subscriptionInput);
     if(editingId!=null) append("delete","Удалить профиль",layout.Delete,AccessibleRole.PushButton,true,null);
     append("save","Сохранить профиль",layout.Submit,AccessibleRole.PushButton,true,null);

    } else if(details.Menu=="delete") {
     append("confirm-delete","Подтвердить удаление профиля",layout.MenuSubmit(details.Menu,settings.Profiles.Count),AccessibleRole.PushButton,true,null);
    } else if(details.Menu=="profiles") {
     for(int i=details.ProfileScroll;i<Math.Min(settings.Profiles.Count,details.ProfileScroll+4);i++) {
      var row=layout.ProfileRow(i,details.ProfileScroll); row.Width-=44;
      append("profile:"+i,settings.Profiles[i].Name,row,AccessibleRole.PushButton,true,null);
      append("edit:"+i,"Редактировать "+settings.Profiles[i].Name,layout.ProfileEdit(i,details.ProfileScroll),AccessibleRole.PushButton,true,null);
     }
     append("new","Добавить профиль",layout.MenuSubmit(details.Menu,settings.Profiles.Count),AccessibleRole.PushButton,true,null);
    }
    append("dismiss","Закрыть меню",layout.MenuClose(details.Menu,settings.Profiles.Count),AccessibleRole.PushButton,true,null);
   }
   return nodes;
  }
  protected override AccessibleObject CreateAccessibilityInstance() { return new CanvasAccessibility(this); }
  sealed class CanvasAccessibility : ControlAccessibleObject {
   readonly MainForm owner;
   internal CanvasAccessibility(MainForm form) : base(form) { owner=form; }
   public override int GetChildCount() { return owner.Nodes().Count; }
   public override AccessibleObject GetChild(int index) { var nodes=owner.Nodes(); if(index<0 || index>=nodes.Count) return null; return nodes[index].Native!=null?nodes[index].Native.AccessibilityObject:new CanvasItem(owner,nodes[index]); }
   public override AccessibleObject GetFocused() {
    var nodes=owner.Nodes(); var input=nodes.FirstOrDefault(n=>n.Native!=null && n.Native.Focused);
    if(input!=null) return input.Native.AccessibilityObject;
    if(!owner.ContainsFocus) return null;
    if(owner.focus==3 && owner.servers.Selected>=0) return new CanvasItem(owner,owner.ServerNode(owner.servers.Selected));
    var focused=nodes.FirstOrDefault(n=>FocusId(n.Id)==owner.focus); return focused==null?null:new CanvasItem(owner,focused);
   }
  }
  string CheckDescription(Server server) {
   ServerCheckResult result;
   return checks.TryGetValue(server,out result)?result.Text+(result.LatencyMilliseconds.HasValue?". Задержка "+result.LatencyLabel:"")+(result.CheckedAt.HasValue?". Проверено "+result.TimeLabel:""):"Не проверен";
  }
  string ServerTooltip(Server server) { return server.Name+"\r\n"+CheckDescription(server); }
  Node ServerNode(int index) {
   var layout=LayoutNow;
   var server=servers.Items[index];
   return new Node { Id="server:"+index,Name=Dashboard.ServerTitle(server),Description=CheckDescription(server),Role=AccessibleRole.ListItem,Enabled=servers.Enabled && servers.CanChoose && !dropdown,Bounds=RectangleF.Intersect(layout.List,new RectangleF(layout.List.X,layout.List.Y+index*DashboardList.RowPitch-servers.Scroll,layout.List.Width,DashboardList.RowHeight)) };
  }
  sealed class CanvasItem : AccessibleObject {
   readonly MainForm owner; readonly Node node;
   internal CanvasItem(MainForm form,Node item) { owner=form; node=item; }
   Node Current { get {
    if(node.Id.StartsWith("server:")) { int index; if(int.TryParse(node.Id.Substring(7),out index) && index>=0 && index<owner.servers.Items.Count) return owner.ServerNode(index); }
    return owner.Nodes().FirstOrDefault(n=>n.Id==node.Id)??new Node { Id=node.Id,Name="",Bounds=RectangleF.Empty,Role=node.Role,Enabled=false };
   } }
   public override string Name { get { return Current.Name; } set {} }
   public override string Description { get { return Current.Description; } }
   public override AccessibleRole Role { get { return Current.Role; } }
   public override Rectangle Bounds { get { var r=Current.Bounds; return owner.RectangleToScreen(Rectangle.Round(new RectangleF(r.X*owner.scale,r.Y*owner.scale,r.Width*owner.scale,r.Height*owner.scale))); } }
   public override AccessibleStates State { get {
    var current=Current;
    if(current.Role==AccessibleRole.StaticText) return AccessibleStates.ReadOnly;
    var state=current.Enabled?AccessibleStates.Focusable:AccessibleStates.Unavailable;
    if(node.Id=="theme" && owner.settings.LightTheme) state|=AccessibleStates.Checked;
    if(node.Id=="launch-at-login" && owner.details.LaunchAtLogin) state|=AccessibleStates.Checked;
    if(node.Id=="connect-at-login" && owner.settings.ConnectAtLogin) state|=AccessibleStates.Checked;
    if(node.Id=="kill" && owner.settings.Network.KillSwitch) state|=AccessibleStates.Checked;
    if(node.Id.StartsWith("route-") && node.Id.Substring(6)==owner.settings.Network.Mode) state|=AccessibleStates.Checked;
    if(owner.ContainsFocus && FocusId(node.Id)==owner.focus) state|=AccessibleStates.Focused;
    if(node.Id.StartsWith("server:")) { int index=int.Parse(node.Id.Substring(7)); state|=AccessibleStates.Selectable; if(index==owner.servers.Selected) { state|=AccessibleStates.Selected; if(owner.focus==3 && owner.ContainsFocus) state|=AccessibleStates.Focused; } if(current.Bounds.IsEmpty) state|=AccessibleStates.Offscreen; }
    return state;
   } }
   public override int GetChildCount() { return node.Id=="list"?owner.servers.Items.Count:0; }
   public override AccessibleObject GetChild(int index) { return node.Id=="list" && index>=0 && index<owner.servers.Items.Count?new CanvasItem(owner,owner.ServerNode(index)):null; }
   public override AccessibleObject GetSelected() { return node.Id=="list" && owner.servers.Selected>=0?new CanvasItem(owner,owner.ServerNode(owner.servers.Selected)):null; }
   public override string DefaultAction { get { return node.Role==AccessibleRole.StaticText?"":"Нажать"; } }
   public override void DoDefaultAction() {
    if(owner.IsDisposed || !owner.IsHandleCreated || !Current.Enabled || Current.Role==AccessibleRole.StaticText) return;
    owner.BeginInvoke((Action)(()=>{ if(!Current.Enabled) return; if(node.Id.StartsWith("server:")) { if(owner.servers.Enabled && !owner.dropdown) owner.servers.Choose(int.Parse(node.Id.Substring(7))); } else if(node.Id=="list") owner.FocusAction("list",true); else owner.RunAction(node.Id); }));
   }
  }
  void Error(string text) { log.Add(text); MessageBox.Show(this,text,"VPN",MessageBoxButtons.OK,MessageBoxIcon.Information); }
  bool Save() {
   try { if(transient) { ProfileStore.Capture(settings); return true; } if(!storageReady) return false; Storage.Save(settingsPath,settings); return true; }
   catch { storageReady=false; Error("Не удалось сохранить защищённые настройки."); return false; }
  }
  Server Selected() { return settings.Selected>=0 && settings.Selected<settings.Servers.Count?settings.Servers[settings.Selected]:null; }
  void NotifyAccess() { if(IsHandleCreated) { AccessibilityNotifyClients(AccessibleEvents.Focus,-1); AccessibilityNotifyClients(AccessibleEvents.Selection,-1); } }
  protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); NotifyAccess(); }
  void UpdateControls() {
   details.Network=settings.Network; details.NetworkEditAllowed=!busy && !closing && storageReady; details.NetworkSessionActive=connection!=null;
   details.Installed=installed; details.LaunchAtLogin=launchAtLogin; details.ConnectAtLogin=settings.ConnectAtLogin;
   bool wasOnline=headerOnline; headerOnline=UiState.IsOnline(state,connection!=null,connection!=null&&connection.Cancelled,closing);
   UpdateLogoMotion();
   if(headerOnline!=wasOnline) {
    RefreshConnectionBadge();
    if(wasOnline) meter.Poll();
    meter.SetActive(headerOnline);
    if(headerOnline) { meter.Poll(); lastPoll=tickClock.ElapsedMilliseconds; }
    bool footerChanged,speedChanged;
    RefreshSessionMetrics(out footerChanged,out speedChanged);
   }
   details.Subscription=settings.SubscriptionInfo; var currentProfile=ProfileStore.Current(settings); details.ProfileName=currentProfile==null?"Нет профиля":currentProfile.Name;
   details.ProfileNames=settings.Profiles.Select(p=>p.Name).ToList(); details.ProfileIndex=settings.Profiles.FindIndex(p=>p.Id==settings.ActiveProfile);
   details.CanRefresh=EnabledAction("refresh-profile");
   add.Enabled=!busy && !closing; check.Enabled=!closing && (checkCancel!=null || (!busy && connection==null && settings.Servers.Count>0));
   servers.Enabled=(!busy || checkCancel!=null) && !closing;
   servers.CanChoose=!busy && (connection==null || (!connection.Cancelled && (state=="CONNECTED" || state=="BLOCKED")));
   connect.Enabled=!busy && !closing && (Selected()!=null || connection!=null);
   connect.Text=connection==null?"Подключить":((state=="CONNECTED" || state=="BLOCKED")?"Отключить":(state=="RETRYING"?"Восстановление…":"Отменить"));
   if(IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange,-1); Invalidate();
  }
  void SelectServer(int index) {
   if(loading) return;
   if(connection!=null) {
    try { connection.SwitchServer(servers.Items[index]); }
    catch(Exception e) {
     servers.Selected=settings.Selected;
     Error(e is SafeException?e.Message:"Не удалось отправить команду смены сервера.");
     return;
    }
    awaitingSwitch=true;
    state="SWITCHING";
    UpdateControls();
    ResetSession();
   }
   settings.Selected=index; Save(); UpdateControls(); NotifyAccess();
  }
  void UpdateLogoMotion() {
   bool pending=!closing && LogoMotion.IsPending(state);
   if(pending && (!motion.IsRunning || logoConfirmedAt>=0)) {
    motion.Restart(); logoConfirmedAt=-1;
   }
   double elapsed=motion.Elapsed.TotalSeconds;
   if(headerOnline && motion.IsRunning && logoConfirmedAt<0) logoConfirmedAt=elapsed;
   details.Moving=motion.IsRunning && (pending || headerOnline) && elapsed<LogoMotion.Duration;
   details.Angle=details.Moving?(float)LogoMotion.Angle(elapsed):0;
   details.WaitingArc=pending && !details.Moving;
   details.ArcAngle=LogoMotion.ArcAngle(elapsed);
   details.SuccessRing=headerOnline && motion.IsRunning && logoConfirmedAt>=0?LogoMotion.SuccessOpacity(elapsed,logoConfirmedAt):0;
   if(!details.Moving && !details.WaitingArc && details.SuccessRing<=0) motion.Reset();
   int interval=motion.IsRunning && Visible && WindowState!=FormWindowState.Minimized && !details.SettingsPage?(details.WaitingArc?33:16):250;
   if(uiTimer.Interval!=interval) uiTimer.Interval=interval;
  }
  void LoadSavedChecks() {
   checks.Clear();
   var profile=ProfileStore.Current(settings);
   if(profile==null || profile.Checks.Count==0) return;
   foreach(var server in settings.Servers) {
    SavedServerCheck saved;
    if(profile.Checks.TryGetValue(server.CheckKey(),out saved) && saved!=null) {
     var result=saved.Read();
     if(result!=null) checks[server]=result;
    }
   }
  }
  void RecordServerCheck(Server server,ServerCheckResult result) {
   checks[server]=result;
   var profile=ProfileStore.Current(settings);
   if(profile!=null && result.CheckedAt.HasValue)
    profile.Checks[server.CheckKey()]=new SavedServerCheck { Status=result.Text,UnixSeconds=result.CheckedAt.Value.ToUnixTimeSeconds(),LatencyMilliseconds=result.LatencyMilliseconds };
  }
  void FillServers() { scrollTimer.Stop(); loading=true; LoadSavedChecks(); servers.Assign(settings.Servers,checks,settings.Selected); loading=false; UpdateControls(); }
  Task AddSubscription() { return SaveProfile(); }
  async Task SaveProfile() {
   if(busy || closing || subscriptionInput==null || profileInput==null) return;
   string address=subscriptionInput.Text.Trim(),name;
   try { name=ProfileStore.CleanName(profileInput.Text); } catch(SafeException e) { details.Notice=e.Message; profileInput.Focus(); Invalidate(); return; }
   Uri uri; if(!Uri.TryCreate(address,UriKind.Absolute,out uri) || uri.Scheme!="https" || uri.UserInfo!="" || uri.Fragment!="") { details.Notice="Введите корректную HTTPS-ссылку"; subscriptionInput.Focus(); Invalidate(); return; }
   string targetId=editingId; var existing=settings.Profiles.FirstOrDefault(p=>p.Id==targetId);
   if(existing!=null && existing.Subscription==address) { ProfileStore.Rename(settings,targetId,name); Save(); HideSubscription(); UpdateControls(); return; }
   await DownloadProfile(targetId,name,address);
  }
  Task RefreshCurrentProfile() {
   var profile=ProfileStore.Current(settings);
   if(!EnabledAction("refresh-profile") || profile==null) return Task.FromResult(0);
   return DownloadProfile(profile.Id,profile.Name,profile.Subscription);
  }
  async Task DownloadProfile(string targetId,string name,string address) {
   HideSubscription(); busy=true; UpdateControls(); await Disconnect(); if(closing) return;
   busy=true; add.Text="Загрузка…"; UpdateControls();
   try {
    Storage.EnsureDeviceId(settings); if(!Save()) { Error("Подписка не запрошена: не удалось сохранить ID установки."); return; }
    var download=await Subscriptions.DownloadDetails(address,"Happ",settings.DeviceId); if(closing) return;
    var result=await Task.Run(()=>Subscriptions.Parse(download.Body)); if(closing) return;
    if(result.Servers.Count==0) { Error("Поддерживаемые серверы не найдены.\r\n"+string.Join("\r\n",result.Errors.Take(4))); return; }
    ProfileStore.Upsert(settings,targetId,name,address,download.Info,result.Servers); checks.Clear(); ResetSession(); Save(); FillServers();
   } catch(SafeException e) { if(!closing) Error(e.Message); } catch { if(!closing) Error("Не удалось обновить подписку. Прежние профили сохранены."); }
   finally { busy=false; if(!closing) { add.Text="Профили"; UpdateControls(); } }
  }
  async Task CheckAll() {
   if(busy || connection!=null || settings.Servers.Count==0 || closing) return;
   busy=true; checkCancel=new CancellationTokenSource();
   var snapshot=settings.Servers.ToList(); int completed=0; check.Text="Стоп · 0/"+snapshot.Count; UpdateControls();
   try {
    checking=ProbeQueue.Run(snapshot,(server,token)=>Task.Run(()=>ServerCheck.Run(server,token)),(index,result)=>{
     RecordServerCheck(snapshot[index],result);
     if(result.CheckedAt.HasValue) {
      completed++;
      if(completed%4==0 && storageReady) Save();
     }
     check.Text="Стоп · "+completed+"/"+snapshot.Count;
     if(!closing) Invalidate(ServerPaintBounds);
    },checkCancel.Token,3);
    await checking;
   } catch(OperationCanceledException) {
    LoadSavedChecks();
   } catch { if(!closing) Error("Не удалось завершить проверку серверов."); }
   finally {
    if(completed>0 && storageReady) Save();
    checkCancel.Dispose(); checkCancel=null; checking=null; busy=false;
    if(!closing) { check.Text="Проверить все"; UpdateControls(); }
   }
  }
  async Task Toggle() {
   if(connection!=null) { await Disconnect(); return; }
   if(busy || closing || Selected()==null) return;
   HideSubscription(); ResetSession();
   var current=new Connection(); connection=current; state="CONNECTING"; UpdateControls();
   current.Status+=value=> { if(IsDisposed || Disposing) return; try { BeginInvoke((Action)(()=> {
    if(connection!=current || closing || current.Cancelled) return;
    string category=CoreFailure.Category(value);
    if(awaitingSwitch && category!="SWITCHING" && !category.StartsWith("ERROR")) return;
    awaitingSwitch=false; state=category; UpdateControls();
    if(state=="CONNECTED" && Selected()!=null) {
     ServerCheckResult previous;
     if(!checks.TryGetValue(Selected(),out previous) || !previous.LatencyMilliseconds.HasValue) {
      RecordServerCheck(Selected(),new ServerCheckResult("Доступен",DateTimeOffset.Now));
      if(storageReady) Save();
     }
    }
    if(state=="BLOCKED") Error("Соединение потеряно. Kill switch блокирует трафик приложений, назначенных VPN. Нажмите «Отключить», чтобы вернуть обычный интернет.");
    if(state.StartsWith("ERROR") && !current.Cancelled) Error(FailureMessage(state)+CoreFailure.ExitSuffix(value));
   })); } catch {} };
   try { session=current.Start(Selected(),true,settings.Network.Snapshot()); await session; }
   catch(Exception e) {
    if(!current.Cancelled && !closing) {
     state="ERROR_CORE"; UpdateControls();
     Error(e is SafeException?e.Message:"Не удалось запустить подключение.");
    }
   }
   finally { current.Dispose(); if(connection==current) connection=null; session=null; awaitingSwitch=false; state="STOPPED"; if(!closing) UpdateControls(); }
  }
  async Task Disconnect() { if(connection==null) return; state="STOPPING"; connection.Stop(); UpdateControls(); connect.Enabled=false; connect.Text="Отключение…"; try { if(session!=null) await session; } catch {} }
  static string FailureMessage(string state) {
   switch(state) {
    case "ERROR_TUN": return "Не удалось настроить TUN. Проверьте, не включён ли другой VPN.";
    case "ERROR_PERMISSION": return "Windows отказала в доступе. Для подключения нужны права администратора.";
    case "ERROR_DNS": return "Не удалось запустить DNS или разрешить адрес сервера.";
    case "ERROR_CONFIG": return "Ядро отклонило параметры сервера.";
    case "ERROR_NETWORK": return "Соединение через сервер не прошло проверку.";
    case "ERROR_BIND": return "Нужный сетевой адрес уже занят.";
    case "ERROR_STOP": return "Ядро ещё завершает работу. Ожидайте остановки.";
    default: return "Не удалось запустить или сохранить работу VPN-ядра.";
   }
  }
 }
}
