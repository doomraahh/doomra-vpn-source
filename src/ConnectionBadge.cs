using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Luna {
 internal static class ConnectionBadge {
  internal static Icon Create(Icon globe=null,int size=16) {
   using(var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb)) {
    using(var g=Graphics.FromImage(bitmap))
    using(var rim=new SolidBrush(Color.FromArgb(24,40,31)))
    using(var green=new SolidBrush(Color.FromArgb(104,200,135))) {
     g.SmoothingMode=SmoothingMode.AntiAlias;
     if(globe!=null) {
      using(var frame=new Icon(globe,size,size))
      using(var pixels=frame.ToBitmap())
       g.DrawImageUnscaled(pixels,0,0);
     }
     int diameter=globe==null?12:Math.Max(7,size*3/8);
     int offset=size-diameter-1;
     int border=globe==null?2:Math.Max(1,size/16);
     g.FillEllipse(rim,offset,offset,diameter,diameter);
     g.FillEllipse(green,offset+border,offset+border,diameter-2*border,diameter-2*border);
    }
    IntPtr handle=bitmap.GetHicon();
    try { using(var borrowed=Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
    finally { DestroyIcon(handle); }
   }
  }

  internal static void SetTaskbarOverlay(IntPtr window,Icon badge) {
   ITaskbarList3 taskbar=null;
   try {
    taskbar=(ITaskbarList3)new TaskbarList();
    taskbar.HrInit();
    taskbar.SetOverlayIcon(window,badge==null?IntPtr.Zero:badge.Handle,badge==null?null:"VPN подключён");
   } catch(COMException) { }
   finally { if(taskbar!=null) Marshal.ReleaseComObject(taskbar); }
  }

  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);

  [ComImport,Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
  class TaskbarList { }

  [ComImport,Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface ITaskbarList3 {
   void HrInit();
   void AddTab(IntPtr window);
   void DeleteTab(IntPtr window);
   void ActivateTab(IntPtr window);
   void SetActiveAlt(IntPtr window);
   void MarkFullscreenWindow(IntPtr window,[MarshalAs(UnmanagedType.Bool)] bool fullscreen);
   void SetProgressValue(IntPtr window,ulong completed,ulong total);
   void SetProgressState(IntPtr window,uint flags);
   void RegisterTab(IntPtr tab,IntPtr window);
   void UnregisterTab(IntPtr tab);
   void SetTabOrder(IntPtr tab,IntPtr before);
   void SetTabActive(IntPtr tab,IntPtr window,uint reserved);
   void ThumbBarAddButtons(IntPtr window,uint count,IntPtr buttons);
   void ThumbBarUpdateButtons(IntPtr window,uint count,IntPtr buttons);
   void ThumbBarSetImageList(IntPtr window,IntPtr images);
   void SetOverlayIcon(IntPtr window,IntPtr icon,[MarshalAs(UnmanagedType.LPWStr)] string description);
  }
 }
}
