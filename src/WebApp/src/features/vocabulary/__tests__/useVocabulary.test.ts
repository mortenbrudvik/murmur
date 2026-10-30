import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useVocabulary } from "../useVocabulary";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { Vocabulary } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const stored: Vocabulary = {
  hintWords: ["Murmur"],
  replacements: [{ spoken: "kubernetes", written: "Kubernetes" }],
};

function updateCalls() {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "vocabulary" && method === "update");
}

/** The server: get returns what was stored, update stores and returns exactly what it was sent. */
function mockEchoServer(initial: Vocabulary = stored) {
  let current = initial;
  vi.mocked(apiClient.call).mockImplementation(async (_service, method, params) => {
    if (method === "update") current = params as Vocabulary;
    return current;
  });
}

describe("useVocabulary", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
  });

  it("loads the vocabulary on mount", async () => {
    mockEchoServer();

    const { result } = renderHook(() => useVocabulary());

    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.vocabulary).toEqual(stored));
    expect(apiClient.call).toHaveBeenCalledWith("vocabulary", "get", {});
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it("reports a failed load", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    const { result } = renderHook(() => useVocabulary());

    await waitFor(() => expect(result.current.error).toMatch(/bridge down/));
    expect(result.current.loading).toBe(false);
    expect(result.current.vocabulary).toBeNull();
  });

  it("saves the full document and adopts the server's normalized result, not the input", async () => {
    const normalized: Vocabulary = { hintWords: ["Murmur", "Whisper"], replacements: [] };
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => (method === "update" ? normalized : stored));
    const { result } = renderHook(() => useVocabulary());
    await waitFor(() => expect(result.current.vocabulary).toEqual(stored));

    const input: Vocabulary = { hintWords: [" Murmur ", "whisper", "WHISPER", ""], replacements: [{ spoken: "", written: "x" }] };
    await act(async () => { await result.current.save(input); });

    expect(apiClient.call).toHaveBeenCalledWith("vocabulary", "update", {
      hintWords: input.hintWords,
      replacements: input.replacements,
    });
    expect(result.current.vocabulary).toEqual(normalized);
    expect(result.current.error).toBeNull();
  });

  it("keeps the previous vocabulary, reports the error and rejects when a save fails", async () => {
    vi.mocked(apiClient.call).mockImplementation(async (_service, method) => {
      if (method === "update") throw new Error("disk full");
      return stored;
    });
    const { result } = renderHook(() => useVocabulary());
    await waitFor(() => expect(result.current.vocabulary).toEqual(stored));

    let failure: unknown;
    await act(async () => {
      await result.current.save({ hintWords: ["x"], replacements: [] }).catch((err: unknown) => { failure = err; });
    });

    expect(failure).toBeInstanceOf(Error);
    expect(result.current.vocabulary).toEqual(stored);
    expect(result.current.error).toMatch(/could not save/i);
    expect(result.current.error).toMatch(/disk full/);
  });

  it("clears the error once a later save succeeds", async () => {
    let failNext = true;
    vi.mocked(apiClient.call).mockImplementation(async (_service, method, params) => {
      if (method === "update") {
        if (failNext) { failNext = false; throw new Error("busy"); }
        return params;
      }
      return stored;
    });
    const { result } = renderHook(() => useVocabulary());
    await waitFor(() => expect(result.current.vocabulary).toEqual(stored));

    await act(async () => { await result.current.save(stored).catch(() => {}); });
    expect(result.current.error).not.toBeNull();

    await act(async () => { await result.current.save(stored); });
    expect(result.current.error).toBeNull();
  });

  it("edit builds the new document from the current one", async () => {
    mockEchoServer();
    const { result } = renderHook(() => useVocabulary());
    await waitFor(() => expect(result.current.vocabulary).toEqual(stored));

    await act(async () => {
      await result.current.edit((v) => ({ ...v, hintWords: [...v.hintWords, "Whisper"] }));
    });

    expect(updateCalls()[0][2]).toEqual({ hintWords: ["Murmur", "Whisper"], replacements: stored.replacements });
    expect(result.current.vocabulary?.hintWords).toEqual(["Murmur", "Whisper"]);
  });

  it("queued edits build on each other instead of overwriting each other", async () => {
    mockEchoServer({ hintWords: [], replacements: [] });
    const { result } = renderHook(() => useVocabulary());
    await waitFor(() => expect(result.current.vocabulary).not.toBeNull());

    await act(async () => {
      const first = result.current.edit((v) => ({ ...v, hintWords: [...v.hintWords, "one"] }));
      const second = result.current.edit((v) => ({ ...v, hintWords: [...v.hintWords, "two"] }));
      await Promise.all([first, second]);
    });

    expect(updateCalls().map(([, , params]) => (params as Vocabulary).hintWords)).toEqual([["one"], ["one", "two"]]);
    expect(result.current.vocabulary?.hintWords).toEqual(["one", "two"]);
  });

  it("a failed edit does not stop the edits queued behind it", async () => {
    let updates = 0;
    vi.mocked(apiClient.call).mockImplementation(async (_service, method, params) => {
      if (method !== "update") return { hintWords: [], replacements: [] };
      if (++updates === 1) throw new Error("busy");
      return params;
    });
    const { result } = renderHook(() => useVocabulary());
    await waitFor(() => expect(result.current.vocabulary).not.toBeNull());

    await act(async () => {
      const first = result.current.edit((v) => ({ ...v, hintWords: ["one"] })).catch(() => {});
      const second = result.current.edit((v) => ({ ...v, hintWords: [...v.hintWords, "two"] }));
      await Promise.all([first, second]);
    });

    expect(result.current.vocabulary?.hintWords).toEqual(["two"]);
    expect(result.current.error).toBeNull();
  });

  it("edit rejects before the vocabulary has loaded", async () => {
    vi.mocked(apiClient.call).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useVocabulary());

    await expect(result.current.edit((v) => v)).rejects.toThrow(/not.*loaded/i);
    expect(updateCalls()).toHaveLength(0);
  });
});
