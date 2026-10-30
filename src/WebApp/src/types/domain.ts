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

export type SpeechModelId = "largeV3Turbo" | "smallEn";
export type SetupStep = "speechModel" | "microphone" | "tryIt";
export type GpuAdviceState = "notApplicable" | "cudaInUse" | "installCuda" | "updateDriver";
export type LinkTarget = "cudaDownload" | "nvidiaDriver" | "microphonePrivacy";
export type MicTestState = "listening" | "accessDenied" | "failed";

/** What to tell the user about CUDA on this PC; gpuName is the graphics card's DXGI description ("NVIDIA GeForce RTX 3060"). */
export interface GpuAdvice {
  state: GpuAdviceState;
  gpuName: string | null;
}

export interface DownloadProgress {
  modelName: string;
  bytesReceived: number;
  totalBytes: number;
  bytesPerSecond: number;
  secondsRemaining: number | null;
  /** Failed attempts in a row; 0 while bytes flow. */
  retries: number;
  reconnecting: boolean;
}

export interface EngineStatus {
  state: EngineState;
  /** Display name of the model the state is about ("Whisper large-v3-turbo"); empty before one is chosen. */
  modelName: string;
  runtime: string | null;
  /** Why the engine failed, or why a model switch failed while the previous model stays ready. */
  error: string | null;
  /** The model download in progress: the model itself while downloading, or the next one during a switch. */
  download: DownloadProgress | null;
  /** The tip about CUDA; null until the engine has looked at the graphics card. */
  gpu: GpuAdvice | null;
  /** True only on the loading status of a model switch: `modelName` is the model being loaded, and the previous one still dictates. */
  switching: boolean;
}

/** Whether Windows let Murmur install the keyboard hook the hotkey needs; `error` is why not, for support. */
export interface InputStatus {
  hookInstalled: boolean;
  error: string | null;
}

export type Page = "history" | "vocabulary" | "settings";

export interface SetupState {
  showSetup: boolean;
  step: SetupStep;
  cudaTipDismissed: boolean;
  openPage: Page | null;
}

export interface SpeechModelOption {
  id: SpeechModelId;
  label: string;
  sizeBytes: number;
}

export interface ModelChoice {
  selected: SpeechModelId;
  recommended: SpeechModelId;
  options: SpeechModelOption[];
}

export interface MicTestResult {
  state: MicTestState;
  error: string | null;
}
