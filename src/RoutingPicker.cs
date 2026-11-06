using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Luna {
 internal sealed class RoutingPicker : Form {
  readonly TextBox search=new TextBox { Dock=DockStyle.Top,BorderStyle=BorderStyle.None,AccessibleName="Поиск приложений" };
  readonly ListView list;
  readonly Label status=new Label { Dock=DockStyle.Bottom,Height=34,TextAlign=ContentAlignment.MiddleLeft,Text="Читаем ярлыки меню «Пуск»…" };
  readonly PickerButton add=new PickerButton("Добавить",106,true) { Enabled=false };
  readonly Font menuFont=UiFonts.Native(10);
  readonly Font headingFont=UiFonts.Get(21,true);
  readonly HashSet<string> existing;
  Entry[] entries=new Entry[0];
  internal string[] SelectedPaths { get; private set; }
  internal string SelectedDomain { get; private set; }

  internal RoutingPicker(IEnumerable<string> existingPaths,bool sites=false) {
   existing=new HashSet<string>(existingPaths,StringComparer.OrdinalIgnoreCase);
   Font=menuFont; Text=sites?"Добавить сайт":"Добавить приложение"; StartPosition=FormStartPosition.CenterParent;
   BackColor=Palette.Background; ForeColor=Palette.Text;
   AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
   FormBorderStyle=FormBorderStyle.None;
   ClientSize=sites?new Size(560,248):new Size(720,460);
   MinimumSize=sites?new Size(480,248):new Size(560,365);
   MinimizeBox=MaximizeBox=false; ShowInTaskbar=ShowIcon=false; Padding=new Padding(22,16,22,18);

   var header=new Panel { Dock=DockStyle.Top,Height=72 };
   var globe=new Panel { Dock=DockStyle.Left,Width=54,AccessibleRole=AccessibleRole.Graphic };
   globe.Paint+=delegate(object sender,PaintEventArgs e) {
    e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
    float size=globe.Width-14;
    Dashboard.Symbol(e.Graphics,new RectangleF(0,5,size,size),ForeColor);
   };
   var heading=new Label { Dock=DockStyle.Top,Height=30,Text=Text,Font=headingFont };
   var description=new Label { Dock=DockStyle.Fill,Text=sites?"Раздельное туннелирование  ·  Сайты":"Раздельное туннелирование  ·  Меню «Пуск»",ForeColor=Palette.Muted };
   var closeArea=new Panel { Dock=DockStyle.Right,Width=38 };
   var close=new PickerButton("",36,false,captionClose:true) { AccessibleName="Закрыть",DialogResult=DialogResult.Cancel };
   closeArea.Controls.Add(close);
   header.Controls.AddRange(new Control[]{description,heading,globe,closeArea});
   foreach(Control part in new Control[]{header,heading,description,globe}) part.MouseDown+=DragHeader;

   var filter=new Panel { Dock=DockStyle.Top,Height=55 };
   var field=new Panel { Dock=DockStyle.Top,Height=40,Padding=new Padding(38,10,12,0) };
   search.BackColor=Palette.Surface; search.ForeColor=Palette.Text;
   if(sites) search.AccessibleName="Домен или ссылка на сайт";
   field.Paint+=delegate(object sender,PaintEventArgs e) {
    var g=e.Graphics;
    g.SmoothingMode=SmoothingMode.AntiAlias;
    var bounds=new RectangleF(.5f,.5f,field.Width-1,field.Height-1);
    Dashboard.Fill(g,bounds,search.BackColor,10);
    Dashboard.Line(g,bounds,search.Focused?Palette.Muted:Palette.Border,10);
    float center=field.Height/2f;
    using(var pen=new Pen(Palette.Muted,1.4f)) {
     if(sites) {
      g.DrawEllipse(pen,13,center-7,14,14);
      g.DrawEllipse(pen,17,center-7,6,14);
      g.DrawLine(pen,13,center,27,center);
     } else {
      g.DrawEllipse(pen,13,center-7,10,10);
      g.DrawLine(pen,21,center+1,26,center+6);
     }
    }
   };
   field.MouseDown+=delegate { search.Focus(); };
   search.GotFocus+=delegate { field.Invalidate(); };
   search.LostFocus+=delegate { field.Invalidate(); };
   search.HandleCreated+=delegate { SendMessage(search.Handle,0x1501,(IntPtr)1,sites?"Домен или ссылка, например example.com":"Поиск по названию или пути"); };
   field.Controls.Add(search); filter.Controls.Add(field);

   var buttons=new Panel { Dock=DockStyle.Bottom,Height=38 };
   var actions=new FlowLayoutPanel { Dock=DockStyle.Right,Width=230,FlowDirection=FlowDirection.RightToLeft,WrapContents=false };
   var cancel=new PickerButton("Отмена",100,false) { DialogResult=DialogResult.Cancel };
   actions.Controls.AddRange(new Control[]{add,cancel});
   buttons.Controls.Add(actions);
   status.ForeColor=Palette.Muted;
   AcceptButton=add; CancelButton=cancel;
   if(sites) {
    Controls.AddRange(new Control[]{status,buttons,filter,header});
    ConfigureSiteInput();
    return;
   }

   var browse=new PickerButton("Выбрать файл…",162,false) { Dock=DockStyle.Left };
   buttons.Controls.Add(browse);
   list=new ListView { Dock=DockStyle.Fill,View=View.Details,HeaderStyle=ColumnHeaderStyle.None,
    FullRowSelect=true,HideSelection=false,ShowItemToolTips=true,OwnerDraw=true,
    BorderStyle=BorderStyle.None,AccessibleName="Приложения из меню Пуск" };
   StyleList();
   var columns=new Panel { Dock=DockStyle.Top,Height=29,BackColor=Palette.Surface,ForeColor=Palette.Muted };
   columns.Controls.AddRange(new Control[]{
    new Label { Dock=DockStyle.Fill,Text="Путь к файлу",TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(9,0,0,0) },
    new Label { Dock=DockStyle.Left,Width=list.Columns[0].Width,Text="Приложение",TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(9,0,0,0) }
   });
   Controls.AddRange(new Control[]{list,status,buttons,columns,filter,header});
   search.TextChanged+=delegate { RefreshList(); };
   list.SelectedIndexChanged+=delegate { UpdateSelection(); };
   list.DoubleClick+=delegate { AddSelection(); };
   add.Click+=delegate { AddSelection(); };
   browse.Click+=delegate {
    using(var picker=new OpenFileDialog { Title="Выберите приложение",Filter="Приложения (*.exe)|*.exe",CheckFileExists=true,Multiselect=true }) {
     if(picker.ShowDialog(this)!=DialogResult.OK) return;
     SelectedPaths=picker.FileNames; DialogResult=DialogResult.OK;
    }
   };
   Shown+=async delegate {
    search.Focus();
    var found=await Task.Run(()=>ReadEntries(
     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)));
    if(IsDisposed || Disposing) return;
    entries=found.Where(entry=>!existing.Contains(entry.Path)).ToArray();
    RefreshList();
   };
  }

  void ConfigureSiteInput() {
   const string hint="Правило применяется к домену и его поддоменам.";
   status.Text=hint;
   search.TextChanged+=delegate {
    add.Enabled=!string.IsNullOrWhiteSpace(search.Text);
    status.Text=hint;
    status.ForeColor=Palette.Muted;
   };
   add.Click+=delegate {
    try {
     string domain=NetworkPolicy.NormalizeDomain(search.Text);
     if(existing.Contains(domain)) throw new SafeException("Этот сайт уже в списке.");
     SelectedDomain=domain;
     DialogResult=DialogResult.OK;
    } catch(SafeException e) {
     status.ForeColor=Palette.Light?Color.FromArgb(153,34,46):Color.FromArgb(241,132,142);
     status.Text=e.Message;
     search.Focus();
    }
   };
   Shown+=delegate { search.Focus(); };
  }

  void DragHeader(object sender,MouseEventArgs e) {
   if(e.Button!=MouseButtons.Left) return;
   ReleaseCapture();
   SendMessage(Handle,0xA1,(IntPtr)2,null);
  }

  protected override void OnSizeChanged(EventArgs e) {
   base.OnSizeChanged(e);
   if(ClientSize.Width<28 || ClientSize.Height<28) return;
   var previous=Region;
   using(var path=Dashboard.Round(ClientRectangle,14)) Region=new Region(path);
   if(previous!=null) previous.Dispose();
  }

  protected override void OnPaint(PaintEventArgs e) {
   base.OnPaint(e);
   e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   Dashboard.Line(e.Graphics,new RectangleF(.5f,.5f,ClientSize.Width-1,ClientSize.Height-1),Palette.Border,14);
  }

  protected override void WndProc(ref Message m) {
   base.WndProc(ref m);
   if(m.Msg!=0x84 || m.Result!=(IntPtr)1) return;
   long raw=m.LParam.ToInt64();
   var point=PointToClient(new Point((short)raw,(short)(raw>>16)));
   bool left=point.X<6,right=point.X>=ClientSize.Width-6;
   if(point.Y<6) m.Result=(IntPtr)(left?13:right?14:12);
   else if(point.Y>=ClientSize.Height-6) m.Result=(IntPtr)(left?16:right?17:15);
   else if(left || right) m.Result=(IntPtr)(left?10:11);
  }

  [DllImport("user32.dll")] static extern bool ReleaseCapture();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]
  static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,string text);

  sealed class PickerButton : Button {
   readonly bool primary,captionClose;
   bool hover;
   internal PickerButton(string text,int width,bool primary,bool captionClose=false) {
    this.primary=primary;
    this.captionClose=captionClose;
    Text=text; Size=new Size(width,36); Margin=new Padding(5,0,0,0);
    FlatStyle=FlatStyle.Flat;
    MouseEnter+=delegate { hover=true; Invalidate(); };
    MouseLeave+=delegate { hover=false; Invalidate(); };
   }
   protected override void OnPaint(PaintEventArgs e) {
    e.Graphics.Clear(Parent.BackColor);
    e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
    if(captionClose) {
     Dashboard.DrawCloseButton(e.Graphics,ClientRectangle,hover || Focused);
     return;
    }
    var bounds=new RectangleF(.5f,.5f,Width-1,Height-1);
    var background=primary && Enabled?Palette.Accent:(hover && Enabled?Palette.Hover:Palette.SurfaceBottom);
    Dashboard.Fill(e.Graphics,bounds,background,10);
    Dashboard.Line(e.Graphics,bounds,Focused?Palette.Muted:Palette.Border,10);
    var text=Enabled?(primary?Palette.OnAccent:Palette.Text):Palette.Muted;
    TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,text,
     TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
   }
  }

  void StyleList() {
   Color surface=Palette.SurfaceBottom,muted=Palette.Muted,selected=Palette.Selection;
   list.BackColor=surface; list.ForeColor=Palette.Text;
   list.Columns.Add("Приложение",218); list.Columns.Add("Путь к файлу",415);
   list.ClientSizeChanged+=delegate {
    list.Columns[1].Width=Math.Max(160,list.ClientSize.Width-list.Columns[0].Width-SystemInformation.VerticalScrollBarWidth);
   };
   var flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis;
   list.DrawItem+=delegate(object sender,DrawListViewItemEventArgs e) {
    using(var brush=new SolidBrush(e.Item.Selected?selected:surface)) e.Graphics.FillRectangle(brush,e.Bounds);
   };
   list.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs e) {
    var bounds=Rectangle.Inflate(e.Bounds,-9,0);
    if(bounds.Width>0)
     TextRenderer.DrawText(e.Graphics,e.SubItem.Text,menuFont,bounds,e.ColumnIndex==0?list.ForeColor:muted,flags);
    if(list.Focused && e.Item.Focused && e.ColumnIndex==0)
     ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(e.Item.Bounds,-1,-1),list.ForeColor,selected);
   };
  }

  void UpdateSelection() {
   int count=list.SelectedItems.Count;
   add.Enabled=count>0;
   status.Text=count>0?"Выбрано: "+count+" из "+list.Items.Count:
    (list.Items.Count==0?"Подходящих приложений нет. Можно выбрать .exe вручную.":"Найдено приложений: "+list.Items.Count);
  }

  void RefreshList() {
   string query=search.Text.Trim();
   list.BeginUpdate();
   try {
    list.Items.Clear();
    foreach(var entry in entries) {
     if(entry.Name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)<0 && entry.Path.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0) continue;
     list.Items.Add(new ListViewItem(new[]{entry.Name,entry.Path}) { Tag=entry.Path,ToolTipText=entry.Path });
    }
   } finally { list.EndUpdate(); }
   UpdateSelection();
  }

  void AddSelection() {
   if(list.SelectedItems.Count==0) return;
   SelectedPaths=list.SelectedItems.Cast<ListViewItem>().Select(item=>(string)item.Tag).ToArray();
   DialogResult=DialogResult.OK;
  }

  protected override void Dispose(bool disposing) {
   base.Dispose(disposing);
   if(disposing) {
    menuFont.Dispose(); headingFont.Dispose();
   }
  }

  internal sealed class Entry {
   internal string Name,Path;
  }

  internal static Entry[] ReadEntries(params string[] roots) {
   var entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
   var shellType=Type.GetTypeFromProgID("WScript.Shell");
   if(shellType==null) return new Entry[0];
   object shell;
   try { shell=Activator.CreateInstance(shellType); }
   catch(COMException) { return new Entry[0]; }
   try {
    foreach(string root in roots.Where(root=>!string.IsNullOrEmpty(root))) {
     foreach(string file in Shortcuts(root)) {
      object shortcut=null;
      try {
       shortcut=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});
       string target=(string)shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);
       string path=NetworkPolicy.NormalizePath(Environment.ExpandEnvironmentVariables(target??""));
       if(!entries.ContainsKey(path) && File.Exists(path))
        entries.Add(path,new Entry { Name=Path.GetFileNameWithoutExtension(file),Path=path });
      } catch(TargetInvocationException) { }
      catch(COMException) { }
      catch(SafeException) { }
      finally { if(shortcut!=null) Marshal.ReleaseComObject(shortcut); }
     }
    }
   } finally { Marshal.ReleaseComObject(shell); }
   return entries.Values.OrderBy(entry=>entry.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(entry=>entry.Path,StringComparer.OrdinalIgnoreCase).ToArray();
  }

  static IEnumerable<string> Shortcuts(string root) {
   var folders=new Stack<string>();
   folders.Push(root);
   while(folders.Count>0) {
    var folder=folders.Pop();
    FileInfo[] files;
    try {
     var directory=new DirectoryInfo(folder);
     files=directory.GetFiles("*.lnk");
     foreach(var child in directory.GetDirectories())
      if((child.Attributes&FileAttributes.ReparsePoint)==0) folders.Push(child.FullName);
    } catch(IOException) { continue; }
    catch(UnauthorizedAccessException) { continue; }
    foreach(var file in files)
     if(string.Equals(file.Extension,".lnk",StringComparison.OrdinalIgnoreCase)) yield return file.FullName;
   }
  }
 }
}
