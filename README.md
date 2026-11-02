# EXET

EXET 是一款原生 Windows 浏览器。窗口、标签页、地址栏、工具栏和设置页由 WinUI 3 原生控件绘制，网页内容由 WebView2 渲染。

```text
版本：1.1.1
平台：Windows 10 1809+ / Windows 11，x64
许可：MIT
```

## 功能

### 浏览与标签

- 水平标签与左侧垂直标签共享同一组页面状态
- 垂直标签可折叠为仅图标模式
- 标签切换、布局切换和面板展开带动画
- 无痕标签使用独立临时配置，不写历史、不保存密码、不加载扩展
- 会话恢复：可保存标签页与活动标签
- 标签音频状态显示，可直接静音/取消静音
- 网页加载时标签图标显示加载圆环

### 界面与外观

- 跟随系统、浅色、深色三种主题
- 夜间模式与增强网页夜间模式
- Mica、Mica Alt、Acrylic、Blur、Smoke、炫彩材质
- 背景透明度、主题色、界面字体、动画速度可调
- 地址栏聚焦描边、域名高亮、路径弱化
- 启动遮罩
- 收藏夹、历史记录、下载使用独立悬浮面板
- 悬浮面板支持固定为侧栏，固定后使用贴边布局

### 工具栏

- 主页、收藏、广告拦截、密码、收藏夹、扩展、下载、历史记录
- 翻译、代理、Ghost Downloader、Clash Meta 入口
- 按钮显示/隐藏、排序、右键隐藏到更多菜单
- 更多菜单集中显示被隐藏的按钮与扩展

### 地址栏与搜索

- 地址、搜索词统一输入
- 地址建议与历史建议
- 右键剪切、复制、粘贴、全选
- 选中文本后右键“在网页中搜索”
- 默认搜索引擎可选 Bing、百度、Google、DuckDuckGo、搜狗、Yandex 或自定义

### 翻译

- 网页整页翻译，翻译结果直接写回页面
- 目标语言：中文、English、日本語、한국어、Français、Deutsch、Español、Русский、العربية、Português、Italiano
- 源语言可选自动识别或指定语言
- 翻译引擎：DeepSeek API、Bing、MyMemory 兜底
- 翻译后可从 A 菜单显示原文或恢复译文
- DeepSeek API Key 在设置中配置

### 扩展

- 加载解压后的 Chromium 扩展
- 扩展启用、停用、删除、刷新
- 读取 `manifest.json` 的 popup 与图标
- 有 popup 的扩展可固定到工具栏
- 点击固定按钮在弹层中打开扩展 popup
- 未固定扩展集中在“更多 → 扩展”菜单

### 下载

- WebView2 原生下载
- 下载进度、速度、剩余时间、暂停/继续、取消
- 打开文件、打开所在位置、PDF 预览
- 删除下载记录
- Ghost Downloader 接入，可把下载任务交给 `ghd`
- Ghost 任务进度同步到下载面板

### 网络

- 跟随系统代理、直连、手动代理
- 内置 Clash Meta（Mihomo）内核
- 支持 Hysteria2、VLESS、Trojan 等节点
- 可配置 Clash YAML、混合端口、控制端口、API 密钥
- 启动/停止内核，代理模式一键切换

### 隐私与安全

- 广告与追踪请求拦截
- 阻止追踪器、禁用 JavaScript、清除退出 Cookie
- 密码保存与自动填充
- 敏感权限拒绝
- Do Not Track
- 无痕浏览

### 设置

- 常规、收藏夹、历史记录、下载、外观、广告拦截、隐私与安全、密码管理器、扩展、关于
- 设置项即时生效或按提示重启生效
- 工具栏按钮与顺序集中管理
- 默认浏览器注册与系统默认应用页面跳转

### 其他

- PDF 内嵌预览
- 网页截图
- 浏览器任务管理器
- 多语言界面：简体中文、English
- 默认主页使用 Litestart，也可设为任意 URL

## 系统要求

- Windows 10 1809（Build 17763）或更新版本
- Windows 11
- x64
- Microsoft Edge WebView2 Runtime
- 不支持 Windows 7 / 8 / 8.1、32 位系统和 ARM64 原生运行

## 安装

安装程序以管理员权限运行，安装到：

```text
C:\Program Files\EXET Browser
```

安装程序提供：

- 桌面快捷方式
- 开始菜单快捷方式
- 卸载程序
- WebView2 Runtime 检测与安装
- EXET 默认浏览器注册

Windows 不允许应用静默修改默认浏览器，最终确认在系统“默认应用”页面完成。

## 构建

需要 .NET 8 SDK、Windows App SDK 和 WebView2 Runtime。

```powershell
dotnet publish native\MicaBrowser.WinUI\MicaBrowser.WinUI.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:Platform=x64 -o .publish\EXET
```

安装包使用 Inno Setup 构建：

```text
.publish\installer\EXET.iss
```

打包前把 Mihomo 内核放到：

```text
.publish\EXET\Assets\Clash\verge-mihomo.exe
```

仓库不包含该二进制文件。

## 本地数据

```text
%LOCALAPPDATA%\EXET
```

包含设置、历史、收藏、密码、WebView2 配置、日志和扩展元数据。

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl+T` | 新建标签页 |
| `Ctrl+Shift+N` | 新建无痕标签 |
| `Ctrl+W` | 关闭标签 |
| `Ctrl+L` | 聚焦地址栏 |
| `Ctrl+R` / `F5` | 刷新 |
| `Ctrl+D` | 收藏当前页 |
| `Ctrl+J` | 下载 |
| `Ctrl+H` | 历史记录 |
| `Ctrl+Shift+S` | 设置 |
| `Ctrl+Shift+M` | 任务管理器 |
| `F11` | 全屏 |
| `F12` | 开发者工具 |

## 已知边界

- WebView2 不提供 Chromium 原生扩展工具栏，扩展按钮由 EXET 自建
- 扩展 popup 内的 `chrome.*` API 只覆盖 WebView2 支持的部分
- 只加载解压后的扩展，不直接安装 `.crx`
- Clash 节点连通性取决于配置文件与节点本身
- Ghost Downloader 需要本机安装 `ghd`
- 安装包未做商业代码签名，SmartScreen 可能提示未知发布者

## License

MIT License，详见 [LICENSE](LICENSE)。

Copyright (c) 2026 PorserX & EXET Team
