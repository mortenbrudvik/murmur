import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../../interop/WebViewApiClient";
import { useSerialQueue } from "../../hooks/useSerialQueue";
import type { SetupState, SetupStep } from "../../types/domain";

/** The fields of the setup state a page can change; a field that is left out stays as it is. */
export interface SetupPatch {
  step?: SetupStep;
  cudaTipDismissed?: boolean;
  completed?: boolean;
}

/** What the window shows when the setup state cannot be read: the normal window, never a blank one. */
const NO_SETUP: SetupState = { showSetup: false, step: "speechModel", cudaTipDismissed: false, openPage: null };

/** The state as it is once the server has stored the patch. */
function applyPatch(state: SetupState, patch: SetupPatch): SetupState {
  return {
    ...state,
    step: patch.step ?? state.step,
    cudaTipDismissed: patch.cudaTipDismissed ?? state.cudaTipDismissed,
    showSetup: patch.completed === undefined ? state.showSetup : !patch.completed,
  };
}

/**
 * The first-run setup state: read once on mount, null until it is known. When it cannot be read the setup is not
 * shown, so the window falls back to the normal one. `openPage` (the page the launch asked for) is only handed out
 * once by the server, so it is read here once and stays in the state. It is dropped while the setup is shown, because
 * finishing the setup always leads to History.
 *
 * `update` shows a change at once and then saves it; saves go one after another. A save that fails rejects, but
 * the change stays shown: the setup never holds the user back, it only resumes at an earlier step the next time.
 */
export function useSetupState(): { state: SetupState | null; update(patch: SetupPatch): Promise<void> } {
  const [state, setState] = useState<SetupState | null>(null);
  const enqueue = useSerialQueue();

  // React mounts, unmounts and mounts again in development; the server only answers the first read with `openPage`,
  // so the read is made once per page and its answer is used by whichever mount is live when it arrives.
  const requested = useRef(false);
  const mounted = useRef(false);

  useEffect(() => {
    mounted.current = true;
    if (!requested.current) {
      requested.current = true;
      apiClient
        .call<SetupState | null>("setup", "getState", {})
        .then((loaded) => loaded ?? NO_SETUP)
        .catch(() => NO_SETUP)
        .then((answer) => {
          if (mounted.current) setState(answer.showSetup ? { ...answer, openPage: null } : answer);
        });
    }

    return () => {
      mounted.current = false;
    };
  }, []);

  const update = useCallback(
    (patch: SetupPatch) => {
      setState((current) => (current ? applyPatch(current, patch) : current));
      // The server's answer is the same document the patch makes here, so it is not read back.
      return enqueue(async () => {
        await apiClient.call<SetupState>("setup", "update", patch);
      });
    },
    [enqueue],
  );

  return { state, update };
}
