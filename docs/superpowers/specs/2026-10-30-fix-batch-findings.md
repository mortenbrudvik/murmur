# Murmur 1.1.0 fix batch: root causes (research, read-only, main at 962a5bb)

Repo `C:\code\projects\murmur`, `main` at 962a5bb. This was read-only research. The only code I ran was two read-only Win32 keyboard-layout queries (PowerShell `Add-Type`) and a history-file benchmark in the scratchpad. No app run, no microphone, `%LOCALAPPDATA%\Murmur` untouched.

Threading background used throughout:
- The hook thread posts every hotkey signal to the UI thread with `Dispatcher.BeginInvoke` at Normal priority (`src/Murmur.UI/App.xaml.cs:353`).
- The controller posts recorder chunks and ticks through `SynchronizationContext.Post`, which on the WPF context is also Normal priority (`DictationController.cs:512-518`).
- The pill presenter queues every call with `Dispatcher.BeginInvoke(DispatcherPriority.Send)` (`src/Murmur.UI/Pill/PillPresenter.cs:18,55-67`).
- WPF renders at `DispatcherPriority.Render`, which is lower than Normal.

---

## 6. On Bluetooth the pill appears late

**Root cause (confirmed from code)**
- `DictationController.StartRecording` runs on the UI thread. It calls `_pill.ShowListening()` (`src/Murmur.Core/Dictation/DictationController.cs:177-179`) and then `_recorder.Start(...)` (`:184`), which blocks that same thread for the whole device open.
- `AudioRecorder.Start` holds `_gate` around `_openDevice(deviceId)` and `device.StartRecording()` (`src/Murmur.Core/Audio/AudioRecorder.cs:44-71`). `WasapiCaptureDevice.Open` (MMDevice and WasapiCapture construction) also runs there (`src/Murmur.Core/Audio/CaptureDevice.cs:63-80`).
- `ShowListening` does not show anything synchronously. `PillPresenter.Post` is `window.Dispatcher.BeginInvoke(DispatcherPriority.Send, ...)` (`PillPresenter.cs:55-67`), and `BeginInvoke` always queues, even at Send priority and even on the dispatcher's own thread.
- So the queued `Show` runs only after `StartRecording` returns, which is after the microphone has opened (about 1 s on the WH-1000XM6). The first frame is drawn later still, at Render priority.
- Two pieces of code assume otherwise:
  - The comment "Send keeps the pill instant even while the UI thread is busy" (`PillPresenter.cs:17`) is wrong. Priority only orders the queue; nothing preempts the operation that is running.
  - The test `ThePillShowsBeforeTheMicrophoneIsOpened` (`tests/Murmur.Core.Tests/Dictation/DictationControllerTests.cs:503`) only checks the call order on a substitute, so it passes while the bug is live.
- Order today: hotkey down, then pill queued, then microphone open (UI thread blocked about 1 s, which also freezes the main window), then pill shown, then the first chunk, then the start cue (`:213-220`, played on the first chunk). The cue order is correct; only the pill is late.
- Making `Show` synchronous when called on the UI thread is not enough. The pill is a layered window (`AllowsTransparency=True`, `PillWindow.xaml:10`), and its content is only committed at Render priority. Forcing that would need a nested pump such as `Dispatcher.Invoke(..., ContextIdle)`. That would let queued hotkey signals (`HotkeyUp`, `OtherInputWhileHeld`) run inside `StartRecording`, before `Start`, which breaks the state machine.

**Proposed fix (smallest correct)**
Open the microphone off the UI thread.
- In `StartRecording`, keep the steps up to `ShowListening`. Then set `_matcher.DictationActive = true` and create the tick timer, so Esc and missed-release recovery work while the microphone opens.
- Run `_opening = OpenMicrophoneAsync(generation, _settings.MicrophoneId)`, which does `await Task.Run(() => _recorder.Start(id))` and resumes on the UI context.
- In its continuation:
  - On `AudioDeviceException` while the generation is still current, show today's "Microphone unavailable" path.
  - If the generation changed or `_recording` is false (Esc, a combo key, or capture failure while opening), call `_recorder.Cancel()`.
  - Log "Microphone opened in …" from the continuation.
- `StopRecording` and `Abandon` must not call `Stop()` or `Cancel()` while `_opening` is incomplete. Those calls take `_gate`, which `Start` holds, so they would block the UI thread again.
  - `StopRecording` should await `_opening` first. `ProcessRecordingAsync` is already async, so start it with `await _opening; var samples = _recorder.Stop();`.
  - `Abandon` can leave the cancel to the continuation.
- A new `StartRecording` must await the previous `_opening`. Otherwise `AudioRecorder.Start` throws `InvalidOperationException("A recording is already in progress")` (`AudioRecorder.cs:46-47`) after a quick Esc and re-press.
- WasapiCapture can safely be built on a pool (MTA) thread. `RecordingStopped` is then raised on the capture thread, which `AudioRecorder.OnRecordingStopped` already supports (`:219-220`).
- Fix the comment at `PillPresenter.cs:17`.

**Test** (`DictationControllerTests`, using the existing `QueueSynchronizationContext`)
- `HotkeyDown_OpensTheMicrophoneOffTheUiThread`: capture `SynchronizationContext.Current` inside the `Start` substitute and assert `ShouldNotBeSameAs(ui)`, the same pattern as `Insert_RunsOffTheUiThread`. Then block `Start` on a `ManualResetEventSlim`, assert `ui.Run(Down)` returns and `_pill.Received(1).ShowListening()` while `Start` is still blocked.
- `EscapeWhileTheMicrophoneOpens_ReleasesItOnceOpen`: release the gate, then assert `_recorder.Received(1).Cancel()`, no `Stop`, and no `PlayStart`.
- `ReleaseWhileTheMicrophoneOpens_StopsAfterItOpens`: assert `Stop` is received after `Start` returns, with `Received.InOrder`.
- Replace `ThePillShowsBeforeTheMicrophoneIsOpened` with the first test.
- Manual: with the headset, the debug log should show the pill's Show before "Microphone opened in N ms". Adding one Debug line in `PillPresenter.Show` would timestamp it.

**Owner decisions:** none.

**Size:** M (controller async start plus 3-4 tests).

---

## 7. Right Ctrl+C plays the start beep and opens the microphone

**Root cause (confirmed from code)**
- A modifier-only hotkey fires `HotkeyDown` on the key-down itself (`src/Murmur.Core/Input/HotkeyMatcher.cs:165-173`).
- `DictationStateMachine.OnHotkeyDown` in Idle immediately returns `StartRecording` (`src/Murmur.Core/Dictation/DictationStateMachine.cs:56-62`). There is no grace period: `TapThreshold` (300 ms, `:24-27`) only classifies the release (`:162-165`).
- `StartRecording` then shows the pill and opens the microphone (`DictationController.cs:168-199`). On the WH-1000XM6 that switches the headset from A2DP to HFP.
- The combo is only noticed when the second key goes down:
  - `HotkeyMatcher.Match`: any non-modifier-repeat key-down while `_held` returns `OtherInputWhileHeld` (`HotkeyMatcher.cs:129-130`). A mouse button or wheel does the same through `:113-117` while the mouse hook is on.
  - The state machine then goes HoldRecording to Idle with `Cancel` (`DictationStateMachine.cs:89-94`).
  - The controller calls `Abandon()` and `_pill.Hide()` (`DictationController.cs:158-161`), which runs `_recorder.Cancel()` (`:471-476`).
- The start cue is not tied to that decision. It plays when the first audio chunk is processed on the UI thread (`DictationController.cs:213-220`).
- Hook signals and chunks are posted at the same Normal priority (FIFO). The beep is therefore heard exactly when the first chunk was posted before the C press was. That means the microphone delivered audio before C went down: a fast microphone, a slow Ctrl…C, or a Bluetooth `Start` that returns quickly and goes slow afterwards.
- If `Start` itself blocks for the full second and C is pressed during it, C is queued ahead of the chunks. The cancel then wins and no beep plays, but the microphone and the headset profile switch still happen. The pill's Show and Hide run back to back and may never render.

**Proposed fix**
Add an "armed" phase for modifier-only hotkeys only. Combo hotkeys are swallowed and cannot be part of another app's shortcut.
- Add `ArmDelay` to `DictationTimings` (suggest 150-200 ms; it must stay below `TapThreshold`) and a state `Armed`.
- `OnHotkeyDown` in Idle with a modifier-only hotkey: go to `Armed`, record `_pressedAt`, return a new action `Arm`. The controller starts only the tick timer: no pill, no microphone, no cue, and `DictationActive` stays false so Esc passes through.
- `OnTick` in `Armed`:
  - At `now - _pressedAt >= ArmDelay` and still physically down, go to HoldRecording with `StartRecording`.
  - If up (missed release), go Idle.
- `OnOtherInputWhileHeld` in `Armed`: go Idle with action None, so nothing was ever started.
- `OnHotkeyUp` in `Armed`: it is a tap (released before `ArmDelay`, which is below `TapThreshold`), so go to `HandsFreeRecording` with `StartRecording`. The hands-free timers already run from `_pressedAt`.
- The machine needs to know whether the hotkey is modifier-only. Pass it to `OnHotkeyDown`, or let the matcher emit a distinct signal.

**Test**
- `DictationStateMachineTests`:
  - `ModifierHotkey_OtherInputWithinArmDelay_GoesIdleWithoutStarting`: assert `None` and `Idle`.
  - `ModifierHotkey_HeldPastArmDelay_StartsRecording`.
  - `ModifierHotkey_ReleasedWithinArmDelay_StartsHandsFree`.
  - `ComboHotkey_StartsAtOnce`.
- `DictationControllerTests` `ShortcutWithTheHotkey_NeverOpensTheMicrophone`: call `Down()`, then `Signal(OtherInputWhileHeld)` with `_time` advanced less than `ArmDelay`. Assert `_recorder.DidNotReceiveWithAnyArgs().Start(default)`, `_cues.DidNotReceive().PlayStart()` and `_pill.DidNotReceive().ShowListening()`.
- Existing controller tests call `Down()` and expect `Start` at once. They need `_time.Advance(ArmDelay)`, or a helper that does it.

**Owner decisions**
- The `ArmDelay` value.
- Defer the microphone open (protects the Bluetooth profile on every Ctrl+C, but every hold opens the microphone `ArmDelay` later and loses that much speech on a fast microphone), or defer only the pill and beep (no profile protection).
- Apply it only to modifier-only hotkeys (recommended).

**Size:** M (state machine plus controller plus tests).

---

## 8. Engine in Error, pill says "Loading model…"

**Root cause (confirmed)**
- The pill text is a single constant: `LoadingModelNotice = "Loading model…"` (`src/Murmur.Core/Dictation/DictationController.cs:26`).
- It is shown for every `DictationAction.NotifyNotReady` (`:162-163`).
- The state machine gets only a bool: `_engine.Status.State == EngineState.Ready` (`:124`). `OnHotkeyDown` returns `NotifyNotReady` for any non-Ready state (`DictationStateMachine.cs:57-58`), so NotStarted, Downloading, Loading and Error all say "Loading model…".
- The tray tooltip already distinguishes these states (`src/Murmur.UI/Tray/TrayIconHost.cs` `FormatToolTip`, Error shows "Murmur — {error}"), and so does the web banner (`src/WebApp/src/features/settings/engineStatusText.ts:38-49`).

**Proposed fix**
In `Execute`'s `NotifyNotReady` case, choose the text from `_engine.Status`. Take one snapshot in `HandleSignal` and use it for both the readiness check and the text:
- `Error`: e.g. "Speech engine failed — open Murmur"
- `Downloading`: "Downloading model…" (optionally with a percent)
- otherwise: "Loading model…"

The pill is click-through, so the notice can only point the user to the window or tray.

**Test** (`DictationControllerTests`)
Turn `EngineNotReady_ShowsLoadingNotice` into a `[Theory]` with one row per state: `Loading` and `NotStarted` give "Loading model…", `Downloading` gives the download text, and `Error` gives the failure text. Each asserts `_pill.Received(1).ShowNotice(expected)` and that the recorder was not started.

**Owner decisions**
- Copy, which must be short because the pill grows with the text.
- Whether pressing the hotkey in Error should also open the main window or show a tray notification.

**Size:** S.

---

## 9. Keyboard hook cannot be installed: silent failure

**Root cause (confirmed)**
- `InputHooks.Install` throws `Win32Exception` when `SetWindowsHookEx` returns 0 (`src/Murmur.Core/Input/InputHooks.cs:52-64`). `InputHookHost.Start` rethrows it, or a `TimeoutException` if the hook thread does not start within 5 s (`src/Murmur.UI/InputHookHost.cs:48-69,136-144`).
- `App.StartInputHooks` catches it, logs Error "Could not install the keyboard hook; dictation is unavailable" and continues (`src/Murmur.UI/App.xaml.cs:355-363`). Nothing is recorded for later.
- The tray is created afterwards (`:167-171`).

What the user sees:
- Everything looks normal. The engine banner reaches Ready.
- The tray tooltip says "Murmur — hold Right Ctrl to talk" (`TrayIconHost.FormatToolTip`).
- After a first download, the notification says "Murmur is ready / Hold Right Ctrl to talk." (`src/Murmur.UI/Tray/TrayNotifier.cs:44`, via `App.xaml.cs:262-272`).
- Pressing the hotkey does nothing: no pill, no sound.
- Settings > Change hotkey waits 10 s and silently reverts. `CaptureHotkeyHandler` times out (`src/Murmur.Core/Features/Settings/CaptureHotkeyRequest.cs:13`), and `SettingsPage.tsx:46-48` treats null as "nothing to save".
- The setup "Try it" step waits for a `history.added` event that never comes (`src/WebApp/src/features/setup/TryItStep.tsx:21`).
- The only trace is the log line.

**Proposed fix**
- Keep the failure: have `StartInputHooks` return the exception, or set a small `InputStatus` singleton (`HookInstalled`, `Error`) registered in Autofac.
- Tray (smallest useful step):
  - After `_tray.Show()`, if the hook failed, call `_tray.ShowNotification("Murmur can't use the hotkey", "Windows didn't let Murmur watch the keyboard, so dictation is off. Restart Murmur.")`.
  - `TrayIconHost.ShowNotification` currently drops notices while the icon is not created yet (`TrayIconHost.cs:114-131`). That is likely at a sign-in start, when the taskbar is not ready and `TryCreate` retries. Keep one pending notice and show it after a successful `TryCreate`.
  - `FormatToolTip`, and the "Hold X to talk" ready notice, should not advertise the hotkey when the hook failed.
- Web: add a `GetInputStatusRequest` (`app`/`getInputStatus`) and render an alert strip next to `EngineStatusBanner` in `App.tsx`, e.g. "The hotkey isn't working…" with a "Restart Murmur" button. `RestartAppRequest` already exists.
- Optional: retry `Install` once after a short delay before giving up. `InputHookHost.Start` returns early once `_thread` is set (`:51-52`), so a retry needs a fresh host.
- Out of scope but related: Windows can also remove a hook silently later (LowLevelHooksTimeout). Nothing detects that today. The dedicated hook thread makes it unlikely.

**Test**
- `TrayIconHostTests` `ShowNotification_IconNotAddedYet_IsShownOnceTheIconIsAdded`: use the `create` seam to fail first and then succeed. It replaces `ShowNotification_IconNotAddedYet_DoesNotThrow`'s "does nothing" contract.
- `TrayIconHostTests` `FormatToolTip_HookUnavailable_SaysTheHotkeyIsOff`.
- `EventHandlerTests` / `RequestJsonContractTests` for the new request.
- Vitest: `HotkeyUnavailableBanner` renders `role="alert"` when `hookInstalled` is false and nothing when true.

**Owner decisions**
- Copy.
- Tray only, or tray plus web banner.
- Whether to open the main window on a sign-in start when the hook failed.
- Whether to retry automatically.

**Size:** S (tray with pending notice), M (with the web banner and IPC).

---

## 10. Combo hotkey: a missed release has no cap

**Root cause (confirmed)**
- `OnTick` for a combo uses the matcher's own view: `physicallyDown = hotkey.IsModifierOnly ? _keys.IsKeyDown(vk) : _matcher.IsHotkeyHeld` (`src/Murmur.Core/Dictation/DictationController.cs:239-241`). This is because a swallowed key never reaches `GetAsyncKeyState` (`src/Murmur.Core/Input/Win32KeyStateProbe.cs:5-8`).
- `_held` is only cleared by a key-up event the hook sees (`HotkeyMatcher.cs:198-202`). So the recovery `if (!physicallyDown && _matcher.IsHotkeyHeld) ResetHeld()` (`:243-245`) can never fire for a combo.
- `DictationStateMachine.OnTick` in HoldRecording only checks the key (`:123-125`). There is no time cap for holds of any hotkey.
- `DictationTimings` has only `TapThreshold`, `StrayTapTimeout` and `HandsFreeLimit` (`:21-28`), and `HandsFreeLimit` (10 min) applies only in HandsFreeRecording (`:136-137`).
- Modifier-only hotkeys are not capped either. They are protected by the 100 ms `GetAsyncKeyState` poll, which turns a missed release into a release (`DictationController.cs:241`, `DictationStateMachine.cs:123-125`).
- F-key hotkeys without modifiers are valid (`src/Murmur.Core/Models/DomainModels.cs:35-40`) and have nothing to poll at all.

When a combo's release is missed (elevated or secure-desktop focus, or another hook eating the key-up), the recording runs on, with the microphone open and samples growing at about 230 MB/h of floats, until one of these happens:
- Any other key or mouse button: `OtherInputWhileHeld` cancels it (`HotkeyMatcher.cs:129-130`, `DictationStateMachine.cs:93-94`). The user's real dictation is then discarded silently.
- The next Space press: its down is swallowed as "repeat" (`HotkeyMatcher.cs:187-188`) and its up becomes `HotkeyUp`. One space character is lost, and the run-on recording is pasted into whatever has focus.

This breaks the spec's "Never stuck recording" (`docs/superpowers/specs/2026-10-29-murmur-design.md:61,98`).

**Proposed fix**
- Add `HoldLimit` to `DictationTimings`. Suggest reusing 10 min, or something shorter.
- In `OnTick` HoldRecording: `if (now - _pressedAt >= timings.HoldLimit) return Enter(Transcribing, StopAndSaveOnly)`. That saves without pasting, matching the hands-free limit's rationale (spec :58).
- In the controller, call `_matcher.ResetHeld()` when a hold ends by the limit, so the next Space press is not swallowed.
- Optional, an owner decision: for combos with modifiers, treat "every required modifier is physically up" (`_keys.IsKeyDown` on the generic Ctrl/Alt/Shift/Win, never swallowed) as the release. That ends a missed release within 100 ms, but cuts a hold short if the user lets go of Ctrl before Space.
- Unverified: whether `GetAsyncKeyState` still reads modifiers while an elevated window has focus. A manual check would settle it: hold Ctrl+Space, focus an admin terminal, release, and read the log.

**Test**
- `DictationStateMachineTests` `Hold_PastTheHoldLimit_StopsAndSavesOnly`: `OnHotkeyDown(t0)`, then `OnTick(t0 + HoldLimit, true)` returns `StopAndSaveOnly`.
- `Default_Timings_MatchSpec` gains the new value.
- `DictationControllerTests` `ComboHotkey_MissedRelease_StopsAtTheHoldLimit`: set up the combo as in `ComboHotkey_TickUsesTheMatchersHeldState`, then `_time.Advance(HoldLimit)`. Assert `_inserter.DidNotReceiveWithAnyArgs().InsertAsync`, `_history.Received(1).Add("Hello world.", "", false)`, `ShowNotice("Saved to history")` and `_matcher.IsHotkeyHeld.ShouldBeFalse()`.

**Owner decisions**
- The `HoldLimit` value.
- Save-only versus paste.
- Whether to add the modifier-released heuristic.

**Size:** S.

---

## 11. Symbol-key names follow the US layout

**Root cause (confirmed)**
- `KeyNames.For` uses a fixed US table for OEM keys (`src/Murmur.Core/Input/KeyNames.cs:58-69`).
- It runs on the hook thread when a hotkey is captured (`src/Murmur.Core/Input/HotkeyCaptureSession.cs:56,77`). The result is stored in settings as `HotkeyDefinition.DisplayName` (`DomainModels.cs:15`).
- That name is shown in Settings (`SettingsPage.tsx:80`), the History and setup copy (`useHotkeyName.ts:16`), the tray tooltip and the ready notification.
- Letters and digits are fine (their virtual-key codes follow the layout). Only the OEM codes are wrong.

Measured on this PC with `MapVirtualKeyEx(vk, MAPVK_VK_TO_CHAR, HKL 0x04140414)` (Norwegian):

| VK | US name today | Norwegian key | Scan code |
|---|---|---|---|
| 0xBA | ";" | "¨" (dead key) | 1B |
| 0xBB | "=" | "+" | 0C |
| 0xBF | "/" | "'" | 2B |
| 0xC0 | "`" | "ø" | 27 |
| 0xDB | "[" | "\\" | 0D |
| 0xDC | "\\" | "\|" | 29 |
| 0xDD | "]" | "å" | 1A |
| 0xDE | "'" | "æ" | 28 |

Correct by coincidence: 0xBC ",", 0xBD "-", 0xBE ".", 0xE2 "<".

`GetKeyNameText` is not usable for this: it returns localised names on this layout ("MELLOMROM" for Space, "TØDLER" for ¨, "CTRL (HØYRE)", "TILBAKE"), all measured. That would mix Norwegian into the English UI.

**Proposed fix**
- For the OEM range only (0xBA–0xC0, 0xDB–0xDF, 0xE2), ask the layout for the character:
  - Take the HKL of the foreground window's thread: `GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _))`. During capture that is Murmur's own window. The hook thread's own `GetKeyboardLayout(0)` can be stale.
  - Call `MapVirtualKeyExW(vk, 2 /*MAPVK_VK_TO_CHAR*/, hkl)`, mask the dead-key bit (`& 0x7FFFFFFF`) and upper-case the result: "Ø", "Å", "Æ".
  - Fall back to the US table when it returns 0.
  - `MapVirtualKeyEx` does not touch the dead-key state, unlike `ToUnicodeEx` without flag 0x4.
- Keep the English names for everything else.
- Put the P/Invokes in `src/Murmur.Core/Interop/NativeMethods.cs`, which already has `MapVirtualKey`.
- Make it injectable for tests: `KeyNames.For(vk, Func<int, char?> layoutChar)`, or an `IKeyboardLayout` passed to `HotkeyCaptureSession`.

**Test**
- `KeyNamesTests`:
  - `For_OemKeyOnNordicLayout_UsesTheLayoutsCharacter`, with a fake that maps 0xC0 to 'ø', gives "Ø".
  - `Describe_CtrlWithNordicOemKey`, gives "Ctrl+Å".
  - `For_OemKeyTheLayoutCannotMap_FallsBackToUsName`.
- Optional Windows-only check: run only if 0x04140414 is already in `GetKeyboardLayoutList`. Do not call `LoadKeyboardLayout`, which changes the user's language list.

**Owner decisions**
- Whether names already saved in settings are recomputed on load or left until the hotkey is captured again. The owner's own hotkeys, Right Ctrl and Ctrl+Space, are unaffected.
- How to show dead keys, e.g. "¨".

**Size:** S.

---

## 12. History "only keeps the last 200"

**Root cause (confirmed: a display limit, not storage)**
- `HistoryStore` keeps every entry forever:
  - `Add` appends one JSON line to `history.jsonl` (`src/Murmur.Core/Services/HistoryStore.cs:21-39`).
  - The file is read whole on first use and kept in memory (`:79-105`).
  - `Delete` and `Clear` rewrite the whole file through a `.tmp` (`:53-77,128-136`).
  - Nothing trims it. That matches the spec: "kept until deleted" (`design.md:84,183`).
- The 200 is the query limit:
  - The web sends `limit: LIMIT` with `LIMIT = 200` (`src/WebApp/src/features/history/useHistory.ts:6,39`) and trims live additions to 200 (`:65`).
  - The server default is `GetHistoryRequest(..., int Limit = 200)` (`src/Murmur.Core/Features/History/GetHistoryRequest.cs:166`).
  - `HistoryStore.Query` returns the newest `limit` entries (`HistoryStore.cs:41-50`).
- Older entries are on disk and still searchable (search scans everything and returns the 200 newest matches), but they cannot be scrolled to. Nothing tells the user that.

**Cost** (measured in the scratchpad: .NET with System.Text.Json and the same options; about 295-character ASCII texts, about 430 B per line; non-ASCII text grows because the default encoder writes `\u00F8`)

| Entries | File | First load (`ReadFile`) | Allocated | Full search | Delete/Clear rewrite |
|---|---|---|---|---|---|
| 1 000 | 0.4 MB | 3 ms | 4 MB | 0.7 ms | 3 ms |
| 10 000 | 4.3 MB | 41 ms | 35 MB | 1.2 ms | 48 ms |
| 100 000 | 43 MB | 501 ms | 352 MB | 14 ms | 200 ms |

At 20 dictations a day, that is about 7 000 entries (3 MB) a year, so storage is not the problem.

Two scale hazards:
- The first `Add` after start loads the whole file on the UI thread, inside `DictationController.SaveAsync`'s `_history.Add` (`DictationController.cs:431`), when the window has not been opened yet. That is a sign-in start.
- Each delete rewrites the whole file while holding the store lock.

In the UI, the History page renders every returned entry with no virtualisation. Raising `LIMIT` to thousands would make the DOM and the JSON IPC payload the bottleneck.

**Proposed fix (paging)**
- Add a cursor to `GetHistoryRequest`: `Before` as the timestamp+id of the oldest entry shown, or a plain `Offset`.
- Add `IHistoryStore.Query(search, limit, before)`.
- Return `hasMore`, or fetch `limit + 1`.
- In the web, `useHistory` appends pages, and the page gets a "Show older" button (or infinite scroll) under the list.
- Optionally pre-load the store off the UI thread at start. That removes the first-`Add` hazard independently of paging.

**Test**
- `HistoryStoreTests` `Query_WithCursor_ReturnsTheNextOlderPage` and `Query_LastPage_ReportsNoMore`.
- `HistoryHandlerTests` `GetHistory_PassesTheCursor`.
- Vitest `useHistory.test.ts`: "Show older appends the next page", and "a live entry does not drop loaded older pages". The latter needs `slice(0, LIMIT)` at `:65` to go.

**Owner decisions**
- Button or infinite scroll, and page size.
- Whether to add a retention option (the spec says history is kept until deleted).
- Whether to move the first load off the UI thread now.

**Size:** M.

---

## 13. Waveform in the pill flickers

**Root cause (likely; not provable from code)**
- Levels are pushed per capture buffer.
  - Capture is event-driven and wakes every device period, about 10 ms (`src/Murmur.Core/Audio/CaptureDevice.cs:30-33`). NAudio raises one `DataAvailable` per wake.
  - Each non-empty chunk becomes one `PushLevel` of that chunk's RMS (`DictationController.cs:229-231`), routed `Post` to `OnChunk`, then `PillPresenter.PushLevel`, then `BeginInvoke(Send)`, then `vm.PushLevel` (`PillPresenter.cs:34`).
  - That is about 100 pushes per second against a 60 Hz display.
- 8 bars therefore hold only about 80 ms of history (`PillViewModel.cs:17`), and every bar shifts 1-2 places per frame.
  - Each bar is a raw 10 ms RMS with no smoothing, mapped across a narrow -60 to -20 dBFS window (`:22-23,41-50`).
  - Speech swings between those ends within tens of milliseconds, so frame to frame the bars jump between minimum and full height. That reads as flicker or strobing.
  - The design mockup animates bars smoothly on a 0.9 s ease (`docs/superpowers/specs/mockups/pill.html:26-28`).
- The collection also churns:
  - `Bars.RemoveAt(0); Bars.Add(...)` (`PillViewModel.cs:35-39`) makes the `ItemsControl` destroy one container and build a new one from the DataTemplate (`Rectangle` plus binding plus `BarHeightConverter`, `PillWindow.xaml:103-124`) 100 times a second. That is not visible on its own, but it is wasted work on a layered window (`AllowsTransparency=True`, `:10`) that also redraws a `DropShadowEffect` (`:79`) on every frame.
- Sub-pixel edges add shimmer:
  - Bars are 2.5 DIP wide with 1.25 radii, centred vertically, with heights of `level × 16` (`BarHeightConverter.cs:11-14`). Their top and bottom edges land on fractional pixels and are anti-aliased differently each frame.
  - There is no `UseLayoutRounding`.
- No binding resets during Listening: `OnModeChanged` only resets bars on entry (`PillViewModel.cs:53-62`), and `Reposition` only runs on mode changes (`PillPresenter.cs:79-81`).

**Proposed fix**
- Push one bar per fixed slice of audio, e.g. 50 ms (800 samples at 16 kHz), counted by samples. The controller accumulates the sum of squares (or the peak) and the sample count, and calls `PushLevel` when 800 samples are in. This needs no timer and is deterministic in tests.
- In the view model, shift in place (`for i: Bars[i] = Bars[i+1]; Bars[^1] = new`). That raises Replace events and reuses containers. Optionally add release smoothing, e.g. a bar falls at most about 0.15 per step.
- Add `UseLayoutRounding="True"` on the capsule.

**Test**
- `DictationControllerTests` `SamplesAvailable_PushesOneLevelPer50msOfAudio`: raise five 10 ms chunks and assert `_pill.ReceivedWithAnyArgs(1).PushLevel(default)`; ten chunks give 2. Update `SamplesAvailable_PushesLevelsToThePill`.
- `PillViewModelTests` `PushLevel_ShiftsInPlaceWithoutRemovingItems`: subscribe to `CollectionChanged` and assert only `Replace` actions. Plus a decay test if smoothing is added.

**Evidence that would settle it**
Record the pill at 60-120 fps (OBS or Game Bar).
- Bars jumping between minimum and maximum from frame to frame confirms the cadence.
- If the whole capsule or shadow blinks, look at the layered-window and `DropShadowEffect` path instead, e.g. temporarily remove the effect.
- A debug counter of `PushLevel` calls per second (expect about 100) confirms the rate.

**Owner decisions**
- Bar rate (e.g. 20 per second, so 8 bars cover 400 ms).
- Number of bars.
- Smoothing feel and the dBFS range.

**Size:** S.

---

## 14. Model switch makes dictation unavailable for 2–13 s (known cause, confirmed)

**Root cause**
- During a switch, after the download, `WhisperEngine.SwitchFromAsync` sets `EngineStatus(EngineState.Loading, target.DisplayName, ...)` (`src/Murmur.Core/Speech/WhisperEngine.cs:379`) while it loads the new model (`:380`).
- Throughout that load `_loaded` is still the old model, and `TranscribeAsync` works: it only needs `_loaded != null` (`:120-146`).
- `DictationController.HandleSignal` accepts a dictation only when `State == EngineState.Ready` (`src/Murmur.Core/Dictation/DictationController.cs:124`), so for the length of the load you get "Loading model…" (item 8).
- The download phase is fine: it reports `from.ReadyStatus with { Download }` (`:376`).

**Proposed fix (smallest)**
- Add `bool CanTranscribe { get; }` to `ISpeechEngine`, as `!IsDisposed && _loaded is not null`.
- Use it at `DictationController.cs:124`, keeping one status snapshot for item 8's notice text.
- Transcriptions already serialise with the swap through `_modelGate` (`:134,451`), so dictating during the load is safe. It is only slower, because of GPU contention.
- Alternative (M): keep reporting Ready with a "loading the next model" field. That changes the web banner and the `WhisperEngineTests` status sequences (`:400`).

**Test**
- `DictationControllerTests` `SwitchLoadingWithAModelLoaded_StillDictates`: `Status` is Loading, `CanTranscribe` is true, then `HoldAndRelease()` gives `InsertAsync` received.
- `WhisperEngineTests` `SwitchModelAsync_WhileTheNextModelLoads_CanTranscribeIsTrue`: block the loader, assert `CanTranscribe` and that `TranscribeAsync` returns "from ggml-large…".
- The `DictationControllerTests` constructor must set `_engine.CanTranscribe.Returns(true)`.

**Owner decisions**
- Whether the web banner during the switch's load should say that dictation still works on the previous model.

**Size:** S.

---

## 15. TLS-intercepting proxy: retries forever with "Reconnecting…" (known cause, confirmed)

**Root cause**
- `ModelManager.SendAsync` turns every `HttpRequestException` without a status code into a `TransientDownloadException` (`src/Murmur.Core/Speech/ModelManager.cs:259-262`). The mid-body read does the same (`:282-284`).
- A failed TLS handshake (an untrusted proxy root) is `HttpRequestException { StatusCode: null, HttpRequestError: SecureConnectionError }` with an inner `AuthenticationException`. It therefore lands in the endless retry loop (`:161-177`, by design "retrying without end", `:12`) with backoff 2/5/15/60 s.
- `Retries > 0` sets `DownloadProgress.Reconnecting` (`DomainModels.cs:151`). The web shows "Reconnecting…" (`engineStatusText.ts:28`), and after 4 retries the tray says "Check your internet connection. Murmur keeps trying." (`TrayNotifier.cs:22,53-58`).
- This only happens when the proxy's CA is not trusted by Windows. .NET on Windows validates against the Windows store.

**Proposed fix**
- Filter the catch: `when (ex.StatusCode is null && ex.HttpRequestError is not HttpRequestError.SecureConnectionError)`. `ProxyTunnelError` and `UserAuthenticationError` arguably belong with it.
- The exception then propagates. `WhisperEngine.DescribeStartFailure` (`WhisperEngine.cs:550-551`) and `DescribeSwitchFailure` (`:561`) need a specific message instead of "Check the internet connection", e.g. "Murmur couldn't make a secure connection to huggingface.co. A proxy, firewall or antivirus that inspects HTTPS may be in the way."
- Caveat: captive portals also cause TLS errors, but transiently. Retrying a few times (e.g. 3) before failing is a fair middle ground.

**Test**
- `ModelManagerTests` `EnsureModelAsync_TlsFailure_ThrowsWithoutRetrying`: `CreateManager(_ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "The SSL connection could not be established", new AuthenticationException()))`. Assert `Should.ThrowAsync<HttpRequestException>`, `_handler.Calls.ShouldBe(1)` and `_progress.Reports.ShouldAllBe(r => r.Retries == 0)`. If limited retries are chosen, assert N calls instead.
- `WhisperEngineTests` `InitializeAsync_TlsFailure_EndsInErrorNamingTheSecureConnection`.

**Owner decisions**
- Fail at once or after N attempts.
- Copy.
- Whether the proxy-auth errors join it.

**Size:** S.

---

## 16. Quit during a model switch, then an offline start: no dictation although the previous model is on disk (known cause, confirmed)

**Root cause**
- `SwitchModelAsync` persists the new choice before switching (`TrySaveChoice`, `WhisperEngine.cs:111-118,185-206`, `settings.Update` at `:192`). This is the documented contract in `src/Murmur.Core/Speech/ISpeechEngine.cs:27`.
- Quitting cancels the operation through the app token (`App.xaml.cs:409`). `SwitchFromAsync`'s cancellation path does not restore the choice (`WhisperEngine.cs:384-390`); only failures do (`:391-397`).
- The previous model's file stays: models are deleted only after the next Ready (`:364-365,412`, `DeleteModelsOtherThan` at `:502-509`).
- On the next start, `LoadFirstModelAsync` resolves the saved choice (`:327`, `ModelSelection.ResolveActive` at `src/Murmur.Core/Speech/ModelSelection.cs:29-30`) and must download it (`:331-333`). It never considers the complete model already on disk. The existing test `InitializeAsync_AnotherModelOnDisk_IsDeletedOnceReady` (`tests/Murmur.Core.Tests/Speech/WhisperEngineTests.cs:320-337`) pins exactly this.
- Discrepancy with the report: when the network is simply down, the code classifies DNS or connection failures as transient. It retries forever in Downloading ("Reconnecting…"); it does not end in Error.
  - Error happens only on a non-transient failure (`:355-360`): an HTTP 4xx, a proxy or captive-portal page (`ModelManager.cs:213-215`), disk space, or TLS after item 15.
  - Either way dictation is unavailable.
  - The log of that start settles which case it was: "download was interrupted … retry N" versus "Speech engine failed to download".

**Proposed fix**
In `LoadFirstModelAsync`, when the chosen model has no complete file and another catalog model does, use the one on disk and then continue the switch in the same operation:
- Load the one on disk first and go Ready on it, verifying only and never downloading it.
- Then continue the switch to the chosen model by calling `SwitchFromAsync(operation, fallback)` with `Target` = the choice. That reuses the background download with "Ready plus Download", cancellation by later switches, `RestoreChoice` on failure, and the swap and delete on success.
- `DeleteModelsOtherThan` after the fallback's Ready must skip the pending target, or it deletes the target's `.part` and throws away the partial download.
- Rejected smaller alternative: restore the choice on a shutdown cancel. It runs on a pool thread racing process exit, it loses the user's choice, and the next Ready deletes the `.part` anyway.

**Test** (`WhisperEngineTests`)
- `InitializeAsync_ChosenModelMissingButPreviousOnDisk_ReadyOnThePreviousThenSwitches`:
  - Set up `_stored.SpeechModel = SmallEn`, `HasExpectedFile(Large) = true`, `HasExpectedFile(Small) = false`, and make `EnsureModelAsync(Small)` hang.
  - Assert the status reaches `Ready` with `ModelName == Large` and `Download.ModelName == Small`.
  - Assert `TranscribeAsync` gives "from ggml-large…" and `_models.DidNotReceive().Delete(SmallEn)`.
  - Release the download and assert Ready on Small plus `Delete(LargeV3Turbo)`.
- Adjust `InitializeAsync_AnotherModelOnDisk_IsDeletedOnceReady` to the case where both files are complete.

**Owner decisions**
- Fall back to the model on disk and resume the switch in the background (recommended), or revert to the previous choice.

**Size:** M.

---

## Cross-item notes
- Items 6 and 7 touch the same start path. Doing 6 first (asynchronous microphone open) makes 7's armed phase simpler, because a cancel during the open is then already handled.
- Items 8 and 14 change the same line (`DictationController.cs:124`). Take one `EngineStatus` snapshot and decide both "can dictate" and the notice text from it.
- Item 15 changes which start failures end in Error, which affects how item 16 shows up offline or behind a proxy.
