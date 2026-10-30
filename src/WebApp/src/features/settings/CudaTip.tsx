import { useState } from "react";
import "./CudaTip.css";
import { openLink, restartMurmur } from "./appActions";
import { failure } from "../../interop/errorMessage";
import type { GpuAdvice, LinkTarget } from "../../types/domain";

interface Props {
  /** What the engine found out about CUDA on this PC; null until it has looked. */
  advice: GpuAdvice | null;
  /** "setup" is the first-run page, "settings" the permanent tip on the engine card. */
  variant: "setup" | "settings";
  /** "Not now" in the setup variant; without it there is no such button. */
  onDismiss?: () => void;
}

/**
 * The tip about CUDA: shown only for an NVIDIA card that could use it (CUDA 13 not installed, or a driver too old for it).
 * CUDA is only picked up when Murmur starts, so the tip also offers to restart it.
 */
export default function CudaTip({ advice, variant, onDismiss }: Props) {
  // In the setup variant, Restart only makes sense once the user has gone to get CUDA.
  const [linkOpened, setLinkOpened] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!advice || (advice.state !== "installCuda" && advice.state !== "updateDriver")) return null;

  const installCuda = advice.state === "installCuda";
  const settings = variant === "settings";
  const gpuName = advice.gpuName || "NVIDIA graphics card";

  async function openTarget(target: LinkTarget) {
    setError(null);
    try {
      await openLink(target);
      setLinkOpened(true);
    } catch (err) {
      setError(failure("Could not open the link", err));
    }
  }

  async function restart() {
    setError(null);
    try {
      await restartMurmur();
    } catch (err) {
      setError(failure("Could not restart Murmur", err));
    }
  }

  return (
    <div className="cuda-tip">
      <span className="cuda-tip-icon" aria-hidden="true">
        ⚡
      </span>
      <div className="cuda-tip-body">
        {installCuda && !settings && (
          <>
            <p className="cuda-tip-title">{`Your ${gpuName} can run Murmur about twice as fast.`}</p>
            <p className="cuda-tip-detail">
              Murmur is using your graphics card through Vulkan. Install NVIDIA's free CUDA 13 toolkit (about 3 GB) and restart Murmur to use CUDA.
            </p>
          </>
        )}
        {installCuda && settings && (
          <p className="cuda-tip-text">{`CUDA isn't installed. Your ${gpuName} can run Murmur about twice as fast with CUDA 13.`}</p>
        )}
        {!installCuda && <p className="cuda-tip-text">Update your NVIDIA driver to use CUDA.</p>}

        <div className="cuda-tip-actions">
          <button type="button" className="cuda-tip-button cuda-tip-primary" onClick={() => void openTarget(installCuda ? "cudaDownload" : "nvidiaDriver")}>
            {installCuda ? "Get CUDA 13 ↗" : "Update NVIDIA driver ↗"}
          </button>
          {(settings || linkOpened) && (
            <button type="button" className="cuda-tip-button cuda-tip-ghost" onClick={() => void restart()}>
              Restart Murmur
            </button>
          )}
          {!settings && installCuda && onDismiss && (
            <button type="button" className="cuda-tip-button cuda-tip-dismiss" onClick={onDismiss}>
              Not now
            </button>
          )}
        </div>
        {error && (
          <p className="cuda-tip-error" role="alert">
            {error}
          </p>
        )}
      </div>
    </div>
  );
}
