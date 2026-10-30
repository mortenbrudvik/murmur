import type { MicrophoneInfo } from "../../types/domain";

interface Props {
  id: string;
  className: string;
  /** The saved microphone; null is the Windows default. */
  value: string | null;
  microphones: MicrophoneInfo[];
  /** False until the list has been read, so a saved microphone is not called unavailable too early. */
  microphonesLoaded: boolean;
  /** True while the saved choice is still being read, so the picker does not show "Windows default" for a moment. */
  disabled?: boolean;
  /** The list is read again when the picker gets focus. */
  onOpen: () => void;
  onChange: (microphoneId: string | null) => void;
}

/** The microphone picker of the Settings page and of the setup page: the same list, with the saved choice kept selectable. */
export default function MicrophoneSelect({ id, className, value, microphones, microphonesLoaded, disabled, onOpen, onChange }: Props) {
  // A saved microphone that is not in the list stays selectable, so saving something else does not change it.
  const missing = value !== null && !microphones.some((m) => m.id === value);

  return (
    <select
      id={id}
      className={className}
      value={value ?? ""}
      disabled={disabled}
      onFocus={onOpen}
      onChange={(e) => {
        // Read now: the event's target is put back to the saved value before the save runs.
        onChange(e.target.value || null);
      }}
    >
      <option value="">Windows default</option>
      {microphones.map((mic) => (
        <option key={mic.id} value={mic.id}>
          {mic.name}
        </option>
      ))}
      {missing && <option value={value}>{microphonesLoaded ? "Unavailable microphone" : "Loading…"}</option>}
    </select>
  );
}
