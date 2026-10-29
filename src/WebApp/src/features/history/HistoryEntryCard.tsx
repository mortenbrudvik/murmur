import type { HistoryEntry } from "../../types/domain";
import CopyButton from "./CopyButton";
import { formatTime } from "./formatTime";

interface Props {
  entry: HistoryEntry;
  onDelete: (id: string) => void;
}

export default function HistoryEntryCard({ entry, onDelete }: Props) {
  return (
    <li className="history-card">
      <div className="history-card-body">
        <p className="history-card-text">{entry.text}</p>
        <p className="history-card-meta">
          <span>{`${entry.targetApp || "—"} · ${formatTime(entry.timestamp)}`}</span>
          {!entry.pasted && <span className="history-tag">Not pasted</span>}
        </p>
      </div>
      <div className="history-card-actions">
        <CopyButton text={entry.text} />
        <button
          type="button"
          className="history-action history-delete"
          aria-label="Delete"
          title="Delete"
          onClick={() => onDelete(entry.id)}
        >
          ×
        </button>
      </div>
    </li>
  );
}
