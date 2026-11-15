import { afterEach, beforeEach, describe, it, expect, vi } from 'vitest';
import { ApiTimeoutError, DEFAULT_TIMEOUT_MS, WebViewApiClient } from '../WebViewApiClient';

type InvokeMethod = (service: string, method: string, jsonParams: string) => Promise<string>;

describe('WebViewApiClient', () => {
  it('on() registers a listener and returns unsubscribe function', () => {
    const client = new WebViewApiClient();
    const callback = vi.fn();
    const unsub = client.on('test.event', callback);
    expect(typeof unsub).toBe('function');
  });

  it('call() throws when WebView2 is not available', async () => {
    const client = new WebViewApiClient();
    await expect(client.call('service', 'method')).rejects.toThrow('WebView2 API bridge not available');
  });

  describe('call() with a bridge', () => {
    let invoke: ReturnType<typeof vi.fn<InvokeMethod>>;

    beforeEach(() => {
      vi.useFakeTimers();
      invoke = vi.fn<InvokeMethod>();
      window.chrome = {
        webview: { addEventListener: vi.fn(), hostObjects: { apibridge: { InvokeMethod: invoke } } },
      };
    });

    afterEach(() => {
      vi.useRealTimers();
      delete window.chrome;
    });

    /** An answer that never comes, as from a handler that hangs. */
    const never = () => new Promise<string>(() => {});

    it('sends the params as JSON and answers the data', async () => {
      invoke.mockResolvedValue(JSON.stringify({ success: true, data: { ok: 1 } }));

      await expect(new WebViewApiClient().call('settings', 'get', { a: 1 })).resolves.toEqual({ ok: 1 });
      expect(invoke).toHaveBeenCalledWith('settings', 'get', '{"a":1}');
    });

    it('rejects with the error the handler reported', async () => {
      invoke.mockResolvedValue(JSON.stringify({ success: false, error: 'disk full' }));

      await expect(new WebViewApiClient().call('settings', 'update')).rejects.toThrow('disk full');
    });

    it('gives up on a call that is not answered within the default timeout', async () => {
      invoke.mockImplementation(never);
      const call = new WebViewApiClient().call('settings', 'get');
      const outcome = vi.fn();
      call.then(outcome, outcome);

      await vi.advanceTimersByTimeAsync(DEFAULT_TIMEOUT_MS - 1);
      expect(outcome).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(1);
      const error = outcome.mock.calls[0][0];
      expect(error).toBeInstanceOf(ApiTimeoutError);
      expect(error).toMatchObject({ message: 'Murmur did not answer in time.', service: 'settings', method: 'get' });
    });

    it('waits as long as a call asks for', async () => {
      invoke.mockImplementation(never);
      const outcome = vi.fn();
      new WebViewApiClient().call('settings', 'captureHotkey', {}, { timeoutMs: 20_000 }).then(outcome, outcome);

      await vi.advanceTimersByTimeAsync(19_999);
      expect(outcome).not.toHaveBeenCalled();

      await vi.advanceTimersByTimeAsync(1);
      expect(outcome.mock.calls[0][0]).toBeInstanceOf(ApiTimeoutError);
    });

    it('waits for ever when a call asks for no timeout', async () => {
      invoke.mockImplementation(never);
      const outcome = vi.fn();
      new WebViewApiClient().call('setup', 'startMicTest', {}, { timeoutMs: null }).then(outcome, outcome);

      await vi.advanceTimersByTimeAsync(60 * 60 * 1000);

      expect(outcome).not.toHaveBeenCalled();
      expect(vi.getTimerCount()).toBe(0);
    });

    it('drops the timer once the answer is in', async () => {
      invoke.mockResolvedValue(JSON.stringify({ success: true, data: null }));

      await new WebViewApiClient().call('engine', 'getStatus');

      expect(vi.getTimerCount()).toBe(0);
    });
  });
});
