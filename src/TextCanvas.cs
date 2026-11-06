using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace Luna {
 internal static class TextCanvas {
  [DllImport("gdi32.dll")] static extern int SaveDC(IntPtr dc);
  [DllImport("gdi32.dll")] static extern bool RestoreDC(IntPtr dc,int saved);
  [DllImport("gdi32.dll")] static extern int SelectClipRgn(IntPtr dc,IntPtr region);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
  [DllImport("gdi32.dll")] static extern int IntersectClipRect(IntPtr dc,int left,int top,int right,int bottom);
  sealed class Context : IDeviceContext {
   readonly IntPtr handle;
   internal Context(IntPtr dc) { handle=dc; }
   public IntPtr GetHdc() { return handle; }
   public void ReleaseHdc() { }
   public void Dispose() { }
  }
  internal static void Draw(Graphics g,Region clip,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags) {
   IntPtr region=clip.GetHrgn(g),dc=IntPtr.Zero; int saved=0;
   try {
    dc=g.GetHdc(); saved=SaveDC(dc);
    if(saved==0 || SelectClipRgn(dc,region)==0) throw new InvalidOperationException("Unable to prepare text context.");
    using(var context=new Context(dc)) TextRenderer.DrawText(context,text,font,bounds,color,flags);
   } finally {
    if(dc!=IntPtr.Zero) { if(saved!=0) RestoreDC(dc,saved); g.ReleaseHdc(dc); }
    DeleteObject(region);
   }
  }
  internal sealed class Batch : IDisposable {
   struct Entry {
    internal string Text;
    internal Font Font;
    internal Rectangle Bounds;
    internal Rectangle? Clip;
    internal Color Color;
    internal TextFormatFlags Flags;
   }
   readonly Graphics graphics;
   readonly float scale,x,y;
   readonly List<Entry> entries=new List<Entry>();
   readonly Dictionary<Tuple<float,bool>,Font> fonts=new Dictionary<Tuple<float,bool>,Font>();
   internal Rectangle? Clip;
   internal Batch(Graphics graphics) {
    this.graphics=graphics;
    using(var transform=graphics.Transform) {
     var m=transform.Elements; scale=m[0]; x=m[4]; y=m[5];
    }
   }
   internal Rectangle Pixels(RectangleF r) {
    return Rectangle.FromLTRB((int)Math.Round(r.Left*scale+x),(int)Math.Round(r.Top*scale+y),(int)Math.Round(r.Right*scale+x),(int)Math.Round(r.Bottom*scale+y));
   }
   internal void Add(string text,RectangleF r,float size,Color color,bool bold,TextFormatFlags flags) {
    var key=Tuple.Create(Math.Max(13,size)*scale,bold); Font font;
    if(!fonts.TryGetValue(key,out font)) fonts.Add(key,font=UiFonts.Get(key.Item1,bold));
    entries.Add(new Entry { Text=text,Font=font,Bounds=Pixels(r),Clip=Clip,Color=color,Flags=flags });
   }
   internal void Flush() {
    if(entries.Count==0) return;
    var state=graphics.Save();
    try {
     graphics.ResetTransform();
     IntPtr dc=graphics.GetHdc();
     try {
      using(var context=new Context(dc)) foreach(var entry in entries) {
       int saved=0;
       try {
        if(entry.Clip.HasValue) {
         var r=entry.Clip.Value; saved=SaveDC(dc);
         if(saved==0 || IntersectClipRect(dc,r.Left,r.Top,r.Right,r.Bottom)==0) continue;
        }
        TextRenderer.DrawText(context,entry.Text,entry.Font,entry.Bounds,entry.Color,entry.Flags);
       } finally { if(saved!=0) RestoreDC(dc,saved); }
      }
     } finally { graphics.ReleaseHdc(dc); }
    } finally { graphics.Restore(state); entries.Clear(); }
   }
   public void Dispose() { foreach(var font in fonts.Values) font.Dispose(); }
  }
 }
}
