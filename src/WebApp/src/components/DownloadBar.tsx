import "./DownloadBar.css";

interface Props {
  /** 0..100. */
  percent: number;
  /** A variant of the bar ("setup-bar" on the setup page). */
  className?: string;
}

/** The progress bar of a model download: in the engine status line, the model switch and the setup's model step. */
export default function DownloadBar({ percent, className }: Props) {
  return (
    <div
      className={className ? `download-bar ${className}` : "download-bar"}
      role="progressbar"
      aria-label="Model download"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={percent}
    >
      <div className="download-bar-fill" style={{ width: `${percent}%` }} />
    </div>
  );
}
