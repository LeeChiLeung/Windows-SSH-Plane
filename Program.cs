using System.Runtime.InteropServices;
using WindowsSshPlane.Core;
using WindowsSshPlane.UI;

namespace WindowsSshPlane;

internal static class Program
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            // WinExe 默认没有控制台，自检时尝试挂到调用方控制台，方便命令行查看
            AttachConsole(AttachParentProcess);
            var report = Path.Combine(Path.GetTempPath(), "Windows-SSH-Plane-selftest.txt");
            return SelfTest.Run(report);
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowUnhandled(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowUnhandled(e.ExceptionObject as Exception);

        // --no-autoconnect：启动后不自动连接，先查看/编辑规则，首次点击“连接”时再输入密码
        var autoConnect = !args.Any(a => a.Equals("--no-autoconnect", StringComparison.OrdinalIgnoreCase));

        var config = ConfigStore.Load();

        var password = string.Empty;
        if (autoConnect)
        {
            using var login = new PasswordForm(config);
            if (login.ShowDialog() != DialogResult.OK)
            {
                return 0;
            }

            password = login.Password;

            // 只保存服务器参数与转发规则，密码留在内存中
            ConfigStore.Save(config);
        }

        Application.Run(new MainForm(config, password, autoConnect));
        return 0;
    }

    private static void ShowUnhandled(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        MessageBox.Show(
            "程序遇到未处理的错误：" + Environment.NewLine + Environment.NewLine + ex.Message,
            "Windows SSH Plane",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}