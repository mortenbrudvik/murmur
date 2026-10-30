import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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

const ready: EngineStatus = { state: "ready", modelName: "Whisper large-v3-turbo", runtime: "Cuda", error: null, download: null, gpu: null, switching: false };

/** The setChoice requests made so far. */
function setChoiceCalls() {
  return vi.mocked(apiClient.call).mock.calls.filter(([, method]) => method === "setChoice");
}

/** Lets time pass inside act, with the promises that settle on the way. */
async function advance(ms: number) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms); });
}

/** Renders the hook under fake timers, after the first read has been answered. */
async function renderLoaded() {
  vi.useFakeTimers();
  const view = renderHook(() => useModelChoice());
  await advance(0);
  expect(view.result.current.choice).toEqual(large);
  return view;
}

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

  afterEach(() => {
    vi.useRealTimers();
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
    expect(failed.result.current.error).toBe("Could not read the speech model (bridge down).");

    vi.mocked(apiClient.call).mockResolvedValueOnce(null);
    const empty = renderHook(() => useModelChoice());
    await act(async () => { await Promise.resolve(); });
    expect(empty.result.current.choice).toBeNull();
    expect(empty.result.current.error).toBeNull();
  });

  it("select shows the new choice at once, and setChoice is sent once, 1 s after the last change", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => (method === "setChoice" ? small : large));
    const { result } = await renderLoaded();

    act(() => result.current.select("smallEn"));
    expect(result.current.choice?.selected).toBe("smallEn");
    await advance(400);
    act(() => result.current.select("largeV3Turbo"));
    expect(result.current.choice?.selected).toBe("largeV3Turbo");
    await advance(400);
    act(() => result.current.select("smallEn"));
    expect(result.current.choice?.selected).toBe("smallEn");

    // 1 s after the first change, but only 200 ms after the last one.
    await advance(999);
    expect(setChoiceCalls()).toHaveLength(0);
    expect(result.current.choice?.selected).toBe("smallEn");

    await advance(1);
    expect(setChoiceCalls()).toEqual([["models", "setChoice", { model: "smallEn" }]]);
    expect(result.current.choice).toEqual(small);
    await advance(5000);
    expect(setChoiceCalls()).toHaveLength(1);
  });

  it("sends nothing when the choice is back on the saved one after the wait", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(large);
    const { result } = await renderLoaded();

    act(() => result.current.select("smallEn"));
    await advance(300);
    act(() => result.current.select("largeV3Turbo"));
    await advance(5000);

    expect(setChoiceCalls()).toHaveLength(0);
    expect(result.current.choice).toEqual(large);
  });

  it("sends the waiting choice at once when the hook is unmounted during the wait", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => (method === "setChoice" ? small : large));
    const { result, unmount } = await renderLoaded();

    act(() => result.current.select("smallEn"));
    await advance(500);
    unmount();

    // Leaving Settings within the second is not taking the choice back.
    expect(setChoiceCalls()).toEqual([["models", "setChoice", { model: "smallEn" }]]);
    await advance(5000);
    expect(setChoiceCalls()).toHaveLength(1);
  });

  it("sends nothing on unmount when no choice is waiting", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(large);
    const { unmount } = await renderLoaded();

    unmount();
    await advance(5000);

    expect(setChoiceCalls()).toHaveLength(0);
  });

  it("sends nothing on unmount when the waiting choice is the saved one", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(large);
    const { result, unmount } = await renderLoaded();

    act(() => result.current.select("smallEn"));
    await advance(300);
    act(() => result.current.select("largeV3Turbo"));
    unmount();
    await advance(5000);

    expect(setChoiceCalls()).toHaveLength(0);
  });

  it("a failed setChoice sets the error and puts the saved choice back", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "setChoice") throw new Error("busy");
      return large;
    });
    const { result } = await renderLoaded();

    act(() => result.current.select("smallEn"));
    expect(result.current.choice?.selected).toBe("smallEn");
    expect(result.current.error).toBeNull();
    await advance(1000);

    expect(result.current.error).toBe("Could not change the speech model (busy).");
    expect(result.current.choice).toEqual(large);
  });

  it("the next change clears the error", async () => {
    let fail = true;
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method !== "setChoice") return large;
      if (fail) throw new Error("busy");
      return small;
    });
    const { result } = await renderLoaded();
    act(() => result.current.select("smallEn"));
    await advance(1000);
    expect(result.current.error).not.toBeNull();

    fail = false;
    act(() => result.current.select("smallEn"));
    expect(result.current.error).toBeNull();
    await advance(1000);

    expect(result.current.error).toBeNull();
    expect(result.current.choice).toEqual(small);
  });

  it("keeps the choice made during the wait when the engine reports ready, and sends it", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => (method === "setChoice" ? small : large));
    const { result } = await renderLoaded();

    act(() => result.current.select("smallEn"));
    await advance(300);
    act(() => pushStatus(ready));
    await advance(0);
    expect(result.current.choice?.selected).toBe("smallEn");

    await advance(700);
    expect(setChoiceCalls()).toHaveLength(1);
    expect(result.current.choice).toEqual(small);
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
