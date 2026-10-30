import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import EngineStatusBanner from "../EngineStatusBanner";
import type { DownloadProgress, EngineStatus } from "../../../types/domain";

const smallDownload: DownloadProgress = {
  modelName: "Whisper small (English)",
  bytesReceived: 186_000_000,
  totalBytes: 488_000_000,
  bytesPerSecond: 18_000_000,
  secondsRemaining: 20,
  retries: 0,
  reconnecting: false,
};
const largeDownload: DownloadProgress = {
  modelName: "Whisper large-v3-turbo",
  bytesReceived: 400_000_000,
  totalBytes: 1_600_000_000,
  bytesPerSecond: 18_000_000,
  secondsRemaining: 70,
  retries: 0,
  reconnecting: false,
};
const ready: EngineStatus = { state: "ready", modelName: "Whisper large-v3-turbo", runtime: "Cuda", error: null, download: null, gpu: null };

describe("EngineStatusBanner", () => {
  describe("while a model switch downloads and the old model stays ready", () => {
    it("shows the model name and percent, with a progress bar, in the status strip", () => {
      render(<EngineStatusBanner status={{ ...ready, download: smallDownload }} />);

      const banner = screen.getByRole("status");
      expect(banner.className).toBe("engine-banner");
      expect(screen.getByText("Downloading Whisper small (English)… 38%")).toBeTruthy();
      const bar = screen.getByRole("progressbar", { name: "Model download" });
      expect(bar.getAttribute("aria-valuenow")).toBe("38");
      expect(bar.getAttribute("aria-valuemin")).toBe("0");
      expect(bar.getAttribute("aria-valuemax")).toBe("100");
    });

    it("shows the amount, the speed and the time left like the first download does", () => {
      render(<EngineStatusBanner status={{ ...ready, download: smallDownload }} />);

      expect(screen.getByText("186 MB of 488 MB · 18 MB/s · less than a minute left")).toBeTruthy();
    });

    it("does not read the text out at every step", () => {
      render(<EngineStatusBanner status={{ ...ready, download: smallDownload }} />);

      expect(screen.getByRole("status").getAttribute("aria-live")).toBe("off");
    });

    it("does not style the ready state as an error", () => {
      render(<EngineStatusBanner status={{ ...ready, download: smallDownload }} />);

      expect(screen.queryByRole("alert")).toBeNull();
      expect(screen.getByRole("status").className).not.toContain("engine-banner-error");
    });

    it("says Reconnecting… instead of the percent while the download reconnects, and keeps the bar", () => {
      render(<EngineStatusBanner status={{ ...ready, download: { ...smallDownload, retries: 2, reconnecting: true } }} />);

      expect(screen.getByText("Reconnecting…")).toBeTruthy();
      expect(screen.queryByText(/^Downloading/)).toBeNull();
      expect(screen.getAllByText(/Reconnecting…/)).toHaveLength(1);
      expect(screen.getByText("186 MB of 488 MB")).toBeTruthy();
      expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("38");
    });

    it("shows an empty bar while the total size is not known", () => {
      render(<EngineStatusBanner status={{ ...ready, download: { ...smallDownload, bytesReceived: 0, totalBytes: 0 } }} />);

      expect(screen.getByText("Downloading Whisper small (English)… 0%")).toBeTruthy();
      expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("0");
    });
  });

  it("shows nothing when the engine is ready and nothing downloads", () => {
    const { container } = render(<EngineStatusBanner status={ready} />);

    expect(container.firstChild).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.queryByRole("progressbar")).toBeNull();
  });

  it("goes away when the switch download is over", () => {
    const { container, rerender } = render(<EngineStatusBanner status={{ ...ready, download: smallDownload }} />);
    expect(screen.getByRole("status")).toBeTruthy();

    rerender(<EngineStatusBanner status={ready} />);

    expect(container.firstChild).toBeNull();
  });

  it("shows nothing while the status is not known", () => {
    const { container } = render(<EngineStatusBanner status={null} />);

    expect(container.firstChild).toBeNull();
  });

  it("keeps the first download as it was", () => {
    render(<EngineStatusBanner status={{ ...ready, state: "downloading", runtime: null, download: largeDownload }} />);

    expect(screen.getByRole("status").className).toBe("engine-banner");
    expect(screen.getByRole("status").getAttribute("aria-live")).toBe("off");
    expect(screen.getByText("Downloading speech model… 25%")).toBeTruthy();
    expect(screen.getByText("400 MB of 1.6 GB · 18 MB/s · about 1 minute left")).toBeTruthy();
    expect(screen.getByRole("progressbar").getAttribute("aria-valuenow")).toBe("25");
  });

  it("keeps Reconnecting… and the amount of the first download as they were", () => {
    render(
      <EngineStatusBanner
        status={{ ...ready, state: "downloading", runtime: null, download: { ...largeDownload, retries: 1, reconnecting: true } }}
      />,
    );

    expect(screen.getAllByText(/Reconnecting…/)).toHaveLength(1);
    expect(screen.getByText("400 MB of 1.6 GB")).toBeTruthy();
  });

  it("keeps Loading model… as it was", () => {
    render(<EngineStatusBanner status={{ ...ready, state: "loading", runtime: null }} />);

    expect(screen.getByRole("status").textContent).toBe("Loading model…");
    expect(screen.queryByRole("progressbar")).toBeNull();
  });

  it("keeps the failure as an alert in error styling", () => {
    render(<EngineStatusBanner status={{ ...ready, state: "error", runtime: null, error: "Model file is corrupt" }} />);

    const alert = screen.getByRole("alert");
    expect(alert.textContent).toBe("Model file is corrupt");
    expect(alert.className).toBe("engine-banner engine-banner-error");
  });
});
