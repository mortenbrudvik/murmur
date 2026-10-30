import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useInputStatus } from "../useInputStatus";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { InputStatus } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const installed: InputStatus = { hookInstalled: true, error: null };
const refused: InputStatus = { hookInstalled: false, error: "Access is denied" };

/** The callback useInputStatus registered for app.inputStatusChanged. */
function changedCallback(): (status: InputStatus) => void {
  const calls = vi.mocked(apiClient.on).mock.calls.filter(([name]) => name === "app.inputStatusChanged");
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1][1] as (status: InputStatus) => void;
}

describe("useInputStatus", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockReset();
    vi.mocked(apiClient.on).mockReturnValue(vi.fn());
  });

  it("is null until the status is known, then the status read once on load", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(refused);

    const { result } = renderHook(() => useInputStatus());

    expect(result.current).toBeNull();
    await waitFor(() => expect(result.current).toEqual(refused));
    expect(apiClient.call).toHaveBeenCalledTimes(1);
    expect(apiClient.call).toHaveBeenCalledWith("app", "getInputStatus", {});
  });

  /** The hook may still be starting when the window asks: the answer is "installed", and the failure is pushed later. */
  it("follows app.inputStatusChanged", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(installed);
    const { result } = renderHook(() => useInputStatus());
    await waitFor(() => expect(result.current).toEqual(installed));

    act(() => changedCallback()(refused));

    expect(result.current).toEqual(refused);
  });

  it("keeps a pushed status when the read finishes later", async () => {
    let answer: (status: InputStatus) => void = () => undefined;
    vi.mocked(apiClient.call).mockReturnValue(new Promise<InputStatus>((resolve) => (answer = resolve)));
    const { result } = renderHook(() => useInputStatus());

    act(() => changedCallback()(refused));
    await act(async () => answer(installed));

    expect(result.current).toEqual(refused);
  });

  it("stays unknown when the status cannot be read", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    const { result } = renderHook(() => useInputStatus());
    await waitFor(() => expect(apiClient.call).toHaveBeenCalled());

    expect(result.current).toBeNull();
  });

  it("stays unknown when the server has nothing to say", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(null);

    const { result } = renderHook(() => useInputStatus());
    await waitFor(() => expect(apiClient.call).toHaveBeenCalled());

    expect(result.current).toBeNull();
  });
});
