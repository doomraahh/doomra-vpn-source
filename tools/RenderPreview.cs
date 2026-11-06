using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
namespace Luna {
 static class RenderPreview {
  [STAThread] static int Main(string[] args) {
   if(args.Length<1) return 1;
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   var settings=new Settings { Subscription="https://example.invalid/subscription",SubscriptionInfo=SubscriptionInfo.Parse("upload=1200000000; download=89190000000; total=0; expire=1795564800") };
   var names=new[]{"EU АвтоВыбор","NL Нидерланды каскад #1","germany #3 DE","usa #3 US","🇨🇦 Toronto","UK Лондон","Финляндия","Япония","Сингапур","Швейцария"};
   for(int i=0;i<names.Length;i++) settings.Servers.Add(new Server { Name=names[i],Host="example.invalid",Protocol=i%2==0?"vless":"trojan",Uuid="11111111-1111-4111-8111-111111111111",Password="synthetic",Security="tls",Port=443 });
   settings.Selected=Array.IndexOf(args,"--us")>=0?3:1; ProfileStore.Initialize(settings);
   string original=settings.ActiveProfile; ProfileStore.Rename(settings,original,"Личный");
   ProfileStore.Upsert(settings,null,"Работа","https://example.invalid/work",settings.SubscriptionInfo,new System.Collections.Generic.List<Server>{settings.Servers[2]});
   ProfileStore.Switch(settings,original); settings.LightTheme=Array.IndexOf(args,"--light")>=0;
   if(Array.IndexOf(args,"--network")>=0) { settings.Network.Mode="include"; settings.Network.KillSwitch=true; settings.Network.Applications.Add(@"C:\Program Files\Mozilla Firefox\firefox.exe"); settings.Network.Applications.Add(@"C:\Users\Demo\Telegram Desktop\Telegram.exe"); }
   using(var form=new MainForm(settings)) {
    if(Array.IndexOf(args,"--small")>=0) form.ClientSize=new Size(900,640);
    if(Array.IndexOf(args,"--dpi150")>=0) { form.GetType().GetField("scale",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(form,1.5f); form.ClientSize=new Size(1560,1080); }
    Create(form); form.PerformLayout();
    if(Array.IndexOf(args,"--scroll")>=0) form.ScrollPreview(234);
    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    if(Array.IndexOf(args,"--profiles")>=0) form.GetType().GetMethod("ShowProfiles",flags).Invoke(form,null);
    if(Array.IndexOf(args,"--settings")>=0) form.GetType().GetMethod("OpenMenu",flags).Invoke(form,new object[]{"settings"});
    if(Array.IndexOf(args,"--delete")>=0) { form.GetType().GetMethod("ShowSubscription",flags).Invoke(form,null); form.GetType().GetMethod("ConfirmDelete",flags).Invoke(form,null); }
    if(Array.IndexOf(args,"--dropdown")>=0) form.GetType().GetMethod("ShowSubscription",flags).Invoke(form,null);
    if(Array.IndexOf(args,"--online")>=0 || Array.IndexOf(args,"--motion")>=0) {
     var details=(DashboardDetails)form.GetType().GetField("details",flags).GetValue(form);
     details.Elapsed="01:24:36"; details.Download="1,28 ГБ"; details.Upload="84,2 МБ";
     if(Array.IndexOf(args,"--motion")>=0) { details.Moving=true; details.Angle=137; }
     form.GetType().GetField("headerOnline",flags).SetValue(form,true);
     form.GetType().GetField("state",flags).SetValue(form,"CONNECTED");
     ((UiAction)form.GetType().GetField("connect",flags).GetValue(form)).Text="Отключить";
    }
    Create(form);
    using(var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.ClientSize)); bitmap.Save(args[0],ImageFormat.Png); }
   }
   return 0;
  }
  static void Create(Control control) { var handle=control.Handle; control.PerformLayout(); foreach(Control child in control.Controls) Create(child); }
 }
}
