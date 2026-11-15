import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useServerDocument } from "../useServerDocument";
import { apiClient } from "../../interop/WebViewApiClient";

vi.mock("../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

interface Doc {
  name: string;
  count: number;
  /** Something the page keeps but the server is never sent. */
  local?: string;
}

const stored: Doc = { name: "a", count: 1 };

function render() {
  return renderHook(() =>
    useServerDocument<Doc>({
      service: "things",
      toParams: (doc) => ({ name: doc.name, count: doc.count }),
      saveFailure: "Could not save the things",
      notLoaded: "The things have not been loaded yet.",
    }),
  );
}

async function loaded() {
  const hook = render();
  await waitFor(() => expect(hook.result.current.doc).toEqual(stored));
  return hook;
}

describe("useServerDocument", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.call).mockImplementation(async (_service, method, params) => (method === "update" ? params : stored));
  });

  it("reads the document from the service's get", async () => {
    await loaded();

    expect(apiClient.call).toHaveBeenCalledWith("things", "get", {});
  });

  it("sends update what toParams makes of the edited document", async () => {
    const { result } = await loaded();

    await act(async () => { await result.current.edit((d) => ({ ...d, count: 2, local: "x" })); });

    expect(apiClient.call).toHaveBeenCalledWith("things", "update", { name: "a", count: 2 });
    expect(result.current.doc).toEqual({ name: "a", count: 2 });
  });

  it("names the service's document in the error of a failed save", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "update") throw new Error("disk full");
      return stored;
    });
    const { result } = await loaded();

    await act(async () => { await result.current.edit((d) => d).catch(() => undefined); });

    expect(result.current.error).toBe("Could not save the things (disk full).");
    expect(result.current.doc).toEqual(stored);
  });

  it("refuses an edit before the document has loaded with the given text", async () => {
    vi.mocked(apiClient.call).mockReturnValue(new Promise(() => {}));
    const { result } = render();

    await expect(result.current.edit((d) => d)).rejects.toThrow("The things have not been loaded yet.");
  });

  it("rejects an edit whose change throws, without sending anything", async () => {
    const { result } = await loaded();

    await expect(
      result.current.edit(() => {
        throw new Error("bad change");
      }),
    ).rejects.toThrow("bad change");
    expect(vi.mocked(apiClient.call).mock.calls.some(([, method]) => method === "update")).toBe(false);
  });

  it("setSaveError shows an error until the next save clears it", async () => {
    const { result } = await loaded();

    act(() => result.current.setSaveError("Could not read the key press."));
    expect(result.current.error).toBe("Could not read the key press.");

    await act(async () => { await result.current.edit((d) => d); });
    expect(result.current.error).toBeNull();
  });

  it("ignores a document that arrives after unmount", async () => {
    let answer: (doc: Doc) => void = () => undefined;
    vi.mocked(apiClient.call).mockReturnValue(new Promise<Doc>((resolve) => (answer = resolve)));
    const { result, unmount } = render();

    unmount();
    await act(async () => answer(stored));

    expect(result.current.doc).toBeNull();
  });
});
