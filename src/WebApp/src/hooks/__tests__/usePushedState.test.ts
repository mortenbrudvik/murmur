import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { usePushedState } from "../usePushedState";
import { apiClient } from "../../interop/WebViewApiClient";
import type { InputStatus } from "../../types/domain";

vi.mock("../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const installed: InputStatus = { hookInstalled: true, error: null };
const refused: InputStatus = { hookInstalled: false, error: "Access is denied" };

/** The callback the hook registered for app.inputStatusChanged. */
function changedCallback(): (status: InputStatus) => void {
  const calls = vi.mocked(apiClient.on).mock.calls.filter(([name]) => name === "app.inputStatusChanged");
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1][1] as (status: InputStatus) => void;
}

const render = () => renderHook(() => usePushedState<InputStatus>("app", "getInputStatus", "app.inputStatusChanged"));

describe("usePushedState", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.on).mockReset();
    vi.mocked(apiClient.on).mockReturnValue(vi.fn());
  });

  it("is null until the state is known, then the state read once on mount", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(refused);

    const { result } = render();

    expect(result.current).toBeNull();
    await waitFor(() => expect(result.current).toEqual(refused));
    expect(apiClient.call).toHaveBeenCalledTimes(1);
    expect(apiClient.call).toHaveBeenCalledWith("app", "getInputStatus", {});
  });

  it("follows the pushed event", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(installed);
    const { result } = render();
    await waitFor(() => expect(result.current).toEqual(installed));

    act(() => changedCallback()(refused));

    expect(result.current).toEqual(refused);
  });

  it("keeps a pushed state when the read finishes later", async () => {
    let answer: (status: InputStatus) => void = () => undefined;
    vi.mocked(apiClient.call).mockReturnValue(new Promise<InputStatus>((resolve) => (answer = resolve)));
    const { result } = render();

    act(() => changedCallback()(refused));
    await act(async () => answer(installed));

    expect(result.current).toEqual(refused);
  });

  it("stays unknown when the state cannot be read, and still follows pushes", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));
    const { result } = render();
    await act(async () => { await Promise.resolve(); });

    expect(result.current).toBeNull();
    act(() => changedCallback()(installed));
    expect(result.current).toEqual(installed);
  });

  it("stays unknown when the server has nothing to say", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(null);

    const { result } = render();
    await waitFor(() => expect(apiClient.call).toHaveBeenCalled());

    expect(result.current).toBeNull();
  });

  it("unsubscribes on unmount", () => {
    const unsubscribe = vi.fn();
    vi.mocked(apiClient.call).mockResolvedValue(installed);
    vi.mocked(apiClient.on).mockReturnValue(unsubscribe);

    const { unmount } = render();
    unmount();

    expect(unsubscribe).toHaveBeenCalled();
  });
});
