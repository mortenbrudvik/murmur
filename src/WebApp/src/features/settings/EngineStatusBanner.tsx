import type { EngineStatus } from "../../types/domain";
import EngineStatusLine from "./EngineStatusLine";

/**
 * A thin strip above every page while the speech engine is not ready. First-run model download progress
 * shows up here. Nothing is shown once the engine is ready, or while its status is still unknown.
 */
export default function EngineStatusBanner({ status }: { status: EngineStatus | null }) {
  if (!status || status.state === "ready") return null;

  const failed = status.state === "error";
  // A download reports every few percent: the progress bar says it, so the text is not read out each time.
  const live = status.state === "downloading" ? "off" : undefined;

  return (
    <div className={failed ? "engine-banner engine-banner-error" : "engine-banner"} role={failed ? "alert" : "status"} aria-live={live}>
      <EngineStatusLine status={status} />
    </div>
  );
}
