using System;
using System.IO;

namespace gtt_sidebar.Core
{
    /// <summary>
    /// 统一的路径常量。
    /// 数据目录名集中在此处定义，避免散落在各 Storage 类中导致改名遗漏。
    /// </summary>
    public static class AppPaths
    {
        /// <summary>
        /// 应用数据目录名（位于 %APPDATA% 下）
        /// </summary>
        public const string DataFolderName = "DesktopSidebar";

        /// <summary>
        /// 应用数据根目录：%APPDATA%/DesktopSidebar
        /// </summary>
        public static string AppDataPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DataFolderName);

        /// <summary>
        /// 自定义图标目录：%APPDATA%/DesktopSidebar/icons
        /// </summary>
        public static string IconsPath => Path.Combine(AppDataPath, "icons");

        /// <summary>
        /// 内置图标目录：%APPDATA%/DesktopSidebar/DefaultIcons
        /// </summary>
        public static string DefaultIconsPath => Path.Combine(AppDataPath, "DefaultIcons");
    }
}
