using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Luna {
 static class CreateIcon {
  static void Main() {
   WriteIcon("assets/doomra.ico",false);
   WriteIcon("assets/doomra-connected.ico",true);
  }

  static void WriteIcon(string file,bool connected) {
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
       if(connected) {
        g.CompositingMode=CompositingMode.SourceOver;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        int diameter=System.Math.Max(7,size*3/8),border=System.Math.Max(1,size/16);
        int offset=size-diameter-System.Math.Max(1,size/32);
        using(var rim=new SolidBrush(Color.FromArgb(24,40,31)))
        using(var green=new SolidBrush(Color.FromArgb(104,200,135))) {
         g.FillEllipse(rim,offset,offset,diameter,diameter);
         g.FillEllipse(green,offset+border,offset+border,diameter-2*border,diameter-2*border);
        }
       }
      }
      if(connected) frames[i]=WriteDib(frame);
      else using(var png=new MemoryStream()) {
       frame.Save(png,ImageFormat.Png);
       frames[i]=png.ToArray();
      }
     }
    }
   }
   using(var icon=new BinaryWriter(File.Create(file))) {
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

  static byte[] WriteDib(Bitmap frame) {
   using(var data=new MemoryStream())
   using(var writer=new BinaryWriter(data)) {
    int size=frame.Width;
    int maskStride=((size+31)/32)*4;
    var mask=new byte[maskStride*size];
    writer.Write(40);
    writer.Write(size);
    writer.Write(size*2);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(0);
    writer.Write(size*size*4);
    for(int i=0;i<4;i++) writer.Write(0);
    for(int y=size-1;y>=0;y--) {
     for(int x=0;x<size;x++) {
      Color pixel=frame.GetPixel(x,y);
      writer.Write(pixel.B);
      writer.Write(pixel.G);
      writer.Write(pixel.R);
      writer.Write(pixel.A);
      if(pixel.A==0) mask[(size-1-y)*maskStride+x/8]|=(byte)(0x80>>(x%8));
     }
    }
    writer.Write(mask);
    return data.ToArray();
   }
  }
 }
}
