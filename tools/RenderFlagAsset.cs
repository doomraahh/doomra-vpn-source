using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
internal static class RenderFlagAsset {
 static readonly CultureInfo Culture=CultureInfo.InvariantCulture;
 static readonly Dictionary<string,XmlElement> Definitions=new Dictionary<string,XmlElement>();
 static float Number(string value) { return float.Parse(value,Culture); }
 static float Attribute(XmlElement node,string name,float fallback=0) { return node.HasAttribute(name)?Number(node.GetAttribute(name)):fallback; }
 static bool Command(string value) { return value.Length==1 && char.IsLetter(value[0]); }
 static GraphicsPath Path(string data) {
  var tokens=Regex.Matches(data,@"[A-Za-z]|[-+]?(?:\d*\.?\d+)(?:[eE][-+]?\d+)?"); var path=new GraphicsPath(FillMode.Winding);
  int i=0; float x=0,y=0; char command=' ';
  while(i<tokens.Count) {
   if(Command(tokens[i].Value)) command=tokens[i++].Value[0];
   if(command=='Z') { path.CloseFigure(); command=' '; continue; }
   if(command=='M') { x=Number(tokens[i++].Value); y=Number(tokens[i++].Value); path.StartFigure(); command='L'; continue; }
   float nx=x,ny=y;
   if(command=='H') nx=Number(tokens[i++].Value);
   else if(command=='V') ny=Number(tokens[i++].Value);
   else if(command=='L') { nx=Number(tokens[i++].Value); ny=Number(tokens[i++].Value); }
   else { path.Dispose(); throw new InvalidOperationException("Unsupported SVG path command."); }
   path.AddLine(x,y,nx,ny); x=nx;y=ny;
  }
  return path;
 }
 static void Draw(Graphics g,XmlElement node,string inherited,int depth) {
  if(depth>32) throw new InvalidOperationException("SVG nesting too deep.");
  string fill=node.HasAttribute("fill")?node.GetAttribute("fill"):inherited;
  if(node.LocalName=="title" || node.LocalName=="defs") return;
  if(node.LocalName=="svg" || node.LocalName=="g") { foreach(XmlNode child in node.ChildNodes) if(child is XmlElement) Draw(g,(XmlElement)child,fill,depth+1); return; }
  if(node.LocalName=="use") {
   string reference=node.GetAttribute("href"); XmlElement target;
   if(!reference.StartsWith("#") || !Definitions.TryGetValue(reference.Substring(1),out target)) throw new InvalidOperationException("Only local SVG references are supported.");
   var state=g.Save(); g.TranslateTransform(Attribute(node,"x"),Attribute(node,"y"));
   try { Draw(g,target,fill,depth+1); } finally { g.Restore(state); } return;
  }
  using(var brush=new SolidBrush(ColorTranslator.FromHtml(fill))) {
   if(node.LocalName=="rect") g.FillRectangle(brush,Attribute(node,"x"),Attribute(node,"y"),Attribute(node,"width"),Attribute(node,"height"));
   else if(node.LocalName=="polygon") {
    var values=Regex.Split(node.GetAttribute("points").Trim(),@"[\s,]+"); var points=new PointF[values.Length/2];
    for(int i=0;i<points.Length;i++) points[i]=new PointF(Number(values[i*2]),Number(values[i*2+1])); g.FillPolygon(brush,points,FillMode.Winding);
   } else if(node.LocalName=="path") using(var path=Path(node.GetAttribute("d"))) {
    if(node.GetAttribute("fill-rule")=="evenodd") path.FillMode=FillMode.Alternate;
    g.FillPath(brush,path);
   }
   else throw new InvalidOperationException("Unsupported SVG element: "+node.LocalName);
  }
 }
 static int Main(string[] args) {
  if(args.Length!=2) return 1;
  var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2000000 };
  var document=new XmlDocument { XmlResolver=null }; using(var reader=XmlReader.Create(args[0],settings)) document.Load(reader);
  foreach(XmlElement element in document.SelectNodes("//*[@id]")) Definitions[element.GetAttribute("id")]=element;
  int width=(int)Attribute(document.DocumentElement,"width"),height=(int)Attribute(document.DocumentElement,"height");
  if(width<1 || height<1 || width>2048 || height>2048) throw new InvalidOperationException("Unsupported SVG dimensions.");
  using(var large=new Bitmap(width*2,height*2,PixelFormat.Format32bppArgb)) {
   using(var g=Graphics.FromImage(large)) { g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(2,2); Draw(g,document.DocumentElement,"#000000",0); }
   using(var result=new Bitmap(width,height,PixelFormat.Format32bppArgb)) {
    using(var g=Graphics.FromImage(result)) { g.CompositingMode=CompositingMode.SourceCopy; g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.DrawImage(large,new Rectangle(0,0,width,height)); }
    result.Save(args[1],ImageFormat.Png);
   }
  }
  return 0;
 }
}
