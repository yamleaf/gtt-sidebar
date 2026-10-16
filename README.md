# gtt-sidebar

> 一个 C# / WPF 编写的 Windows 桌面侧边栏工具，用于填充宽屏上的空白区域。
> 本项目基于 [bitlamas/gtt-sidebar](https://github.com/bitlamas/gtt-sidebar) 改造，
> 在原有基础上做了**中文界面汉化**、**失去焦点贴边隐藏**、**组件增删**与**数据源替换**。

[English README](README.en.md) | [详细优化说明](docs/优化说明.md)

---

## 功能一览

| 组件 | 说明 | 刷新频率 |
|---|---|---|
| 🕐 **时钟** | 时间与中文日期（含星期） | 1 秒 |
| 📅 **日历** | 当月月历 + 中国法定节假日标记 + 下一假期倒计时 | 界面 30 分钟 / 数据 24 小时 |
| 🌤️ **天气** | 实时天气 + 3 天预报（Open-Meteo，**无需 API Key**） | 30 分钟 |
| 📝 **备注** | 多标签记事本，自动保存 | 手动 |
| 🚀 **快捷方式** | 自定义启动器，支持内置图标与自定义图片 | 手动 |
| 💻 **系统信息** | CPU / 内存 / 延迟（含告警阈值与均值）+ 运行时长、磁盘可用、网络速率、进程数 | 2 秒（可配置） |

### 特色功能

- **失去焦点贴边隐藏** —— 侧边栏失去焦点后自动滑出屏幕，仅留 3px 边缘；
  鼠标触碰屏幕边缘即自动滑回。延迟时间与触发区范围均可调，
  感应区默认避开屏幕顶部（不遮挡窗口关闭按钮）与底部（不遮挡任务栏），
  设置窗口与弹窗打开期间自动抑制。
- **中文界面** —— 全部窗口、菜单、弹窗、提示消息均已汉化，
  日期、货币、天气描述等数据格式同步本土化。
- **零密钥依赖** —— 天气改用 Open-Meteo（免费、免注册），
  节假日数据来自公开的 holiday-cn，
  **本项目不需要配置任何 API Key 即可运行**。
- **离线可用** —— 节假日数据本地缓存，网络不可用时自动降级，
  不会影响侧边栏正常显示。

---

## 快速开始

### 从源码编译

本仓库仅包含源代码（不含编译产物）。需要 **.NET Framework 4.7.2** 与 Visual Studio：

```bash
git clone <本仓库地址>
cd gtt-sidebar-master
# 用 Visual Studio 打开 gtt-sidebar.sln
# 生成 → 配置 Release（或 Ctrl+Shift+B）
```

编译完成后，在 `bin/Release/` 下取得可执行文件。

### 运行所需文件

编译产物中，运行只需以下两个文件放在同一目录：

```
DesktopSidebar.exe
Newtonsoft.Json.dll
```

> 同目录下的 `DesktopSidebar.exe.config` 建议保留（仅 183 字节），
> 用于声明所需的 .NET 运行时版本。
> `DesktopSidebar.pdb` 为调试符号，日常使用不需要。

若作者有在 [Releases](../../releases) 页面提供已编译版本，可直接下载运行、无需自行编译。

---

## 界面

侧边栏默认贴屏幕右侧，宽度 122px，置顶显示，可最小化到系统托盘。

拖动窗口可调整位置；点击右下角 ⚙ 打开设置；
托盘图标双击可快速显示 / 隐藏。

### 设置项

- **窗口与布局**：停靠左/右、宽度、上下边距、侧边距、失去焦点自动隐藏（含延迟与触发区范围）
- **系统监控设置**：CPU / 内存 / 延迟告警阈值、刷新频率（作用于系统信息卡片）
- **快捷方式**：增删改、排序、图标选择

---

## 项目结构

```
gtt-sidebar/
├── Core/                              [应用基础设施]
│   ├── Application/                   → 程序入口与主窗口
│   ├── Interfaces/                    → 组件契约接口
│   ├── Managers/                      → 组件管理、窗口定位、自动隐藏、开机启动
│   │   ├── AutoHideManager.cs         → 失去焦点贴边隐藏（本项目新增）
│   │   ├── SharedResourceManager.cs   → 单例：HttpClient、定时器、设置缓存
│   │   └── WidgetManager.cs            → 组件自动发现与生命周期
│   └── Settings/                      → 设置模型、持久化、设置窗口、图标库
│
├── Widgets/                           [各组件实现]
│   ├── ClockWidget/                   → 时钟
│   ├── CalendarWidget/                → 日历 + 节假日（本项目新增）
│   ├── WeatherWidget/                 → 天气（已改用 Open-Meteo）
│   ├── Notes/                         → 多标签备注
│   ├── Shortcuts/                     → 快捷方式启动器
│   ├── SystemInfo/                    → 系统信息 + 实时负载（本项目新增）
│   ├── SystemMonitor/                 → 系统监控（已合并进系统信息卡片，源码保留，不参与编译）
│   └── StockWidget/                   → 行情（已停用，源码保留）
│
└── docs/                              [说明文档]
    └── 优化说明.md                     → 详细改造记录
```

### 扩展方式

所有组件实现 `IWidget` 接口后即可被自动发现与加载，
无需手动注册。需要定时刷新的组件再实现 `ITimerSubscriber` 即可。

---

## 数据存储

配置与数据存放于 `%APPDATA%/DesktopSidebar/`：

| 文件 | 内容 |
|---|---|
| `settings.json` | 窗口、自动隐藏、监控阈值等配置 |
| `shortcuts.json` | 快捷方式列表 |
| `notes.json` | 备注内容 |
| `holidays.json` | 节假日缓存 |
| `icons/` | 自定义图标 |

---

## 外部服务

| 服务 | 用途 | 密钥 |
|---|---|---|
| [Open-Meteo](https://open-meteo.com) | 天气 + 城市地理编码 | 不需要 |
| holiday-cn | 中国法定节假日 | 不需要 |
| Google DNS | 网络延迟检测 | 不需要 |

---

## 致谢

原项目：[bitlamas/gtt-sidebar](https://github.com/bitlamas/gtt-sidebar)

详细改造记录见 [docs/优化说明.md](docs/优化说明.md)。
