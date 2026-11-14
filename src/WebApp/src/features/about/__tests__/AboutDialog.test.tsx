import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AboutDialog from "../AboutDialog";
import { buildDetails } from "../aboutText";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { AboutInfo, EngineStatus } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const about: AboutInfo = {
  version: "1.1.0",
  channel: "Microsoft Store",
  dataFolder: "C:\\Users\\me\\AppData\\Local\\Murmur",
  windowsBuild: "Windows 11, build 26200",
  dictations: 12,
  words: 420,
  since: "2026-10-03T08:00:00+00:00",
};

const ready: EngineStatus = {
  state: "ready",
  modelName: "Whisper large-v3-turbo",
  runtime: "Cuda",
  error: null,
  download: null,
  gpu: { state: "cudaInUse", gpuName: "NVIDIA GeForce RTX 3090" },
  switching: false,
};

function mockShell(info: AboutInfo | Error = about) {
  vi.mocked(apiClient.call).mockImplementation(async (service, method) => {
    if (`${service}.${method}` === "app.getAbout") {
      if (info instanceof Error) throw info;
      return info;
    }
    return null;
  });
}

function mockClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
}

/** The row's value: the dd that follows the dt with this label. */
function row(label: string): string {
  const term = screen.getByText(label, { selector: "dt" });
  return term.nextElementSibling?.textContent ?? "";
}

async function openDialog(engineStatus: EngineStatus | null = ready, onClose = vi.fn()) {
  const view = render(<AboutDialog open engineStatus={engineStatus} onClose={onClose} />);
  await waitFor(() => expect(vi.mocked(apiClient.call)).toHaveBeenCalledWith("app", "getAbout", {}));
  await act(async () => {});
  return { ...view, onClose };
}

describe("AboutDialog", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    mockShell();
  });

  it("is not in the document while closed, and asks the shell nothing", () => {
    render(<AboutDialog open={false} engineStatus={ready} onClose={vi.fn()} />);

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(apiClient.call).not.toHaveBeenCalled();
  });

  it("is a modal dialog named Murmur with the version, channel and tagline", async () => {
    await openDialog();

    const dialog = screen.getByRole("dialog", { name: "Murmur" });
    expect(dialog.getAttribute("aria-modal")).toBe("true");
    expect(screen.getByText("Version 1.1.0 · Microsoft Store")).toBeTruthy();
    expect(
      screen.getByText("Hold a key, talk, and the words land where you were typing. Nothing you say leaves this PC."),
    ).toBeTruthy();
  });

  it("shows the model, what it runs on, what was dictated and the data folder", async () => {
    await openDialog();

    expect(row("Speech model")).toBe("Whisper large-v3-turbo");
    expect(row("Running on")).toBe("NVIDIA GeForce RTX 3090 · CUDA");
    expect(row("Dictated so far")).toContain("420 words");
    expect(row("Dictated so far")).toContain("in 12 dictations since October 2026");
    expect(row("Your data")).toContain("C:\\Users\\me\\AppData\\Local\\Murmur");
  });

  it("says Nothing yet for an empty history", async () => {
    mockShell({ ...about, dictations: 0, words: 0, since: null });

    await openDialog();

    expect(row("Dictated so far")).toBe("Nothing yet");
  });

  it("shows Not chosen yet and Starting… before the engine reports", async () => {
    await openDialog(null);

    expect(row("Speech model")).toBe("Not chosen yet");
    expect(row("Running on")).toBe("Starting…");
  });

  it("updates the model and running-on rows when the engine status changes while open", async () => {
    const loading: EngineStatus = { ...ready, state: "loading", runtime: null, gpu: null };
    const { rerender, onClose } = await openDialog(loading);
    expect(row("Running on")).toBe("Starting…");

    rerender(<AboutDialog open engineStatus={{ ...ready, runtime: "Vulkan" }} onClose={onClose} />);

    expect(row("Running on")).toBe("Vulkan");
  });

  it("puts the year of the build and the author in the footer", async () => {
    await openDialog();

    expect(screen.getByText(`© ${new Date().getFullYear()}`, { exact: false })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Morten Brudvik" })).toBeTruthy();
  });

  it("focuses Close when it opens", async () => {
    await openDialog();

    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Close" }));
  });

  it("closes on Esc, on Close and on a press on the dimmed area, but not on a press inside", async () => {
    const { onClose } = await openDialog();
    const dialog = screen.getByRole("dialog");

    fireEvent.mouseDown(dialog);
    expect(onClose).not.toHaveBeenCalled();

    fireEvent.keyDown(dialog, { key: "Escape" });
    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    fireEvent.mouseDown(dialog.parentElement!);

    expect(onClose).toHaveBeenCalledTimes(3);
  });

  it("keeps Tab inside the dialog", async () => {
    await openDialog();
    const first = screen.getByRole("button", { name: "Open folder" });
    const last = screen.getByRole("button", { name: "Close" });

    last.focus();
    fireEvent.keyDown(last, { key: "Tab" });
    expect(document.activeElement).toBe(first);

    fireEvent.keyDown(first, { key: "Tab", shiftKey: true });
    expect(document.activeElement).toBe(last);
  });

  it("gives focus back to what had it when it closes", async () => {
    const opener = document.createElement("button");
    document.body.appendChild(opener);
    opener.focus();
    const { rerender, onClose } = await openDialog();

    rerender(<AboutDialog open={false} engineStatus={ready} onClose={onClose} />);

    expect(document.activeElement).toBe(opener);
    opener.remove();
  });

  it.each([
    ["Source on GitHub", "repository"],
    ["Privacy policy", "privacyPolicy"],
    ["MIT License", "license"],
    ["Morten Brudvik", "homePage"],
    ["Open folder", "dataFolder"],
  ])("opens %s by its fixed target", async (name, target) => {
    await openDialog();

    fireEvent.click(screen.getByRole("button", { name }));

    expect(apiClient.call).toHaveBeenCalledWith("app", "openLink", { target });
  });

  it("says so when a link cannot be opened", async () => {
    await openDialog();
    vi.mocked(apiClient.call).mockRejectedValue(new Error("no browser"));

    fireEvent.click(screen.getByRole("button", { name: "Privacy policy" }));

    expect((await screen.findByRole("alert")).textContent).toContain("Could not open the link");
  });

  it("copies the details and says Copied", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    mockClipboard(writeText);
    await openDialog();

    fireEvent.click(screen.getByRole("button", { name: "Copy details" }));

    await screen.findByRole("button", { name: "Copied" });
    expect(writeText).toHaveBeenCalledWith(buildDetails(about, ready));
  });

  it("says Could not copy when the clipboard refuses", async () => {
    mockClipboard(vi.fn().mockRejectedValue(new Error("denied")));
    await openDialog();

    fireEvent.click(screen.getByRole("button", { name: "Copy details" }));

    expect(await screen.findByRole("button", { name: "Could not copy" })).toBeTruthy();
  });

  it("says Unavailable and cannot copy when the shell does not answer", async () => {
    mockShell(new Error("bridge down"));

    await openDialog();

    expect(row("Dictated so far")).toBe("Unavailable");
    expect(row("Your data")).toContain("Unavailable");
    expect(screen.getByText("Version unavailable")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Copy details" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("reads the details again each time it opens, so the count is current", async () => {
    const { rerender, onClose } = await openDialog();
    rerender(<AboutDialog open={false} engineStatus={ready} onClose={onClose} />);

    rerender(<AboutDialog open engineStatus={ready} onClose={onClose} />);

    await waitFor(() =>
      expect(vi.mocked(apiClient.call).mock.calls.filter(([, method]) => method === "getAbout").length).toBe(2),
    );
  });
});
