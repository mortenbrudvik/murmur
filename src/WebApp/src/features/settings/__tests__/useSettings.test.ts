import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useSettings } from "../useSettings";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { HotkeyDefinition, MurmurSettings } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const rightAlt: HotkeyDefinition = { virtualKey: 163, modifiers: "None", displayName: "Right Alt" };
const f9: HotkeyDefinition = { virtualKey: 120, modifiers: "Ctrl", displayName: "Ctrl + F9" };

const stored: MurmurSettings = {
  hotkey: rightAlt,
  microphoneId: "mic-1",
  soundCues: true,
  startWithWindows: false,
  keepAudio: true,
};

function updateCalls() {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "settings" && method === "update");
}

/** The server: get returns what was stored, update stores and returns exactly what it was sent. */
function mockEchoServer(captured: HotkeyDefinition | null = null) {
  let current = stored;
  vi.mocked(apiClient.call).mockImplementation(async (_service, method, params) => {
    if (method === "update") current = params as MurmurSettings;
    if (method === "captureHotkey") return captured;
    return current;
  });
}

async function loaded() {
  const hook = renderHook(() => useSettings());
  await waitFor(() => expect(hook.result.current.settings).toEqual(stored));
  return hook;
}

describe("useSettings", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
  });

  it("loads the settings on mount", async () => {
    mockEchoServer();

    const { result } = renderHook(() => useSettings());

    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.settings).toEqual(stored));
    expect(apiClient.call).toHaveBeenCalledWith("settings", "get", {});
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it("reports a failed load", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    const { result } = renderHook(() => useSettings());

    await waitFor(() => expect(result.current.error).toMatch(/bridge down/));
    expect(result.current.loading).toBe(false);
    expect(result.current.settings).toBeNull();
  });

  it("saves all five fields and adopts the server's result", async () => {
    mockEchoServer();
    const { result } = await loaded();
    // The server keeps the previous hotkey when it cannot use the new one.
    const serverResult: MurmurSettings = { ...stored, soundCues: false };
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => (method === "update" ? serverResult : stored));

    const next: MurmurSettings = { ...stored, soundCues: false, hotkey: f9 };
    await act(async () => { await result.current.save(next); });

    expect(apiClient.call).toHaveBeenCalledWith("settings", "update", {
      hotkey: f9,
      microphoneId: "mic-1",
      soundCues: false,
      startWithWindows: false,
      keepAudio: true,
    });
    expect(result.current.settings).toEqual(serverResult);
    expect(result.current.error).toBeNull();
  });

  it("keeps the previous settings, reports the error and rejects when a save fails", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "update") throw new Error("read-only");
      return stored;
    });
    const { result } = await loaded();

    let failure: unknown;
    await act(async () => {
      await result.current.save({ ...stored, soundCues: false }).catch((err: unknown) => { failure = err; });
    });

    expect(failure).toBeInstanceOf(Error);
    expect(result.current.settings).toEqual(stored);
    expect(result.current.error).toMatch(/could not save/i);
    expect(result.current.error).toMatch(/read-only/);
  });

  it("edit sends the whole current document with one field changed", async () => {
    mockEchoServer();
    const { result } = await loaded();

    await act(async () => { await result.current.edit((s) => ({ ...s, keepAudio: false })); });

    expect(updateCalls()[0][2]).toEqual({ ...stored, keepAudio: false });
    expect(result.current.settings?.keepAudio).toBe(false);
  });

  it("queued edits build on each other, so no change is lost", async () => {
    mockEchoServer();
    const { result } = await loaded();

    await act(async () => {
      const first = result.current.edit((s) => ({ ...s, soundCues: false }));
      const second = result.current.edit((s) => ({ ...s, startWithWindows: true }));
      await Promise.all([first, second]);
    });

    expect(updateCalls()[1][2]).toEqual({ ...stored, soundCues: false, startWithWindows: true });
    expect(result.current.settings).toEqual({ ...stored, soundCues: false, startWithWindows: true });
  });

  it("edit rejects before the settings have loaded", async () => {
    vi.mocked(apiClient.call).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useSettings());

    await expect(result.current.edit((s) => s)).rejects.toThrow(/not.*loaded/i);
    expect(updateCalls()).toHaveLength(0);
  });

  it("captureHotkey returns the captured key without saving it", async () => {
    mockEchoServer(f9);
    const { result } = await loaded();

    let key: HotkeyDefinition | null = null;
    await act(async () => { key = await result.current.captureHotkey(); });

    expect(apiClient.call).toHaveBeenCalledWith("settings", "captureHotkey", {});
    expect(key).toEqual(f9);
    expect(updateCalls()).toHaveLength(0);
  });

  it("captureHotkey returning null leaves the hotkey alone and saves nothing", async () => {
    mockEchoServer(null);
    const { result } = await loaded();

    let key: HotkeyDefinition | null = f9;
    await act(async () => { key = await result.current.captureHotkey(); });

    expect(key).toBeNull();
    expect(updateCalls()).toHaveLength(0);
    expect(result.current.settings?.hotkey).toEqual(rightAlt);
    expect(result.current.capturing).toBe(false);
  });

  it("reports capturing while the key press is awaited", async () => {
    let finish: (key: HotkeyDefinition | null) => void = () => {};
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "captureHotkey") return new Promise((resolve) => { finish = resolve as typeof finish; });
      return stored;
    });
    const { result } = await loaded();

    let pending: Promise<HotkeyDefinition | null> = Promise.resolve(null);
    act(() => { pending = result.current.captureHotkey(); });
    expect(result.current.capturing).toBe(true);

    await act(async () => { finish(f9); await pending; });
    expect(result.current.capturing).toBe(false);
  });

  it("captureHotkey reports a bridge failure and returns null", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "captureHotkey") throw new Error("hook failed");
      return stored;
    });
    const { result } = await loaded();

    let key: HotkeyDefinition | null = f9;
    await act(async () => { key = await result.current.captureHotkey(); });

    expect(key).toBeNull();
    expect(result.current.error).toMatch(/hook failed/);
    expect(result.current.capturing).toBe(false);
  });

  it("ignores a key press that arrives after the hook was unmounted", async () => {
    let finish: (key: HotkeyDefinition | null) => void = () => {};
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "captureHotkey") return new Promise((resolve) => { finish = resolve as typeof finish; });
      return stored;
    });
    const { result, unmount } = await loaded();

    let pending: Promise<HotkeyDefinition | null> = Promise.resolve(null);
    act(() => { pending = result.current.captureHotkey(); });
    unmount();
    finish(f9);

    await expect(pending).resolves.toBeNull();
  });
});
