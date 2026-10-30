import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useEngineStatus } from "../useEngineStatus";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { EngineStatus } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const loading: EngineStatus = { state: "loading", modelName: "Whisper large-v3-turbo", runtime: null, error: null, download: null, gpu: null, switching: false };
const ready: EngineStatus = { state: "ready", modelName: "Whisper large-v3-turbo", runtime: "Cuda", error: null, download: null, gpu: null, switching: false };

/** The callback useEngineStatus registered for engine.statusChanged. */
function changedCallback(): (status: EngineStatus) => void {
  const calls = vi.mocked(apiClient.on).mock.calls.filter(([name]) => name === "engine.statusChanged");
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1][1] as (status: EngineStatus) => void;
}

describe("useEngineStatus", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockReset();
    vi.mocked(apiClient.on).mockReturnValue(vi.fn());
  });

  it("is null until the status is known, then the initial status", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(loading);

    const { result } = renderHook(() => useEngineStatus());

    expect(result.current).toBeNull();
    await waitFor(() => expect(result.current).toEqual(loading));
    expect(apiClient.call).toHaveBeenCalledWith("engine", "getStatus", {});
  });

  it("follows engine.statusChanged", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(loading);
    const { result } = renderHook(() => useEngineStatus());
    await waitFor(() => expect(result.current).toEqual(loading));

    act(() => changedCallback()(ready));

    expect(result.current).toEqual(ready);
  });

  it("does not let a slow initial status overwrite a newer pushed one", async () => {
    let resolveInitial: (status: EngineStatus) => void = () => {};
    vi.mocked(apiClient.call).mockReturnValue(new Promise<EngineStatus>((resolve) => { resolveInitial = resolve; }));
    const { result } = renderHook(() => useEngineStatus());

    act(() => changedCallback()(ready));
    await act(async () => { resolveInitial(loading); });

    expect(result.current).toEqual(ready);
  });

  it("stays null when the status cannot be read, and still follows pushed changes", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));
    const { result } = renderHook(() => useEngineStatus());
    await act(async () => { await Promise.resolve(); });

    expect(result.current).toBeNull();
    act(() => changedCallback()(ready));
    expect(result.current).toEqual(ready);
  });

  it("unsubscribes on unmount", () => {
    const unsubscribe = vi.fn();
    vi.mocked(apiClient.call).mockResolvedValue(loading);
    vi.mocked(apiClient.on).mockReturnValue(unsubscribe);

    const { unmount } = renderHook(() => useEngineStatus());
    unmount();

    expect(unsubscribe).toHaveBeenCalled();
  });
});
