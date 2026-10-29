interface WebViewMessage {
  eventName: string;
  data: unknown;
}

type Listener = (data: unknown) => void;

export class WebViewApiClient {
  private listeners: { [eventName: string]: Listener[] } = {};
  private messageHandler: ((event: MessageEvent<unknown>) => void) | null = null;

  constructor() {
    if (window?.chrome?.webview) {
      this.messageHandler = (event: MessageEvent<unknown>) => {
        try {
          const msg = event.data as WebViewMessage | null | undefined;
          if (!msg || typeof msg.eventName !== "string") return;
          const { eventName, data } = msg;
          this.listeners[eventName]?.forEach((callback) => callback(data));
        } catch (err) {
          console.error("WebView message handler error:", err);
        }
      };
      window.chrome.webview.addEventListener("message", this.messageHandler);
    }
  }

  async call<T>(service: string, method: string, params: unknown = {}): Promise<T> {
    const jsonParams = JSON.stringify(params);
    if (!window.chrome?.webview?.hostObjects?.apibridge) {
      throw new Error("WebView2 API bridge not available");
    }
    const result = await window.chrome.webview.hostObjects.apibridge.InvokeMethod(
      service, method, jsonParams
    );
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

  dispose() {
    if (this.messageHandler && window?.chrome?.webview) {
      window.chrome.webview.removeEventListener("message", this.messageHandler);
    }
    this.messageHandler = null;
    this.listeners = {};
  }
}

export const apiClient = new WebViewApiClient();
