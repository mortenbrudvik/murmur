import { useState } from "react";
import StepFooter from "./StepFooter";
import { useMicLevel } from "./useMicLevel";
import { openLink } from "../settings/appActions";
import MicrophoneSelect from "../settings/MicrophoneSelect";
import { useMicrophones } from "../settings/useMicrophones";
import { useSettings } from "../settings/useSettings";
import { failure } from "../../interop/errorMessage";

interface Props {
  onNext: () => void;
  saveError: string | null;
}

/** How tall each of the bars gets at full volume; one volume drives all eight, and the shape keeps them from moving in lockstep. */
const BAR_SHAPE = [0.55, 0.8, 1, 0.75, 0.95, 0.65, 0.85, 0.5];
const BAR_MIN_PERCENT = 12;

function LevelMeter({ level }: { level: number }) {
  return (
    <span className="setup-meter" role="meter" aria-label="Microphone level" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(level * 100)}>
      {BAR_SHAPE.map((shape, index) => (
        <i key={index} className="setup-meter-bar" aria-hidden="true" style={{ height: `${BAR_MIN_PERCENT + (100 - BAR_MIN_PERCENT) * level * shape}%` }} />
      ))}
    </span>
  );
}

/** Listens on one microphone for as long as it is mounted: whether Windows lets Murmur in, and the level meter. */
function MicrophoneTest({ microphoneId }: { microphoneId: string | null }) {
  const { level, result } = useMicLevel(microphoneId);
  const [linkError, setLinkError] = useState<string | null>(null);

  async function openMicrophoneSettings() {
    setLinkError(null);
    try {
      await openLink("microphonePrivacy");
    } catch (err) {
      setLinkError(failure("Could not open the link", err));
    }
  }

  return (
    <div className="setup-row">
      <div className="setup-mic-status">
        {result?.state === "listening" && <span className="setup-ok setup-small">✓ Microphone access allowed</span>}
        {result?.state === "accessDenied" && (
          <>
            <span className="setup-small">Windows is blocking microphone access for Murmur.</span>
            <button type="button" className="setup-button" onClick={() => void openMicrophoneSettings()}>
              Open microphone settings
            </button>
          </>
        )}
        {result?.state === "failed" && (
          <p className="setup-error setup-small" role="alert">
            {result.error || "The microphone could not be opened."}
          </p>
        )}
        {linkError && (
          <p className="setup-error setup-small" role="alert">
            {linkError}
          </p>
        )}
      </div>
      <LevelMeter level={level} />
    </div>
  );
}

/** Step 2: pick the microphone, see that Windows allows it and that the bars move. */
export default function MicrophoneStep({ onNext, saveError }: Props) {
  const { settings, loading, error, edit } = useSettings();
  const { microphones, loaded, refresh } = useMicrophones();

  const microphoneId = settings?.microphoneId ?? null;

  return (
    <>
      <h1 className="setup-title">Check your microphone</h1>
      <p className="setup-muted">Say something — the bars should move.</p>
      <div className="setup-card">
        <div className="setup-row">
          <label htmlFor="setup-microphone">Microphone</label>
          <MicrophoneSelect
            id="setup-microphone"
            className="text-input setup-select"
            value={microphoneId}
            microphones={microphones}
            microphonesLoaded={loaded}
            disabled={loading}
            onOpen={() => void refresh()}
            // A failed save is reported by the hook as `error`, and the picker keeps showing the saved microphone.
            onChange={(chosen) => edit((current) => ({ ...current, microphoneId: chosen })).catch(() => undefined)}
          />
        </div>
        {loaded && microphones.length === 0 && <p className="setup-muted setup-small">No microphones found</p>}
        {error && (
          <p className="setup-error setup-small" role="alert">
            {error}
          </p>
        )}
        {/* Not before the saved microphone is known: the test would otherwise open the default one first and then switch. */}
        {!loading && <MicrophoneTest microphoneId={microphoneId} />}
      </div>
      <p className="setup-muted setup-small">
        Bluetooth headsets take about a second to switch on their microphone — wait for the start sound before you talk.
      </p>
      <StepFooter note="Problems? Windows Settings → Privacy → Microphone" action="Next" onAction={onNext} error={saveError} />
    </>
  );
}
