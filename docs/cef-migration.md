# EXET: WebView2 → CEF Chromium migration

## Status

- Current shipping line: 1.1.x, WebView2.
- Target: embedded CEF (Chromium Embedded Framework), no dependency on the installed Edge/WebView2 runtime.
- Chosen route: **CefSharp + native child HWND**, not off-screen bitmap composition.
- The 1.1.x line stays usable while the CEF line is developed separately.

## Why not a drop-in replacement

`CoreWebView2` is used across the browser core:

- 163 API references in 36 source files (excluding `obj/`).
- Main areas: `MainWindow.Web.cs` (33), `Services/BrowserEnvironment.cs` (12), `MainWindow.Exet.cs` (7), `Models/ExtensionInfo.cs`, `Services/ExtensionService.cs`, tab lifecycle, downloads, permissions, screenshots, PDF, DevTools, session restore.

CEF has equivalent capabilities, but not the same API shape. Every call site needs an adapter or rewrite.

## Architecture decision

### Hosting

CefSharp's WinForms `ChromiumWebBrowser` creates a native child HWND. In WinUI 3 there is no supported `WindowsFormsHost`, so:

1. Initialize CEF once with a multi-threaded message loop.
2. Create the browser as a native child window.
3. Reparent it to the EXET main HWND with `SetParent`.
4. Position it over the `BrowserHost` rectangle with `SetWindowPos`.
5. Move/resize it on `BrowserHost.SizeChanged`, tab switch, sidebar open/close, and window state changes.

This preserves GPU rendering and avoids copying frames through a bitmap.

Known constraint: native child HWNDs are airspace regions. XAML overlays cannot draw on top of the page. Existing overlays (find bar, download shelf, side panel) must either:

- be moved into the browser chrome outside the page rectangle, or
- use a separate layered popup window, or
- temporarily hide/resize the CEF child while the overlay is open.

### Isolation

Create an `IBrowserEngine` boundary before replacing call sites:

```csharp
interface IBrowserEngine
{
    event EventHandler<EngineNavigationEventArgs> NavigationCompleted;
    event EventHandler<EngineDownloadEventArgs> DownloadStarted;
    Task NavigateAsync(string url);
    Task<string> ExecuteScriptAsync(string script);
    void Reload();
    void Stop();
    void SetVisible(bool visible);
    void Resize(int x, int y, int width, int height);
    void Dispose();
}
```

`WebView2Engine` wraps the current implementation. `CefEngine` implements the same contract. UI code depends only on `IBrowserEngine`.

## Migration phases

### Phase 0 — Audit and adapter (current)

- Inventory every `CoreWebView2` call.
- Define `IBrowserEngine`, `IBrowserTab`, download/navigation event DTOs.
- Keep WebView2 implementation as default.
- Acceptance: current 1.1.x builds and runs unchanged.

### Phase 1 — CEF spike

- Add `CefSharp.Common` + `CefSharp.WinForms` x64 packages.
- Initialize CEF in `App`.
- Open one hard-coded CEF browser in a probe window.
- Verify: Google loads, input works, resize works, no crash on close.
- Acceptance: 10-minute browsing session without crash; GPU rendering active.

### Phase 2 — Tab host

- Replace `WebView2` in `BrowserTab` with `IBrowserEngine`.
- One CEF browser per tab; hide inactive browsers.
- Reparent/resize on tab switch and window resize.
- Acceptance: create/close/switch 20 tabs; no orphan HWNDs; memory returns after close.

### Phase 3 — Navigation and chrome

- Address bar, back/forward/reload, title/favicon, new-window handling.
- Zoom, find-in-page, print, DevTools.
- Acceptance: parity checklist for all toolbar actions.

### Phase 4 — Downloads

- `IDownloadHandler`, progress, pause/resume/cancel, open/reveal, history persistence.
- Acceptance: large file download, cancel, resume, restart app, history preserved.

### Phase 5 — Permissions, profile, cookies

- Permission handler mapping to EXET settings.
- Cache/cookie/profile paths; clear-on-exit; private tabs.
- Acceptance: camera/mic/geolocation prompts; cookies persist; private tab isolation.

### Phase 6 — Extensions and advanced features

- CEF extension loading (Chrome extension subset), PDF, screenshot, session restore.
- Acceptance: feature parity matrix signed off.

### Phase 7 — Packaging

- Bundle CEF binaries (`libcef.dll`, locales, resources, `CefSharp.dll`, VC++ runtime).
- Installer size grows by roughly 250–350 MB.
- Code signing for all shipped binaries.
- Acceptance: clean VM install, no Edge/WebView2 runtime present, browser launches.

## Risks

- WinUI 3 airspace: page will cover XAML overlays; side panel must be outside the browser rectangle or a separate window.
- CEF binary size and update cadence.
- Extension compatibility is not 100% with Chrome/Edge.
- Some WebView2-only features (e.g. `CapturePreviewAsync`, built-in PDF viewer behavior) need replacements.
- Crash isolation and GPU process management need explicit handling.

## Phase 1 probe result (verified)

`native/EXET.CefProbe` now loads a real page with CefSharp 154.0.340:

```text
CEF_INIT_OK
CEF_OK=True
CEF_URL=https://example.com/
CEF_TITLE=Example Domain
exit=0
```

Environment findings:

- Must use `CefSharp.OffScreen.NETCore`, not `CefSharp.OffScreen` (the latter is .NET Framework only).
- `CefSharp.BrowserSubprocess.exe` targets .NET 6. On a machine without the system runtime it shows a runtime dialog. The probe runs with `DOTNET_ROOT` pointing at the bundled SDK and `DOTNET_ROLL_FORWARD=LatestMajor`.
- Shipping CEF must either publish a self-contained browser subprocess or install/embed the required Desktop Runtime.
- Probe needs `no-sandbox`, `disable-gpu`, `disable-software-rasterizer` in this environment to avoid GPU/network subprocess crashes.
- CEF output must explicitly copy `libcef.dll`, `icudtl.dat`, `*.pak`, `locales`, and the CefSharp subprocess files; package targets did not copy all of them for this SDK-style project.

## Phase 2 windowed host result (verified)

`native/EXET.CefWinFormsProbe` hosts `CefSharp.WinForms.ChromiumWebBrowser` in a real native window.

Verified:

- Window title: `EXET CEF Host Probe`
- The rendered page showed the full `example.com` content in multiple languages and the `Learn more` link
- CEF browser subprocesses were running
- Window size: 1082x750
- Input/selection is native CEF, not a bitmap copy

This validates the windowed CEF path. It does not yet validate WinUI 3 reparenting; that is the next gate.

## WinUI hosting findings

Two blockers were isolated:

1. Any XAML in a project that references CefSharp makes `XamlCompiler.exe` exit 1 with no diagnostic. This happens with `App.xaml` alone, not just `MainWindow.xaml`.
2. A no-XAML WinUI 3 app builds, but crashes at startup with `0xC000027B` inside `Microsoft.UI.Xaml.dll`. CEF initialization logs successfully before the crash, so the failure is in the WinUI bootstrap, not CEF.

The CEF WinForms window remains the only verified interactive host. WinUI 3 hosting needs either:
- a separate process hosting CEF with an HWND bridge, or
- a CEF integration that does not place CefSharp references in the XAML-compiled project.

## Immediate next step

Phase 0: add the engine abstraction and keep WebView2 as the only implementation. Do not add CEF packages to the shipping project until the probe passes.
