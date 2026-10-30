import StepFooter from "./StepFooter";
import "../settings/EngineStatus.css";
import CudaTip from "../settings/CudaTip";
import { describeEngineStatus } from "../settings/engineStatusText";
import { describeDownload, formatBytes } from "../settings/formatDownload";
import type { EngineStatus } from "../../types/domain";

interface Props {
  /** The speech engine's status; null until it is known. */
  engineStatus: EngineStatus | null;
  cudaTipDismissed: boolean;
  onDismissCudaTip: () => void;
  onNext: () => void;
  saveError: string | null;
}

/** The card under the explanation: the download with its progress, or the model loading, ready or failed. */
function ModelCard({ status }: { status: EngineStatus | null }) {
  if (status?.state === "downloading") {
    // The download is null for a moment between the engine starting the download and the first progress report.
    const progress = status.download ? describeDownload(status.download) : null;
    return (
      <div className="setup-card">
        <div className="setup-row">
          <span>Downloading speech model</span>
          {progress && <span className="setup-muted setup-small">{progress.amount}</span>}
        </div>
        <div
          className="engine-progress setup-bar"
          role="progressbar"
          aria-label="Model download"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={progress?.percent ?? 0}
        >
          <div className="engine-progress-fill" style={{ width: `${progress?.percent ?? 0}%` }} />
        </div>
        <div className="setup-row setup-muted setup-small">
          <span>{progress?.detail}</span>
          <span>Resumes if interrupted</span>
        </div>
      </div>
    );
  }

  if (status?.state === "ready") {
    return (
      <div className="setup-card">
        <span className="setup-ok">✓ Speech model ready</span>
      </div>
    );
  }

  if (status?.state === "error") {
    return (
      <div className="setup-card">
        <p className="setup-error" role="alert">
          {describeEngineStatus(status).text}
        </p>
      </div>
    );
  }

  // Loading, not started and not yet known: from the user's side the model is on its way.
  return (
    <div className="setup-card">
      <span>Loading the speech model…</span>
    </div>
  );
}

/**
 * "Murmur keeps downloading" is only true until the download is over: before the first status and before the download
 * starts it is what is about to happen; while the model loads, is ready or has failed, nothing is downloading.
 */
function downloadIsComing(status: EngineStatus | null): boolean {
  return status === null || status.state === "downloading" || status.state === "notStarted";
}

/** Step 1: the speech model downloads while the user goes on, and the CUDA tip is offered once. */
export default function ModelStep({ engineStatus, cudaTipDismissed, onDismissCudaTip, onNext, saveError }: Props) {
  const total = engineStatus?.download?.totalBytes ?? 0;
  const size = total > 0 ? ` — about ${formatBytes(total)}` : "";
  // The footer keeps its note's place when the note is gone, so Next stays on the right.
  const note = downloadIsComing(engineStatus) ? "You can close this window — Murmur keeps downloading." : "";

  return (
    <>
      <h1 className="setup-title">Getting Murmur ready</h1>
      <p className="setup-muted">{`Murmur turns speech into text on this PC. It needs its speech model once${size}.`}</p>
      <ModelCard status={engineStatus} />
      {!cudaTipDismissed && <CudaTip advice={engineStatus?.gpu ?? null} variant="setup" onDismiss={onDismissCudaTip} />}
      <StepFooter note={note} action="Next" onAction={onNext} error={saveError} />
    </>
  );
}
