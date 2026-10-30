import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import VocabularyPage from "../VocabularyPage";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { Vocabulary } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const stored: Vocabulary = {
  hintWords: ["Murmur", "Kubernetes"],
  replacements: [
    { spoken: "btw", written: "by the way" },
    { spoken: "gonna", written: "going to" },
  ],
};

type UpdateHandler = (params: Vocabulary) => Vocabulary | Promise<Vocabulary>;

/** The server: get returns the stored document; update runs the handler (default: store and echo). */
function mockServer(initial: Vocabulary = stored, onUpdate?: UpdateHandler) {
  let current = initial;
  vi.mocked(apiClient.call).mockImplementation(async (_service, method, params) => {
    if (method !== "update") return current;
    current = onUpdate ? await onUpdate(params as Vocabulary) : (params as Vocabulary);
    return current;
  });
}

function updateCalls() {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => service === "vocabulary" && method === "update");
}

function lastUpdate(): Vocabulary {
  const calls = updateCalls();
  return calls[calls.length - 1][2] as Vocabulary;
}

async function renderLoaded(initial: Vocabulary = stored, onUpdate?: UpdateHandler) {
  mockServer(initial, onUpdate);
  render(<VocabularyPage />);
  await screen.findByRole("heading", { name: "Hint words" });
  await screen.findByLabelText("Add a hint word");
}

function addWord(word: string) {
  const input = screen.getByLabelText("Add a hint word");
  fireEvent.change(input, { target: { value: word } });
  fireEvent.keyDown(input, { key: "Enter" });
}

function fillRule(spoken: string, written: string) {
  fireEvent.change(screen.getByLabelText("New rule: when I hear"), { target: { value: spoken } });
  fireEvent.change(screen.getByLabelText("New rule: write"), { target: { value: written } });
}

describe("VocabularyPage", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
  });

  it("shows the title, the helper text, the hint words and the rules", async () => {
    await renderLoaded();

    expect(screen.getByRole("heading", { name: "Vocabulary" })).toBeTruthy();
    expect(
      screen.getByText("Hint words help Murmur spell names and jargon. Rules replace what it hears with what you want written."),
    ).toBeTruthy();
    const chips = within(screen.getByRole("list", { name: "Hint words" })).getAllByRole("listitem");
    expect(chips.map((c) => c.textContent)).toEqual(["Murmur×", "Kubernetes×"]);
    expect(screen.getByPlaceholderText("Add a word…")).toBeTruthy();

    const table = screen.getByRole("table", { name: "Replacement rules" });
    expect(within(table).getByRole("columnheader", { name: "When I hear" })).toBeTruthy();
    expect(within(table).getByRole("columnheader", { name: "Write" })).toBeTruthy();
    expect(within(table).getByText("gonna")).toBeTruthy();
    expect(within(table).getByText("going to")).toBeTruthy();
  });

  it("says so when there are no hint words or rules yet", async () => {
    await renderLoaded({ hintWords: [], replacements: [] });

    expect(screen.getByText("No hint words yet.")).toBeTruthy();
    expect(screen.getByText("No rules yet.")).toBeTruthy();
  });

  it("adds a word with Enter, sends the whole document and clears the input", async () => {
    await renderLoaded();

    addWord("  Whisper ");

    await waitFor(() => expect(screen.getByRole("button", { name: "Remove Whisper" })).toBeTruthy());
    expect(lastUpdate()).toEqual({ hintWords: ["Murmur", "Kubernetes", "Whisper"], replacements: stored.replacements });
    expect((screen.getByLabelText("Add a hint word") as HTMLInputElement).value).toBe("");
  });

  it("shows what the server normalized rather than what was sent", async () => {
    await renderLoaded(stored, () => ({ hintWords: ["Murmur", "Whisper"], replacements: [] }));

    addWord("Whisper");

    await waitFor(() => expect(screen.queryByRole("button", { name: "Remove Kubernetes" })).toBeNull());
    expect(screen.getByRole("button", { name: "Remove Whisper" })).toBeTruthy();
    expect(screen.getByText("No rules yet.")).toBeTruthy();
  });

  it("does nothing for a blank word", async () => {
    await renderLoaded();

    addWord("   ");

    expect(updateCalls()).toHaveLength(0);
  });

  it("does not save a word that is already there, ignoring case, and clears the input", async () => {
    await renderLoaded();

    addWord("murmur");

    expect(updateCalls()).toHaveLength(0);
    expect((screen.getByLabelText("Add a hint word") as HTMLInputElement).value).toBe("");
  });

  it("does not add a word while an input method composition is in progress", async () => {
    await renderLoaded();
    const input = screen.getByLabelText("Add a hint word");

    fireEvent.change(input, { target: { value: "にほん" } });
    fireEvent.keyDown(input, { key: "Enter", isComposing: true });

    expect(updateCalls()).toHaveLength(0);
  });

  it("removes a word with its x", async () => {
    await renderLoaded();

    fireEvent.click(screen.getByRole("button", { name: "Remove Murmur" }));

    await waitFor(() => expect(screen.queryByRole("button", { name: "Remove Murmur" })).toBeNull());
    expect(lastUpdate()).toEqual({ hintWords: ["Kubernetes"], replacements: stored.replacements });
  });

  it("adds a rule with the Add button, sends the whole document and clears both inputs", async () => {
    await renderLoaded();

    fillRule("  wanna ", " want to ");
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(screen.getByText("wanna")).toBeTruthy());
    expect(lastUpdate()).toEqual({
      hintWords: stored.hintWords,
      replacements: [...stored.replacements, { spoken: "wanna", written: "want to" }],
    });
    expect((screen.getByLabelText("New rule: when I hear") as HTMLInputElement).value).toBe("");
    expect((screen.getByLabelText("New rule: write") as HTMLInputElement).value).toBe("");
  });

  it("adds a rule with Enter in the second input", async () => {
    await renderLoaded();

    fillRule("wanna", "want to");
    fireEvent.keyDown(screen.getByLabelText("New rule: write"), { key: "Enter" });

    await waitFor(() => expect(updateCalls()).toHaveLength(1));
  });

  it("allows a rule that writes nothing, to drop a filler word", async () => {
    await renderLoaded();

    fillRule("um", "");
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(updateCalls()).toHaveLength(1));
    expect(lastUpdate().replacements.at(-1)).toEqual({ spoken: "um", written: "" });
  });

  it("keeps Add disabled until there is something to hear", async () => {
    await renderLoaded();
    const add = screen.getByRole("button", { name: "Add" }) as HTMLButtonElement;

    expect(add.disabled).toBe(true);
    fillRule("   ", "x");
    expect(add.disabled).toBe(true);
    fillRule("x", "y");
    expect(add.disabled).toBe(false);
  });

  it("removes a rule with its x", async () => {
    await renderLoaded();

    fireEvent.click(screen.getByRole("button", { name: "Remove rule for gonna" }));

    await waitFor(() => expect(screen.queryByText("going to")).toBeNull());
    expect(lastUpdate()).toEqual({ hintWords: stored.hintWords, replacements: [stored.replacements[0]] });
  });

  it("keeps the typed word and shows an inline error when the save fails", async () => {
    await renderLoaded(stored, () => { throw new Error("disk full"); });

    addWord("Whisper");

    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toMatch(/could not save the vocabulary/i);
    expect((screen.getByLabelText("Add a hint word") as HTMLInputElement).value).toBe("Whisper");
    expect(screen.queryByRole("button", { name: "Remove Whisper" })).toBeNull();
  });

  it("keeps the typed rule and shows an inline error when the save fails", async () => {
    await renderLoaded(stored, () => { throw new Error("disk full"); });

    fillRule("wanna", "want to");
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    expect((await screen.findByRole("alert")).textContent).toMatch(/could not save the vocabulary/i);
    expect((screen.getByLabelText("New rule: when I hear") as HTMLInputElement).value).toBe("wanna");
    expect((screen.getByLabelText("New rule: write") as HTMLInputElement).value).toBe("want to");
  });

  it("clears the error when a later save works", async () => {
    let fail = true;
    await renderLoaded(stored, (params) => {
      if (fail) { fail = false; throw new Error("busy"); }
      return params;
    });

    addWord("Whisper");
    await screen.findByRole("alert");
    addWord("Whisper");

    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    expect(screen.getByRole("button", { name: "Remove Whisper" })).toBeTruthy();
  });

  it("does not wipe text typed while a save was still in flight", async () => {
    let finish: () => void = () => {};
    await renderLoaded(stored, (params) => new Promise<Vocabulary>((resolve) => { finish = () => resolve(params); }));

    addWord("One");
    await waitFor(() => expect(updateCalls()).toHaveLength(1));
    fireEvent.change(screen.getByLabelText("Add a hint word"), { target: { value: "Two" } });
    finish();

    await waitFor(() => expect(screen.getByRole("button", { name: "Remove One" })).toBeTruthy());
    expect((screen.getByLabelText("Add a hint word") as HTMLInputElement).value).toBe("Two");
  });

  it("shows an error when the vocabulary cannot be loaded", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));

    render(<VocabularyPage />);

    expect((await screen.findByRole("alert")).textContent).toMatch(/bridge down/);
    expect(screen.queryByLabelText("Add a hint word")).toBeNull();
  });
});
