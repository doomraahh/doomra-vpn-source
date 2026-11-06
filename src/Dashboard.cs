using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Text.RegularExpressions;
namespace Luna {
 internal sealed class UiAction { internal string Text; internal bool Enabled; internal UiAction(string text) { Text=text; } }
 internal sealed class DashboardDetails { internal NetworkPolicy Network; internal int AppScroll; internal bool NetworkEditAllowed=true,NetworkSessionActive,Installed,LaunchAtLogin,ConnectAtLogin; internal SubscriptionInfo Subscription; internal string Elapsed="00:00:00",Upload="—",Download="—",Speed=MetricText.Speed(null,null),Notice="Из личного кабинета провайдера"; internal float Angle,ArcAngle,SuccessRing; internal bool Moving,WaitingArc,CanRefresh,Dropdown,Light,KeyboardFocus,NewProfile,SettingsPage; internal string Menu="",ProfileName="Основной"; internal List<string> ProfileNames=new List<string>(); internal int ProfileIndex,ProfileScroll; internal string DeleteName=""; }
 internal sealed class UiSelection { internal string Text="Не выбран"; }
 internal sealed class DashboardList {
  internal List<Server> Items=new List<Server>(); internal Dictionary<Server,ServerCheckResult> Checks=new Dictionary<Server,ServerCheckResult>();
  internal const int RowHeight=40,RowPitch=44;
  internal int Selected=-1,Hover=-1,Scroll,Viewport=400; internal bool Enabled=true,CanChoose=true;
  internal event Action<int> SelectedChanged;
  internal int MaxScroll { get { return Math.Max(0,Math.Max(0,Items.Count*RowPitch-(RowPitch-RowHeight))-Viewport); } }
  internal void Assign(List<Server> items,Dictionary<Server,ServerCheckResult> checks,int selected) { Items=items; Checks=checks; Selected=selected; ScrollTo(Scroll); }
  internal void ScrollTo(int offset) { Scroll=Math.Max(0,Math.Min(MaxScroll,offset)); }
  internal void Choose(int index) { if(!Enabled || !CanChoose || index<0 || index>=Items.Count) return; bool changed=index!=Selected; Selected=index; if(index*RowPitch<Scroll) ScrollTo(index*RowPitch); if(index*RowPitch+RowHeight>Scroll+Viewport) ScrollTo(index*RowPitch+RowHeight-Viewport); var callback=SelectedChanged; if(changed && callback!=null) callback(index); }
 }
 internal struct ServerTableColumns {
  internal const float ScrollbarSpace=12;
  internal const float NameInset=32;
  internal readonly RectangleF Server,Protocol,Status,Checked,Latency;
  internal RectangleF Name { get { return new RectangleF(Server.X+NameInset,Server.Y,Server.Width-NameInset,Server.Height); } }
  internal ServerTableColumns(RectangleF row) {
   const float inset=8,gap=4,protocol=92,status=92,timeWidth=106,latencyWidth=76;
   float latencyX=row.Right-inset-latencyWidth;
   float timeX=latencyX-gap-timeWidth;
   float statusX=timeX-gap-status;
   float protocolX=statusX-gap-protocol;
   Server=new RectangleF(row.X+inset,row.Y,protocolX-gap-row.X-inset,row.Height);
   Protocol=new RectangleF(protocolX,row.Y,protocol,row.Height);
   Status=new RectangleF(statusX,row.Y,status,row.Height);
   Checked=new RectangleF(timeX,row.Y,timeWidth,row.Height);
   Latency=new RectangleF(latencyX,row.Y,latencyWidth,row.Height);
  }
 }
 internal sealed class DashboardLayout {
  internal RectangleF ConnectCard,SelectedCard,ServersCard,Connect,Add,Check,List,Footer,RefreshProfile,Popup,Input,Submit,Dismiss,NameInput,Settings,SettingsPage,Back,ThemeRow,PageThemeToggle,Delete,Minimize,Maximize,Close;
  internal readonly Size Size;
  internal DashboardLayout(Size size) {
   Size=size; float width=size.Width,height=size.Height,left=280,gap=24,pad=24,top=76;
   ConnectCard=new RectangleF(pad,top,left,280);
   SelectedCard=new RectangleF(pad,ConnectCard.Bottom+20,left,height-ConnectCard.Bottom-44);
   ServersCard=new RectangleF(pad+left+gap,top,width-left-gap-pad*2,height-top-pad);
   Connect=new RectangleF(pad+24,ConnectCard.Bottom-64,left-48,44);
   Add=new RectangleF(ServersCard.Right-140,top+24,116,40);
   Check=new RectangleF(Add.X-52,Add.Y,40,40);
   RefreshProfile=new RectangleF(Check.X-52,Add.Y,40,40);
   Settings=new RectangleF(RefreshProfile.X-52,Add.Y,40,40);
   SettingsPage=new RectangleF(pad,top,width-pad*2,height-top-pad);
   Back=new RectangleF(pad+24,top+24,112,40);
   ThemeRow=new RectangleF(pad+24,top+136,width-pad*2-48,88);
   PageThemeToggle=new RectangleF(ThemeRow.Right-76,ThemeRow.Y+30,52,28);
   Delete=new RectangleF(ServersCard.Right-84,top+88,40,32);
   Footer=new RectangleF(ServersCard.X+24,ServersCard.Bottom-112,ServersCard.Width-48,104);
   float listTop=top+118;
   List=new RectangleF(ServersCard.X+24,listTop,ServersCard.Width-48,Footer.Y-listTop-16);
   Popup=new RectangleF(ServersCard.Right-Math.Min(448,ServersCard.Width-48)-24,Add.Bottom+12,Math.Min(448,ServersCard.Width-48),336);

   NameInput=new RectangleF(Popup.X+20,Popup.Y+80,Popup.Width-40,40);
   Input=new RectangleF(Popup.X+20,Popup.Y+152,Popup.Width-40,40);
   Submit=new RectangleF(Popup.X+20,Popup.Bottom-60,Popup.Width-164,40);
   Dismiss=new RectangleF(Submit.Right+12,Submit.Y,112,40);
   Close=new RectangleF(width-48,0,48,48); Maximize=new RectangleF(width-96,0,48,48); Minimize=new RectangleF(width-144,0,48,48);
  }
  internal RectangleF MenuRect(string mode,int count=4) { return new RectangleF(Popup.X,Popup.Y,Popup.Width,mode=="delete"?224:(mode=="profiles"?152+Math.Min(4,count)*48:336)); }
  internal RectangleF MenuClose(string mode,int count=4) { var r=MenuRect(mode,count); return new RectangleF(r.Right-132,r.Bottom-60,112,40); }
  internal RectangleF MenuSubmit(string mode,int count) { var r=MenuRect(mode,count); return new RectangleF(r.X+20,r.Bottom-60,r.Width-164,40); }
  internal RectangleF ProfileRow(int index,int offset) { return new RectangleF(Popup.X+12,Popup.Y+48+(index-offset)*48,Popup.Width-24,44); }
  internal RectangleF ProfileEdit(int index,int offset) { var r=ProfileRow(index,offset); return new RectangleF(r.Right-44,r.Y,44,44); }
  internal string Hit(PointF point) {
   if(Close.Contains(point)) return "close"; if(Maximize.Contains(point)) return "max"; if(Minimize.Contains(point)) return "min";
   if(Connect.Contains(point)) return "connect"; if(Add.Contains(point)) return "add"; if(Check.Contains(point)) return "check"; if(RefreshProfile.Contains(point)) return "refresh-profile"; if(Settings.Contains(point)) return "settings"; return "";
  }
 }
 internal static class Dashboard {
  [ThreadStatic] static TextCanvas.Batch liveText;
  internal static Color Text { get { return Palette.Text; } }
  internal static Color Muted { get { return Palette.Muted; } }
  internal static Color Accent { get { return Palette.Accent; } }
  internal static string ConnectionText(string state,bool online) {
   if(online) return "Подключено";
   switch(state) {
    case "BLOCKED": return "Защита активна";
    case "VERIFYING": return "Проверяем связь…";
    case "SWITCHING": return "Меняем сервер…";
    case "CONNECTING": return "Запускаем VPN…";
    case "RETRYING": return "Восстановление";
    case "STOPPING": return "Отключение…";
    default: return "Не подключено";
   }
  }
  internal static GraphicsPath Round(RectangleF rect,float radius) {
   var path=new GraphicsPath(); float d=Math.Max(1,Math.Min(radius*2,Math.Min(rect.Width,rect.Height)));
   path.AddArc(rect.X,rect.Y,d,d,180,90); path.AddArc(rect.Right-d,rect.Y,d,d,270,90); path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.X,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
  }
  internal static void Fill(Graphics g,RectangleF r,Color color,float radius=14) { using(var path=Round(r,radius)) using(var brush=new SolidBrush(color)) g.FillPath(brush,path); }
  internal static void Line(Graphics g,RectangleF r,Color color,float radius=14) { using(var path=Round(r,radius)) using(var pen=new Pen(color,1)) g.DrawPath(pen,path); }
  internal static void DrawCloseButton(Graphics g,RectangleF bounds,bool highlighted) {
   if(highlighted) Fill(g,RectangleF.Inflate(bounds,-4,-6),Color.FromArgb(100,78,78,78),7);
   float x=bounds.X+bounds.Width/2,y=bounds.Y+bounds.Height/2+1;
   using(var pen=new Pen(Text,1.2f)) {
    g.DrawLine(pen,x-5,y-5,x+5,y+5);
    g.DrawLine(pen,x+5,y-5,x-5,y+5);
   }
  }
  internal static void Label(Graphics g,string text,RectangleF r,float size,Color color,bool bold=false,bool center=false,bool wrap=false) {
   var flags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis;
   flags|=center?TextFormatFlags.HorizontalCenter:TextFormatFlags.Left;
   flags|=wrap?TextFormatFlags.WordBreak:TextFormatFlags.SingleLine;
   if(liveText!=null) { liveText.Add(text??"",r,size,color,bold,flags); return; }
   using(var transform=g.Transform) {
    var points=new[]{new PointF(r.X,r.Y),new PointF(r.Right,r.Bottom)}; transform.TransformPoints(points);
    float scale=(float)Math.Sqrt(transform.Elements[2]*transform.Elements[2]+transform.Elements[3]*transform.Elements[3]);
    var bounds=Rectangle.FromLTRB((int)Math.Round(points[0].X),(int)Math.Round(points[0].Y),(int)Math.Round(points[1].X),(int)Math.Round(points[1].Y));
    using(var clip=g.Clip) {
     clip.Transform(transform); var saved=g.Save(); g.ResetTransform(); g.ResetClip();
     try {
      using(var font=UiFonts.Get(Math.Max(13,size)*scale,bold)) TextCanvas.Draw(g,clip,text??"",font,bounds,color,flags);
     } finally { g.Restore(saved); }
    }
   }
  }
  static readonly Image Logo=RasterArtwork("Logo",256,256,new Rectangle(30,30,1194,1194));
  static Image LoadArtwork(string name) {
   using(var stream=typeof(Dashboard).Assembly.GetManifestResourceStream("Doomra."+name))
   using(var image=Image.FromStream(stream)) return new Bitmap(image);
  }
  static Bitmap RasterArtwork(string name,int width,int height,Rectangle crop) {
   var result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
   using(var source=LoadArtwork(name))
   using(var g=Graphics.FromImage(result)) {
    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
    g.DrawImage(source,new Rectangle(0,0,width,height),crop,GraphicsUnit.Pixel);
   }
   return result;
  }
  static void DrawBackground(Graphics g,RectangleF bounds) {
   using(var background=new SolidBrush(Palette.Background)) g.FillRectangle(background,bounds);
  }
  internal static void Symbol(Graphics g,RectangleF rect,Color ink) {
   g.DrawImage(Logo,rect);
  }
  internal static void DrawWatermark(Graphics g,Rectangle bounds,float opacity) {
   using(var alpha=new ImageAttributes()) {
    alpha.SetColorMatrix(new ColorMatrix { Matrix33=opacity });
    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
    g.DrawImage(Logo,bounds,0,0,Logo.Width,Logo.Height,GraphicsUnit.Pixel,alpha);
   }
  }
  internal static void Card(Graphics g,RectangleF r) {
   using(var path=Round(r,16)) using(var gradient=new LinearGradientBrush(r,Palette.Surface,Palette.SurfaceBottom,90)) g.FillPath(gradient,path);
   Line(g,r,Palette.Border,16);
  }
  internal static void Button(Graphics g,RectangleF r,UiAction action,string id,string hot,string pressed,int focus,bool primary) {
   bool hover=hot==id && action.Enabled,down=pressed==id && action.Enabled;
   Color color=primary?(action.Enabled?(down?Palette.Gray(Palette.Light?60:185):Accent):Palette.Gray(Palette.Light?215:72)):(hover?Palette.Hover:Palette.SurfaceBottom);
   Fill(g,r,color,10);
   if(!primary) Line(g,r,Palette.Border,10);
   var label=r;
   if(id=="add") label.Width-=22;
   Label(g,action.Text,label,id=="connect"?16:14,primary?(action.Enabled?Palette.OnAccent:Muted):(action.Enabled?Text:Muted),true,true);
   if(id=="add") Icon(g,new RectangleF(r.Right-28,r.Y,16,r.Height),"down",action.Enabled?Text:Muted);
   int index=id=="refresh-profile"?16:id=="connect"?0:(id=="add"?1:(id=="check"?2:(id=="save"?10:(id=="new"?14:(id=="back"?17:(id=="confirm-delete"?19:11))))));
   if(focus==index) {
    if(id=="connect") using(var pen=new Pen(Palette.OnAccent,2)) g.DrawLine(pen,r.X+r.Width/2-24,r.Bottom-6,r.X+r.Width/2+24,r.Bottom-6);
    else Line(g,RectangleF.Inflate(r,3,3),Palette.Muted,12);
   }
  }
  internal static void Icon(Graphics g,RectangleF r,string id,Color color) {
   using(var pen=new Pen(color,1.8f) { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round }) {
    float x=r.X+r.Width/2,y=r.Y+r.Height/2;
    if(id=="refresh") { g.DrawArc(pen,x-8,y-8,16,16,40,285); g.DrawLine(pen,x+7,y-7,x+8,y-2); g.DrawLine(pen,x+8,y-2,x+3,y-3); }
    else if(id=="pulse") { g.DrawLines(pen,new[]{new PointF(x-9,y),new PointF(x-5,y),new PointF(x-2,y-7),new PointF(x+2,y+7),new PointF(x+5,y),new PointF(x+9,y)}); }
    else if(id=="stop") { g.DrawRectangle(pen,x-6,y-6,12,12); }
    else if(id=="edit") { g.DrawLine(pen,x-6,y+5,x+5,y-6); g.DrawLine(pen,x-6,y+5,x-7,y+8); g.DrawLine(pen,x-7,y+8,x-3,y+7); g.DrawLine(pen,x+3,y-7,x+7,y-3); }
    else if(id=="settings") { g.DrawEllipse(pen,x-6,y-6,12,12); g.DrawEllipse(pen,x-2,y-2,4,4); for(int i=0;i<8;i++) { double a=i*Math.PI/4; g.DrawLine(pen,x+(float)Math.Cos(a)*7,y+(float)Math.Sin(a)*7,x+(float)Math.Cos(a)*9,y+(float)Math.Sin(a)*9); } }
    else if(id=="trash") { g.DrawLine(pen,x-7,y-5,x+7,y-5); g.DrawLine(pen,x-3,y-8,x+3,y-8); g.DrawLine(pen,x-5,y-2,x-4,y+8); g.DrawLine(pen,x+5,y-2,x+4,y+8); g.DrawLine(pen,x-4,y+8,x+4,y+8); }
    else if(id=="tick") { g.DrawLine(pen,x-5,y,x-1,y+4); g.DrawLine(pen,x-1,y+4,x+6,y-4); }
    else if(id=="down") { g.DrawLine(pen,x-4,y-2,x,y+2); g.DrawLine(pen,x,y+2,x+4,y-2); }
   }
  }
  static void IconButton(Graphics g,RectangleF r,string id,bool enabled,string hot,string pressed,bool focused) {
   Fill(g,r,hot==id && enabled?Palette.Hover:Palette.SurfaceBottom,10);
   Icon(g,r,id=="check"?"pulse":(id=="refresh-profile"?"refresh":id),enabled?Text:Muted);
   if(focused) Line(g,RectangleF.Inflate(r,3,3),Muted,12);
  }
  static readonly string[,] CountryTitles={
   {"germany|deutschland|DE","Германия","DE"},{"netherlands|holland|NL","Нидерланды","NL"},
   {"united states|usa|US","США","US"},{"united kingdom|great britain|uk|GB","Великобритания","GB"},
   {"canada|CA","Канада","CA"},{"finland|FI","Финляндия","FI"},{"france|FR","Франция","FR"},
   {"japan|JP","Япония","JP"},{"singapore|SG","Сингапур","SG"},{"switzerland|CH","Швейцария","CH"},
   {"sweden|SE","Швеция","SE"},{"norway|NO","Норвегия","NO"},{"spain|ES","Испания","ES"},
   {"poland|PL","Польша","PL"},{"austria|AT","Австрия","AT"},{"russia|RU","Россия","RU"}
  };
  sealed class CachedTitle {
   internal string Name,Host,Title;
  }
  static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Server,CachedTitle> ServerTitles=new System.Runtime.CompilerServices.ConditionalWeakTable<Server,CachedTitle>();
  internal static string ServerTitle(Server server) {
   var cached=ServerTitles.GetValue(server,key=>new CachedTitle());
   if(cached.Title==null || cached.Name!=server.Name || cached.Host!=server.Host) {
    cached.Name=server.Name; cached.Host=server.Host; cached.Title=FormatServerTitle(server);
   }
   return cached.Title;
  }
  static string FormatServerTitle(Server server) {
   string text=Regex.Replace(ServerNames.Display(server),@"\s*·\s*(VLESS|VMESS|TROJAN|SS|SHADOWSOCKS)\s*/.*$","",RegexOptions.IgnoreCase);
   text=Regex.Replace(text,@"[\uD83C][\uDDE6-\uDDFF][\uD83C][\uDDE6-\uDDFF]","");
   text=Regex.Replace(text,@"[^\p{L}\p{N}\s#.,_()\-+/]","").Replace('_',' ');
   text=Regex.Replace(text,@"\s+"," ").Trim();
   if(text.StartsWith("EU ",StringComparison.Ordinal)) text=text.Substring(3);
   for(int i=0;i<CountryTitles.GetLength(0);i++) {
    string aliases=string.Join("|",Array.ConvertAll(CountryTitles[i,0].Split('|'),alias=>alias.Length==2?"(?-i:"+Regex.Escape(alias.ToUpperInvariant())+")":Regex.Escape(alias)));
    var match=Regex.Match(text,"^("+aliases+@")(?![\p{L}\p{N}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    if(!match.Success) continue;
    string suffix=text.Substring(match.Length).Trim();
    suffix=Regex.Replace(suffix,@"(?:^|\s)"+CountryTitles[i,2]+@"$","",RegexOptions.IgnoreCase).Trim();
    if(suffix.Length==0) return CountryTitles[i,1];
    if(suffix.Equals(CountryTitles[i,1],StringComparison.OrdinalIgnoreCase) || suffix.StartsWith(CountryTitles[i,1]+" ",StringComparison.OrdinalIgnoreCase)) return suffix;
    return CountryTitles[i,1]+" · "+suffix;
   }
   return text;
  }
  static void Flag(Graphics g,RectangleF r,string code) {
   var image=CountryFlags.Get(code);
   if(image!=null) {
    float factor=Math.Min(r.Width/image.Width,r.Height/image.Height),w=image.Width*factor,h=image.Height*factor;
    var fit=new RectangleF(r.X+(r.Width-w)/2,r.Y+(r.Height-h)/2,w,h);
    var save=g.Save(); using(var clip=Round(fit,2)) g.SetClip(clip,CombineMode.Intersect);
    g.DrawImage(image,fit); g.Restore(save);
   } else using(var pen=new Pen(Muted,1.2f)) { float x=r.X+r.Width/2,y=r.Y+r.Height/2; g.DrawEllipse(pen,x-8,y-8,16,16); g.DrawEllipse(pen,x-3,y-8,6,16); g.DrawLine(pen,x-8,y,x+8,y); }
  }
  internal static void DrawConnection(Graphics g,DashboardLayout layout,UiAction connect,string state,bool online,string hot,string pressed,int focus,DashboardDetails details,bool repaintBackground=true) {
   Palette.Light=details.Light;
   if(!details.KeyboardFocus) focus=-1;
   g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=liveText==null?InterpolationMode.HighQualityBicubic:InterpolationMode.Low;
   if(repaintBackground) DrawBackground(g,RectangleF.Inflate(layout.ConnectCard,2,2));
   Card(g,layout.ConnectCard);
   if(online) Line(g,layout.ConnectCard,Palette.Light?Color.FromArgb(36,122,79):Color.FromArgb(121,201,156),16);
   var c=layout.ConnectCard;
   Label(g,"Соединение",new RectangleF(c.X+24,c.Y+16,c.Width-48,24),12,Muted);
   var logoRect=new RectangleF(c.X+c.Width/2-52,c.Y+37,104,104);
   if(details.Moving) {
    var transform=g.Save(); float cx=logoRect.X+logoRect.Width/2,cy=logoRect.Y+logoRect.Height/2;
    g.TranslateTransform(cx,cy); g.RotateTransform(details.Angle);
    g.TranslateTransform(-cx,-cy);
    Symbol(g,logoRect,Accent); g.Restore(transform);
   } else Symbol(g,logoRect,Accent);
   var ring=RectangleF.Inflate(logoRect,5,5);
   if(details.WaitingArc) {
    using(var pen=new Pen(Muted,1.5f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
     g.DrawArc(pen,ring,details.ArcAngle,76);
   } else if(details.SuccessRing>0) {
    var green=Palette.Light?Color.FromArgb(36,122,79):Color.FromArgb(121,201,156);
    using(var pen=new Pen(Color.FromArgb((int)(255*details.SuccessRing),green),1.8f)) g.DrawEllipse(pen,ring);
   }
   string status=ConnectionText(state,online);
   Label(g,status,new RectangleF(c.X+16,c.Y+144,c.Width-32,32),20,Text,false,true);
   string hint=online?details.Speed:(state=="VERIFYING"?"Проверяем доступ через VPN":(state=="CONNECTING" || state=="SWITCHING"?"Настраиваем сетевой адаптер":"Выберите сервер в списке"));
   Label(g,hint,new RectangleF(c.X+16,c.Y+180,c.Width-32,24),13,online?Text:Muted,false,true);
   Button(g,layout.Connect,connect,"connect",hot,pressed,focus,true);
  }
  internal static void DrawServerViewport(Graphics g,DashboardLayout layout,DashboardList list,bool light) {
   Palette.Light=light;
   g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit; g.InterpolationMode=InterpolationMode.HighQualityBicubic;
   var saved=g.Save();
   g.SetClip(layout.List,CombineMode.Intersect);
   Card(g,layout.ServersCard);
   DrawServerRows(g,layout,list);
   g.Restore(saved);
  }
  static void DrawServerRows(Graphics g,DashboardLayout layout,DashboardList list) {
   var clipState=g.Save(); g.SetClip(layout.List,CombineMode.Intersect);
   if(liveText!=null) liveText.Clip=liveText.Pixels(layout.List);
   if(list.Items.Count==0) {
    Label(g,"Здесь появятся серверы",new RectangleF(layout.List.X,layout.List.Y+72,layout.List.Width,32),15,Text,false,true);
    Label(g,"Профили → Добавить",new RectangleF(layout.List.X,layout.List.Y+108,layout.List.Width,28),12,Muted,false,true);
   }
   int first=list.Scroll/DashboardList.RowPitch;
   int last=Math.Min(list.Items.Count-1,(int)(list.Scroll+layout.List.Height)/DashboardList.RowPitch);
   for(int i=first;i<=last;i++) {
    float y=layout.List.Y+i*DashboardList.RowPitch-list.Scroll; if(y+DashboardList.RowHeight<layout.List.Top || y>layout.List.Bottom) continue;
    var r=new RectangleF(layout.List.X,y,layout.List.Width-ServerTableColumns.ScrollbarSpace,DashboardList.RowHeight); bool chosen=i==list.Selected;
    if(chosen) { Fill(g,r,Palette.Selection,8); Line(g,r,Palette.Border,8); }
    else if(i==list.Hover && list.Enabled && list.CanChoose) Fill(g,r,Palette.Hover,8);
    else if(i<list.Items.Count-1) using(var divider=new Pen(Palette.Border)) g.DrawLine(divider,r.X+12,r.Bottom+2,r.Right-12,r.Bottom+2);
    Server server=list.Items[i]; ServerCheckResult result; list.Checks.TryGetValue(server,out result);
    var columns=new ServerTableColumns(r);
    Flag(g,new RectangleF(columns.Server.X,r.Y+11,24,18),CountryFlags.Resolve(server.Name,server.Host));
    Label(g,ServerTitle(server),columns.Name,13,Text);
    Label(g,server.Protocol.ToUpperInvariant()+"/"+server.Transport.ToUpperInvariant(),columns.Protocol,13,Muted);
    string status=result==null?"Не проверен":result.Text;
    bool checkedServer=result!=null && result.CheckedAt.HasValue;
    Color color=Muted;
    if(checkedServer) color=status=="Доступен"?(Palette.Light?Color.FromArgb(23,101,60):Color.FromArgb(121,201,156)):(Palette.Light?Color.FromArgb(161,35,53):Color.FromArgb(243,152,157));
    Label(g,status=="Ошибка проверки"?"Ошибка":status,columns.Status,13,color,checkedServer);
    Label(g,checkedServer?result.CompactTimeLabel:"—",columns.Checked,13,checkedServer?Text:Muted);
    Label(g,result==null?"—":result.LatencyLabel,columns.Latency,13,result!=null && result.LatencyMilliseconds.HasValue?Text:Muted,false,true);
   }
   g.Restore(clipState);
   if(liveText!=null) liveText.Clip=null;
   if(list.MaxScroll>0) {
    float track=layout.List.Height-8,thumb=Math.Max(24,track*layout.List.Height/Math.Max(1,list.Items.Count*DashboardList.RowPitch));
    Fill(g,new RectangleF(layout.List.Right-5,layout.List.Y+4+(track-thumb)*list.Scroll/list.MaxScroll,3,thumb),Color.FromArgb(105,149,149,149),1.5f);
   }
  }
  static void DrawServerHeaders(Graphics g,DashboardLayout layout) {
   var header=new RectangleF(layout.List.X,layout.List.Top-28,layout.List.Width-ServerTableColumns.ScrollbarSpace,26);
   var columns=new ServerTableColumns(header);
   Label(g,"Сервер",columns.Name,13,Muted,true);
   Label(g,"Протокол",columns.Protocol,13,Muted,true);
   Label(g,"Статус",columns.Status,13,Muted,true);
   Label(g,"Проверено",columns.Checked,13,Muted,true);
   Label(g,"Задержка",columns.Latency,13,Muted,true,true);
  }
  internal static void DrawSessionFooter(Graphics g,DashboardLayout layout,DashboardDetails details,bool repaintBackground=false) {
   if(repaintBackground) {
    Palette.Light=details.Light; g.SmoothingMode=SmoothingMode.AntiAlias;
    Card(g,layout.ServersCard);
   }
   var footer=layout.Footer;
   using(var line=new Pen(Palette.Border)) g.DrawLine(line,footer.X,footer.Y,footer.Right,footer.Y);
   float column=footer.Width/3;
   Label(g,"Время сеанса",new RectangleF(footer.X,footer.Y+8,column-12,24),12,Muted);
   Label(g,details.Elapsed,new RectangleF(footer.X,footer.Y+32,column-12,24),16,Text);
   Label(g,"Получено",new RectangleF(footer.X+column,footer.Y+8,column-12,24),12,Muted);
   Label(g,details.Download,new RectangleF(footer.X+column,footer.Y+32,column-12,24),16,Text);
   Label(g,"Отправлено",new RectangleF(footer.X+column*2,footer.Y+8,column-4,24),12,Muted);
   Label(g,details.Upload,new RectangleF(footer.X+column*2,footer.Y+32,column-4,24),16,Text);
   Label(g,"За текущее подключение · TUN",new RectangleF(footer.X,footer.Y+84,footer.Width,20),12,Muted);
  }
  internal static void DrawLive(Graphics g,DashboardLayout layout,DashboardList list,UiAction connect,UiAction add,UiAction check,string state,bool online,string hot,string pressed,int focus,bool maximized,DashboardDetails details) {
   using(var batch=new TextCanvas.Batch(g)) {
    liveText=batch;
    try { Draw(g,layout,list,connect,add,check,state,online,hot,pressed,focus,maximized,details); batch.Flush(); }
    finally { liveText=null; }
   }
  }
  internal static void Draw(Graphics g,DashboardLayout layout,DashboardList list,UiAction connect,UiAction add,UiAction check,string state,bool online,string hot,string pressed,int focus,bool maximized,DashboardDetails details) {
   Palette.Light=details.Light;
   if(!details.KeyboardFocus) focus=-1;
   g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit; g.InterpolationMode=liveText==null?InterpolationMode.HighQualityBicubic:InterpolationMode.Low;
   DrawBackground(g,new RectangleF(0,0,layout.Size.Width,layout.Size.Height));
   var interpolation=g.InterpolationMode;
   g.InterpolationMode=InterpolationMode.HighQualityBicubic;
   Symbol(g,new RectangleF(23,12,24,24),Accent);
   g.InterpolationMode=interpolation;
   Label(g,"VPN",new RectangleF(57,12,30,25),12,Muted);
   using(var dot=new SolidBrush(online?Color.FromArgb(104,200,135):Color.FromArgb(229,104,116))) g.FillEllipse(dot,89,20,7,7);
   using(var line=new Pen(Color.FromArgb(27,170,170,170))) g.DrawLine(line,24,49,layout.Size.Width-24,49);
   foreach(string id in new[]{"min","max","close"}) {
    RectangleF r=id=="min"?layout.Minimize:(id=="max"?layout.Maximize:layout.Close);
    if(id=="close") { DrawCloseButton(g,r,hot==id); continue; }
    if(hot==id) Fill(g,new RectangleF(r.X+4,r.Y+6,r.Width-8,r.Height-12),Color.FromArgb(40,119,119,119),7);
    using(var pen=new Pen(Text,1.2f)) { float x=r.X+19,y=r.Y+20;
     if(id=="min") g.DrawLine(pen,x,y+4,x+10,y+4);
     else { if(maximized) { g.DrawRectangle(pen,x+2,y,9,8); g.DrawRectangle(pen,x,y+2,9,8); } else g.DrawRectangle(pen,x,y,10,9); }
    }
   }
   if(details.SettingsPage) { NetworkSettingsView.Draw(g,layout,details,hot,pressed,focus); return; }
   Card(g,layout.ServersCard);
   var selected=list.Selected>=0 && list.Selected<list.Items.Count?list.Items[list.Selected]:null;
   Card(g,layout.SelectedCard);
   DrawConnection(g,layout,connect,state,online,hot,pressed,focus,details,false);
   var c=layout.SelectedCard;
   Color cardMuted=Palette.Light?Palette.Gray(70):Palette.Gray(205);
   Label(g,"Выбранный сервер",new RectangleF(c.X+24,c.Y+16,c.Width-48,24),12,cardMuted);
   if(selected!=null) {
    Flag(g,new RectangleF(c.X+24,c.Y+56,32,24),CountryFlags.Resolve(selected.Name,selected.Host));
    Label(g,ServerTitle(selected),new RectangleF(c.X+68,c.Y+44,c.Width-92,48),14,Text,false,false,true);
    Label(g,selected.Protocol.ToUpperInvariant()+" / "+selected.Transport.ToUpperInvariant(),new RectangleF(c.X+24,c.Y+104,c.Width-48,24),12,cardMuted);
   } else Label(g,"Не выбран",new RectangleF(c.X+24,c.Y+48,c.Width-48,48),16,Text);
   float billing=layout.Footer.Y,half=(c.Width-48)/2;
   using(var line=new Pen(Palette.Border)) g.DrawLine(line,c.X+24,billing,c.Right-24,billing);
   Label(g,"Подписка до",new RectangleF(c.X+24,billing+8,half-8,24),12,cardMuted);
   Label(g,MetricText.Expiry(details.Subscription),new RectangleF(c.X+24,billing+32,half-8,24),14,Text);
   Label(g,"Использовано",new RectangleF(c.X+24+half,billing+8,half,24),12,cardMuted);
   Label(g,MetricText.Used(details.Subscription),new RectangleF(c.X+24+half,billing+32,half,24),14,Text);
   Label(g,MetricText.Plan(details.Subscription),new RectangleF(c.X+24,billing+60,c.Width-48,20),12,cardMuted);
   Label(g,details.ProfileIndex<0?"Добавьте профиль":MetricText.Updated(details.Subscription),new RectangleF(c.X+24,billing+84,c.Width-48,20),12,cardMuted);
   c=layout.ServersCard;
   Label(g,"Серверы",new RectangleF(c.X+24,c.Y+16,layout.Settings.X-c.X-36,32),22,Text);
   Label(g,details.ProfileName+" · "+list.Items.Count,new RectangleF(c.X+24,c.Y+52,layout.Check.X-c.X-36,24),12,Muted);
   IconButton(g,layout.Settings,"settings",true,hot,pressed,focus==12);
   IconButton(g,layout.RefreshProfile,"refresh-profile",details.CanRefresh,hot,pressed,focus==16);
   IconButton(g,layout.Check,"check",check.Enabled,hot,pressed,focus==2);
   if(check.Text=="Остановить" || check.Text.StartsWith("Стоп")) { Fill(g,layout.Check,Palette.Hover,10); Icon(g,layout.Check,"stop",Text); }
   Button(g,layout.Add,add,"add",hot,pressed,focus,false);
   DrawServerHeaders(g,layout);
   DrawServerRows(g,layout,list);
   DrawSessionFooter(g,layout,details);
   if(details.Dropdown) {
    if(liveText!=null) liveText.Flush();

    var panel=layout.MenuRect(details.Menu,details.ProfileNames.Count); Fill(g,panel,Palette.Surface,16); Line(g,panel,Palette.Border,16);
    string title=details.Menu=="profiles"?"Профили":(details.Menu=="settings"?"Настройки":(details.Menu=="delete"?"Удалить профиль?":(details.NewProfile?"Новый профиль":"Редактировать профиль")));
    Label(g,title,new RectangleF(panel.X+20,panel.Y+12,panel.Width-40,28),18,Text);
    if(details.Menu=="delete") {
     Label(g,details.DeleteName,new RectangleF(panel.X+20,panel.Y+56,panel.Width-40,28),16,Text);
     Label(g,"Ссылка и серверы этого профиля будут удалены",new RectangleF(panel.X+20,panel.Y+92,panel.Width-40,48),13,Muted,false,false,true);
     Button(g,layout.MenuSubmit(details.Menu,details.ProfileNames.Count),new UiAction("Удалить") { Enabled=true },"confirm-delete",hot,pressed,focus,true);
    } else if(details.Menu=="profiles") {
     for(int i=details.ProfileScroll;i<Math.Min(details.ProfileNames.Count,details.ProfileScroll+4);i++) {
      var row=layout.ProfileRow(i,details.ProfileScroll);
      if(i==details.ProfileIndex || hot=="profile:"+i) Fill(g,row,Palette.Selection,8);
      Label(g,details.ProfileNames[i],new RectangleF(row.X+12,row.Y+4,row.Width-64,36),14,Text);
      Icon(g,layout.ProfileEdit(i,details.ProfileScroll),"edit",Muted);
      if(focus==100+i*2) Line(g,row,Muted,8); if(focus==101+i*2) Line(g,layout.ProfileEdit(i,details.ProfileScroll),Muted,8);
     }
     Label(g,online?"Смена профиля отключит VPN":(details.ProfileNames.Count>4?"Прокрутите список профилей":(details.ProfileNames.Count==0?"Добавьте первый профиль":"Выберите профиль или добавьте новый")),new RectangleF(panel.X+20,panel.Bottom-96,panel.Width-40,24),13,Muted);
     Button(g,layout.MenuSubmit(details.Menu,details.ProfileNames.Count),new UiAction("+ Добавить") { Enabled=true },"new",hot,pressed,focus,false);
    } else {
     if(!details.NewProfile) { var trash=new RectangleF(panel.Right-60,panel.Y+12,40,32); Icon(g,trash,"trash",Text); if(focus==18) Line(g,trash,Muted,8); }
     Label(g,"Название",new RectangleF(panel.X+20,panel.Y+52,panel.Width-40,24),13,Muted);
     Label(g,"Ссылка подписки",new RectangleF(panel.X+20,panel.Y+124,panel.Width-40,24),13,Muted);

     Fill(g,layout.NameInput,Palette.Input,7); Line(g,layout.NameInput,Palette.Border,7);
     Fill(g,layout.Input,Palette.Input,7); Line(g,layout.Input,Palette.Border,7);
     Label(g,details.Notice,new RectangleF(panel.X+20,panel.Y+204,panel.Width-40,44),13,Muted,false,false,true);
     Button(g,layout.Submit,new UiAction("Сохранить") { Enabled=true },"save",hot,pressed,focus,true);
    }
    Button(g,layout.MenuClose(details.Menu,details.ProfileNames.Count),new UiAction(details.Menu=="delete"?"Отмена":"Закрыть") { Enabled=true },"dismiss",hot,pressed,focus,false);
   }
   Line(g,new RectangleF(.5f,.5f,layout.Size.Width-1,layout.Size.Height-1),Color.FromArgb(55,150,150,150),14);
  }
 }
}
