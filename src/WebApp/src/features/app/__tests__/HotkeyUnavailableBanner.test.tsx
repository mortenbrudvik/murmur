import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import HotkeyUnavailableBanner from "../HotkeyUnavailableBanner";
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

describe("HotkeyUnavailableBanner", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.call).mockResolvedValue({});
  });

  it("says dictation is off and what to do, as an alert with a Restart Murmur button", () => {
    render(<HotkeyUnavailableBanner status={refused} />);

    const alert = screen.getByRole("alert");
    expect(alert.textContent).toContain("Windows didn't let Murmur watch the keyboard, so dictation is off. Restart Murmur to try again.");
    expect(screen.getByRole("button", { name: "Restart Murmur" })).toBeTruthy();
  });

  it("shows nothing when the hook is installed", () => {
    const { container } = render(<HotkeyUnavailableBanner status={installed} />);

    expect(container.textContent).toBe("");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("shows nothing while the status is unknown", () => {
    const { container } = render(<HotkeyUnavailableBanner status={null} />);

    expect(container.textContent).toBe("");
  });

  it("restarts Murmur when the button is pressed", () => {
    render(<HotkeyUnavailableBanner status={refused} />);

    fireEvent.click(screen.getByRole("button", { name: "Restart Murmur" }));

    expect(apiClient.call).toHaveBeenCalledWith("app", "restart", {});
  });

  it("reports a restart that could not be asked for, and keeps the button", async () => {
    vi.mocked(apiClient.call).mockRejectedValue(new Error("bridge down"));
    render(<HotkeyUnavailableBanner status={refused} />);

    fireEvent.click(screen.getByRole("button", { name: "Restart Murmur" }));

    expect(await screen.findByText("Could not restart Murmur (bridge down).")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Restart Murmur" })).toBeTruthy();
  });
});
