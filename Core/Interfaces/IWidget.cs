using System.Threading.Tasks;
using System.Windows.Controls;

namespace gtt_sidebar.Core.Interfaces
{
    /// <summary>
    /// 侧边栏组件（插件）统一接口。
    ///
    /// 任何实现了本接口、且具备无参构造函数的类，都会被 <c>WidgetManager</c>
    /// 通过反射自动发现，并按用户在设置里的启用状态与排序装载。
    /// 因此新增组件只需实现本接口，无需改动主窗口代码。
    /// </summary>
    public interface IWidget
    {
        /// <summary>
        /// 组件唯一标识（英文，用于持久化启用状态与排序，改名会导致配置丢失）
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 在设置界面展示的中文名称。未覆写时退回 <see cref="Name"/>。
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// 组件用途说明，显示在设置界面的名称下方
        /// </summary>
        string Description { get; }

        UserControl GetControl();
        Task InitializeAsync();
        void Dispose();
    }
}
