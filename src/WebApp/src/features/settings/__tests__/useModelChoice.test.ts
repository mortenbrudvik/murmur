import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useModelChoice } from "../useModelChoice";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { EngineStatus, ModelChoice } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const large: ModelChoice = {
  selected: "largeV3Turbo",
  recommended: "largeV3Turbo",
  options: [
    { id: "largeV3Turbo", label: "Best accuracy (large, 1.6 GB)", sizeBytes: 1_624_555_275 },
    { id: "smallEn", label: "Faster (small, 488 MB)", sizeBytes: 487_614_201 },
  ],
};
const small: ModelChoice = { ...large, selected: "smallEn" };

const ready: EngineStatus = { state: "ready", modelName: "Whisper large-v3-turbo", runtime: "Cuda", error: null, download: null, gpu: null };

function pushStatus(status: EngineStatus) {
  const calls = vi.mocked(apiClient.on).mock.calls.filter(([name]) => name === "engine.statusChanged");
  expect(calls.length).toBeGreaterThan(0);
  (calls[calls.length - 1][1] as (status: EngineStatus) => void)(status);
}

describe("useModelChoice", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockReset();
    vi.mocked(apiClient.on).mockReturnValue(vi.fn());
  });

  it("is null until the choice is known, then the choice", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(large);

    const { result } = renderHook(() => useModelChoice());

    expect(result.current.choice).toBeNull();
    await waitFor(() => expect(result.current.choice).toEqual(large));
    expect(apiClient.call).toHaveBeenCalledWith("models", "getChoice", {});
  });

  it("stays null when the choice cannot be read, or the answer is empty", async () => {
    vi.mocked(apiClient.call).mockRejectedValueOnce(new Error("bridge down"));
    const failed = renderHook(() => useModelChoice());
    await act(async () => { await Promise.resolve(); });
    expect(failed.result.current.choice).toBeNull();

    vi.mocked(apiClient.call).mockResolvedValueOnce(null);
    const empty = renderHook(() => useModelChoice());
    await act(async () => { await Promise.resolve(); });
    expect(empty.result.current.choice).toBeNull();
  });

  it("select asks for the model and shows the selection that comes back", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => (method === "setChoice" ? small : large));
    const { result } = renderHook(() => useModelChoice());
    await waitFor(() => expect(result.current.choice).toEqual(large));

    await act(async () => { await result.current.select("smallEn"); });

    expect(apiClient.call).toHaveBeenCalledWith("models", "setChoice", { model: "smallEn" });
    expect(result.current.choice).toEqual(small);
  });

  it("select keeps the old selection and does not reject when the request fails", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "setChoice") throw new Error("busy");
      return large;
    });
    const { result } = renderHook(() => useModelChoice());
    await waitFor(() => expect(result.current.choice).toEqual(large));

    await act(async () => { await result.current.select("smallEn"); });

    expect(result.current.choice).toEqual(large);
  });

  it("reads the choice again when the engine reports ready with no download, in case a failed switch put the old model back", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(small);
    const { result } = renderHook(() => useModelChoice());
    await waitFor(() => expect(result.current.choice).toEqual(small));

    vi.mocked(apiClient.call).mockResolvedValue(large);
    act(() => pushStatus(ready));

    await waitFor(() => expect(result.current.choice).toEqual(large));
  });

  it("does not read the choice again for other engine statuses", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(large);
    const { result } = renderHook(() => useModelChoice());
    await waitFor(() => expect(result.current.choice).toEqual(large));
    const reads = vi.mocked(apiClient.call).mock.calls.length;
    const download = { modelName: "Whisper small (English)", bytesReceived: 1, totalBytes: 2, bytesPerSecond: 0, secondsRemaining: null, retries: 0, reconnecting: false };

    act(() => pushStatus({ ...ready, download }));
    act(() => pushStatus({ ...ready, state: "loading", runtime: null }));
    act(() => pushStatus({ ...ready, state: "downloading", runtime: null, download }));
    await act(async () => { await Promise.resolve(); });

    expect(vi.mocked(apiClient.call).mock.calls).toHaveLength(reads);
  });

  it("lets the latest answer win when a slow read finishes after a newer one", async () => {
    let resolveFirst: (value: ModelChoice) => void = () => {};
    vi.mocked(apiClient.call).mockReturnValueOnce(new Promise<ModelChoice>((resolve) => { resolveFirst = resolve; }));
    const { result } = renderHook(() => useModelChoice());

    vi.mocked(apiClient.call).mockResolvedValueOnce(small);
    act(() => pushStatus(ready));
    await waitFor(() => expect(result.current.choice).toEqual(small));
    await act(async () => { resolveFirst(large); });

    expect(result.current.choice).toEqual(small);
  });

  it("unsubscribes on unmount, and ignores an answer that arrives after it", async () => {
    const unsubscribe = vi.fn();
    vi.mocked(apiClient.on).mockReturnValue(unsubscribe);
    let resolveRead: (value: ModelChoice) => void = () => {};
    vi.mocked(apiClient.call).mockReturnValue(new Promise<ModelChoice>((resolve) => { resolveRead = resolve; }));
    const { result, unmount } = renderHook(() => useModelChoice());

    unmount();
    await act(async () => { resolveRead(large); });

    expect(unsubscribe).toHaveBeenCalled();
    expect(result.current.choice).toBeNull();
  });
});
