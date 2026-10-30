import "./EngineStatus.css";
import type { EngineStatus } from "../../types/domain";
import { describeEngineStatus } from "./engineStatusText";
import { describeDownload } from "./formatDownload";

/** "612 MB of 1.6 GB · 18 MB/s · about 1 minute left" for the first download; null at any other time. */
function downloadDetail(status: EngineStatus): string | null {
  if (status.state !== "downloading" || !status.download) return null;

  const { amount, detail } = describeDownload(status.download);
  // While reconnecting the status text already says so.
  return detail && !status.download.reconnecting ? `${amount} · ${detail}` : amount;
}

/** The engine's status text, with a progress bar while the model downloads. Shared by the card and the banner. */
export default function EngineStatusLine({ status }: { status: EngineStatus }) {
  const view = describeEngineStatus(status);
  const detail = downloadDetail(status);

  return (
    <div className="engine-status">
      {view.progress !== null && (
        <div
          className="engine-progress"
          role="progressbar"
          aria-label="Model download"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={view.progress}
        >
          <div className="engine-progress-fill" style={{ width: `${view.progress}%` }} />
        </div>
      )}
      <span className={`engine-status-text engine-status-${view.tone}`}>{view.text}</span>
      {detail && <span className="engine-status-detail">{detail}</span>}
    </div>
  );
}
