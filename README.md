# Desktop Drawer

把 Windows 桌面图标收进一个**可以翻页的桌面**。它不移动、不改名、不删除你的文件，只是在壁纸上铺一层和原生桌面几乎一样的图标网格，文件多了也不用再挤成一团。

- 图标直接画在壁纸上，没有大顶栏、没有窗口边框
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

程序是单实例的，重复启动只会把已经运行的窗口叫到前面。

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
| 刷新 | `F5` |
| 新建文件夹 | `Ctrl+Shift+N` |
| 临时隐藏 / 恢复原生桌面图标 | `Ctrl+Shift+H` |
| 关闭搜索 / 关闭面板 | `Esc` |
| 右键菜单 | 图标上右键是文件操作，空白处右键是桌面设置 |
| 拖出文件 | 直接把图标拖到资源管理器、聊天窗口等接受文件的地方 |

## 设置面板

顶栏右侧的齿轮按钮可以打开设置（顶栏隐藏时先把鼠标动一下；也可以从托盘图标重新打开窗口）：

- **Icon size**：Compact / Cozy / Large，控制每页能放多少图标
- **Folders first**：文件夹排在文件前面
- **Show hidden files**：显示桌面上的隐藏/系统项目
- **Close to tray**：点关闭按钮时留在托盘而不是退出
- **Start with Windows**：开机自启
- **Hide desktop icons now**：立刻隐藏 / 恢复 Windows 原生桌面图标
- **Hide icons on launch**：启动时自动隐藏原生图标
- **Open Desktop folder / Refresh icons**：打开桌面目录、重新读取图标

所有设置即时保存，不用点确定。

## 原生桌面图标

程序启动时会把 Windows 自带的桌面图标隐藏起来（和桌面右键 →「查看」→ 取消「显示桌面图标」是同一个开关），退出时恢复成启动前的状态，所以不会永久改坏系统设置。

如果程序被强制结束（任务管理器里结束进程、蓝屏、断电），图标可能保持隐藏状态。补救办法任选其一：

- 重新启动 Desktop Drawer，然后从设置面板正常退出
- 桌面右键 →「查看」→ 勾选「显示桌面图标」

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
```

## 已知限制

- 按 `Win+D`（显示桌面）会让窗口像普通窗口一样最小化，露出被隐藏图标后的空白桌面；再按一次就回来了。
- 壁纸在启动时读取一次，换壁纸后需要重启程序才会更新。
- 只读取用户桌面和公共桌面两个目录，不会移动或整理原始文件。
- 图标尺寸按 100% 及更高 DPI 自动缩放，极端的自定义缩放比例可能需要微调密度档位。
