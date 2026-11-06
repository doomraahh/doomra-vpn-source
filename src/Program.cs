using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
[assembly: System.Reflection.AssemblyFileVersion("0.50.0.0")]
namespace Luna {
 static class Program {
  [STAThread] static int Main(string[] args) {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   if(args.Length==1 && args[0]=="--service") {
    System.ServiceProcess.ServiceBase.Run(new VpnService()); return 0;
   }
   if(args.Length==1 && args[0]=="--register-service-owner") return VpnService.RegisterOwner();
   if(args.Length==1 && args[0]=="--start-service") return VpnService.SetRunning(true);
   if(args.Length==1 && args[0]=="--stop-service") return VpnService.SetRunning(false);
   if(args.Length==1 && args[0]=="--remove-service-settings") return VpnService.RemoveOwnerSettings();
   if(args.Length==3 && args[0]=="--service-worker") {
    using(var identity=System.Security.Principal.WindowsIdentity.GetCurrent()) {
     int pid;
     return identity.IsSystem && VpnService.IsInstalled && int.TryParse(args[2],out pid)?Worker.Run(args[1],pid,VpnService.ReadOwner().Value):1;
    }
   }
   if(args.Length==1 && args[0]=="--recover-network") return Worker.RecoverNetwork();
   if(args.Length==3 && args[0]=="--worker") { int pid; return int.TryParse(args[2],out pid)?Worker.Run(args[1],pid):1; }
   bool created;
   using(var single=new Mutex(true,@"Local\LunaVPN.UI."+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value,out created)) {
    if(!created) { if(!args.Contains("--autostart")) MainForm.RequestShow(); return 0; }
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    Application.ThreadException+=delegate { MessageBox.Show("Внутренняя ошибка интерфейса. Отключите VPN и перезапустите приложение.","VPN"); };
    Application.Run(new MainForm(args.Contains("--autostart"))); return 0;
   }
  }
 }
}
