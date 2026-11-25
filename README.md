<div align="center">

<img src="icon/index.png" width="112" alt="EasyShot 图标" />

# EasyShot

**截图、标注、长截图、贴图与离线识别，一个 exe 即可使用。**

[![Release](https://img.shields.io/github/v/release/CoffeeCat0667/EasyShot?display_name=tag)](https://github.com/CoffeeCat0667/EasyShot/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/CoffeeCat0667/EasyShot/total)](https://github.com/CoffeeCat0667/EasyShot/releases)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4?logo=windows)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-2563EB)
[![Issues](https://img.shields.io/github/issues/CoffeeCat0667/EasyShot)](https://github.com/CoffeeCat0667/EasyShot/issues)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

[下载最新版](https://github.com/CoffeeCat0667/EasyShot/releases/latest) · [反馈问题](https://github.com/CoffeeCat0667/EasyShot/issues)

</div>

## 简介

EasyShot 是基于 C# / .NET 10 / WPF 开发的 Windows 截图工具。发布版采用自包含单文件形式，不要求用户另外安装 .NET、Python 或系统 OCR 语言包；图片处理和识别均在本机完成，不依赖云端服务。

程序启动后驻留托盘，不显示主窗口。默认按 **`Alt+S`** 开始截图。

## 功能

| 功能 | 说明 |
| --- | --- |
| 区域与窗口截图 | 鼠标悬停高亮窗口，单击选择窗口；按住拖动则框选区域。支持显示器覆盖层与选区调整。 |
| 全屏截图 | 从托盘或自定义热键截取鼠标所在显示器，不拼接多屏。 |
| 图形标注 | 矩形、箭头、画笔、文字、递增序号、矩形马赛克；支持对象移动、缩放、删除及 20 步撤销/重做。 |
| 悬浮工具栏 | 矢量图标、中文提示、字号/粗细/颜色下拉框；左侧点阵可拖动，小屏自动换行和避让。 |
| 双向长截图 | 从区域截图工具栏进入，用户手动上下滚动；拼接预览、折返去重、等待重叠及自动恢复，最长 20000 px。 |
| 贴图 | 独立彩色线框、置顶切换、窗口整体缩放、图像局部缩放、中键平移及不透明度滑块。 |
| 离线 OCR | 内嵌 PP-OCRv4 中文模型，支持中英文；启动后台预加载，长图分块；带边框结果窗左侧显示截图、右侧可编辑文字，使用系统关闭/最小化/最大化，不提供图标按钮。 |
| 二维码与条形码 | 多码列表、格式及内容展示、逐项复制；包括 QR、Code128、Code39、EAN、UPC、ITF、Data Matrix、Aztec、PDF417。 |
| 本地设置 | 区域/全屏热键、截图与贴图框线颜色、保存初始目录、开机自启、保存时同时复制。 |

## v0.1.4

- 长截图改为无边框预览，点击 **X** 取消退出，点击 **√** 复制拼接图到剪贴板并退出；优化快速滚动下的重叠匹配。
- OCR 与二维码结果窗恢复系统边框和关闭/最小化/最大化按钮，移除图标按钮；OCR 左侧显示截图、右侧显示可编辑文本。
- 点击 OCR/二维码识别后关闭截图框和悬浮栏，结果窗独立显示；关闭结果窗后程序留在托盘。
- 序号改为空心圆与同色数字；仅在“选择”工具下可移动、拉伸或编辑标注；文字输入框改为透明无边框。
- 减少拖动、绘制标注和窗口悬停时的重复重绘与悬浮栏刷新，改善交互流畅度。

## 下载与运行

1. 前往 [Releases](https://github.com/CoffeeCat0667/EasyShot/releases/latest)，下载 **`EasyShot.exe`**。
2. 放到可写目录或 `C:\Program Files`，双击运行；不需要安装程序或管理员权限。
3. 按 `Alt+S`，或从托盘菜单选择截图。

> 使用 Windows 10 / Windows 11 **x64**。未在所有 Windows 版本或未安装运行库的干净机器上验证兼容性，发现问题请提交 Issue。
>
> 设置保存在 `%LOCALAPPDATA%\EasyShot\config.json`，不写入 exe 所在目录。首次运行且用户配置不存在时，会尝试导入 exe 同目录旧版 `config.json`，不会删除旧文件。
>
> 分发文件只有一个 exe。开机自启写当前用户的 `HKCU\...\Run`，不要求管理员权限。原生依赖按 .NET 单文件机制解压到用户临时目录；OCR 初始化也会临时释放内嵌模型，加载后清理。程序启动后后台加载 OCR，不阻塞截图，但模型内存会保留至退出。
>
> 更新前请先在旧版托盘菜单中退出程序，再运行新版；否则单实例机制会激活仍在运行的旧进程。开机自启使用 exe 的绝对路径，移动程序后请在设置中重新配置自启。

## 使用说明

### 截图与标注

- `Alt+S` 后单击选择窗口，或拖动框选区域；松手后显示选区抓手和工具栏。
- 尚未拉出第一个截图框时，按右键退出区域截图；该次右键不会传给下方程序。
- 拖动选区不带动标注：标注固定在冻结屏幕位置，移出选区的部分隐藏，重新框入后恢复。
- 仅在“选择”工具下可选中标注、拖动、调整大小或按 `Delete` 删除；切换到其他工具后，点击已有标注会按当前工具继续绘制。
- 序号显示为空心彩色圆圈和同色数字；文字输入框为透明无边框。
- **区域截图框的 8 个抓手始终自由调整宽高，不受 `Shift` 限制。标注对象默认等比缩放，按住 `Shift` 自由调整；贴图线框始终等比。** 初次框选与绘制不锁比例。
- 文字：点击输入，`Enter` 确认文字，`Esc` 放弃输入；双击已有文字重新编辑。
- 拖动工具栏左侧点阵可移动悬浮栏，本次截图保持手动位置，下次截图恢复自动定位。

| 快捷键 | 操作 |
| --- | --- |
| `Alt+S` | 区域截图，包含窗口选择；可在设置中修改或清除 |
| `Enter` | 确认截图，复制到剪贴板并关闭，不保存文件 |
| `Ctrl+S` | 选择位置保存 PNG；保存成功后关闭编辑态 |
| `Esc` | 取消截图，不保存不复制；文字输入期间先取消文字输入 |
| `Ctrl+Z` | 撤销 |
| `Ctrl+Y` / `Ctrl+Shift+Z` | 重做 |
| `Delete` | 删除选中标注 |

保存时是否同时复制由设置决定，默认关闭；若开启，先写文件，再写剪贴板。复制失败会提示，不删除已保存的文件。保存对话框取消后继续编辑。

### 识别

- 点击工具栏 **OCR** 或二维码按钮后，当前画面会从截图编辑态取出，区域/全屏截图框和悬浮栏关闭，结果窗独立显示。
- OCR 窗口左侧显示截图，右侧为可编辑文本；关闭、最小化、最大化使用系统标题栏，底部“复制”复制全部文本且窗口不关闭。
- 二维码窗口按码制列出结果，每项可单独复制。

### 长截图

1. 区域截图中选择滚动内容范围，点击工具栏 **长截图** 图标。
2. 等控制窗显示累计尺寸后，手动向上或向下滚动；捕捉期间保留线框和外部遮罩，鼠标可操作下方页面。
3. 如果显示“等待重叠”，回滚一些，画面回到可匹配范围后自动恢复，不需要点“继续”。
4. 点击 **√** 或按 `Esc` 将拼接图复制到剪贴板并退出；点击 **X** 直接取消退出。

不提供独立长截图热键或托盘入口，不模拟滚轮，不支持横向拼接。相邻画面需要有可辨认的重叠内容；固定导航、动画、懒加载和重复纹理可能影响结果，不能确认时不会强行拼接。

### 贴图

截图工具栏点击 **贴图**，当前结果显示为独立窗口：

| 操作 | 效果 |
| --- | --- |
| 左键拖动内部 | 移动窗口 |
| 拖动线框边缘或角 | 整体调整窗口大小，始终等比缩放（`Shift` 不解除比例），无可见抓手 |
| 内部滚轮 | 围绕鼠标局部缩放图片，窗口大小不变，范围 100%–800% |
| 按住中键拖动 | 平移放大的图像，不移动窗口，不露空白 |
| 右键菜单 | 整体/局部放大缩小、置顶切换、不透明度 0–100% 滑块、确认、保存、取消 |
| `Enter` / 右键确认 | 复制当前可见视图并关闭 |
| `Ctrl+S` / 右键保存 | 保存当前可见视图，保留贴图窗口 |
| `Esc` / 右键取消 | 关闭，不复制不保存 |

不透明度只影响屏幕显示，线框保持可见；0% 时仍可右键调回。复制与保存不包含线框/菜单，也不应用屏幕显示的不透明度。多个贴图可并存，退出程序后不保留。

### 设置与托盘

- 托盘左键截图，右键菜单为“区域截图 / 全屏截图 / 设置 / 退出”。
- 自启开关仅在设置页。配置写入当前用户的 Windows `Run` 注册表项。
- 区域默认绑定 `Alt+S`，全屏默认未绑定；窗口选择合并到区域截图，不设独立热键。
- 截图框线颜色与贴图框线颜色独立；贴图支持预设色和 `#RRGGBB` 输入，保存后新建窗口生效。

## 从源码构建

### 环境

- Windows x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 首次还原 NuGet 依赖需要网络；构建后的截图和识别不需要网络。

```powershell
git clone https://github.com/CoffeeCat0667/EasyShot.git
cd EasyShot
dotnet restore
dotnet build -c Release
```

发布自包含单文件：

```powershell
dotnet publish EasyShot.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
```

主产物为 `artifacts/publish/EasyShot.exe`。ONNX Runtime 包可能额外输出 `.lib` 链接开发文件，用户运行不需要这些文件。WPF 不使用 `PublishTrimmed`。

内嵌中文模型位于 `Assets/models/`，所需 VC++ DLL 位于 `Assets/native/`，两者属于构建输入而不是可忽略的编译产物。

## 目录

```text
Capture/      抓屏、窗口枚举、滚动拼接与剪贴板输出
Config/       本地配置
Core/         Win32、单实例、热键、托盘和自启
Overlay/      选区、工具栏、标注、等比缩放与贴图
Ocr/          离线 OCR 与结果窗口
Qr/           多码识别与结果窗口
Settings/     设置页
UI/           矢量图标与 WPF 样式
Assets/       内嵌 OCR 模型及原生依赖
icon/         原始 PNG 与程序 ICO
ThirdParty/   第三方许可与来源说明
```

## 已知限制与反馈

- 受保护或部分硬件加速窗口可能返回黑图；程序会提示疑似黑图并尝试保留可见冻结区域，不绕过窗口保护。
- 长截图依赖画面重叠，固定控件和动态内容可能影响接缝；最高 20000 px。
- OCR 的效果受文字大小、清晰度和布局影响；启动预加载、像素直传及分块扫描的提速未做完整基准，不承诺固定耗时。
- 默认保存子目录不存在时，对话框回退到系统图片目录，不自动创建目录。
- M3–M5 已由项目使用者验收；M6 和后续界面/贴图改动已编译交付，兼容性、识别完整性及全部 UI 行为仍需进一步验证。

提交 [Issue](https://github.com/CoffeeCat0667/EasyShot/issues) 时，请附 Windows 版本、显示器缩放比例、操作步骤和完整错误提示；图片请先去掉私人信息。

## 第三方组件与许可

本项目原创代码采用 [MIT License](LICENSE)。第三方组件与模型分别遵循各自许可：H.NotifyIcon、RapidOcrNet、ONNX Runtime、SkiaSharp、ZXing.Net、PaddleOCR / RapidOCR 等来源及许可文本见 [ThirdParty/NOTICE.txt](ThirdParty/NOTICE.txt) 和 `ThirdParty/`。MIT 仅适用于本项目原创代码，不替代或改变第三方许可。
