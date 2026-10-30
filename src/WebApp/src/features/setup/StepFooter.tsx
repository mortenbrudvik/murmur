import type { ReactNode } from "react";

interface Props {
  /** The small print on the left. */
  note: string;
  /** The label of the button on the right. */
  action: string;
  /** The last step's button is the solid one. */
  primary?: boolean;
  onAction: () => void;
  /** Why the progress could not be saved, when it could not. */
  error?: ReactNode;
}

/** The bottom of a setup step: a note and the button that goes on. The button is always enabled: no step blocks the next. */
export default function StepFooter({ note, action, primary = false, onAction, error }: Props) {
  return (
    <>
      {error && (
        <p className="page-error" role="alert">
          {error}
        </p>
      )}
      <footer className="setup-footer">
        <span className="setup-muted setup-small">{note}</span>
        <button type="button" className={primary ? "setup-button setup-button-primary" : "setup-button"} onClick={onAction}>
          {action}
        </button>
      </footer>
    </>
  );
}
