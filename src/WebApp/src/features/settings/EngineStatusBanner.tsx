import type { EngineStatus } from "../../types/domain";
import EngineStatusLine, { ModelSwitchLine } from "./EngineStatusLine";

/**
 * A thin strip above every page while the speech engine is not ready: the first-run model download shows up here.
 * A model switch downloads while the old model stays ready, so a download shows here in that state too. Nothing is
 * shown once the engine is ready and nothing downloads, or while its status is still unknown.
 */
export default function EngineStatusBanner({ status }: { status: EngineStatus | null }) {
  if (!status) return null;

  const switching = status.state === "ready" ? status.download : null;
  if (status.state === "ready" && !switching) return null;

  const failed = status.state === "error";
  // A download reports every few percent: the progress bar says it, so the text is not read out each time.
  const live = status.state === "downloading" || switching ? "off" : undefined;

  return (
    <div className={failed ? "engine-banner engine-banner-error" : "engine-banner"} role={failed ? "alert" : "status"} aria-live={live}>
      {switching ? <ModelSwitchLine download={switching} /> : <EngineStatusLine status={status} />}
    </div>
  );
}
