import { useState } from "react";
import "./HotkeyUnavailableBanner.css";
import { restartMurmur } from "../settings/appActions";
import { failure } from "../../interop/errorMessage";
import type { InputStatus } from "../../types/domain";

/**
 * A strip above every page (and the setup) when Windows refused Murmur's keyboard hook: without it the hotkey does
 * nothing, which would otherwise look like a Murmur that works but never answers. A restart is the way to try again.
 * Nothing is shown while the hook works or its status is still unknown.
 */
export default function HotkeyUnavailableBanner({ status }: { status: InputStatus | null }) {
  const [error, setError] = useState<string | null>(null);

  if (!status || status.hookInstalled) return null;

  async function restart() {
    setError(null);
    try {
      await restartMurmur();
    } catch (err) {
      setError(failure("Could not restart Murmur", err));
    }
  }

  return (
    <div className="hotkey-banner" role="alert">
      <span className="hotkey-banner-text">
        Windows didn't let Murmur watch the keyboard, so dictation is off. Restart Murmur to try again.
      </span>
      <button type="button" className="hotkey-banner-button" onClick={() => void restart()}>
        Restart Murmur
      </button>
      {error && <span className="hotkey-banner-error">{error}</span>}
    </div>
  );
}
