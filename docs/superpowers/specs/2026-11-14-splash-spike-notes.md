# Splash spike: loading the main window's page before the window is seen

**Date:** 2026-11-14/15
**For:** `2026-11-14-splash-about-design.md` §5.5, plan Task 8
**Measured on:** the owner's PC, Release build, unpackaged, started from a terminal

## Result

**Option 2 is used, in the form of a cloaked window.** `MainWindow.Preload()` shows the main window with
`DWMWA_CLOAK` set; `MainWindow.Reveal(activate)` uncloaks it. The web UI reported ready about 0.5 s after the preload
began, with nothing drawn on screen.

## What was tried

| Option | What was done | Observed |
|---|---|---|
| 1. Handle only | `EnsureHandle()`, then `ApplyTemplate`/`Measure`/`Arrange`/`UpdateLayout`, then the WebView start called directly | Does not work. The WebView control had no visual parent and no `PresentationSource`, and its HWND stayed 0: WPF only attaches a window's content to its source when the window is shown. `EnsureCoreWebView2Async` waited, and finished 0.33 s after the window was shown 6 s later. |
| 2. Shown but cloaked | `EnsureHandle()`, `DwmSetWindowAttribute(DWMWA_CLOAK, 1)`, `ShowActivated = false`, `Show()` | Works. WebView2 core created 0.24 s after the preload began, navigation completed at 0.30 s, `app.ready` received at 0.36–0.48 s. The window reported visible and cloaked, and the foreground window did not change. |
| 3. Shown underneath the splash | Not needed | It is still the fallback inside `Preload()`: if the compositor refuses to cloak, the window is simply visible. |

## Things Task 9 must know

- **A cloaked window has a taskbar button.** The taskbar showed a "Murmur" button while the window was cloaked (checked
  through UI Automation; no button when Murmur was not running). That is what any starting app looks like, so it is
  kept. A click on it activates the cloaked window, which shows nothing: treat the main window's `Activated` event
  during the splash as a skip.
- **`Reveal(false)` leaves the foreground alone.** Measured: the foreground process was the same before and after.
- **`Reveal(true)` could not be judged from the test.** The test started Murmur from a script behind a terminal, so
  Murmur never had the foreground and Windows would not hand it over. In real use the splash takes the foreground at
  launch, and the rule in §5.3 only activates when Murmur still has it, which is the case Windows allows. This needs
  the by-hand check in Task 10 (and the owner's eyes).
- **`ShowMainWindow` now goes through `Reveal(true)`**, so no path can leave the window shown but cloaked.
- **The gap the splash covers is short on this PC:** about 0.45 s from showing the window to the first page. The
  2.5 s minimum, not the page, decides how long the splash stays here.
- `WebViewHandler.StartAsync()` is now idempotent. It is still started by the control's `Loaded` event; nothing calls
  it early, because option 1 was the only one that needed to.
