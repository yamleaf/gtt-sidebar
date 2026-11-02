using System;
using System.IO;
using gtt_sidebar.Core;

namespace gtt_sidebar.Core.Application
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application  // Fully qualify Application
    {
        public App()
        {
            // 尽早在构造函数里挂异常兜底，避免 StartupUri 创建窗口时抛异常却无从记录
            DispatcherUnhandledException += (s, args) => LogCrash("DispatcherUnhandledException", args.Exception);

            AppDomain.CurrentDomain.UnhandledException +=
                (s, args) => LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        }

        /// <summary>
        /// 崩溃日志：%APPDATA%/DesktopSidebar/crash.log
        /// </summary>
        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.AppDataPath);
                var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}"
                           + $"{ex}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(AppPaths.AppDataPath, "crash.log"), text);
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
