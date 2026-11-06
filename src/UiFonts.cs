using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
namespace Luna {
 internal static class UiFonts {
  static readonly PrivateFontCollection collection=new PrivateFontCollection();
  static readonly List<IntPtr> memory=new List<IntPtr>();
  static readonly FontFamily family;
  [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr file,uint size,IntPtr reserved,ref uint count);
  static UiFonts() {
   foreach(string name in new[]{"Regular","Bold"}) {
    using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Doomra.Font"+name)) {
     if(stream==null) throw new InvalidOperationException("Missing bundled JetBrains Mono.");
     byte[] bytes=new byte[stream.Length]; int read=0,n;
     while(read<bytes.Length && (n=stream.Read(bytes,read,bytes.Length-read))>0) read+=n;
     if(read!=bytes.Length) throw new InvalidDataException("Incomplete bundled font.");
     IntPtr pointer=Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes,0,pointer,bytes.Length); memory.Add(pointer);
     collection.AddMemoryFont(pointer,bytes.Length); uint count=0;
     if(AddFontMemResourceEx(pointer,(uint)bytes.Length,IntPtr.Zero,ref count)==IntPtr.Zero) throw new InvalidOperationException("Unable to load bundled font.");
    }
   }
   family=collection.Families[0];
  }
  internal static Font Get(float pixels,bool bold=false) { return new Font(family,pixels,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel); }
  internal static Font Native(float points) { return new Font(family,points,FontStyle.Regular,GraphicsUnit.Point); }
 }
}
