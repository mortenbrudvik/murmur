import type { DownloadProgress } from "../../types/domain";

// Decimal units, so the copy reads "1.6 GB" and "488 MB" like the sizes the model choice names.
const MB = 1_000_000;
const GB = 1_000_000_000;

/** "612 MB" below a gigabyte (whole megabytes), "1.6 GB" from there on (one decimal). */
export function formatBytes(bytes: number): string {
  const size = Number.isFinite(bytes) ? Math.max(0, bytes) : 0;
  const megabytes = Math.round(size / MB);
  // 999.7 MB is shown as 1.0 GB rather than as "1000 MB".
  if (megabytes < 1000) return `${megabytes} MB`;
  return `${(size / GB).toFixed(1)} GB`;
}

/** "18 MB/s", or "640 KB/s" below a megabyte; null when there is no speed to show. */
export function formatSpeed(bytesPerSecond: number): string | null {
  if (!Number.isFinite(bytesPerSecond) || bytesPerSecond <= 0) return null;
  const kilobytes = Math.max(1, Math.round(bytesPerSecond / 1000));
  if (kilobytes < 1000) return `${kilobytes} KB/s`;
  return `${Math.round(bytesPerSecond / MB)} MB/s`;
}

/** "about 5 minutes left"; null when the time is unknown. */
export function formatRemaining(seconds: number | null): string | null {
  if (seconds === null || !Number.isFinite(seconds)) return null;
  if (seconds < 60) return "less than a minute left";
  if (seconds < 90) return "about 1 minute left";

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `about ${minutes} minutes left`;

  const hours = Math.round(seconds / 3600);
  return hours === 1 ? "about 1 hour left" : `about ${hours} hours left`;
}

export interface DownloadDescription {
  /** "612 MB of 1.6 GB" */
  amount: string;
  /** "Reconnecting…", or the speed and the time left ("18 MB/s · about 1 minute left"); null when neither is known. */
  detail: string | null;
  /** 0..100, rounded; 0 until the total size is known. */
  percent: number;
}

export function describeDownload(download: DownloadProgress): DownloadDescription {
  const { bytesReceived, totalBytes } = download;
  const fraction = totalBytes > 0 ? bytesReceived / totalBytes : 0;
  const percent = Math.round(Math.min(1, Math.max(0, fraction)) * 100);

  const parts = [formatSpeed(download.bytesPerSecond), formatRemaining(download.secondsRemaining)].filter(
    (part): part is string => part !== null,
  );
  const detail = download.reconnecting ? "Reconnecting…" : parts.length > 0 ? parts.join(" · ") : null;

  return { amount: `${formatBytes(bytesReceived)} of ${formatBytes(totalBytes)}`, detail, percent };
}

/**
 * "612 MB of 1.6 GB · 18 MB/s · about 1 minute left" for the status line next to the bar. While reconnecting only the
 * amount is given: the status text beside it already says "Reconnecting…".
 */
export function formatDownloadLine(download: DownloadProgress): string {
  const { amount, detail } = describeDownload(download);
  return detail && !download.reconnecting ? `${amount} · ${detail}` : amount;
}
