# LanTouchPad

Android 手机通过局域网控制 Windows 鼠标，并向电脑当前聚焦的输入框发送文字。无需账号、验证码或配对。

## 下载

- 下载页面（支持一键复制链接）：https://yuxiboy.online/lantouchpad/
- Android APK：https://yuxiboy.online/downloads/lantouchpad/LanTouchPad-1.0.0.apk
- Windows 安装包：https://yuxiboy.online/downloads/lantouchpad/LanTouchPad-Setup-1.0.0-win-x64.exe

## 使用

1. 在 Windows 10/11 x64 上安装 PC 程序。PC 1.0.4 起安装 LocalSystem 系统服务，默认勾选随系统自动启动，登录前即可接收手机连接；登录后启动托盘管理界面。安装程序包含 .NET 运行时，无需保存 Windows 密码，并添加仅允许本地子网的防火墙规则。安装和服务管理需要管理员权限。
2. 电脑和 Android 手机连接同一局域网，启动 PC 程序。
3. 打开 Android APP，扫描并点击电脑名称即可控制。扫描失败时可使用右上角的连接 IP 功能，填写 PC 窗口中显示的 IPv4 地址。
4. 触控区中央单指滑动移动鼠标，轻点左键，连续轻点双击。左右两侧淡色窄条为滚轮区，单指上下滑动即可滚动，轻点或长按不会点击；手势按起点锁定，中央开始的移动或拖选滑到边缘仍继续移动或拖选。轻点一次后，在 300 毫秒内再次按下并保持，即可直接拖动选中，松手结束；也支持单指长按后拖动。双指轻点右键，双指上下滑动滚动。底部仅保留整行键盘按钮，不再显示独立的左右键按钮。左键拖动结束后默认发送 Ctrl+C，复制到电脑剪贴板；取消手势和断线不会触发复制。
5. 连接后默认隐藏键盘，点击键盘按钮会展开输入框并直接唤起手机输入法，触摸触控区会收起输入框和输入法，未发送的草稿会保留。手机输入法的发送/完成/回车会先发送草稿，再向 PC 透传一次回车；草稿为空时也会发送回车，用于提交 PC 当前输入框（遵循 PC 应用的回车设置）。APP 内独立发送按钮只发送文字。发送后键盘保持展开。手机键盘的退格会同步到电脑，即使手机草稿为空；真正的向前 Delete 同步为电脑 Delete。编辑中文候选字时不发送删除。支持中文、多行粘贴和表情，下方提供退格、回车、Tab 和方向键。连续发送与删除按顺序执行；未收到确认时恢复草稿并取消排队的回车，不自动重发。
6. 关闭 PC 窗口后保留托盘，托盘可打开窗口或退出，窗口内可暂停接收和切换开机自启。
7. APP 右上角设置可独立调节指针速度和滚轮速度。滚轮默认 2 倍速（相对于 Android 1.0.5），可在 0.5～5 倍之间调整，同时作用于两侧边缘和双指滚动；滚轮设置保存在手机上，重启和重新连接后仍然生效。

## 网络与系统限制

- 按需求免验证，网络中的其他设备可以直接控制电脑。仅在可信局域网使用；不用时可暂停或退出。控制数据以局域网明文传输。
- IPv4 发现使用 UDP 45831，控制使用 TCP/WebSocket 45832。同一时刻仅一个手机控制，第二个连接被拒绝。服务端只接受私有 IPv4、链路本地 IPv4 和回环地址，拒绝浏览器 Origin 请求。
- 路由器的访客隔离、AP 隔离、VPN 和广播屏蔽会影响发现；隔离导致无法互访时，手动 IP 也无法连接。
- PC 1.0.4 的服务在当前物理控制台会话启动 SYSTEM 输入进程，专用输入线程跟随普通桌面、登录和锁屏等输入桌面；锁屏期间无需先解锁电脑才能连接。仍需正确的 Windows 密码或 PIN，不会绕过认证。BitLocker 开机解密等 Windows 启动前的界面不受支持；要求 Ctrl+Alt+Delete 的安全注意序列不能通过普通键盘模拟。没有改变 Windows 安全策略或自动登录设置。远程桌面（RDP）会话不在此版本支持范围内。
- PC 1.0.3 的正常移动保留原来的相对输入和 Windows 鼠标加速，仅在持续移动指令约 100 毫秒仍未改变光标位置时启用系统光标定位恢复。恢复模式沿用近期观察到的移动倍率，支持多显示器和负坐标桌面；手机端仍可调整指针速度。窗口边框拖动支持缩放，移动窗口和调整大小时不自动复制。
- 输入被 Windows 拒绝或光标移动受限时，状态会记录到 `%LOCALAPPDATA%\LanTouchPad\input-diagnostics.jsonl`，托盘菜单“输入诊断日志”可以打开。日志包含程序名、输入桌面和光标状态，不包含输入文字。仅本机可读取 `http://127.0.0.1:45832/diagnostics`；具体软件的弹窗兼容性仍需实测。
- 文本功能向当前焦点发送 Unicode 键盘输入，不读取目标输入框内容，也不替换整段原文。个别游戏或自行处理键盘的应用可能不支持 Unicode 输入。
- 移动端切到后台会释放鼠标并断开，回到前台自动重连。网络异常时服务器最多 10 秒释放按住的鼠标。控制期间手机保持亮屏。
- APK 使用本地专用发布密钥签名；Windows 安装包未购买商业代码签名证书。

## 开发与构建

目录：`mobile/` 为 Flutter Android APP，`pc/` 为 .NET 8 WinForms + Kestrel，`installer/` 为 Inno Setup，`website/` 为静态下载页面。

需要 Flutter 3.32.8 或兼容版本、JDK 21、Android SDK 36、.NET 8 SDK、Inno Setup 6。

```powershell
# 首次生成或更新图标
./scripts/generate-assets.ps1

# Android：首次自动生成专用发布密钥
./scripts/build-android.ps1 -Flutter 'C:\path\to\flutter.bat'

# Windows：包含运行时的独立程序和安装包
./scripts/build-windows.ps1 -Iscc 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

# PC 本机开发
dotnet build pc -c Release
Start-Process 'pc\bin\Release\net8.0-windows\LanTouchPad.exe' -Verb RunAs

# 手势测试
cd mobile
flutter analyze
flutter test

# 真实 Windows 输入测试，需 PC 服务已启动且桌面可交互
cd ..
dotnet build tests/DesktopSmoke -c Release
Start-Process 'tests\DesktopSmoke\bin\Release\net8.0-windows\DesktopSmoke.exe' -Verb RunAs

# 使用独立端口和独立接收端，手机无需断开连接
Start-Process 'tests\DesktopSmoke\bin\Release\net8.0-windows\DesktopSmoke.exe' -ArgumentList '--isolated' -Verb RunAs
```

真实输入测试前请先断开手机连接。测试会新建独立窗口，点击窗口内的输入区使其获得焦点后，验证中文、表情、多行、管理员模态窗口的鼠标与键盘输入、拖选复制和断线释放，结果位于测试输出目录的 `smoke-report.txt`。测试期间不要操作鼠标键盘。测试不连接真实手机；跨 Wi-Fi 的设备发现仍需真机验收。

输出文件在 `dist/`，不提交二进制产物。**请单独备份 `mobile/android/app/lantouchpad-release.jks` 和 `mobile/android/key.properties`**，后续 APK 更新必须使用同一密钥。这两个文件均已加入 Git 忽略，不能提交或公开。

Windows 服务架构、验证和排查见 [docs/windows-service.md](docs/windows-service.md)。

## 协议

协议和手动联调示例见 [docs/protocol.md](docs/protocol.md)。APK、Windows 安装包和校验文件部署在服务器已有 HTTPS 静态目录中，不启动新的公开服务。
