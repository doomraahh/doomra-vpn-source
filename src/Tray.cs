using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Luna {
 internal sealed class VpnTrayRenderer : ToolStripRenderer {
  internal const int HeaderTextLeft=22,HeaderTextRight=10;
  internal bool Light,Online;
  internal ToolStripLabel Header;
  Color Text { get { return Palette.Gray(Light?28:238); } }
  Color Muted { get { return Palette.Gray(Light?88:167); } }
  Color Border { get { return Palette.Gray(Light?192:66); } }
  protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
   var bounds=new Rectangle(Point.Empty,e.ToolStrip.Size);
   using(var background=new LinearGradientBrush(bounds,Palette.Gray(Light?245:32),Palette.Gray(Light?229:19),90))
    e.Graphics.FillRectangle(background,bounds);
   int size=e.ToolStrip.Font.Height*3;
   Dashboard.DrawWatermark(e.Graphics,new Rectangle(bounds.Right-size-9,bounds.Bottom-size-8,size,size),Light?.22f:.18f);
  }
  protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
   using(var pen=new Pen(Border)) e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
  }
  protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
   if(!e.Item.Enabled || !e.Item.Selected) return;
   var saved=e.Graphics.Save();
   e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   Dashboard.Fill(e.Graphics,new RectangleF(3,1,e.Item.Width-6,e.Item.Height-2),
    Light?Color.FromArgb(190,203,203,203):Color.FromArgb(200,59,59,59),7);
   e.Graphics.Restore(saved);
  }
  protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
   using(var pen=new Pen(Border)) e.Graphics.DrawLine(pen,10,e.Item.Height/2,e.Item.Width-10,e.Item.Height/2);
  }
  protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
   bool header=e.Item==Header;
   e.TextColor=header || !e.Item.Enabled?Muted:Text;
   if(header) {
    var saved=e.Graphics.Save();
    e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
    using(var dot=new SolidBrush(Online?Color.FromArgb(104,200,135):Muted))
     e.Graphics.FillEllipse(dot,9,(e.Item.Height-6)/2f,6,6);
    e.Graphics.Restore(saved);
    var text=new Rectangle(HeaderTextLeft,0,e.Item.Width-HeaderTextLeft-HeaderTextRight,e.Item.Height);
    TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,text,e.TextColor,
     TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
    return;
   }
   base.OnRenderItemText(e);
  }
 }

 internal static class LoginStartup {
  const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
  const string Name="doomra VPN";
  internal static string Command { get { return "\""+Assembly.GetExecutingAssembly().Location+"\" --autostart"; } }
  internal static bool Enabled {
   get {
    using(var key=Registry.CurrentUser.OpenSubKey(RunKey))
     return key!=null && string.Equals(key.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase);
   }
  }
  internal static void Set(bool enabled) {
   using(var key=Registry.CurrentUser.CreateSubKey(RunKey)) {
    if(enabled) key.SetValue(Name,Command);
    else if(string.Equals(key.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase)) key.DeleteValue(Name,false);
   }
  }
  internal static void RemoveFor(string sid) {
   using(var key=Registry.Users.OpenSubKey(sid+"\\"+RunKey,true))
    if(key!=null && string.Equals(key.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase)) key.DeleteValue(Name,false);
  }
 }

 public sealed partial class MainForm {
  static readonly int ShowMessage=RegisterWindowMessage("doomraVPN.Show.v1");
  static readonly int TaskbarButtonCreated=RegisterWindowMessage("TaskbarButtonCreated");
  NotifyIcon tray;
  ContextMenuStrip trayMenu;
  Icon connectedTrayIcon,taskbarBadge;
  bool taskbarButtonReady;
  bool exitRequested,installed,launchAtLogin;

  internal static void RequestShow() { if(ShowMessage!=0) PostMessage(new IntPtr(0xffff),ShowMessage,IntPtr.Zero,IntPtr.Zero); }
  void InitializeTray(bool atLogin) {
   installed=VpnService.IsInstalled;
   launchAtLogin=installed && LoginStartup.Enabled;
   UpdateControls();
   trayMenu=CreateTrayMenu();
   tray=new NotifyIcon { Text="VPN",Icon=Icon,ContextMenuStrip=trayMenu,Visible=true };
   tray.DoubleClick+=delegate { ShowFromTray(); };
   if(atLogin && installed) Shown+=async delegate {
    Hide();
    if(launchAtLogin && settings.ConnectAtLogin && storageReady && Selected()!=null) await Toggle();
   };
  }
  ContextMenuStrip CreateTrayMenu() {
   var menuFont=new Font("Segoe UI Light",10f);
   var menu=new ContextMenuStrip { ShowImageMargin=false,Font=menuFont,Padding=new Padding(4,3,4,3) };
   menu.Disposed+=delegate { menuFont.Dispose(); };
   var status=new ToolStripLabel("VPN") { AutoSize=false,TextAlign=ContentAlignment.MiddleLeft };
   var renderer=new VpnTrayRenderer { Header=status };
   menu.Renderer=renderer;
   menu.Items.Add(status);
   menu.Items.Add(new ToolStripSeparator());
   menu.Items.Add("Открыть VPN",null,delegate { ShowFromTray(); });
   var connectItem=new ToolStripMenuItem("Подключить",null,async delegate { await Toggle(); });
   menu.Items.Add(connectItem);
   menu.Items.Add("Выход",null,delegate { exitRequested=true; Close(); });
   foreach(ToolStripItem item in menu.Items)
    if(item is ToolStripMenuItem) item.Padding=new Padding(9,3,12,3);
   menu.Opening+=delegate {
    renderer.Light=settings.LightTheme; renderer.Online=headerOnline;
    status.Text="VPN · "+Dashboard.ConnectionText(state,headerOnline);
    connectItem.Text=connection==null?"Подключить":"Отключить"; connectItem.Enabled=connect.Enabled;
    var textSize=TextRenderer.MeasureText(status.Text,status.Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
    status.Size=new Size(textSize.Width+VpnTrayRenderer.HeaderTextLeft+VpnTrayRenderer.HeaderTextRight,textSize.Height+8);
    menu.MinimumSize=new Size(status.Width+menu.Padding.Horizontal+2,0);
   };
   return menu;
  }
  void ShowFromTray() {
   if(closing) return;
   Show();
   if(WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal;
   Activate();
  }
  void DisposeTray() {
   if(tray!=null) { tray.Visible=false; tray.Dispose(); }
   if(trayMenu!=null) trayMenu.Dispose();
   if(connectedTrayIcon!=null) connectedTrayIcon.Dispose();
   if(taskbarBadge!=null) taskbarBadge.Dispose();
  }
  void RefreshConnectionBadge() {
   if(tray==null) return;
   if(headerOnline && connectedTrayIcon==null) connectedTrayIcon=ConnectionBadge.LoadTrayIcon(SystemInformation.SmallIconSize.Width);
   tray.Icon=headerOnline?connectedTrayIcon:Icon;
   tray.Text=headerOnline?"VPN · Подключено":"VPN";
   RefreshTaskbarBadge();
  }
  void RefreshTaskbarBadge() {
   if(tray==null || !taskbarButtonReady || !IsHandleCreated) return;
   if(headerOnline && taskbarBadge==null) taskbarBadge=ConnectionBadge.Create();
   ConnectionBadge.SetTaskbarOverlay(Handle,headerOnline?taskbarBadge:null);
  }
  protected override void OnHandleDestroyed(EventArgs e) {
   taskbarButtonReady=false;
   base.OnHandleDestroyed(e);
  }
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string text);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
 }
}
