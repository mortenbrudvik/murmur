# Murmur — Splash and About dialog (design)

**Date:** 2026-11-14
**Status:** Approved 2026-11-14
**Builds on:** `2026-10-29-murmur-design.md`, `2026-10-30-first-run-setup-design.md`; the "Hum" icon (commit `75b3043`)
**Mockup:** `docs/superpowers/specs/mockups/splash-about.html` — the animated splash and the About dialog. The owner
approved the look on 2026-11-14, including his name in the footer linking to his home page.

---

## 1. Intent and why

Murmur has a new icon with an idea in it: a signal line that rises into a lowercase m. The owner asked for a creative
splash and an About dialog. Both carry that idea further, so the app feels like one piece of work:

- **Splash:** a speech waveform crosses a small window, calms down and folds into the m; the tile lights up and the
  name is typed out, the way Murmur types for its user.
- **About:** says what is running and where the user's words go, with the icon's line humming across its header.

The splash also covers a real gap: the main window's WebView only starts when the window is first shown, so today a
manual launch opens an empty window before the page appears.

**Success:** the owner opens Murmur and sees the animation in the mock-up, then the main window already drawn; he
opens About from the sidebar or the tray and every value in it is true for his PC.

## 2. What exists today

- No splash and no About. The tray menu has "Open Murmur" and "Quit" (`TrayIconHost.Show`). The sidebar has History,
  Vocabulary and Settings (`Sidebar.tsx`).
- `App.Start` shows the main window at the end of start-up unless it is a sign-in start (`IsSignInStart`). The window
  is a WPF-UI `FluentWindow`, 900 by 620, centred, Mica backdrop.
- The web UI can only have fixed links opened: it names a `LinkTarget` and `AppLinks.For` gives the address.
- The engine's model name, runtime and graphics card already reach the UI (`EngineStatus.modelName`, `.runtime`,
  `.gpu.gpuName`).
- The history is held in memory once read (`HistoryStore`, from `history.jsonl`).
- **The app does not know its own version.** `version.json` (1.1.0) is read only by `build/build-msix.ps1`; no
  project sets `Version`, so the assembly says 1.0.0.

## 3. Goals

1. A native splash, as in the mock-up, on a launch that opens the window (§5).
2. An About dialog in the main window, as in the mock-up, opened from the sidebar and the tray (§6).
3. One version source: the assembly version comes from `version.json` (§7).

## 4. Non-goals and anti-goals

**Non-goals:** an update check; a "what's new" list; third-party notices; changing the tray icons or the pill; the
Store listing; localisation (the UI is English).

**Anti-goals (failure even if it "works"):**
- A splash the user must sit through. A click or any key closes it at once (§5.3), and it never waits for the speech
  model.
- A splash at sign-in, or when the window is reopened from the tray. Murmur starting with Windows stays silent.
- A splash that can get stuck. It always closes within the timeout in §5.3, whatever the web UI does.
- A splash that takes focus from the app the user is typing in after it has closed, or that delays the hotkey: hooks
  and the engine start exactly as they do today.
- About showing a value that is not true (a made-up version, a graphics card the engine is not using).
- Letting the web UI open an arbitrary address or path. The new links are fixed `LinkTarget`s like the existing ones.

## 5. Splash

### 5.1 Look and motion (mockup, "Splash")

A borderless window, 460 by 280 DIPs, background `#14161D`, 12 DIP corner radius, 1 DIP border `#1AFFFFFF`, drop
shadow, centred on the screen the main window will open on. Not in the taskbar, topmost while it is up.

The drawing uses the icon's own geometry (`murmur-icon.svg`, 64-unit grid) with the tile 96 DIPs wide, centred
horizontally, top at 46; the line's flat ends are extended to the window's edges. Timeline, in seconds from showing:

| From | What happens |
|---|---|
| 0.0 | The line spans the window as a speech-like waveform (about 21 DIPs peak), in the periwinkle-to-violet gradient `#8FB3FF` to `#B59BFF`, fading out at both window edges. |
| 0.3 – 0.6 | The status line (§5.4) fades in near the bottom, and the version in the bottom right corner, small and dim. Both stay until the splash closes. |
| 0.9 – 1.65 | The waveform dies down while every point of the line moves to its place on the icon's line (ease in-out). |
| 1.45 – 1.9 | The tile (gradient `#56A0FF`, `#7A78FF`, `#AE62F2`) fades in and grows from 90 % to 100 %, with a soft glow behind it. Inside the tile the line turns white; outside it dims to about 40 %. |
| 1.75 – 2.2 | "Murmur" is typed below the tile, one letter every 85 ms, with a caret in the accent colour. After the last letter the caret blinks until the splash closes, or for about a second on a slow start. |

**Changed while building (2026-11-15), both from measurements on the owner's PC:**

- *The typing starts 0.2 s earlier than in the mock-up (1.75 s, not 1.95 s).* With the mock-up's timing the last
  letter appeared at 2.375 s and the splash could close at 2.5 s, so the finished name was on screen for about a
  tenth of a second. It now stands for a third of a second.
- *The timeline counts from when start-up is done, not from when the splash appears.* Showing the splash takes about
  0.5 s, and the rest of start-up then keeps the UI thread busy for about 0.4 s more, during which nothing can
  animate. So the splash is drawn at once, at rest (the line lying flat), start-up goes on behind it, and the
  animation and the 2.5 s minimum of §5.3 begin when start-up has finished. The splash is therefore on screen for
  about 3 s in all: roughly half a second at rest, then the 2.5 s. Counting from when it appears instead would hide
  the animation's opening and close the splash before its name was typed.

**One difference from the mock-up (approved by the owner):** in the mock-up the status line appears only
at 2.5 s and the sequence runs to 4.3 s. With the 2.5 s minimum of §5.3 the splash would usually close just as the
status line began to show, so it would almost never be seen. Here it is on screen from 0.3 s instead, and the minimum
stays 2.5 s. The final frame is the same as the mock-up's.

Apart from that, the mock-up's script (`makeSplash`, `hum`) is the reference for the curves and timings; matching it
to the eye is enough, exact maths is not required. When Windows has animations turned off
(`SystemParameters.ClientAreaAnimation` is false) the splash shows its final frame from the start.

### 5.2 When it shows

On every start where `App.Start` goes on to show the main window: a manual launch, a restart from Settings, and the
first run (the splash is followed by the setup page). **Not** on a sign-in start, not for a second launch that only
signals the running instance, and not when the window is opened later from the tray.

It is shown as early in `Start` as the single-instance check allows (after step 3, before the container is built), so
it is on screen while the rest of start-up runs. It must not become `Application.MainWindow`.

### 5.3 When it closes

It closes when **both** are true: the web UI has drawn its first page, and 2.5 s have passed since its animation
began (the owner accepted this minimum so the name finishes typing; see the note in §5.1 on when the animation
begins). The 8 s timeout below counts from the same moment. Then the main window is shown and the splash fades out over
about 150 ms.

- **Focus:** the splash takes the foreground when it appears, as the main window does today. When it closes, the main
  window is activated only if Murmur still has the foreground. If the user has gone to another app in the meantime, the
  main window is shown without being activated, so a dictation already under way is not pasted into Murmur. (My call.)
  The exception is a window that was asked for while the splash was up (the tray icon, a second launch): it comes to
  the front, because the app in front is then only the taskbar or whatever started the second launch. (Added after
  the code review, 2026-11-15.)

- **Ready signal:** the React app calls a new IPC request (`app` / `ready`) once, after its first real render (the
  setup page or the shell, not the `null` it returns while the setup state loads).
- **Skip:** a click on the splash, or any key while it has focus, closes it at once and shows the main window, drawn
  or not.
- **Timeout:** 8 s after its animation began it closes regardless and the main window is shown. In a Debug build with the
  Vite server down, this is the path taken.
- **Quit** from the tray while it is up closes it with everything else.

The decision "close now?" is a small class of its own with no WPF in it (inputs: ready, skipped, elapsed; uses
`TimeProvider`), so it can be unit tested.

### 5.4 Status line

The speech engine's current status, in the wording the UI already uses (`engineStatusText.ts`): "Loading model…" with
three pulsing dots, "Downloading speech model… 38%", "Ready · CUDA" with a green dot, or the error text in the error
colour. It follows `ISpeechEngine.StatusChanged`. The splash never waits for it.

### 5.5 The main window during the splash — to be settled by a spike

The WebView must load while the splash is up, but the WebView2 control only starts once its window exists on screen.
The plan starts with a short spike to find the least visible way to do that, in this order of preference:

1. Create the main window's handle and start the WebView without showing the window (`EnsureHandle`, then
   `EnsureCoreWebView2Async`), and show it only when the splash closes.
2. If that does not start the WebView: show the main window off-screen or fully transparent, then move or reveal it.
3. If neither works cleanly with `FluentWindow` and Mica: show the main window normally underneath the topmost
   splash. The gap is then still visible around the splash, which is acceptable but the least good outcome.

Whichever is used, the window is centred as today, and the focus rule in §5.3 must hold: the spike checks that the
chosen way can show the main window both activated and not activated.

## 6. About dialog

### 6.1 Where it lives and how it opens

A React modal dialog over the current page, in the existing UI. Opened by:

- a new **About** item at the foot of the sidebar (pushed down by a spacer; in the narrow layout it is the last item in
  the row). It is a button that opens the dialog, not a page: `Page` does not change.
- a new **About Murmur** item in the tray menu, between "Open Murmur" and the separator. It shows the main window and
  opens the dialog, also during first-run setup (where the sidebar is hidden) and when the window has never been shown
  before. The builder chooses the mechanism (a push event plus a one-shot flag for a UI that is not loaded yet, in the
  manner of `LaunchOptions.OpenPage`).

Close with the Close button, Esc, or a click on the dimmed area. Focus goes to Close when it opens, stays inside the
dialog while it is open, and returns to the sidebar item (when that is what opened it) on close.
`role="dialog"`, `aria-modal`, labelled by the name.

### 6.2 Content (mockup, "About"), top to bottom

| Part | Shows | Source |
|---|---|---|
| Header | The tile, 64 px, centred, with the line running the dialog's width and moving slowly (a few pixels) outside the tile | Static art plus a small animation; stopped when closed or with `prefers-reduced-motion` |
| Name | "Murmur" | Fixed |
| Version line | "Version 1.1.0 · Microsoft Store" or "Version 1.1.0 · Built from source" | Assembly version (§7); `IPackageIdentity.IsPackaged` |
| Tagline | "Hold a key, talk, and the words land where you were typing. Nothing you say leaves this PC." | Fixed |
| Speech model | "Whisper large-v3-turbo" | `EngineStatus.modelName`; "Not chosen yet" while empty |
| Running on | "NVIDIA GeForce RTX 3090 · CUDA" | §6.2.1 |
| Dictated so far | "18,420 words" and below it "in 1,204 dictations since October 2026" | §6.3 |
| Your data | The data folder's real path, and an "Open folder" link | `MurmurPaths` root; opens in Explorer |
| Links | Source on GitHub · Privacy policy · MIT License | `https://github.com/mortenbrudvik/murmur`, `https://brudvik.com/murmur/privacy`, the repo's `LICENSE` on GitHub |
| Footer | "© 2026 Morten Brudvik" with the name linking to `https://brudvik.com`; buttons "Copy details" and "Close" | Fixed; the year is the build's |

The model and runtime rows follow engine status changes while the dialog is open.

#### 6.2.1 "Running on" (my call)

`EngineStatus.gpu` is the CUDA tip, not the device in use: it names a card only when a CUDA-capable NVIDIA card was
found (`GpuAdvisor.Decide`), and on Vulkan nothing confirms which card is doing the work. So the card's name is shown
only when it is certain:

| Engine | Row | "Runtime:" line in Copy details |
|---|---|---|
| Ready on CUDA | card name · CUDA | CUDA, card name |
| Ready on Vulkan | Vulkan | Vulkan |
| Ready on the processor | CPU | CPU |
| Downloading or loading | Starting… | Starting |
| Error | Not running | Not running |

Finding out which card Vulkan uses would need new work in the engine and is out of scope.

### 6.3 Dictated so far

Computed from the history when the dialog opens: the number of entries, the total of their words (runs of
non-whitespace), and the month and year of the oldest entry. No new stored value. It therefore counts what is in the
history: deleting entries or clearing the history lowers it, and that is accepted. With an empty history the row reads
"Nothing yet". Numbers use the user's locale grouping.

### 6.4 Shell calls

- One new request (`app` / `getAbout`) returning version, channel, data folder path, Windows build, and the three
  history figures.
- New fixed `LinkTarget`s for the repository, the privacy policy, the license, the home page and the data folder,
  opened through the existing `OpenLinkRequest`. The web UI still never passes an address or a path.

### 6.5 Copy details

Puts this on the clipboard and shows "Copied" on the button for a moment (the values are the live ones):

```
Murmur 1.1.0 (Microsoft Store)
Model: Whisper large-v3-turbo
Runtime: CUDA, NVIDIA GeForce RTX 3090
Windows 11, build 26200
```

## 7. Version

`version.json` becomes the source of the assembly version for every project (read in `Directory.Build.props`), so
About, the splash and "Copy details" show what `build-msix.ps1` packages. The value shown is the three-part version
with no commit suffix. `AppxManifest.xml` stays hand-kept and checked by the script, as today.

## 8. Approaches considered

**Splash**
- *A separate native WPF window* — **chosen.** It can be on screen before the container, the main window and
  WebView2 exist, which is the gap it is there to cover, and it is what the owner saw and approved.
- *A layer inside the main window, over the WebView.* One window instead of two, but WPF content cannot be drawn over
  a WebView2 control, so the WebView would have to stay hidden until ready; it also ties the splash to the 900 by 620
  window rather than the small card that was approved.
- *A splash page inside the web UI.* Easiest to animate, but it cannot show until WebView2 has started, so it covers
  none of the gap.

**About**
- *A React dialog in the existing UI* — **chosen.** Same look as the rest of the app, the engine values are already
  there, and it was approved in this form.
- *A native WPF dialog.* Would work without the web UI, but needs the app's styling rebuilt in XAML for one dialog.
- *A section at the bottom of Settings.* No new surface, but nowhere for the header, and the tray item would land on
  a scrolled page.

## 9. Testing

By the project's conventions (xUnit v3, NSubstitute, Shouldly; Vitest for the web app; zero warnings):

- The splash's close decision (ready, skip, minimum, timeout) with a fake `TimeProvider`.
- That a sign-in start shows no splash (the rule next to `IsSignInStart`).
- The focus rule of §5.3: activate when Murmur has the foreground, show without activating when it does not.
- The `getAbout` handler: channel text for packaged and unpackaged, the three history figures including an empty
  history and entries with irregular whitespace.
- `AppLinks.For` for every new target.
- The dialog: opens from the sidebar item, closes on Esc and on Close, focus handling, each row's text for ready,
  loading and CPU engine states, "Copy details" text, the links calling `openLink` with the right target.
- The animation itself is checked by eye against the mock-up, by the owner.

## 10. Left to the builder

The drawing technique for the splash (a custom-drawn element or animated geometry); class and file names; the exact
easing; the tray-to-dialog mechanism (§6.1); how the React hum animation is implemented; where the splash's status
wording lives so that it cannot drift from `engineStatusText.ts`; the wording of log lines.
