import { useState } from "react";
import StepFooter from "./StepFooter";
import { describeEngineStatus } from "../settings/engineStatusText";
import { useHotkeyName } from "../history/useHotkeyName";
import { useWebViewEvent } from "../../hooks/useWebViewEvent";
import type { EngineStatus, HistoryEntry } from "../../types/domain";

interface Props {
  /** The speech engine's status; null until it is known. */
  engineStatus: EngineStatus | null;
  onDone: () => void;
  saveError: string | null;
}

/** Step 3: a practice box for the first real dictation. */
export default function TryItStep({ engineStatus, onDone, saveError }: Props) {
  const hotkey = useHotkeyName();
  const [dictated, setDictated] = useState(false);

  // A dictation that finishes while this step is open is the proof that everything works.
  useWebViewEvent<HistoryEntry>("history.added", () => setDictated(true));

  const ready = engineStatus?.state === "ready";

  return (
    <>
      <h1 className="setup-title">Try it</h1>
      {ready ? (
        <>
          <p className="setup-muted">
            Click the box, hold <kbd className="setup-key">{hotkey}</kbd>, say a sentence, and let go.
          </p>
          <textarea className="text-input setup-practice" aria-label="Practice dictation" rows={3} />
          {dictated && <p className="setup-ok">✓ That's it — Murmur types wherever your cursor is.</p>}
          <p className="setup-muted setup-small">
            Tap <kbd className="setup-key">{hotkey}</kbd> once for hands-free; tap again to finish. Esc cancels. Change the key in Settings.
          </p>
        </>
      ) : (
        <>
          <p className="setup-muted">Waiting for the speech model…</p>
          {engineStatus?.state === "error" && (
            <p className="setup-error" role="alert">
              {describeEngineStatus(engineStatus).text}
            </p>
          )}
        </>
      )}
      <StepFooter note="Murmur lives in the tray from now on." action="Done" primary onAction={onDone} error={saveError} />
    </>
  );
}
