import { renderHook } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { useWebViewEvent } from '../useWebViewEvent';
import { apiClient } from '../../interop/WebViewApiClient';

vi.mock('../../interop/WebViewApiClient', () => ({
  apiClient: {
    on: vi.fn(() => vi.fn()),
  },
}));

describe('useWebViewEvent', () => {
  it('subscribes to event on mount and unsubscribes on unmount', () => {
    const unsub = vi.fn();
    (apiClient.on as any).mockReturnValue(unsub);

    const { unmount } = renderHook(() =>
      useWebViewEvent('test.event', vi.fn())
    );

    expect(apiClient.on).toHaveBeenCalledWith('test.event', expect.any(Function));
    unmount();
    expect(unsub).toHaveBeenCalled();
  });
});
