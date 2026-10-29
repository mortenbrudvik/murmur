# Murmur — Design

**Date:** 2026-10-29
**Status:** Approved 2026-10-29
**Mockups:** `docs/superpowers/specs/mockups/` (`pill.html` option **A**, `main-window.html` option **A**). These are fragments from the brainstorming companion; the inline styles show the look, but the card layout around them is missing when opened standalone.

---

## 1. Intent and why

Murmur is voice typing for Windows that actually works.

The moment that started it: typing out an answer to Claude that could just have been said out loud. Windows' built-in voice typing (Win+H) is not good enough — it produced garbled text, and its dialog stays open after you're done.

The model to follow is **superwhisper** (https://superwhisper.com/): press a hotkey, talk, and the text lands in whatever app you're in. Murmur takes that core loop, runs it entirely on the owner's own PC (RTX 3090), and leaves out everything else for v1.

Murmur is **for the owner only for now**, possibly shared later. So: no accounts, no installer in v1, but nothing personal hard-coded (paths, device names) that would block handing it to someone else later.

**Success:** the owner stops typing answers to Claude and talks instead.

## 2. Goals (v1)

1. **Accurate English dictation.** Words come out as said. Names and jargon come out right once added to the vocabulary.
2. **Gets out of the way.** Shows a tiny pill only while recording/transcribing; never takes focus; disappears the instant text is pasted.
3. **Fast.** Pill appears immediately on keypress; text is pasted within about a second of releasing the key for a typical utterance (up to ~30 s of speech).
4. **Works in any text field.** Paste into whatever has focus — Claude Code in Windows Terminal, Claude in the browser/desktop app, VS Code, Outlook, Teams, Notepad, browser text fields.
5. **Nothing is ever lost.** Every dictation lands in history, even if pasting fails.
6. **Private and free.** All speech recognition runs locally on the GPU. No audio or text leaves the PC.

## 3. Non-goals for v1 (planned for later)

- **AI modes** — an LLM pass after transcription (filler-word cleanup, "no wait, I meant…" handling, message/email tones, custom prompts), per-app auto-switching, rewriting selected text. The 3090 can run a small local LLM for this later. **v1 must be structured so a post-processing step can slot into the pipeline without rework** (see §6.1).
- **Live words while talking** (pill option C in the mockup) — needs streaming transcription.
- **Installer and sharing** — the owner has a `wpf-installer` skill for this later.
- **Other speech engines** — see §6.3.
- **Tray flyout** (main-window option C).

## 4. Anti-goals — failures even if it "works"

- **Changing what was said.** v1 does no AI rewriting. The only text changes allowed are Whisper's own punctuation/casing and the owner's explicit vocabulary replacement rules.
- **Garbage from silence.** Whisper is known to "hallucinate" phrases like "Thank you." or "you" on silent or near-silent audio. An accidental tap or a silent recording must paste nothing.
- **Stealing focus / pasting in the wrong place.** The pill must never become the active window.
- **Lingering UI.** No dialog, toast, or window left open after a dictation.
- **Clipped first word.** Recording must capture speech from the moment the key goes down.
- **Losing a dictation.** If paste can't happen, the text must still be in history (and on the clipboard — see §5.4).
- **Trashing the clipboard.** Whatever the owner had copied before dictating must be back afterwards.
- **Slow warm-up.** The model stays loaded; no per-dictation load time.
- **Audio leaving the machine.**

## 5. User experience

### 5.1 Recording flow

- **Hotkey:** default **Right Ctrl**, configurable. (Owner may not use Right Ctrl; the setting must allow single-key "hold" hotkeys such as Right Ctrl / Right Alt, and ordinary combinations.)
- **Hold to talk:** press and hold → recording starts immediately and the pill appears. Release → stop, transcribe, paste, pill disappears.
- **Tap for hands-free:** a quick tap (press + release under a short threshold) starts hands-free recording; the next tap stops it, then transcribe → paste as above. *(Owner's decision: single tap, as in superwhisper.)*
- **Stray-tap guard:** if no speech is detected within about **8 seconds** of a hands-free start, the recording cancels quietly — nothing pasted, nothing saved, pill disappears. *(Owner's decision; exact timeout is builder's call.)*
- **Hands-free time limit:** when a hands-free recording hits the upper limit (§6.2), it stops, is transcribed and saved to history, but is **not pasted**; the pill briefly shows "Saved to history" and disappears. Rationale: if it ran that long, the owner probably isn't at the target window anymore. *(My call.)*
- **Esc cancels** while recording or transcribing: nothing is pasted, nothing is added to history, pill disappears. Esc is only intercepted while Murmur is recording/transcribing; otherwise it passes through untouched.
- **Using the hotkey key normally:** if another key is pressed, or a mouse button is clicked or the wheel scrolled, while the hotkey key is held (e.g. Right Ctrl+C, Ctrl+click, Ctrl+scroll), Murmur silently cancels the recording and lets the input through. Murmur must not break the hotkey key's normal use in combinations.
- **Never stuck recording:** the recording state machine must recover if a key release is missed (see §5.4 on admin windows) — e.g. by re-checking the physical key state — rather than staying in "recording" forever. *(Method is builder's call.)*
- **Optional sound cues** (on by default): a soft sound on start and on stop. Settings toggle.
- **Silence / too short:** if no speech was detected, nothing is pasted or saved; the pill simply disappears.
- **Paste target:** whatever window has focus when the text is ready.
- **Inserted text:** the transcript trimmed of leading/trailing whitespace, with vocabulary replacement rules applied. No automatic trailing space. *(Builder's call — revisit if it feels wrong in use.)*

### 5.2 The pill (mockup `pill.html`, option A — "Barely there")

- Small dark capsule (dark translucent background, subtle border and shadow), **bottom-center, just above the taskbar**, on the monitor of the window that has focus.
- **Listening:** red dot + small waveform driven by the live microphone level.
- **Transcribing:** the waveform is replaced by three pulsing dots.
- **Done:** disappears.
- **No buttons.** Release the key (or tap again) to finish, Esc to cancel. Because it has no controls, it should also be click-through.
- **Must never take focus:** not activatable, not shown in Alt+Tab or the taskbar, always on top.
- Appears effectively instantly on keypress (target under ~100 ms) — keep the window created and hidden rather than creating it per dictation.
- **Implemented as a native WPF window, not WebView2** (my call): it must appear instantly and the no-focus/click-through window behaviour is simplest and most reliable in native WPF.

### 5.3 Main window (mockup `main-window.html`, option A — "Classic sidebar")

React app hosted in WebView2 inside the WPF-UI `FluentWindow` from the owner's hybrid template. Opened from the tray; closing it hides it (Murmur keeps running in the tray). Dark look to match the pill and mockup.

Sidebar pages: **History**, **Vocabulary**, **Settings** — with room for a **Modes** page later.

- **History:** newest first, grouped by day (Today / Yesterday / date). Each entry: the text, the app it was pasted into, the time, and a **Copy** button. Search box at the top. Delete single entries and clear all. Stored locally; kept until deleted.
- **Vocabulary:** two lists.
  - **Hint words** (e.g. "WebView2", "Murmur", "WPF") — fed to the model as context so it favours those spellings.
  - **Replacement rules** (e.g. "web view two" → "WebView2") — applied to the transcript after recognition, case-insensitive, whole-word/phrase matches.
- **Settings:** hotkey (with a "press the key you want" recorder), microphone (default: follow the Windows default device), sound cues on/off, start with Windows on/off, keep audio recordings on/off (default **off**; useful later for testing other engines on the owner's voice), and a read-only status of the speech engine (model name, loading/ready/error, GPU in use).

### 5.4 Tray, startup, and failure handling

- Tray icon is the app's home. Left-click opens the main window. Menu: Open Murmur, Quit. Icon reflects state at least for "ready" vs "not ready" (model loading or error).
- **Start with Windows** via the per-user startup mechanism (no admin rights needed).
- **Single instance.**
- **Model not ready** (still loading, or failed): pressing the hotkey must not appear to work silently and then do nothing. Give a brief, non-focus-stealing indication via the pill (e.g. a short "loading model" state) and nothing else.
- **Admin (elevated) windows:** the owner never runs the apps they dictate into as administrator, so Murmur stays a normal-privilege app. Windows (UIPI) imposes two limits that are accepted, not worked around:
  - While an admin window has focus, Murmur's keyboard hook receives no keystrokes, so the hotkey simply doesn't work there. Accepted; no admin/`uiAccess` workaround in v1.
  - If focus moves to an admin window *during* recording or transcription, Ctrl+V into it is blocked. In that case leave the text on the clipboard (do not restore the old clipboard), keep it in history, and briefly show "Copied — press Ctrl+V" in the pill before it disappears. The key release may also be missed in this situation — covered by "Never stuck recording" in §5.1. *(Detecting the elevated target vs. detecting failure is builder's call.)*
- **First run:** the model file is downloaded automatically into Murmur's local app-data folder, with progress shown in the main window and the tray icon showing "not ready" until done.

## 6. How it works

### 6.1 Pipeline

```
hotkey down → capture audio → hotkey up / tap
  → speech check (discard if silence)
  → transcribe (engine)
  → post-process (vocabulary replacement rules)    ← AI modes slot in here later
  → insert (clipboard paste)
  → save to history
```

Each stage behind its own interface so the engine can be swapped and a post-processing step (AI modes) can be added later without restructuring.

### 6.2 Audio capture

- 16 kHz mono (what Whisper expects).
- The first word must not be clipped. Opening the microphone on keypress may take long enough to clip the start; keeping it open permanently means Windows shows its "microphone in use" indicator all the time. **Builder's call** how to resolve this (e.g. open on keypress if measured fast enough, or a warm stream with a short pre-roll buffer) — the requirement is no clipping and no audio retained beyond the current dictation unless "keep audio" is on.
- Waveform level for the pill comes from this stream.
- Hands-free recordings need some upper limit so a forgotten tap doesn't record forever (builder picks; something like 10 minutes).

### 6.3 Speech engine

- **v1 engine:** OpenAI **Whisper large-v3-turbo**, running in-process on the RTX 3090 via **whisper.cpp** through the **Whisper.net** .NET bindings with the CUDA runtime (verify current package names/versions at build time). Language fixed to **English**. Model loaded once at startup and kept in VRAM.
- **Why Whisper for v1** (my call): it accepts a text prompt, which is how the vocabulary hint words are applied, and it runs natively inside a C# app with no Python sidecar.
- **Silence/hallucination guard:** use voice-activity detection (whisper.cpp has VAD support) or an energy gate before transcription, plus whisper's no-speech signal, so silent/near-silent audio produces nothing.
- **Later candidates** behind the same engine interface: newer models that score slightly better on English benchmarks (as of the May 2026 Open ASR Leaderboard snapshot: IBM Granite Speech 4.1, NVIDIA Canary-Qwen 2.5B, NVIDIA Parakeet TDT 0.6B). Compare on the owner's own voice (using kept audio) before switching. Not v1.
- Model quantization (full vs. quantized ggml) is the builder's call — the 3090 has 24 GB VRAM, so accuracy should win over size.

### 6.4 Text insertion

- Save the current clipboard contents (all formats), put the transcript on the clipboard, send Ctrl+V to the focused window, then restore the saved clipboard after a short delay (long enough for slow apps to read it).
- Mark Murmur's clipboard writes — both the transcript and the restored previous contents — so they are **excluded from Windows clipboard history (Win+V)** and cloud clipboard (otherwise every dictation adds duplicate entries).
- Record the target window's app name for the history entry.

### 6.5 Hotkey

- Requires a **low-level keyboard hook** — ordinary registered hotkeys can't detect hold/release of a single modifier key like Right Ctrl.
- Hold vs. tap is decided by press duration; the threshold is the builder's call (a few hundred ms).

## 7. Platform and project setup

- **Scaffold with the owner's `wpf-scaffold` skill, Hybrid type** (WPF + WebView2 + React). That gives: `Murmur.UI`, `Murmur.Core`, `Murmur.WebView`, `Murmur.Interop`, `Murmur.Common`, `WebApp/` (Vite + React + TypeScript), test projects, .NET 10, WPF-UI `FluentWindow`, Autofac, and the ApiBridge IPC between React and .NET. Follow the owner's `wpf-dev-guide` conventions for services, views, and IPC.
- Main-window pages are React; data and actions (history, vocabulary, settings, engine status) go over the template's IPC bridge. The engine status and history should update live in the window while it's open.
- Runs as a normal (non-elevated) user process.
- Local data (settings, vocabulary, history, model, optional audio) lives under the user's local app-data folder. Storage format is the builder's call.
- No installer in v1; run from the build output.

## 8. Done for v1

- Dictation works end to end, hold and tap, in: Windows Terminal (Claude Code), a browser text field (claude.ai), VS Code, Outlook, Teams, Notepad.
- Pill appears instantly, never takes focus, and disappears after paste/cancel.
- Release-to-paste is about a second for typical utterances on the 3090.
- Silent recordings paste nothing; an accidental tap followed by silence auto-cancels within ~8 s.
- Right Ctrl+C, Ctrl+click, Ctrl+scroll (and other combos with the hotkey key) still work normally.
- Clipboard contents are restored after dictation; Murmur's writes do not appear in Win+V history.
- History, Vocabulary (both lists), and Settings pages work; changes take effect without restarting.
- If focus moves to an admin window mid-dictation, the text is left on the clipboard and in history, and Murmur does not get stuck recording.
- Unit tests for the pure logic: hold/tap/cancel/auto-cancel/time-limit state machine (including missed key release), replacement rules, silence decision, history store.

## 9. Decisions record

**Decided by the owner**
- Voice typing for Windows modelled on superwhisper's core behaviour.
- For personal use now, maybe shared later.
- WPF hybrid with an embedded web app.
- Runs on their RTX 3090.
- English only.
- v1 scope as recommended: core dictation loop + vocabulary + history + settings + tray; AI modes later; meetings/file transcription/system audio/translation/mobile excluded.
- Pill design A ("Barely there").
- Main window design A ("Classic sidebar").
- Never dictates into admin windows → Murmur stays a normal-privilege app; the hotkey not working in admin windows is accepted.
- Hands-free starts with a single tap and auto-cancels if no speech within ~8 seconds.

**My calls (owner can override)**
- Local speech recognition only; no cloud option.
- Whisper large-v3-turbo via whisper.cpp/Whisper.net (CUDA), English locked, vocabulary as prompt hints + replacement rules.
- Right Ctrl as default hotkey; hold to talk, tap for hands-free, Esc to cancel.
- Pill as a native WPF window; main window in React/WebView2.
- Paste via clipboard with restore, excluded from Win+V history.
- Audio not kept by default (setting to keep it).
- History kept until deleted.
- No automatic trailing space on inserted text.
- A hands-free recording that hits the time limit is saved to history but not pasted.
- Dark look to match the mockups.

## 10. Left to the builder

- Hold/tap threshold, sound-cue sounds, clipboard restore delay, hands-free time limit.
- How to avoid clipping the first word (mic-open strategy).
- VAD/silence method and thresholds.
- Model quantization and download source.
- Storage format for settings/history/vocabulary.
- How the elevated-window case is detected, and how a missed key release is recovered.
- Whether kept audio files are linked to their history entries.
- Behaviour if CUDA/GPU is unavailable (clear "not ready" status is the minimum).
- Exact pill dimensions/animation, within the mockup's look.
- Project-internal placement of services within the template's structure.

## 11. Later (v2+)

AI modes (local LLM on the 3090, e.g. cleanup / message / email / custom prompts; per-app auto-switching; rewrite selected text) · live words in the pill · alternative speech engines tested on the owner's voice · installer and sharing · tray flyout.
