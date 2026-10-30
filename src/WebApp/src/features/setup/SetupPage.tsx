import { useEffect, useRef, useState } from "react";
import "./SetupPage.css";
import MicrophoneStep from "./MicrophoneStep";
import ModelStep from "./ModelStep";
import TryItStep from "./TryItStep";
import type { SetupPatch, useSetupState } from "./useSetupState";
import { failure } from "../../interop/errorMessage";
import type { EngineStatus, SetupState, SetupStep } from "../../types/domain";

const STEPS: { id: SetupStep; label: string }[] = [
  { id: "speechModel", label: "Speech model" },
  { id: "microphone", label: "Microphone" },
  { id: "tryIt", label: "Try it" },
];

interface Props {
  state: SetupState;
  /** The speech engine's status; null until it is known. */
  engineStatus: EngineStatus | null;
  update: ReturnType<typeof useSetupState>["update"];
  /** Setup is finished: the window goes on to the normal pages. */
  onDone: () => void;
}

/**
 * The first-run setup, in the main window without the sidebar: Speech model, Microphone, Try it. Closable at every
 * step, and no step has to be finished before the next: the model downloads in the background while the user goes on.
 */
export default function SetupPage({ state, engineStatus, update, onDone }: Props) {
  const [saveError, setSaveError] = useState<string | null>(null);

  // A step the server does not know is shown as the first one.
  const current = Math.max(
    0,
    STEPS.findIndex((step) => step.id === state.step),
  );

  // On a step change the focus goes to the new step's heading: the button that was pressed is gone with the old step.
  // Not on the first render, which is the window opening and not the user moving on.
  const inner = useRef<HTMLDivElement>(null);
  const shownStep = useRef(current);
  useEffect(() => {
    if (shownStep.current === current) return;
    shownStep.current = current;
    inner.current?.querySelector<HTMLElement>(".setup-title")?.focus();
  }, [current]);

  /** Saves the progress. A failure is reported, never allowed to hold the user back: the page has already moved on. */
  async function save(patch: SetupPatch) {
    setSaveError(null);
    try {
      await update(patch);
    } catch (err) {
      setSaveError(failure("Could not save your progress", err));
    }
  }

  async function finish() {
    // The setup is shown again at the next start if this was not saved, which is better than a button that does nothing.
    await save({ completed: true });
    onDone();
  }

  return (
    <div className="setup">
      <div className="setup-inner" ref={inner}>
        <ol className="setup-steps" aria-label="Setup steps">
          {STEPS.map((step, index) => {
            const status = index < current ? "done" : index === current ? "on" : "todo";
            return (
              <li key={step.id} className={`setup-chip setup-chip-${status}`} aria-current={status === "on" ? "step" : undefined}>
                {status === "done" ? `✓ ${step.label}` : `${index + 1} · ${step.label}`}
              </li>
            );
          })}
        </ol>

        {current === 0 && (
          <ModelStep
            engineStatus={engineStatus}
            cudaTipDismissed={state.cudaTipDismissed}
            onDismissCudaTip={() => void save({ cudaTipDismissed: true })}
            onNext={() => void save({ step: "microphone" })}
            saveError={saveError}
          />
        )}
        {current === 1 && <MicrophoneStep onNext={() => void save({ step: "tryIt" })} saveError={saveError} />}
        {current === 2 && <TryItStep engineStatus={engineStatus} onDone={() => void finish()} saveError={saveError} />}
      </div>
    </div>
  );
}
