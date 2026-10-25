# LanTouchPad

Android 手机通过局域网控制 Windows 鼠标，并向电脑当前聚焦的输入框发送文字。无需账号、验证码或配对。

## 下载

- 下载页面（支持一键复制链接）：https://yuxiboy.online/lantouchpad/
- Android APK：https://yuxiboy.online/downloads/lantouchpad/LanTouchPad-1.0.0.apk
- Windows 安装包：https://yuxiboy.online/downloads/lantouchpad/LanTouchPad-Setup-1.0.0-win-x64.exe

## 使用

1. 在 Windows 10/11 x64 上安装 PC 程序。PC 端需要管理员权限，以控制卸载确认等管理员窗口。安装程序包含 .NET 运行时，默认勾选登录 Windows 后自动启动（使用当前用户的交互式最高权限计划任务，无需保存密码），并添加仅允许本地子网的防火墙规则。
2. 电脑和 Android 手机连接同一局域网，启动 PC 程序。
3. 打开 Android APP，扫描并点击电脑名称即可控制。扫描失败时可使用右上角的连接 IP 功能，填写 PC 窗口中显示的 IPv4 地址。
4. 单指滑动移动鼠标，轻点左键，连续轻点双击。轻点一次后，在 300 毫秒内再次按下并保持，即可直接拖动选中，松手结束；也支持单指长按后拖动。双指轻点右键，双指上下滑动滚动。按住底部左键按钮后直接拖动这个手指可选中文本，也可按住左键并用另一手指滑动触控区。左键拖动结束后默认发送 Ctrl+C，复制到电脑剪贴板；取消手势和断线不会触发复制。
5. 连接后默认隐藏键盘，点击键盘按钮会展开输入框并直接唤起手机输入法，触摸触控区会收起输入框和输入法，未发送的草稿会保留。输入文字后按手机键盘的发送/回车，或点击发送按钮。文字会插入电脑当前的光标位置，发送后键盘保持展开。手机键盘的退格会同步到电脑，即使手机草稿为空；真正的向前 Delete 同步为电脑 Delete。编辑中文候选字时不发送删除。支持中文、多行粘贴和表情，下方提供退格、回车、Tab 和方向键。连续发送与删除按顺序执行；未收到确认时恢复草稿，不自动重发。
6. 关闭 PC 窗口后保留托盘，托盘可打开窗口或退出，窗口内可暂停接收和切换开机自启。

## 网络与系统限制

- 按需求免验证，网络中的其他设备可以直接控制电脑。仅在可信局域网使用；不用时可暂停或退出。控制数据以局域网明文传输。
- IPv4 发现使用 UDP 45831，控制使用 TCP/WebSocket 45832。同一时刻仅一个手机控制，第二个连接被拒绝。服务端只接受私有 IPv4、链路本地 IPv4 和回环地址，拒绝浏览器 Origin 请求。
- 路由器的访客隔离、AP 隔离、VPN 和广播屏蔽会影响发现；隔离导致无法互访时，手动 IP 也无法连接。
- PC 端以管理员权限运行，可以控制普通桌面上的管理员程序与模态窗口。背景变暗的 UAC 授权安全桌面、锁屏和登录界面仍不能控制，需要在电脑端确认。
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
```

真实输入测试前请先断开手机连接。测试会新建独立窗口，点击窗口内的输入区使其获得焦点后，验证中文、表情、多行、管理员模态窗口的鼠标与键盘输入、拖选复制和断线释放，结果位于测试输出目录的 `smoke-report.txt`。测试期间不要操作鼠标键盘。测试不连接真实手机；跨 Wi-Fi 的设备发现仍需真机验收。

输出文件在 `dist/`，不提交二进制产物。**请单独备份 `mobile/android/app/lantouchpad-release.jks` 和 `mobile/android/key.properties`**，后续 APK 更新必须使用同一密钥。这两个文件均已加入 Git 忽略，不能提交或公开。

## 协议

协议和手动联调示例见 [docs/protocol.md](docs/protocol.md)。APK、Windows 安装包和校验文件部署在服务器已有 HTTPS 静态目录中，不启动新的公开服务。
