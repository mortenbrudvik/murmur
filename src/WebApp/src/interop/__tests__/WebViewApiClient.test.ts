import { describe, it, expect, vi } from 'vitest';
import { WebViewApiClient } from '../WebViewApiClient';

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
});
