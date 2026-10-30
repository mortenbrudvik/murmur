import { StrictMode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useMicLevel } from "../useMicLevel";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { MicTestResult } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const listening: MicTestResult = { state: "listening", error: null };

/** The setup.* calls so far, in the order they were made: "start:<id>" and "stop". */
function setupCalls(): string[] {
  return vi.mocked(apiClient.call).mock.calls.flatMap(([service, method, params]) => {
    if (service !== "setup") return [];
    if (method === "startMicTest") return [`start:${(params as { microphoneId: string | null }).microphoneId}`];
    if (method === "stopMicTest") return ["stop"];
    return [];
  });
}

/** Delivers a setup.micLevel push to everything that subscribed to it. */
function pushLevel(level: number) {
  const callbacks = vi.mocked(apiClient.on).mock.calls
    .filter(([name]) => name === "setup.micLevel")
    .map(([, callback]) => callback as (data: { level: number }) => void);
  expect(callbacks.length).toBeGreaterThan(0);
  act(() => callbacks.forEach((callback) => callback({ level })));
}

function setVisibility(state: "visible" | "hidden") {
  Object.defineProperty(document, "visibilityState", { configurable: true, get: () => state });
  act(() => {
    document.dispatchEvent(new Event("visibilitychange"));
  });
}

/** The window gets the keyboard focus again (for example after the user came back from another program). */
function focusWindow() {
  act(() => {
    window.dispatchEvent(new Event("focus"));
  });
}

/** A promise the test settles itself, to hold a request back. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

describe("useMicLevel", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.call).mockImplementation(async (service, method) =>
      service === "setup" && method === "startMicTest" ? listening : null,
    );
    vi.mocked(apiClient.on).mockReset();
    vi.mocked(apiClient.on).mockReturnValue(vi.fn());
  });

  afterEach(() => {
    // Remove the visibility override of a test, so the next one sees the real (visible) document.
    Reflect.deleteProperty(document, "visibilityState");
  });

  it("starts the test on the given microphone and reports how it went", async () => {
    const { result } = renderHook(() => useMicLevel("headset"));

    expect(result.current.result).toBeNull();
    await waitFor(() => expect(result.current.result).toEqual(listening));
    expect(apiClient.call).toHaveBeenCalledWith("setup", "startMicTest", { microphoneId: "headset" });
  });

  it("tests the Windows default when no microphone is chosen", async () => {
    renderHook(() => useMicLevel(null));

    await waitFor(() => expect(setupCalls()).toEqual(["start:null"]));
  });

  it("passes on a denied access, which comes without a message", async () => {
    vi.mocked(apiClient.call).mockResolvedValue({ state: "accessDenied", error: null });

    const { result } = renderHook(() => useMicLevel(null));

    await waitFor(() => expect(result.current.result).toEqual({ state: "accessDenied", error: null }));
  });

  it("reports a request that could not be made as a failure", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    const { result } = renderHook(() => useMicLevel(null));

    await waitFor(() => expect(result.current.result?.state).toBe("failed"));
    expect(result.current.result?.error).toBe("Could not test the microphone (bridge down).");
  });

  it("follows setup.micLevel, kept between 0 and 1", async () => {
    const { result } = renderHook(() => useMicLevel(null));
    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(result.current.level).toBe(0);

    pushLevel(0.42);
    expect(result.current.level).toBe(0.42);

    pushLevel(1.7);
    expect(result.current.level).toBe(1);
    pushLevel(-0.2);
    expect(result.current.level).toBe(0);
    pushLevel(0.5);
    pushLevel(Number.NaN);
    expect(result.current.level).toBe(0);
  });

  it("shows no level when the microphone could not be opened", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (service, method) => {
      if (service === "setup" && method === "startMicTest") {
        // A level that arrived before the answer is not a level of anything that listens.
        pushLevel(0.6);
        return { state: "failed", error: "No device" };
      }
      return null;
    });

    const { result } = renderHook(() => useMicLevel(null));

    await waitFor(() => expect(result.current.result?.state).toBe("failed"));
    expect(result.current.level).toBe(0);
  });

  it("stops the test on unmount", async () => {
    const { result, unmount } = renderHook(() => useMicLevel(null));
    await waitFor(() => expect(result.current.result).not.toBeNull());

    unmount();

    await waitFor(() => expect(setupCalls()).toEqual(["start:null", "stop"]));
    expect(apiClient.call).toHaveBeenCalledWith("setup", "stopMicTest", {});
  });

  it("never stops before the start it follows has answered", async () => {
    const start = deferred<MicTestResult>();
    vi.mocked(apiClient.call).mockImplementation((service, method) =>
      service === "setup" && method === "startMicTest" ? start.promise : Promise.resolve(null),
    );

    const { unmount } = renderHook(() => useMicLevel(null));
    await waitFor(() => expect(setupCalls()).toEqual(["start:null"]));
    unmount();
    // Give a stop that was sent too early the chance to show up.
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(setupCalls()).toEqual(["start:null"]);

    start.resolve(listening);

    await waitFor(() => expect(setupCalls()).toEqual(["start:null", "stop"]));
  });

  it("keeps start, stop and start in order when React mounts, unmounts and mounts again", async () => {
    const { result } = renderHook(() => useMicLevel("usb"), { wrapper: StrictMode });

    await waitFor(() => expect(setupCalls()).toEqual(["start:usb", "stop", "start:usb"]));
    await waitFor(() => expect(result.current.result).toEqual(listening));
  });

  it("restarts the test on the new microphone: stop, then start", async () => {
    const { result, rerender } = renderHook(({ id }) => useMicLevel(id), { initialProps: { id: "usb" as string | null } });
    await waitFor(() => expect(result.current.result).toEqual(listening));

    rerender({ id: "headset" });

    await waitFor(() => expect(setupCalls()).toEqual(["start:usb", "stop", "start:headset"]));
  });

  it("shows the answer of the microphone that is chosen now, never of the one before", async () => {
    const first = deferred<MicTestResult>();
    vi.mocked(apiClient.call).mockImplementation((service, method, params) => {
      if (service !== "setup" || method !== "startMicTest") return Promise.resolve(null);
      return (params as { microphoneId: string }).microphoneId === "usb"
        ? first.promise
        : Promise.resolve({ state: "failed", error: "The headset is busy" });
    });

    const { result, rerender } = renderHook(({ id }) => useMicLevel(id), { initialProps: { id: "usb" as string | null } });
    await waitFor(() => expect(setupCalls()).toEqual(["start:usb"]));
    rerender({ id: "headset" });

    first.resolve(listening);

    await waitFor(() => expect(result.current.result).toEqual({ state: "failed", error: "The headset is busy" }));
    expect(setupCalls()).toEqual(["start:usb", "stop", "start:headset"]);
  });

  it("shows no answer while the new microphone's test is starting", async () => {
    const second = deferred<MicTestResult>();
    vi.mocked(apiClient.call).mockImplementation((service, method, params) => {
      if (service !== "setup" || method !== "startMicTest") return Promise.resolve(null);
      return (params as { microphoneId: string }).microphoneId === "usb" ? Promise.resolve(listening) : second.promise;
    });

    const { result, rerender } = renderHook(({ id }) => useMicLevel(id), { initialProps: { id: "usb" as string | null } });
    await waitFor(() => expect(result.current.result).toEqual(listening));

    rerender({ id: "headset" });

    expect(result.current.result).toBeNull();
    second.resolve({ state: "accessDenied", error: null });
    await waitFor(() => expect(result.current.result?.state).toBe("accessDenied"));
  });

  it("starts the test again when the window is shown again", async () => {
    const { result } = renderHook(() => useMicLevel("usb"));
    await waitFor(() => expect(result.current.result).toEqual(listening));

    // The .NET side stopped the test when the window was hidden.
    setVisibility("hidden");
    expect(setupCalls()).toEqual(["start:usb"]);
    setVisibility("visible");

    await waitFor(() => expect(setupCalls()).toEqual(["start:usb", "start:usb"]));
  });

  it("starts the test again when the window gets focus, e.g. after Windows' microphone settings were closed", async () => {
    vi.mocked(apiClient.call).mockResolvedValueOnce({ state: "accessDenied", error: null });
    const { result } = renderHook(() => useMicLevel("usb"));
    await waitFor(() => expect(result.current.result?.state).toBe("accessDenied"));

    // The user allowed access in Windows' settings: Murmur's window was never hidden, so only the focus tells.
    focusWindow();

    await waitFor(() => expect(result.current.result).toEqual(listening));
    expect(setupCalls()).toEqual(["start:usb", "start:usb"]);
  });

  it("sends a restart after the start that has not answered yet, never before", async () => {
    const first = deferred<MicTestResult>();
    vi.mocked(apiClient.call).mockImplementationOnce(() => first.promise);
    renderHook(() => useMicLevel("usb"));
    await waitFor(() => expect(setupCalls()).toEqual(["start:usb"]));

    focusWindow();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(setupCalls()).toEqual(["start:usb"]);

    first.resolve(listening);

    await waitFor(() => expect(setupCalls()).toEqual(["start:usb", "start:usb"]));
  });

  it("does not open the microphone while the window is hidden, and does once it is shown", async () => {
    setVisibility("hidden");
    const { result } = renderHook(() => useMicLevel("usb"));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(setupCalls()).toEqual([]);
    expect(result.current.result).toBeNull();

    // Neither a focus event nor anything else may open the microphone for a window nobody sees.
    focusWindow();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(setupCalls()).toEqual([]);

    setVisibility("visible");

    await waitFor(() => expect(result.current.result).toEqual(listening));
    expect(setupCalls()).toEqual(["start:usb"]);
  });

  it("does not start the new microphone's test while the window is hidden", async () => {
    const { result, rerender } = renderHook(({ id }) => useMicLevel(id), { initialProps: { id: "usb" as string | null } });
    await waitFor(() => expect(result.current.result).toEqual(listening));

    setVisibility("hidden");
    rerender({ id: "headset" });

    await waitFor(() => expect(setupCalls()).toEqual(["start:usb", "stop"]));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(setupCalls()).toEqual(["start:usb", "stop"]);
  });

  it("tests again on restart: stop, then start", async () => {
    const { result } = renderHook(() => useMicLevel("usb"));
    await waitFor(() => expect(result.current.result).toEqual(listening));

    act(() => result.current.restart());

    await waitFor(() => expect(setupCalls()).toEqual(["start:usb", "stop", "start:usb"]));
  });

  it("stops listening for the window being shown or focused once it is unmounted", async () => {
    const { result, unmount } = renderHook(() => useMicLevel("usb"));
    await waitFor(() => expect(result.current.result).toEqual(listening));
    unmount();
    await waitFor(() => expect(setupCalls()).toEqual(["start:usb", "stop"]));

    setVisibility("visible");
    focusWindow();
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(setupCalls()).toEqual(["start:usb", "stop"]);
  });
});
