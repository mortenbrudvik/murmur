import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useMicrophones } from "../useMicrophones";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { MicrophoneInfo } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const usb: MicrophoneInfo = { id: "usb", name: "USB Mic", isDefault: true };
const headset: MicrophoneInfo = { id: "headset", name: "Headset", isDefault: false };

describe("useMicrophones", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
  });

  it("loads the microphones on mount", async () => {
    vi.mocked(apiClient.call).mockResolvedValue([usb, headset]);

    const { result } = renderHook(() => useMicrophones());

    expect(result.current.loaded).toBe(false);
    expect(result.current.microphones).toEqual([]);
    await waitFor(() => expect(result.current.microphones).toEqual([usb, headset]));
    expect(apiClient.call).toHaveBeenCalledWith("settings", "getMicrophones", {});
    expect(result.current.loaded).toBe(true);
  });

  it("is loaded with an empty list when there are no microphones", async () => {
    vi.mocked(apiClient.call).mockResolvedValue([]);

    const { result } = renderHook(() => useMicrophones());

    await waitFor(() => expect(result.current.loaded).toBe(true));
    expect(result.current.microphones).toEqual([]);
  });

  it("refresh reads the list again", async () => {
    vi.mocked(apiClient.call).mockResolvedValueOnce([usb]).mockResolvedValueOnce([usb, headset]);
    const { result } = renderHook(() => useMicrophones());
    await waitFor(() => expect(result.current.microphones).toEqual([usb]));

    await act(async () => { await result.current.refresh(); });

    expect(result.current.microphones).toEqual([usb, headset]);
  });

  it("keeps the previous list when a refresh fails", async () => {
    vi.mocked(apiClient.call).mockResolvedValueOnce([usb]).mockRejectedValueOnce(new Error("busy"));
    const { result } = renderHook(() => useMicrophones());
    await waitFor(() => expect(result.current.microphones).toEqual([usb]));

    await act(async () => { await result.current.refresh(); });

    expect(result.current.microphones).toEqual([usb]);
    expect(result.current.loaded).toBe(true);
  });

  it("ignores a slow answer that was overtaken by a newer refresh", async () => {
    let resolveFirst: (list: MicrophoneInfo[]) => void = () => {};
    vi.mocked(apiClient.call)
      .mockReturnValueOnce(new Promise<MicrophoneInfo[]>((resolve) => { resolveFirst = resolve; }))
      .mockResolvedValueOnce([usb, headset]);
    const { result } = renderHook(() => useMicrophones());

    await act(async () => { await result.current.refresh(); });
    await act(async () => { resolveFirst([usb]); });

    expect(result.current.microphones).toEqual([usb, headset]);
  });
});
