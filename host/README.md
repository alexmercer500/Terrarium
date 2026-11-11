# Terrarium Windows 宿主

以 C# / .NET Framework 和 WebView2 把主题页面挂到 Windows 桌面图标层下方。

## 构建与运行

```powershell
powershell -ExecutionPolicy Bypass -File host\build.ps1
host\bin\Terrarium.exe
```

构建脚本使用 Windows 自带的 C# 编译器，首次从 NuGet 下载固定版本的 WebView2 开发库。依赖保存在 `host/lib/`，输出位于 `host/bin/`；二者均不提交到仓库。

## 常用参数

| 参数 | 行为 |
| --- | --- |
| 无参数 | 启动壁纸并打开设置；已运行时打开设置 |
| `--autostart` | 后台启动，不弹设置 |
| `--settings` | 打开设置 |
| `--menu` | 打开托盘菜单 |
| `--exit` | 正常退出，恢复系统壁纸 |
| `--exit-installed` | 安装器使用：仅退出使用当前可执行文件路径的实例 |

## 行为与数据

- 主题来自 `prototype/themes.json`，仅加载状态为 `ready` 的主题。
- 每 400ms 检查桌面图标位置，有变化才通知主题页面。
- 前台窗口最大化或全屏时暂停；壁纸启动不抢占前台，自身、不可见及 cloaked 窗口不会误触发暂停；托盘可手动暂停与继续。设置页按程序版本刷新缓存，缩略图使用内嵌图片和真实 `<img>`。
- Windows 11 24H2 及以后使用 Progman 分层子窗口；较早系统使用 WorkerW。
- 当前仅覆盖主显示器；系统壁纸幻灯片或 Windows 聚焦换图时可能短暂闪烁。
- 设置保存到 `%LOCALAPPDATA%\Terrarium\settings.json`，日志为同目录下的 `host.log`。
- 不提供账号登录、购买恢复或自动检查更新；不会启动时或打开设置时联网检查新版本。
- 分享只使用图标位置，主题页面会画示例图标和名字色块，不截取桌面窗口。
- 主题页面改动后，使用托盘“重新载入”；宿主 C# 改动后需要退出并重新构建。

安装测试可设置绝对路径环境变量 `DESKLAND_PROFILE_DIR`，隔离设置、WebView2 缓存和单实例事件；此模式跳过旧数据迁移，并禁用开机启动写入。正常启动不设置此变量，继续使用原数据目录。

## 页面通信

设置页发送 `hello`、`set`、`reset`、`theme`、`lang`、`autostart`、分享相关消息；宿主发送初始化设置、图标矩形和分享结果。

主题页发送 `ready` 后，宿主依次传递设置和图标位置；随后传递参数变化、图标变化和暂停状态。设置与主题页面统一从本地虚拟域名加载。
