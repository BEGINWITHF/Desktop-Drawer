# Desktop Drawer

把 Windows 桌面图标收进一个**可以翻页的桌面**。它不移动、不改名、不删除你的文件，只是在壁纸上铺一层和原生桌面几乎一样的图标网格，文件多了也不用再挤成一团。

- 图标直接画在壁纸上，没有大顶栏、没有窗口边框
- 窗口是**真透明**的：背景完全透出系统壁纸，换壁纸、壁纸幻灯片、动态壁纸都直接生效
- 任务栏、Alt+Tab、窗口切换器里都不会出现它；`Win+D` 之后它会自己贴回桌面
- 永远贴在桌面之上、所有软件窗口之下：不会遮挡任何正在运行的程序
- 启动时默认隐藏 Windows 原生桌面图标，退出时自动恢复
- 默认开机自启，登录后直接接管桌面
- 鼠标滚轮、PageUp / PageDown、底部圆点都能翻页
- 顶栏和底栏在鼠标静止约 2.4 秒后自动淡出，鼠标一动就回来

## 安装

需要 Windows 10/11 和 .NET 9 桌面运行时（本机已装 SDK 也可以）。

```powershell
.\build.ps1
```

脚本会发布到 `%LocalAppData%\DesktopDrawer`，并把这台机器的开机自启项指向该目录。之后直接运行：

```text
%LocalAppData%\DesktopDrawer\DesktopDrawer.exe
```

程序是单实例的，重复启动不会叠出第二个桌面。

## 操作速查

| 操作 | 方式 |
| --- | --- |
| 翻页 | 鼠标滚轮、`PageUp` / `PageDown`、`Alt+←` / `Alt+→`、底部圆点 |
| 第一页 / 最后一页 | `Ctrl+Home` / `Ctrl+End` |
| 搜索 | `Ctrl+F` |
| 打开 | 双击，或选中后按 `Enter` |
| 选择 | 单击、`Ctrl+单击`、`Shift+单击`、方向键 |
| 重命名 | `F2` |
| 删除到回收站 | `Delete` |
| 复制文件 | `Ctrl+C` |
| 剪切文件 | `Ctrl+X` |
| 粘贴到桌面 | `Ctrl+V` |
| 复制文件路径 | `Ctrl+Shift+C` |
| 全选当前页 | `Ctrl+A` |
| 刷新 | `F5` |
| 新建文件夹 | `Ctrl+Shift+N` |
| 临时隐藏 / 恢复原生桌面图标 | `Ctrl+Shift+H` |
| 关闭搜索 / 关闭面板 | `Esc` |
| 右键菜单 | 图标上右键是文件操作，空白处右键是桌面设置 |
| 拖出文件 | 直接把图标拖到资源管理器、聊天窗口等接受文件的地方 |
| 拖入文件 | 从资源管理器把文件拖到桌面空白处或某个文件夹图标上；默认同盘移动、跨盘复制，按住 `Ctrl` 复制、`Shift` 移动 |

## 设置面板

顶栏右侧的齿轮按钮可以打开设置（顶栏隐藏时先把鼠标动一下；也可以从托盘图标重新打开窗口）：

- **Icon size**：Compact / Cozy / Large 三个预设，控制每页能放多少图标
- **Custom icon size**：滑杆自定义图标大小（24–160 px），拖动后自动覆盖预设
- **Fit to window / Columns / Rows**：关掉自适应后可以指定每页的列数（2–16）和行数（1–16）；格子放不下时图标会自动缩小到合适尺寸
- **Folders first**：文件夹排在文件前面
- **Show hidden files**：显示桌面上的隐藏/系统项目
- **Close to tray**：点关闭按钮时留在托盘而不是退出
- **Start with Windows**：开机自启
- **Hide desktop icons now**：立刻隐藏 / 恢复 Windows 原生桌面图标
- **Hide icons on launch**：启动时自动隐藏原生图标
- **Open Desktop folder / Refresh icons**：打开桌面目录、重新读取图标

所有设置即时保存，不用点确定。

## 原生桌面图标

程序启动时会把 Windows 自带的桌面图标隐藏起来（和桌面右键 →「查看」→ 取消「显示桌面图标」是同一个开关）。无论是正常退出、崩溃，还是被任务管理器强制结束，只要分页桌面消失，原生桌面图标就会恢复显示。点关闭按钮收进托盘时也会先把图标还给你，从托盘重新打开时再隐藏。

这靠一个后台守卫进程 `DesktopDrawer.Watchdog.exe` 实现。它由主程序启动，任务管理器里会看到两个和 Desktop Drawer 有关的进程：守卫进程没有界面、占用很小，主进程一消失它就把原生图标恢复好然后自己退出，所以正常退出时不用手动结束它。守卫进程万一被单独结束，主程序会在 30 秒内重新拉起一个。

恢复时判断的是桌面图标列表窗口当前**实际**有没有显示，而不是只看注册表里的 `HideIcons`，所以即使上一次是强制结束、注册表和画面已经对不上，也能把图标真正显示回来。

只有把主进程和守卫进程一起杀掉（任务管理器的「结束进程树」、`taskkill /T`）才会没人负责恢复，这时手动补救：

- 桌面右键 →「查看」→ 勾选「显示桌面图标」
- 或者重新启动一次 Desktop Drawer，再从设置面板正常退出（退出时同样会恢复显示）

## 布局与整理

**调整布局**

设置面板 → LAYOUT → 打开 `Adjust layout`，顶栏、页码胶囊、分组标签、左下角统计、右下角提示都会出现虚线框，直接用鼠标拖到想要的位置即可：

- 位置按比例写进 `settings.json` 的 `Layout`，换分辨率也不会跑丢
- `Reset positions` 恢复默认（顶栏在顶部、页码在底部居中、统计在左下、提示在右下）
- 按 `Esc` 或再点一次开关结束编辑
- 顶栏占满整行，所以只支持上下移动；其余元素可以任意拖动

**虚拟分组**

右键桌面 → `Group by`（排序菜单里也有），可以按 `Type`（文件夹 / 文档 / 图片 / 视频 / 音频 / 压缩包 / 程序 / 其他）、`Name` 首字母、`Date modified` 分组。

- 文件不会被移动、改名或删除，只是把同一组的图标排到同一段页码里
- 页码胶囊会显示当前组，比如 `Images · 2/3`；左上角的标签显示组名和数量
- 组名标签本身也在 `Adjust layout` 里可以拖动
- 选 `None` 就回到不分组的分页

## 数据位置

| 内容 | 路径 |
| --- | --- |
| 设置 | `%APPDATA%\DesktopDrawer\settings.json` |
| 日志 | `%APPDATA%\DesktopDrawer\desktop-drawer.log` |
| 程序 | `%LocalAppData%\DesktopDrawer\` |
| 开机自启项 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopDrawer` |

删掉 `settings.json` 就会恢复默认设置（包括重新隐藏原生图标、开机自启）。

## 开发

```powershell
dotnet build src\DesktopDrawer\DesktopDrawer.csproj
dotnet run --project src\DesktopDrawer\DesktopDrawer.csproj
```

项目的技术栈是 .NET 9 + WPF，不依赖任何第三方 NuGet 包：

```text
src\DesktopDrawer
├─ MainWindow.xaml            界面（壁纸层、图标网格、顶栏、浮动页码、设置面板）
├─ MainWindow.xaml.cs         布局、分页、图标尺寸、壁纸、顶栏自动隐藏
├─ MainWindow.Interactions.cs 键鼠、搜索、重命名、删除、拖拽
├─ MainWindow.Settings.cs     托盘、设置面板、原生图标开关、退出
├─ MainWindow.Menus.cs        排序菜单、图标右键菜单、空白处菜单
├─ Services\                  桌面枚举、图标提取、壁纸、设置、开机自启、Shell 操作
├─ Interop\NativeMethods.cs   Win32 / Shell 声明
└─ Themes\Theme.xaml          颜色、按钮、开关等样式

src\DesktopDrawer.Watchdog      独立的守卫进程，主程序消失后恢复原生图标
```

## 已知限制

- 它是一个顶层工具窗口（只是不显示在任务栏和 Alt+Tab 里）。`Win+D` 时系统会把它和其他窗口一起最小化，程序会立刻恢复并重新贴到桌面之上，中间可能有极短的闪烁。
- 只读取用户桌面和公共桌面两个目录，不会移动或整理原始文件。
- 图标尺寸按 100% 及更高 DPI 自动缩放，极端的自定义缩放比例可能需要微调密度档位。
