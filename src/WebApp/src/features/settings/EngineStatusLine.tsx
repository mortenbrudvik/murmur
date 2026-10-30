import "./EngineStatus.css";
import type { EngineStatus } from "../../types/domain";
import { describeEngineStatus } from "./engineStatusText";

/** The engine's status text, with a progress bar while the model downloads. Shared by the card and the banner. */
export default function EngineStatusLine({ status }: { status: EngineStatus }) {
  const view = describeEngineStatus(status);

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
    </div>
  );
}
