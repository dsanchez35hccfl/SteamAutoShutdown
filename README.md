# 🎮 Steam 自动关机助手

> 下载完成后自动关机，安心挂机不操心

Steam 自动关机助手是一款 Windows 桌面工具，监测 Steam 游戏下载进度，在下载完成后自动执行关机操作。适合夜间挂机下载大型游戏的场景。

---

## ✨ 功能特性

- **智能监测**：解析 Steam `.acf` 清单文件，实时获取下载进度和游戏名称
- **自动关机**：下载完成后 60 秒倒计时关机，无需人工干预
- **紧急取消**：一键取消关机计划，并明确提示取消结果
- **迷你悬浮窗**：最小化为始终置顶的悬浮窗，显示游戏名称和进度条
- **系统托盘**：最小化到系统托盘，不占用任务栏空间
- **Steam 暗色主题**：参考 Steam UI 风格，深色渐变背景 + 卡片式布局
- **窗口自适应**：窗口高度随内容动态变化，始终保持屏幕居中

## 📸 界面预览

主窗口采用 Steam 风格暗色主题，包含路径选择、状态监控、下载进度和操作按钮。

迷你悬浮窗始终置顶，实时显示游戏名称、下载进度条及百分比。

## 🔧 技术栈

| 项目 | 技术 |
|------|------|
| 框架 | .NET 10 + WPF |
| 语言 | C# |
| 托盘 | Windows Forms 互操作 (`NotifyIcon`) |
| 监测 | Steam `.acf` 清单文件解析（Valve VDF 格式） |
| UI | 自定义 ControlTemplate / Storyboard 动画 |

## 📦 环境要求

- Windows 10 / 11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Steam 客户端

## 🚀 快速开始

### 从源码构建

```bash
git clone https://github.com/dsanchez35hccfl/SteamAutoShutdown.git
cd SteamAutoShutdown
dotnet build -c Release
dotnet run -c Release
```

### 使用方法

1. **选择路径**：点击「浏览」选择 Steam 的 `steamapps\downloading` 文件夹
   - 默认路径通常为 `X:\...\Steam\steamapps\downloading`
2. **开始监测**：点击「▶ 开始监测」
3. **查看进度**：主窗口和迷你悬浮窗实时显示游戏名称及下载百分比
4. **自动关机**：下载完成后自动进入 60 秒倒计时
5. **紧急取消**：在倒计时期间点击「⚠ 紧急取消关机」可取消

## 🏗️ 监测原理

```
开始监测
    │
    ▼
DispatcherTimer (1秒)
    │
    ├─── 每5秒 ──→ 读取 .acf 清单文件 → 获取游戏名 + 下载进度
    │
    └─── 每1秒 ──→ 检查 downloading/ 文件夹子目录
                        │
            ┌───────────┴───────────┐
         有子目录              无子目录
        (下载中)         (曾检测到下载？)
       标记活跃              │
       重置计数          连续5次确认
                             │
                             ▼
                       下载完成！
                       60秒倒计时 → 关机
```

- 通过 `downloading` 文件夹子目录判定下载是否完成（非文件大小，因为 Steam 会预分配磁盘空间）
- `.acf` 文件仅用于展示游戏名和进度百分比
- `_hasSeenDownload` 标志防止程序启动时文件夹为空导致误判

## 📁 项目结构

```
SteamAutoShutdown/
├── App.xaml              # 全局 Steam 暗色主题样式
├── App.xaml.cs
├── MainWindow.xaml       # 主窗口 UI
├── MainWindow.xaml.cs    # 核心逻辑（监测、倒计时、托盘）
├── MiniWindow.xaml       # 迷你悬浮窗 UI
├── MiniWindow.xaml.cs    # 悬浮窗事件与更新
├── SteamAutoShutdown.csproj
└── favicon.ico           # 应用图标
```

## 📄 License

MIT License
