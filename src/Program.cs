using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QuotaPeek.Windows
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            using (var instance = new SingleInstance())
            {
                if (!instance.IsPrimary)
                {
                    // Already running: bring up its quota flyout instead of starting a second copy.
                    if (!instance.SignalPrimary())
                    {
                        MessageBox.Show("QuotaPeek 已在运行，请点击任务栏通知区域中的额度图标。", "QuotaPeek",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                try
                {
                    SetProcessDPIAware();
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var controller = new AppController(args))
                    {
                        instance.ListenForShowRequests(controller.RequestShowDashboard);
                        Application.Run(controller);
                    }
                }
                catch
                {
                    // Only fixed text reaches the UI; raw exception messages may carry system or server details.
                    MessageBox.Show("QuotaPeek 无法启动。请确认程序所在文件夹可写，然后重新打开程序。", "QuotaPeek",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
