import "./EngineStatus.css";
import DownloadBar from "../../components/DownloadBar";
import type { DownloadProgress, EngineStatus } from "../../types/domain";
import { describeEngineStatus, describeModelSwitch, type EngineStatusView } from "./engineStatusText";
import { formatDownloadLine } from "./formatDownload";

/** The progress bar, the text and the size, speed and time left of one status. */
function StatusLine({ view, detail }: { view: EngineStatusView; detail: string | null }) {
  return (
    <div className="engine-status">
      {view.progress !== null && <DownloadBar percent={view.progress} />}
      <span className={`engine-status-text engine-status-${view.tone}`}>{view.text}</span>
      {detail && <span className="engine-status-detail">{detail}</span>}
    </div>
  );
}

/** The engine's status text, with a progress bar and the amount downloaded while the first download runs. Shared by the card and the banner. */
export default function EngineStatusLine({ status }: { status: EngineStatus }) {
  const detail = status.state === "downloading" && status.download ? formatDownloadLine(status.download) : null;

  return <StatusLine view={describeEngineStatus(status)} detail={detail} />;
}

/** A model switch's download, which runs in the background while the old model stays ready: in the banner, and under the Settings model choice. */
export function ModelSwitchLine({ download }: { download: DownloadProgress }) {
  return <StatusLine view={describeModelSwitch(download)} detail={formatDownloadLine(download)} />;
}
