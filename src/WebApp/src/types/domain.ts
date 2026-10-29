// Shapes exchanged with the .NET side over the IPC bridge (camelCase JSON, string enums,
// Guid ids as strings, timestamps as ISO-8601).

export interface HotkeyDefinition {
  virtualKey: number;
  /** Opaque to the web app: shown and passed back unchanged, never parsed. */
  modifiers: string;
  displayName: string;
}

export interface MurmurSettings {
  hotkey: HotkeyDefinition;
  microphoneId: string | null;
  soundCues: boolean;
  startWithWindows: boolean;
  keepAudio: boolean;
}

export interface ReplacementRule {
  spoken: string;
  written: string;
}

export interface Vocabulary {
  hintWords: string[];
  replacements: ReplacementRule[];
}

export interface HistoryEntry {
  id: string;
  timestamp: string;
  text: string;
  targetApp: string;
  pasted: boolean;
}

export interface MicrophoneInfo {
  id: string;
  name: string;
  isDefault: boolean;
}

export type EngineState = "notStarted" | "downloading" | "loading" | "ready" | "error";

export interface EngineStatus {
  state: EngineState;
  modelName: string;
  runtime: string | null;
  downloadProgress: number | null;
  error: string | null;
}

export type Page = "history" | "vocabulary" | "settings";
