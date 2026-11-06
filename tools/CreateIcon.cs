using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Luna {
 static class CreateIcon {
  static void Main() {
   int[] sizes={16,20,24,32,40,48,64,128,256};
   var frames=new byte[sizes.Length][];
   var crop=new RectangleF(30,30,1194,1194);
   using(var logo=Image.FromFile("assets/doomra-logo.png")) {
    for(int i=0;i<sizes.Length;i++) {
     int size=sizes[i];
     using(var frame=new Bitmap(size,size,PixelFormat.Format32bppArgb)) {
      using(var g=Graphics.FromImage(frame))
      using(var finish=new ImageAttributes()) {
       g.CompositingMode=CompositingMode.SourceCopy;
       g.InterpolationMode=InterpolationMode.HighQualityBicubic;
       g.PixelOffsetMode=PixelOffsetMode.HighQuality;
       float brightness=size<=48?1.22f:1f;
       finish.SetColorMatrix(new ColorMatrix { Matrix00=brightness,Matrix11=brightness,Matrix22=brightness });
       int margin=size<=48?0:1;
       var target=new Rectangle(margin,margin,size-2*margin,size-2*margin);
       g.DrawImage(logo,target,crop.X,crop.Y,crop.Width,crop.Height,GraphicsUnit.Pixel,finish);
      }
      using(var png=new MemoryStream()) {
       frame.Save(png,ImageFormat.Png);
       frames[i]=png.ToArray();
      }
     }
    }
   }
   using(var icon=new BinaryWriter(File.Create("assets/doomra.ico"))) {
    icon.Write((ushort)0);
    icon.Write((ushort)1);
    icon.Write((ushort)sizes.Length);
    int offset=6+16*sizes.Length;
    for(int i=0;i<sizes.Length;i++) {
     byte dimension=(byte)(sizes[i]==256?0:sizes[i]);
     icon.Write(dimension);
     icon.Write(dimension);
     icon.Write((ushort)0);
     icon.Write((ushort)1);
     icon.Write((ushort)32);
     icon.Write(frames[i].Length);
     icon.Write(offset);
     offset+=frames[i].Length;
    }
    foreach(var frame in frames) icon.Write(frame);
   }
  }
 }
}
