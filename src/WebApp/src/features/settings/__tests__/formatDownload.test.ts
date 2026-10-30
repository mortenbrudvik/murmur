import { describe, expect, it } from "vitest";
import { describeDownload, formatBytes, formatDownloadLine, formatRemaining, formatSpeed } from "../formatDownload";
import type { DownloadProgress } from "../../../types/domain";

const d: DownloadProgress = {
  modelName: "Whisper large-v3-turbo",
  bytesReceived: 0,
  totalBytes: 1_624_555_275,
  bytesPerSecond: 0,
  secondsRemaining: null,
  retries: 0,
  reconnecting: false,
};

describe("formatBytes", () => {
  it("uses decimal units: whole megabytes below a gigabyte, one decimal of gigabytes above", () => {
    expect(formatBytes(1_624_555_275)).toBe("1.6 GB");
    expect(formatBytes(487_614_201)).toBe("488 MB");
    expect(formatBytes(612_000_000)).toBe("612 MB");
    expect(formatBytes(1_000_000_000)).toBe("1.0 GB");
    expect(formatBytes(0)).toBe("0 MB");
  });

  it("does not show 1000 MB just below a gigabyte", () => {
    expect(formatBytes(999_700_000)).toBe("1.0 GB");
  });

  it("reads a negative or non-finite size as 0 MB", () => {
    expect(formatBytes(-5)).toBe("0 MB");
    expect(formatBytes(Number.NaN)).toBe("0 MB");
  });
});

describe("formatSpeed", () => {
  it("rounds to whole megabytes per second", () => {
    expect(formatSpeed(18_400_000)).toBe("18 MB/s");
  });

  it("switches to kilobytes per second below a megabyte", () => {
    expect(formatSpeed(640_000)).toBe("640 KB/s");
    expect(formatSpeed(999_800)).toBe("1 MB/s");
  });

  it("is null when there is no speed to show", () => {
    expect(formatSpeed(0)).toBeNull();
    expect(formatSpeed(-1)).toBeNull();
    expect(formatSpeed(Number.NaN)).toBeNull();
    expect(formatSpeed(Infinity)).toBeNull();
  });
});

describe("formatRemaining", () => {
  it("is null when the time is unknown or not finite", () => {
    expect(formatRemaining(null)).toBeNull();
    expect(formatRemaining(Infinity)).toBeNull();
    expect(formatRemaining(Number.NaN)).toBeNull();
  });

  it("says less than a minute under 60 seconds", () => {
    expect(formatRemaining(45)).toBe("less than a minute left");
    expect(formatRemaining(0)).toBe("less than a minute left");
  });

  it("says about 1 minute up to 90 seconds, then rounds to minutes", () => {
    expect(formatRemaining(60)).toBe("about 1 minute left");
    expect(formatRemaining(70)).toBe("about 1 minute left");
    expect(formatRemaining(89)).toBe("about 1 minute left");
    expect(formatRemaining(90)).toBe("about 2 minutes left");
    expect(formatRemaining(300)).toBe("about 5 minutes left");
  });

  it("switches to hours from an hour on, and never says 60 minutes", () => {
    expect(formatRemaining(3599)).toBe("about 1 hour left");
    expect(formatRemaining(3600)).toBe("about 1 hour left");
    expect(formatRemaining(7200)).toBe("about 2 hours left");
  });
});

describe("describeDownload", () => {
  it("gives the amount, the speed with the time left, and the rounded percent", () => {
    expect(
      describeDownload({ ...d, bytesReceived: 612_000_000, bytesPerSecond: 18_000_000, secondsRemaining: 56 }),
    ).toEqual({ amount: "612 MB of 1.6 GB", detail: "18 MB/s · less than a minute left", percent: 38 });
  });

  it("says Reconnecting… instead of the speed while reconnecting", () => {
    expect(describeDownload({ ...d, retries: 2, reconnecting: true }).detail).toBe("Reconnecting…");
  });

  it("has no detail when neither the speed nor the time left is known", () => {
    expect(describeDownload({ ...d, bytesPerSecond: 0, secondsRemaining: null }).detail).toBeNull();
  });

  it("shows whichever of the speed and the time left is known", () => {
    expect(describeDownload({ ...d, bytesPerSecond: 18_000_000, secondsRemaining: null }).detail).toBe("18 MB/s");
    expect(describeDownload({ ...d, bytesPerSecond: 0, secondsRemaining: 300 }).detail).toBe("about 5 minutes left");
  });

  it("keeps the percent within 0..100, and 0 when the total is not known yet", () => {
    expect(describeDownload({ ...d, totalBytes: 0 }).percent).toBe(0);
    expect(describeDownload({ ...d, bytesReceived: 2_000_000_000 }).percent).toBe(100);
    expect(describeDownload({ ...d, bytesReceived: -1 }).percent).toBe(0);
  });
});

describe("formatDownloadLine", () => {
  it("joins the amount, the speed and the time left", () => {
    expect(formatDownloadLine({ ...d, bytesReceived: 612_000_000, bytesPerSecond: 18_000_000, secondsRemaining: 56 })).toBe(
      "612 MB of 1.6 GB · 18 MB/s · less than a minute left",
    );
  });

  it("is only the amount when neither the speed nor the time left is known", () => {
    expect(formatDownloadLine({ ...d, bytesReceived: 612_000_000 })).toBe("612 MB of 1.6 GB");
  });

  it("is only the amount while reconnecting, because the status text says so", () => {
    expect(formatDownloadLine({ ...d, bytesReceived: 612_000_000, bytesPerSecond: 18_000_000, retries: 2, reconnecting: true })).toBe(
      "612 MB of 1.6 GB",
    );
  });
});
