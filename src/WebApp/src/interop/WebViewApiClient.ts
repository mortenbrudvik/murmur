interface WebViewMessage {
  eventName: string;
  data: unknown;
}

type Listener = (data: unknown) => void;

/**
 * How long a call may take before the page gives up on it. Every handler answers within milliseconds, so this only
 * ends a call whose handler hangs, which would otherwise leave the page loading, capturing or saving for ever.
 */
export const DEFAULT_TIMEOUT_MS = 10_000;

export interface CallOptions {
  /** Milliseconds before the call is given up on, for a handler that waits on the user or a device; null waits for ever. */
  timeoutMs?: number | null;
}

/** A call the .NET side did not answer in time. Its answer, should it still come, is ignored. */
export class ApiTimeoutError extends Error {
  readonly service: string;
  readonly method: string;

  constructor(service: string, method: string) {
    super("Murmur did not answer in time.");
    this.name = "ApiTimeoutError";
    this.service = service;
    this.method = method;
  }
}

export class WebViewApiClient {
  private listeners: { [eventName: string]: Listener[] } = {};

  // One client lives as long as the page, so the message listener is never removed.
  constructor() {
    if (window?.chrome?.webview) {
      window.chrome.webview.addEventListener("message", (event: MessageEvent<unknown>) => {
        try {
          const msg = event.data as WebViewMessage | null | undefined;
          if (!msg || typeof msg.eventName !== "string") return;
          const { eventName, data } = msg;
          this.listeners[eventName]?.forEach((callback) => callback(data));
        } catch (err) {
          console.error("WebView message handler error:", err);
        }
      });
    }
  }

  async call<T>(service: string, method: string, params: unknown = {}, options: CallOptions = {}): Promise<T> {
    const jsonParams = JSON.stringify(params);
    if (!window.chrome?.webview?.hostObjects?.apibridge) {
      throw new Error("WebView2 API bridge not available");
    }
    const invocation = window.chrome.webview.hostObjects.apibridge.InvokeMethod(service, method, jsonParams);
    const timeoutMs = options.timeoutMs === undefined ? DEFAULT_TIMEOUT_MS : options.timeoutMs;
    const result = timeoutMs === null ? await invocation : await withTimeout(invocation, timeoutMs, service, method);
    const parsed = JSON.parse(result);
    if (parsed.success === false) {
      throw new Error(parsed.error || `API call failed: ${service}.${method}`);
    }
    return parsed.data as T;
  }

  on<T = unknown>(eventName: string, callback: (data: T) => void): () => void {
    const listener = callback as Listener;
    if (!this.listeners[eventName]) this.listeners[eventName] = [];
    this.listeners[eventName].push(listener);

    return () => {
      this.listeners[eventName] = this.listeners[eventName].filter((cb) => cb !== listener);
      if (this.listeners[eventName].length === 0) {
        delete this.listeners[eventName];
      }
    };
  }
}

/** The invocation's answer, or an ApiTimeoutError once `timeoutMs` has passed without one. */
async function withTimeout(invocation: Promise<string>, timeoutMs: number, service: string, method: string): Promise<string> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new ApiTimeoutError(service, method)), timeoutMs);
  });
  try {
    return await Promise.race([invocation, timeout]);
  } finally {
    clearTimeout(timer);
  }
}

export const apiClient = new WebViewApiClient();
