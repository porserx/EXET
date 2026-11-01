using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MicaBrowser.Services;

/// <summary>
/// Small, dependency-free UI localization layer. The Chinese strings are used
/// as stable keys so XAML remains readable and new English labels are explicit.
/// </summary>
public static class Localization
{
    public static bool English { get; set; }

    private static readonly Dictionary<string, string> EnglishText = new()
    {
        ["常规"] = "General",
        ["收藏夹管理"] = "Favorites",
        ["下载管理"] = "Downloads",
        ["外观"] = "Appearance",
        ["广告拦截"] = "Ad blocking",
        ["隐私与安全"] = "Privacy & security",
        ["密码管理器"] = "Passwords",
        ["扩展"] = "Extensions",
        ["关于"] = "About",
        ["浏览器的基本行为和布局。"] = "Basic browser behavior and layout.",
        ["保存主页设置"] = "Save home page",
        ["打开新标签页"] = "New tab",
        ["打开主页"] = "Home page",
        ["保存自定义搜索引擎"] = "Save custom search engine",
        ["搜索 URL（用 {query} 代表关键词）"] = "Search URL (use {query} for the term)",
        ["自定义"] = "Custom",
        ["开启"] = "On",
        ["关闭"] = "Off",
        ["增强"] = "Enhanced",
        ["跟随网站"] = "Follow site",
        ["图标"] = "Icons",
        ["完整"] = "Full",
        ["仅显示图标"] = "Icons only",
        ["收藏当前页"] = "Save page",
        ["下载"] = "Downloads",
        ["主页"] = "Home",
        ["收藏夹、历史记录和默认文件位置。"] = "Favorites, history and default file locations.",
        ["默认下载目录和下载前询问设置。"] = "Default download folder and save prompts.",
        ["选择外观、材质、颜色和动画速度。"] = "Choose the look, material, accent and motion.",
        ["在网页加载时挡住广告和跟踪脚本。"] = "Block ads and tracking scripts while pages load.",
        ["控制网站能不能跟踪你、使用权限，以及网页是否跟随夜间模式。"] = "Control tracking, site permissions and page dark mode.",
        ["保存网站账号，打开对应网站时可以自动填入。密码只保存在当前 Windows 用户下。"] = "Save site accounts and fill them when you return. Passwords stay with this Windows user.",
        ["加载本地扩展，并在普通标签页和窗口中生效。"] = "Load local extensions for normal tabs and windows.",
        ["版本信息、运行环境和项目说明"] = "Version, runtime and project information",
        ["默认搜索引擎"] = "Default search engine",
        ["在地址栏输入关键词时，用这个搜索引擎查找。"] = "Use this engine when you type a search in the address bar.",
        ["自定义搜索 URL"] = "Custom search URL",
        ["默认主页"] = "Home page",
        ["新标签页和主页按钮会打开它。默认是 Litestart。"] = "New tabs and the Home button open this page. Litestart is the default.",
        ["启动行为"] = "Startup",
        ["没有恢复标签页时，启动后先打开新标签页还是主页。"] = "When tabs are not restored, open a new tab or the home page.",
        ["关闭后重新打开时恢复标签页"] = "Restore tabs after closing",
        ["关闭窗口时保存标签，下次打开自动恢复。默认关闭。"] = "Save your tabs when the window closes and restore them next time. Off by default.",
        ["标签与性能"] = "Tabs & performance",
        ["垂直标签页"] = "Vertical tabs",
        ["在左侧显示标签页列表。"] = "Show the tab list on the left.",
        ["垂直标签栏宽度"] = "Vertical tab width",
        ["调整左侧标签栏宽度；仅影响显示完整标题的模式。"] = "Adjust the left rail width when full titles are shown.",
        ["垂直标签仅显示图标"] = "Show icons only",
        ["收起垂直标签栏，仅保留站点图标。"] = "Collapse the rail and keep site icons only.",
        ["后台标签页休眠"] = "Sleep background tabs",
        ["暂时不用的标签页会释放内存，切回来时自动恢复。"] = "Release memory from unused tabs and restore them when selected.",
        ["工具栏按钮"] = "Toolbar buttons",
        ["勾选要显示的按钮；隐藏的按钮会收进左侧展开菜单。"] = "Choose visible buttons. Hidden ones move into the expand menu.",
        ["收藏夹"] = "Favorites",
        ["默认收藏夹"] = "Default favorite folder",
        ["保存"] = "Save",
        ["数据管理"] = "Data management",
        ["清除全部浏览历史"] = "Clear all history",
        ["清除全部收藏"] = "Clear all favorites",
        ["默认下载目录"] = "Default download folder",
        ["选择文件夹"] = "Choose folder",
        ["下载前询问保存位置"] = "Ask where to save",
        ["请求级广告过滤"] = "Request-level ad filtering",
        ["已启用"] = "On",
        ["已停用"] = "Off",
        ["累计拦截"] = "Blocked total",
        ["域名规则"] = "Domain rules",
        ["路径规则"] = "Path rules",
        ["更新过滤规则"] = "Update filter rules",
        ["已暂停过滤的网站"] = "Sites with filtering paused",
        ["当前没有暂停过滤的网站。"] = "No sites have filtering paused.",
        ["应用主题"] = "Theme",
        ["选择浏览器的浅色或深色外观，也可以跟随 Windows。"] = "Choose light, dark or follow Windows.",
        ["夜间模式"] = "Night mode",
        ["让网页也使用深色显示，适合晚上使用。"] = "Use dark pages too, ideal at night.",
        ["界面材质"] = "Window material",
        ["选择窗口的模糊、玻璃或彩色效果，不同材质的可读性不同。"] = "Choose a blur, glass or color effect for the window.",
        ["Acrylic 自定义颜色"] = "Acrylic color",
        ["调色盘…"] = "Color picker…",
        ["背景透明度"] = "Background transparency",
        ["数值越大，背景越透明；Blur 固定使用更清晰的不透明度。"] = "Higher values are more transparent. Blur stays clearer for readability.",
        ["主题色"] = "Accent color",
        ["按钮、选中标签和强调文字使用的颜色。"] = "Color used for buttons, selected tabs and highlights.",
        ["界面动画速度"] = "Animation speed",
        ["数字越大动画越慢；0 表示关闭动画。"] = "Higher is slower. Set 0 to turn animations off.",
        ["跟随系统"] = "Follow system",
        ["浅色"] = "Light",
        ["深色"] = "Dark",
        ["云母"] = "Mica",
        ["云母 Alt"] = "Mica Alt",
        ["炫彩"] = "Rainbow",
        ["标志蓝"] = "Brand blue",
        ["紫色"] = "Purple",
        ["蓝色"] = "Blue",
        ["青色"] = "Teal",
        ["绿色"] = "Green",
        ["橙色"] = "Orange",
        ["红色"] = "Red",
        ["粉色"] = "Pink",
        ["自动填充登录信息"] = "Autofill logins",
        ["添加密码"] = "Add password",
        ["生成密码"] = "Generate password",
        ["刷新"] = "Refresh",
        ["复制"] = "Copy",
        ["编辑"] = "Edit",
        ["删除"] = "Delete",
        ["安全说明"] = "Security notes",
        ["兼容性说明"] = "Compatibility",
        ["加载解压扩展…"] = "Load unpacked extension…",
        ["安装内置示例"] = "Install built-in sample",
        ["打开扩展目录"] = "Open extensions folder",
        ["作者：PorserX & EXET Team"] = "By PorserX & EXET Team",
        ["这个版本能做什么"] = "What is in this version",
        ["运行环境"] = "Runtime",
        ["打开数据目录"] = "Open data folder",
        ["复制环境信息"] = "Copy environment info",
        ["说明"] = "Notes",
        ["界面语言"] = "Interface language",
        ["选择 EXET 的界面语言。更改后重新打开浏览器生效。"] = "Choose the EXET interface language. Restart the browser to apply changes.",
        ["简体中文"] = "Simplified Chinese",
        ["更多工具栏按钮"] = "More toolbar buttons",
        ["密码"] = "Passwords",
        ["搜索网页或输入网址"] = "Search or enter address",
        ["标签页"] = "Tabs",
        ["新建标签页"] = "New tab",
        ["新标签页"] = "New tab",
        ["无痕标签页"] = "New InPrivate tab",
        ["新建无痕标签页"] = "New InPrivate tab",
        ["新建窗口"] = "New window",
        ["缩放"] = "Zoom",
        ["历史记录"] = "History",
        ["下载内容"] = "Downloads",
        ["放大 10%"] = "Zoom in 10%",
        ["缩小 10%"] = "Zoom out 10%",
        ["重置为 100%"] = "Reset to 100%",
        ["打开 PDF 文件…"] = "Open PDF file…",
        ["浏览器任务管理器"] = "Browser task manager",
        ["设置"] = "Settings",
        ["开发者工具"] = "Developer tools",
        ["退出 EXET"] = "Exit EXET",
        ["浏览器的基础行为和布局。"] = "Basic browser behavior and layout.",
        ["下载内容 (Ctrl+J)"] = "Downloads (Ctrl+J)",
        ["关闭侧栏"] = "Close sidebar",
        ["关闭标签页"] = "Close tab",
        ["删除收藏"] = "Remove favorite",
        ["删除文件夹"] = "Delete folder",
        ["删除记录"] = "Delete entry",
        ["刷新 (F5)"] = "Refresh (F5)",
        ["前进 (Alt+右箭头)"] = "Forward (Alt+Right)",
        ["后退 (Alt+左箭头)"] = "Back (Alt+Left)",
        ["取消"] = "Cancel",
        ["启用"] = "Enable",
        ["停用"] = "Disable",
        ["最大化"] = "Maximize",
        ["最小化"] = "Minimize",
        ["新建标签页 (Alt+Home)"] = "New tab (Alt+Home)",
        ["新建文件夹"] = "New folder",
        ["新建收藏夹"] = "New favorite folder",
        ["重命名"] = "Rename",
        ["编辑收藏"] = "Edit favorite",
        ["设为默认浏览器"] = "Set as default browser",
        ["设置及其他"] = "Settings and more",
        ["保存下载设置"] = "Save download settings",
        ["打开位置"] = "Open location",
        ["在资源管理器中显示"] = "Show in File Explorer",
        ["保存到…"] = "Save to…",
        ["直接保存"] = "Save directly",
        ["询问"] = "Ask",
        ["关闭浏览器后清除网站 Cookie 和登录状态。"] = "Clear cookies and sign-ins when the browser closes.",
        ["关闭表单自动填充、地址和密码自动保存提示。"] = "Disable form autofill and password save prompts.",
        ["发送 Do Not Track 请求"] = "Send Do Not Track",
        ["向网站请求不要进行跨站跟踪。"] = "Ask sites not to track you across sites.",
        ["启动遮罩动画"] = "Startup mask animation",
        ["开启后启动时先播放 1.2 秒遮罩，再显示浏览器界面。"] = "Play a 1.2s mask before showing the browser.",
        ["半透明窗口"] = "Translucent window",
        ["系统在关闭透明效果、节电模式或硬件不支持时会自动改用纯色背景，这是 Windows 的行为。"] = "Windows falls back to a solid background when transparency is off or unsupported.",
        ["界面字体"] = "Interface font",
        ["字体粗细"] = "Font weight",
        ["常规 400"] = "Regular 400",
        ["中等 500"] = "Medium 500",
        ["半粗 600"] = "Semibold 600",
        ["粗体 700"] = "Bold 700",
        ["细体 300"] = "Light 300",
        ["MiSans 默认"] = "MiSans default",
        ["亚克力"] = "Acrylic",
        ["细亚克力"] = "Acrylic thin",
        ["Mica / Acrylic 材质"] = "Mica / Acrylic materials",
        ["原生界面"] = "Native UI",
        ["Windows 原生浏览器"] = "Native Windows browser",
        ["WebView2 内核"] = "WebView2 engine",
        ["网页内核：正在读取…"] = "Web engine: loading…",
        ["数据目录：正在读取…"] = "Data folder: loading…",
        ["广告过滤规则：正在读取…"] = "Ad filter rules: loading…",
        ["清除全部历史"] = "Clear all history",
        ["清除历史记录和收藏夹数据。操作无法撤销。"] = "Clear history and favorites. This cannot be undone.",
        ["过滤规则"] = "Filter rules",
        ["阻止第三方跟踪器"] = "Block third-party trackers",
        ["自动拒绝敏感权限"] = "Auto-deny sensitive permissions",
        ["摄像头、麦克风、定位和通知请求直接拒绝，不再弹窗。"] = "Deny camera, microphone, location and notification requests without prompts.",
        ["禁用 JavaScript"] = "Disable JavaScript",
        ["增强网页夜间模式"] = "Enhanced page dark mode",
        ["对未适配深色模式的网页强制使用深色样式，部分图片可能被反色。"] = "Force dark styles on pages without dark mode; some images may invert.",
        ["点击文件夹查看其中的收藏网页。"] = "Open a folder to see its favorites.",
        ["收藏内容"] = "Favorites",
        ["收藏当前页面"] = "Save current page",
        ["收藏此页 (Ctrl+D)"] = "Save page (Ctrl+D)",
        ["收藏网页时先保存到默认收藏夹，随后可以在弹出的小窗口中更改或新建收藏夹。"] = "Save to the default folder first; you can change or create a folder afterward.",
        ["更改收藏夹分类"] = "Change favorite folder",
        ["返回分类"] = "Back to folders",
        ["←  返回上一级"] = "←  Back",
        ["新建分类"] = "New category",
        ["最近浏览"] = "Recently visited",
        ["还没有浏览记录"] = "No history yet",
        ["查看和管理最近浏览过的网页"] = "View and manage recent pages",
        ["选择文件夹…"] = "Choose folder…",
        ["展开隐藏的工具栏按钮"] = "Show hidden toolbar buttons",
        ["折叠或展开设置导航"] = "Collapse or expand settings navigation",
        ["按钮顺序"] = "Button order",
        ["多标签 / 垂直标签"] = "Tabs / vertical tabs",
        ["水平、竖向与无痕模式"] = "Horizontal, vertical and private modes",
        ["仅显示标签图标"] = "Icons only",
        ["启动、网络与浏览器标识"] = "Startup, network & browser ID",
        ["预启动"] = "Preload",
        ["开机预启动"] = "Preload at sign-in",
        ["登录 Windows 后后台预热 EXET；之后点图标只唤醒已有进程，启动更快。"] = "Warm up EXET in the background after sign-in. Clicking the icon then only wakes the existing process.",
        ["网络代理"] = "Network proxy",
        ["直连（不使用代理）"] = "Direct (no proxy)",
        ["手动代理"] = "Manual proxy",
        ["手动代理地址"] = "Proxy address",
        ["不走代理的地址"] = "Bypass list",
        ["修改后重启 EXET 生效。系统代理异常时可切到“直连”。"] = "Restart EXET to apply. Use Direct if the system proxy is broken.",
        ["浏览器标识"] = "Browser identity",
        ["切换后对新请求生效；已打开的页面刷新一次即可。"] = "Applies to new requests; refresh open pages once.",
        ["电脑（桌面）"] = "Desktop",
        ["手机"] = "Mobile",
        ["平板"] = "Tablet",
        ["自定义 User-Agent"] = "Custom User-Agent",
        ["关闭设置"] = "Close settings",
        ["全部"] = "All",
        ["最近关闭"] = "Recently closed",
        ["搜索"] = "Search",
        ["搜索历史记录"] = "Search history",
        ["清除历史记录"] = "Clear history",
        ["固定为侧栏"] = "Dock to sidebar",
        ["取消固定"] = "Undock",
        ["在侧栏打开"] = "Open in sidebar",
        ["更多设置"] = "More settings",
        ["后退"] = "Back",
        ["前进"] = "Forward",
        ["打开"] = "Open",
        ["安装示例扩展"] = "Install sample extension",
        ["刷新扩展"] = "Refresh extensions",
        ["打开扩展文件夹"] = "Open extensions folder",
        ["版本"] = "Version",
        ["网页内核"] = "Web engine",
        ["数据目录"] = "Data folder",
        ["广告过滤规则"] = "Ad filter rules",
        ["条"] = "rules",
        ["更多"] = "More",
        ["标签页布局和本次记录"] = "Tab layout and recent tabs"
    };

    public static string T(string text)
    {
        if (!English)
        {
            return text;
        }

        if (text.StartsWith("缩放  ", StringComparison.Ordinal))
        {
            return "Zoom  " + text[4..];
        }

        return EnglishText.TryGetValue(text, out var translated) ? translated : text;
    }

    public static void Apply(DependencyObject root)
    {
        if (!English || root is null)
        {
            return;
        }

        ApplyElement(root);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            Apply(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static void ApplyElement(DependencyObject element)
    {
        try
        {
            switch (element)
            {
                case TextBlock textBlock:
                    textBlock.Text = T(textBlock.Text);
                    break;
                case TextBox textBox when textBox.Header is string header:
                    textBox.Header = T(header);
                    break;
                case ToggleSwitch toggle:
                    toggle.OnContent = T(toggle.OnContent?.ToString() ?? string.Empty);
                    toggle.OffContent = T(toggle.OffContent?.ToString() ?? string.Empty);
                    break;
                case ContentControl contentControl when contentControl.Content is string content:
                    contentControl.Content = T(content);
                    break;
            }

            if (ToolTipService.GetToolTip(element) is string tip)
            {
                ToolTipService.SetToolTip(element, T(tip));
            }
        }
        catch
        {
            // Localization is best effort; never let a label block startup.
        }
    }
}
