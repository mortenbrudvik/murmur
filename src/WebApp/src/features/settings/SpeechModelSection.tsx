import { useId } from "react";
import { ModelSwitchLine } from "./EngineStatusLine";
import { useModelChoice } from "./useModelChoice";
import type { EngineStatus } from "../../types/domain";

const RECOMMENDED_SUFFIX = " — recommended for this PC";

/**
 * Which speech model Murmur uses: a radio group of the models on offer. Switching downloads the other model in the
 * background while the current one keeps working, so while the engine is ready its download is shown under the group
 * (the banner above the page leaves it out on Settings, so there is one bar).
 * A choice is shown at once and sent a second after the last change; a choice that could not be sent is reported under
 * the group and the saved one is selected again.
 */
export default function SpeechModelSection({ status }: { status: EngineStatus | null }) {
  const { choice, error, select } = useModelChoice();
  const legendId = useId();

  if (!choice) return null;

  const switching = status?.state === "ready" ? status.download : null;
  // A failed switch leaves the engine ready on the old model, with the reason in the status.
  const switchError = status?.state === "ready" ? status.error : null;

  return (
    <div className="speech-model">
      <div role="radiogroup" aria-labelledby={legendId}>
        <p className="speech-model-title" id={legendId}>
          Speech model
        </p>
        {choice.options.map((option) => (
          <label className="speech-model-option" key={option.id}>
            <input
              type="radio"
              name="speech-model"
              value={option.id}
              checked={choice.selected === option.id}
              onChange={() => select(option.id)}
            />
            <span>{option.label + (option.id === choice.recommended ? RECOMMENDED_SUFFIX : "")}</span>
          </label>
        ))}
      </div>

      {switching && (
        <div className="speech-model-download">
          <ModelSwitchLine download={switching} />
        </div>
      )}

      {error && (
        <p className="page-error" role="alert">
          {error}
        </p>
      )}

      {switchError && (
        <p className="page-error" role="alert">
          {switchError}
        </p>
      )}
    </div>
  );
}
