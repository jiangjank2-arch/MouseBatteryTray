# MouseBatteryTray 鼠标电量任务栏显示

一个轻量的 Windows 鼠标电量 Demo：在通知区域显示圆角电池图标和电量数字，并根据**最近产生输入的鼠标**切换设备。

![圆角电池图标预览](assets/icon-preview.png)

## 功能

- 鼠标移动或点击后，使用 Windows Raw Input 识别实际产生输入的鼠标。
- 每 30 秒刷新一次 Windows 报告的电量；设备插拔时也会刷新。
- 图标采用 iOS 26 参考图的横向圆角电池比例、灰色外框、白色内圈和深色中心；右键可手动刷新或固定设备。
- 百分比显示在电池中心；低电量时数字按警示阈值变色。托盘尺寸由 Windows 控制，因此图标内不会绘制参考图下方的“iOS 26”说明文字。
- 电量 11%–30% 显示黄色，10% 及以下显示红色，其余显示绿色。
- 可安装到当前用户目录，并在登录 Windows 后自动启动；无需管理员权限。
- 若当前连接方式未向 Windows 提供电量，显示 **?**，不会复用另一连接方式的旧电量。

## 下载与运行

Windows 10/11 用户可直接运行 [release/MouseBatteryTray.exe](release/MouseBatteryTray.exe) 体验。安装自启动则运行 [scripts/install.bat](scripts/install.bat)；卸载运行 [scripts/uninstall.bat](scripts/uninstall.bat)。安装脚本从 release 目录查找 EXE，因此下载 ZIP 后应保留目录结构。

右键单击通知区域图标可选择设备、立即刷新或退出。如果图标藏在 **^** 隐藏图标菜单，可以将它拖到任务栏常驻。退出仅结束当前进程；下次登录仍按自启动设置运行。

## 从源码构建

双击 [build.bat](build.bat)，构建结果写入 release 目录。脚本调用 Windows 自带的 .NET Framework C# 编译器，不依赖 NuGet 或网络下载。源码位于 [src/MouseBatteryTray.cs](src/MouseBatteryTray.cs)。

## 蓝牙与 2.4G 模式

程序通过 Windows 原始输入识别当前使用的鼠标，再读取系统公布的设备电量。蓝牙鼠标通常可报告百分比；**2.4G 接收器是否提供电量取决于具体设备与驱动**。如果 Windows 没有公布电量，程序会显示 **?**，而不是把之前蓝牙模式的数值显示为当前值。切换连接模式后，移动鼠标一次，程序才能识别新的输入设备。

本项目在任务栏通知区域添加自己的图标，不修改系统电池图标。图标的精确排列顺序由 Windows 控制。

## 隐私与资源

程序在本机读取鼠标设备 ID、电量与原始输入设备句柄，不记录鼠标轨迹、不读取键盘内容、不发送网络请求。仅在当前用户注册表保存手动固定设备的 ID；安装脚本也在当前用户注册表设置自启动。一次本机测试中后台进程约占 31 MB 内存；具体占用随系统变化。

图标字体优先使用用户已安装的 Apple SF Pro；项目不分发该字体。未安装 SF Pro 时，Windows 自动使用外形接近的 Segoe UI Variable Display。

## 项目结构

- README.md：使用、构建和限制说明
- LICENSE：MIT 开源许可
- build.bat：源码构建脚本
- src/MouseBatteryTray.cs：完整源码
- release/MouseBatteryTray.exe：预编译 Demo
- scripts/install.bat：安装与自启动
- scripts/uninstall.bat：卸载
- assets/icon-preview.png：图标预览

许可证：[MIT](LICENSE)。
