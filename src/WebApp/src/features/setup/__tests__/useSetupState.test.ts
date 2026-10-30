import { StrictMode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useSetupState } from "../useSetupState";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { SetupState } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const firstRun: SetupState = { showSetup: true, step: "speechModel", cudaTipDismissed: false, openPage: null };

function callsTo(key: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => `${service}.${method}` === key);
}

describe("useSetupState", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.call).mockResolvedValue(firstRun);
  });

  it("is null until the state is known, then the state", async () => {
    const { result } = renderHook(() => useSetupState());

    expect(result.current.state).toBeNull();
    await waitFor(() => expect(result.current.state).toEqual(firstRun));
    expect(apiClient.call).toHaveBeenCalledWith("setup", "getState", {});
  });

  it("passes on the page the launch asked for, unless the setup is shown, which always ends on History", async () => {
    vi.mocked(apiClient.call).mockResolvedValue({ ...firstRun, showSetup: false, openPage: "settings" });
    const asked = renderHook(() => useSetupState());
    await waitFor(() => expect(asked.result.current.state?.openPage).toBe("settings"));

    vi.mocked(apiClient.call).mockResolvedValue({ ...firstRun, openPage: "settings" });
    const duringSetup = renderHook(() => useSetupState());
    await waitFor(() => expect(duringSetup.result.current.state?.showSetup).toBe(true));
    expect(duringSetup.result.current.state?.openPage).toBeNull();
  });

  it("falls back to no setup when the state cannot be read, so the window is never blank", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    const { result } = renderHook(() => useSetupState());

    await waitFor(() => expect(result.current.state).not.toBeNull());
    expect(result.current.state?.showSetup).toBe(false);
    expect(result.current.state?.openPage).toBeNull();
  });

  it("falls back to no setup when the answer is empty", async () => {
    vi.mocked(apiClient.call).mockResolvedValue(null);

    const { result } = renderHook(() => useSetupState());

    await waitFor(() => expect(result.current.state?.showSetup).toBe(false));
  });

  it("reads the state once even when React mounts the page twice, because the open-page request is handed out once", async () => {
    const { result } = renderHook(() => useSetupState(), { wrapper: StrictMode });

    await waitFor(() => expect(result.current.state).toEqual(firstRun));
    expect(callsTo("setup.getState")).toHaveLength(1);
  });

  it("sends only the fields that change", async () => {
    const { result } = renderHook(() => useSetupState());
    await waitFor(() => expect(result.current.state).not.toBeNull());

    await act(() => result.current.update({ step: "microphone" }));
    await act(() => result.current.update({ cudaTipDismissed: true }));

    expect(callsTo("setup.update").map(([, , params]) => params)).toEqual([{ step: "microphone" }, { cudaTipDismissed: true }]);
  });

  it("shows a change at once and keeps it", async () => {
    const { result } = renderHook(() => useSetupState());
    await waitFor(() => expect(result.current.state).not.toBeNull());

    // The answer to the update is not awaited here: the page moves on before the save has finished.
    let saved!: Promise<void>;
    act(() => {
      saved = result.current.update({ step: "tryIt", cudaTipDismissed: true });
    });
    expect(result.current.state).toMatchObject({ step: "tryIt", cudaTipDismissed: true, showSetup: true });

    await act(() => saved);
    expect(result.current.state).toMatchObject({ step: "tryIt", cudaTipDismissed: true, showSetup: true });
  });

  it("ends the setup when it is completed", async () => {
    const { result } = renderHook(() => useSetupState());
    await waitFor(() => expect(result.current.state).not.toBeNull());

    await act(() => result.current.update({ completed: true }));

    expect(result.current.state?.showSetup).toBe(false);
  });

  it("keeps the change and rejects when it could not be saved", async () => {
    const { result } = renderHook(() => useSetupState());
    await waitFor(() => expect(result.current.state).not.toBeNull());
    vi.mocked(apiClient.call).mockRejectedValue(new Error("disk full"));

    let failure: unknown;
    await act(async () => {
      await result.current.update({ step: "microphone" }).catch((err: unknown) => (failure = err));
    });

    expect(failure).toBeInstanceOf(Error);
    expect(result.current.state?.step).toBe("microphone");
  });

  it("sends the changes one after another, in order", async () => {
    const { result } = renderHook(() => useSetupState());
    await waitFor(() => expect(result.current.state).not.toBeNull());

    let releaseFirst!: () => void;
    vi.mocked(apiClient.call).mockImplementationOnce(() => new Promise((resolve) => (releaseFirst = () => resolve(firstRun))));
    let first!: Promise<void>;
    let second!: Promise<void>;
    act(() => {
      first = result.current.update({ step: "microphone" });
      second = result.current.update({ step: "tryIt" });
    });
    await waitFor(() => expect(callsTo("setup.update")).toHaveLength(1));
    // The second request waits until the first has been answered.
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(callsTo("setup.update")).toHaveLength(1);

    releaseFirst();
    await act(async () => {
      await Promise.all([first, second]);
    });

    expect(callsTo("setup.update").map(([, , params]) => params)).toEqual([{ step: "microphone" }, { step: "tryIt" }]);
  });
});
