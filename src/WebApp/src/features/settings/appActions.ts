import { apiClient } from "../../interop/WebViewApiClient";
import type { LinkTarget } from "../../types/domain";

/** Opens one of Murmur's fixed links (the CUDA download, the NVIDIA driver page, Windows' microphone privacy settings). */
export async function openLink(target: LinkTarget): Promise<void> {
  await apiClient.call("app", "openLink", { target });
}

/** Restarts Murmur, for example after CUDA was installed: the runtime is only picked up at start. */
export async function restartMurmur(): Promise<void> {
  await apiClient.call("app", "restart", {});
}
