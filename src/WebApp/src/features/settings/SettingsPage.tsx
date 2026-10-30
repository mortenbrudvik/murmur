import { useState } from "react";
import "./SettingsPage.css";
import CudaTip from "./CudaTip";
import EngineStatusLine from "./EngineStatusLine";
import SpeechModelSection from "./SpeechModelSection";
import { useMicrophones } from "./useMicrophones";
import { useSettings } from "./useSettings";
import type { EngineStatus, MurmurSettings } from "../../types/domain";

/** Which group of settings the last change came from, so a failed save is reported next to it. */
type Group = "hotkey" | "microphone" | "options";

type ToggleField = "soundCues" | "startWithWindows" | "keepAudio";

const TOGGLES: { field: ToggleField; label: string; hint: string }[] = [
  { field: "soundCues", label: "Sound cues", hint: "Play a short sound when recording starts and stops." },
  { field: "startWithWindows", label: "Start with Windows", hint: "Open Murmur in the tray when you sign in." },
  {
    field: "keepAudio",
    label: "Keep audio recordings",
    hint: "Save each dictation's audio with its history entry. When off, audio is discarded after transcription.",
  },
];

const CAPTURING_LABEL = "Press the key you want… (Esc to cancel)";

interface Props {
  /** The speech engine's status; null until it is known. */
  engineStatus: EngineStatus | null;
}

export default function SettingsPage({ engineStatus }: Props) {
  const { settings, loading, error, capturing, edit, captureHotkey } = useSettings();
  const { microphones, loaded: microphonesLoaded, refresh: refreshMicrophones } = useMicrophones();
  const [lastGroup, setLastGroup] = useState<Group>("hotkey");

  /** Saves the change; a failure is shown by the hook as `error`, so there is nothing more to do with it here. */
  function change(group: Group, update: (current: MurmurSettings) => MurmurSettings) {
    setLastGroup(group);
    edit(update).catch(() => undefined);
  }

  async function changeHotkey() {
    setLastGroup("hotkey");
    // Null when cancelled, timed out, failed or when this page was left in the meantime: nothing to save.
    const hotkey = await captureHotkey();
    if (hotkey) change("hotkey", (current) => ({ ...current, hotkey }));
  }

  const alertFor = (group: Group) =>
    error && lastGroup === group ? (
      <p className="page-error" role="alert">
        {error}
      </p>
    ) : null;

  // A saved microphone that is not in the list stays selectable, so saving something else does not change it.
  const savedMicrophone = settings?.microphoneId ?? null;
  const microphoneMissing = savedMicrophone !== null && !microphones.some((m) => m.id === savedMicrophone);

  return (
    <section className="page settings">
      <h1>Settings</h1>

      {error && !settings && (
        <p className="page-error" role="alert">
          {error}
        </p>
      )}
      {loading && !settings && <p className="page-muted">Loading…</p>}

      {settings && (
        <>
          <section className="settings-card" aria-labelledby="settings-hotkey-title">
            <div className="settings-row">
              <div className="settings-label">
                <h2 id="settings-hotkey-title">Hotkey</h2>
                <p className="settings-hint">Hold it to talk, release to paste.</p>
              </div>
              <div className="settings-control">
                <kbd className="settings-keycap">{settings.hotkey.displayName}</kbd>
                <button type="button" className="settings-button" disabled={capturing} onClick={() => void changeHotkey()}>
                  {capturing ? CAPTURING_LABEL : "Change"}
                </button>
              </div>
            </div>
            {alertFor("hotkey")}
          </section>

          <section className="settings-card">
            <div className="settings-row">
              <div className="settings-label">
                <label htmlFor="settings-microphone">Microphone</label>
              </div>
              <div className="settings-control">
                <select
                  id="settings-microphone"
                  className="text-input settings-select"
                  value={savedMicrophone ?? ""}
                  onFocus={() => void refreshMicrophones()}
                  onChange={(e) => {
                    // Read now: the event's target is put back to the saved value before the save runs.
                    const microphoneId = e.target.value || null;
                    change("microphone", (current) => ({ ...current, microphoneId }));
                  }}
                >
                  <option value="">Windows default</option>
                  {microphones.map((mic) => (
                    <option key={mic.id} value={mic.id}>
                      {mic.name}
                    </option>
                  ))}
                  {microphoneMissing && (
                    <option value={savedMicrophone}>{microphonesLoaded ? "Unavailable microphone" : "Loading…"}</option>
                  )}
                </select>
              </div>
            </div>
            {microphonesLoaded && microphones.length === 0 && <p className="settings-hint">No microphones found</p>}
            {alertFor("microphone")}
          </section>

          <section className="settings-card">
            {TOGGLES.map(({ field, label, hint }) => (
              <label className="settings-row settings-toggle" key={field}>
                <span className="settings-label">
                  <span className="settings-toggle-name" id={`settings-${field}-name`}>
                    {label}
                  </span>
                  <span className="settings-hint" id={`settings-${field}-hint`}>
                    {hint}
                  </span>
                </span>
                <input
                  type="checkbox"
                  role="switch"
                  className="settings-switch"
                  aria-labelledby={`settings-${field}-name`}
                  aria-describedby={`settings-${field}-hint`}
                  checked={settings[field]}
                  onChange={(e) => {
                    const on = e.target.checked;
                    change("options", (current) => ({ ...current, [field]: on }));
                  }}
                />
              </label>
            ))}
            {alertFor("options")}
          </section>
        </>
      )}

      <section className="settings-card settings-engine" aria-label="Speech engine">
        {engineStatus?.modelName && <h2 className="settings-engine-name">{engineStatus.modelName}</h2>}
        {engineStatus && (
          <>
            <EngineStatusLine status={engineStatus} />
            <CudaTip advice={engineStatus.gpu} variant="settings" />
          </>
        )}
        <SpeechModelSection status={engineStatus} />
      </section>
    </section>
  );
}
