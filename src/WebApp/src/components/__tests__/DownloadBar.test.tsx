import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import DownloadBar from "../DownloadBar";

describe("DownloadBar", () => {
  it("is a progress bar named Model download, filled to the percent", () => {
    render(<DownloadBar percent={38} />);

    const bar = screen.getByRole("progressbar", { name: "Model download" });
    expect(bar.getAttribute("aria-valuenow")).toBe("38");
    expect(bar.getAttribute("aria-valuemin")).toBe("0");
    expect(bar.getAttribute("aria-valuemax")).toBe("100");
    expect((bar.firstElementChild as HTMLElement).style.width).toBe("38%");
  });

  it("adds a variant class to its own", () => {
    render(<DownloadBar percent={0} className="setup-bar" />);

    expect(screen.getByRole("progressbar").className).toBe("download-bar setup-bar");
  });
});
