# gtt-sidebar

This is a simple Windows desktop sidebar developed in C# that fills the empty space on my 14" laptop screen (1920x1080 @ 125% scaling) when using LibreWolf's default window size. 

![gtt-sidebar-image](https://github.com/user-attachments/assets/31d40c02-c6fa-4529-aa39-edc97fb3cf8b)

A personal utility project that helped me get into C# development with widgets for weather, notes, stocks, shortcuts and system monitoring. This is a work in progress with no clear roadmap, but I intend to add more customization options. The sidebar is already very modular and easy to add new widgets, but I will make it simpler and create a proper interface to manage them.

## Quick Start

<img align="right" height="800" src="https://github.com/user-attachments/assets/619d3864-a740-47c1-9c1e-c365676b36d2">

**Download** the latest executable from the [Releases page](../../releases), no installation required,

or **build from source**:
```bash
git clone https://github.com/bitlamas/gtt-sidebar
cd gtt-sidebar
# Open gtt-sidebar.sln in Visual Studio
# Build → Release (requires .NET Framework 4.7.2)
```

## What's Included

- **Clock**: Time and date
- **Calendar**: Month grid with Chinese public-holiday markers + next-holiday countdown
- **Weather**: Current conditions + 3-day forecast (Open-Meteo, no API key needed)
- **Notes**: Multi-tab notepad with auto-save (because note taking with notepad.exe sucks)
- **Shortcuts**: Custom launcher with icon support (5×4 grid)
- **System Info**: Uptime, disk free space, network link speed, process count
- **System Monitor**: CPU, RAM usage, network ping

All widgets are configurable through a unified settings window. The sidebar stays on top, can be positioned left or right, and minimizes to system tray.

## Features

- **Real-time updates** - weather (30min), stocks (10min), system metrics (configurable)
- **Persistent storage** - JSON-based settings and data (settings.json, shortcuts.json, notes.json)
- **Custom icons for shortcuts** - 50 built-in icons + PNG upload support
- **失去焦点贴边隐藏 (Auto-hide on focus loss)** - the sidebar slides off-screen when it loses focus, leaving a thin edge strip; move the mouse to that screen edge (or use the tray icon) to slide it back
- **中文界面 (Chinese UI)** - all windows, dialogs, tray menu and messages are localized to Simplified Chinese

---

## Architecture Overview

<details>
<summary>Core Structure (click to expand)</summary>

```
gtt-sidebar/
│
├── Core/                              [Application infrastructure]
│   ├── Application/
│   │   ├── App.xaml/.cs              → WPF entry point
│   │   └── MainWindow.xaml/.cs       → Main 122px sidebar window
│   │
│   ├── Interfaces/
│   │   ├── IWidget.cs                → Base widget contract
│   │   └── ITimerSubscriber.cs       → Timer-based widgets
│   │
│   ├── Managers/
│   │   ├── SharedResourceManager.cs  → Singleton: HttpClient, timers, settings cache
│   │   ├── WidgetManager.cs          → Auto-discovery and lifecycle
│   │   ├── WindowPositioner.cs       → Sidebar positioning
│   │   └── StartupManager.cs         → Windows startup registry
│   │
│   ├── Settings/
│   │   ├── SettingsData.cs           → Data models
│   │   ├── SettingsStorage.cs        → JSON persistence
│   │   ├── SettingsWindow.xaml/.cs   → 350×600px settings UI
│   │   ├── ShortcutData.cs           → Shortcut models
│   │   ├── IconCatalog.cs            → 50 built-in icons
│   │   └── IconPicker.xaml/.cs       → Icon selection popup
│   │
│   └── Icons/DefaultIcons/           → 50 PNG icons (18×18px)
│
├── Widgets/                           [Individual implementations]
│   ├── ClockWidget/                  → Time/date (1s updates)
│   ├── WeatherWidget/                → OpenWeatherMap API (30min)
│   ├── StockWidget/                  → Kraken + Yahoo Finance (10min)
│   ├── Notes/                        → Multi-tab notepad with popup
│   ├── Shortcuts/                    → 4×4 launcher grid
│   └── SystemMonitor/                → CPU/RAM/Ping monitoring
│
└── Properties/                        [Project configuration]
```

### Key Patterns
- **Auto-discovery**: Widgets implement `IWidget`, no manual registration
- **Shared resources**: Single HttpClient, coordinated timers, cached settings
- **Event-driven**: Settings changes trigger immediate widget updates
- **Async storage**: JSON files in `%APPDATA%/gtt-sidebar/`

</details>

## Technical Details

**Framework**: .NET Framework 4.7.2 (C# WPF)  
**Dependencies**: Newtonsoft.Json 13.0.3  
**Performance**: <50MB memory, <1% CPU idle, <2s startup  

### External Services
- **Open-Meteo**: Weather + geocoding (free, **no API key required**)
- **holiday-cn** (via jsDelivr): Chinese public-holiday schedule, sourced from gov.cn papers
- **Google DNS**: 8.8.8.8 for ping monitoring

### API Keys
None required. Weather data comes from [Open-Meteo](https://open-meteo.com), which is free
for non-commercial use and needs no registration.

> 注：原 StockWidget（行情）组件已停用，源码保留在 `Widgets/StockWidget/`
> 目录中但不再参与编译，如需恢复请把相关条目加回 `gtt-sidebar.csproj`。

### Holiday Data
The calendar card fetches the Chinese public-holiday schedule on startup and then
refreshes **once every 24 hours** (low-frequency by design). Data is cached to
`%APPDATA%/gtt-sidebar/holidays.json`, and the loader falls back through
jsDelivr → fastly.jsDelivr → timor.tech → local cache, so the calendar keeps
working offline.
