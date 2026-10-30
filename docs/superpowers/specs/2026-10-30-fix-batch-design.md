# Murmur 1.1.0 fix batch (design)

**Date:** 2026-10-30
**Status:** Approved 2026-10-30 (owner chose the batch, "150 ms, defer everything" and "5 minutes"; the rest are my calls, listed at the end)
**Builds on:** `2026-10-29-murmur-design.md` (v1) and `2026-10-30-first-run-setup-design.md` (1.1.0, merged as 962a5bb)
**Root-cause research:** `2026-10-30-fix-batch-findings.md` — one section per item with file:line evidence and test ideas. Line numbers are as of 962a5bb.

## 1. Intent

Murmur 1.1.0 is merged but not yet submitted to the Store. Before it goes out, fix the known 1.0 bugs and the leftovers from the 1.1.0 reviews, so the Store gets one solid update instead of 1.1.0 followed quickly by 1.1.1. The version stays **1.1.0**.

**Success:** on the owner's PC (Bluetooth WH-1000XM6 headset, Nordic layout, RTX 3090) the pill appears the moment the hotkey goes down, Right Ctrl+C never touches the microphone, nothing can record forever, and every failure the user can hit says what happened.

## 2. Non-goals

New features beyond what is listed; changing the engine's model set; a history retention setting (history stays "kept until deleted"); detecting a hook that Windows removes later (LowLevelHooksTimeout).

## 3. The fixes

### Dictation start and stop
1. **Pill shows instantly, microphone opens off the UI thread** (findings §6). The UI thread is never blocked by a device open. The pill shows on key-down (subject to item 2). Esc, release and a combo key during the open are handled when the open completes: a cancelled start releases the microphone, a release stops it once open. A quick re-press waits for the previous open. The misleading comment in `PillPresenter.cs` and the call-order-only test are replaced.
2. **Armed delay for lone-modifier hotkeys** (findings §7, owner: **150 ms, defer everything**). A hotkey that is a lone modifier (e.g. Right Ctrl) arms on key-down and starts recording only after it has been held **150 ms**. Another key or mouse button within those 150 ms means it was a shortcut: nothing happens — no microphone, no pill, no beep. Released within 150 ms = a tap → hands-free starts (as a tap does today). Combo and F-key hotkeys start at once (they are swallowed, so they can't be part of another app's shortcut).
3. **Hold limit and missed releases** (findings §10, owner: **5 minutes**). A held hotkey stops on its own after **5 minutes**; the dictation is saved to History, not pasted, with the "Saved to history" notice, and the matcher's held state is cleared so the next press works. For a combo with modifiers, all of its modifiers being physically up (read through the key-state probe; modifiers are never swallowed) counts as the release, so a missed key-up ends within one tick.
4. **The pill says what is wrong when the engine isn't ready** (findings §8). Instead of always "Loading model…": Downloading → "Downloading speech model… 38%" (percent when known); Loading/NotStarted → "Loading speech model…"; Error → "Speech engine unavailable — open Murmur".
5. **Dictation keeps working while a switched model loads** (findings §14). Dictation is accepted whenever a model is loaded (`ISpeechEngine.CanTranscribe`), not only in Ready. The engine status during a switch's load carries `Switching = true`; the web banner then says "Switching to {model}…".

### Failures that were silent
6. **Keyboard hook failure is visible** (findings §9). If the hook cannot be installed, Murmur retries once after 2 s with a fresh host. If it still fails: a tray notification "Murmur can't use the hotkey" / "Windows didn't let Murmur watch the keyboard, so dictation is off. Restart Murmur to try again." (queued until the tray icon exists), the tray tooltip says "Murmur — the hotkey isn't working", the ready notice doesn't advertise the hotkey, and the main window shows an alert strip with the same message and a **Restart Murmur** button (new IPC `app.getInputStatus`).
7. **Secure-connection failures stop retrying** (findings §15). A TLS failure (`HttpRequestError.SecureConnectionError`) is retried 3 times (captive portals cause brief TLS errors), then the download ends in Error: "Murmur couldn't make a secure connection to huggingface.co. A proxy, firewall or antivirus that inspects HTTPS may be blocking it." Proxy-tunnel and proxy-authentication errors go the same way.
8. **Quit mid-switch, then start: Murmur starts on the model already on disk** (findings §16). When the chosen model has no complete file but another catalog model does, Murmur loads that one (verify only, never download it), goes Ready, and continues the switch to the chosen model in the background — the same Ready + download path as a switch from Settings. The sweep after the fallback's Ready must not delete the chosen model's `.part`.

### Display
9. **Symbol-key names follow the keyboard layout** (findings §11). For OEM keys only (0xBA–0xC0, 0xDB–0xDF, 0xE2) the name is the current layout's character (upper-cased: "Ø", "Å", "Æ"; dead keys shown as their character, e.g. "¨"), falling back to the US name. Saved hotkeys with an OEM key get their display name recomputed when settings load. Everything else keeps its English name.
10. **History pages instead of stopping at 200** (findings §12). The History page loads 200 at a time with a **Show older** button under the list; live additions never drop loaded pages. The history file is loaded once off the UI thread at start-up, so the first save after a sign-in start doesn't stall dictation.
11. **Calmer waveform** (findings §13). One bar per 50 ms of audio (counted in samples; 8 bars = 400 ms), bars shift in place instead of being removed and re-added, each bar falls at most 0.15 per step, and the capsule uses layout rounding. The owner judges the result by eye.

### Polish from the 1.1.0 reviews
12. **Microphone meter shows peaks**: the setup meter publishes the loudest level of each 50 ms window instead of the level of whichever chunk lands on the boundary.
13. **Model choice waits 1 s** before switching, so arrowing through the options doesn't start downloads; a newer choice within the second replaces it.
14. **A failed model-choice save is reported** under the choice ("Could not change the speech model (…)"), like other failed saves on the Settings page.
15. **Setup screens for keyboard and screen-reader users**: status changes on steps 2 and 3 are in a polite live region, and moving to the next step puts focus on the new step's heading.

## 4. Done when

- Unit and web tests named in the plan pass; the whole suite is green with zero warnings.
- On the owner's PC with the Bluetooth headset: the pill shows before "Microphone opened in N ms" in the log; Right Ctrl+C in an editor never opens the microphone (no beep, no headset profile switch); the waveform looks calm; a Nordic symbol-key hotkey shows its real character.

## 5. My calls (owner can override)

Copy for the pill notices, the hook-failure messages and the TLS error; one automatic hook retry after 2 s; tray + web alert for the hook failure; the modifiers-released rule for combos; TLS failing after 3 tries; fall back to the model on disk after a cut-short switch; "Show older" button with pages of 200 and the off-thread pre-load; 50 ms bars with 0.15 fall-off; 1 s model-choice debounce; keeping the version at 1.1.0.
