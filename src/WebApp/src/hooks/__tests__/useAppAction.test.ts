import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useAppAction } from "../useAppAction";

describe("useAppAction", () => {
  it("runs the action with its arguments and answers true when it worked", async () => {
    const action = vi.fn<(target: string) => Promise<void>>(async () => undefined);
    const { result } = renderHook(() => useAppAction(action, "Could not open the link"));

    let worked = false;
    await act(async () => { worked = await result.current[0]("cudaDownload"); });

    expect(action).toHaveBeenCalledWith("cudaDownload");
    expect(worked).toBe(true);
    expect(result.current[1]).toBeNull();
  });

  it("answers false and reports the failure under the label, without rejecting", async () => {
    const { result } = renderHook(() => useAppAction(() => Promise.reject(new Error("no browser")), "Could not open the link"));

    let worked = true;
    await act(async () => { worked = await result.current[0](); });

    expect(worked).toBe(false);
    expect(result.current[1]).toBe("Could not open the link (no browser).");
  });

  it("clears the last failure when it runs again", async () => {
    let fail = true;
    const { result } = renderHook(() =>
      useAppAction(async () => {
        if (fail) throw new Error("busy");
      }, "Could not restart Murmur"),
    );

    await act(async () => { await result.current[0](); });
    expect(result.current[1]).not.toBeNull();

    fail = false;
    await act(async () => { await result.current[0](); });
    expect(result.current[1]).toBeNull();
  });

  it("keeps run the same function across renders", () => {
    const { result, rerender } = renderHook(() => useAppAction(async () => undefined, "Could not restart Murmur"));
    const first = result.current[0];

    rerender();

    expect(result.current[0]).toBe(first);
  });
});
