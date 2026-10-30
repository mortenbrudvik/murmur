import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import CudaTip from "../CudaTip";
import { apiClient } from "../../../interop/WebViewApiClient";
import type { GpuAdvice } from "../../../types/domain";

vi.mock("../../../interop/WebViewApiClient", () => ({
  apiClient: {
    call: vi.fn(),
    on: vi.fn(() => vi.fn()),
  },
}));

const GPU = "NVIDIA GeForce RTX 3060";
const installCuda: GpuAdvice = { state: "installCuda", gpuName: GPU };
const updateDriver: GpuAdvice = { state: "updateDriver", gpuName: GPU };

function callsTo(key: string) {
  return vi.mocked(apiClient.call).mock.calls.filter(([service, method]) => `${service}.${method}` === key);
}

describe("CudaTip", () => {
  beforeEach(() => {
    vi.mocked(apiClient.call).mockReset();
    vi.mocked(apiClient.call).mockResolvedValue({});
  });

  describe("setup variant, installCuda", () => {
    it("names the graphics card, explains Vulkan and what to install", () => {
      render(<CudaTip advice={installCuda} variant="setup" onDismiss={() => undefined} />);

      expect(screen.getByText("Your NVIDIA GeForce RTX 3060 can run Murmur about twice as fast.")).toBeTruthy();
      expect(
        screen.getByText(
          "Murmur is using your graphics card through Vulkan. Install NVIDIA's free CUDA 13 toolkit (about 3 GB) and restart Murmur to use CUDA.",
        ),
      ).toBeTruthy();
      expect(screen.getByRole("button", { name: "Get CUDA 13 ↗" })).toBeTruthy();
      expect(screen.getByRole("button", { name: "Not now" })).toBeTruthy();
    });

    it("opens the CUDA download, and only then offers Restart Murmur", async () => {
      render(<CudaTip advice={installCuda} variant="setup" onDismiss={() => undefined} />);
      expect(screen.queryByRole("button", { name: "Restart Murmur" })).toBeNull();

      fireEvent.click(screen.getByRole("button", { name: "Get CUDA 13 ↗" }));

      expect(await screen.findByRole("button", { name: "Restart Murmur" })).toBeTruthy();
      expect(apiClient.call).toHaveBeenCalledWith("app", "openLink", { target: "cudaDownload" });
    });

    it("restarts Murmur from the Restart button", async () => {
      render(<CudaTip advice={installCuda} variant="setup" onDismiss={() => undefined} />);
      fireEvent.click(screen.getByRole("button", { name: "Get CUDA 13 ↗" }));

      fireEvent.click(await screen.findByRole("button", { name: "Restart Murmur" }));

      expect(apiClient.call).toHaveBeenCalledWith("app", "restart", {});
    });

    it("calls onDismiss from Not now, and does not open anything", () => {
      const onDismiss = vi.fn();
      render(<CudaTip advice={installCuda} variant="setup" onDismiss={onDismiss} />);

      fireEvent.click(screen.getByRole("button", { name: "Not now" }));

      expect(onDismiss).toHaveBeenCalledTimes(1);
      expect(apiClient.call).not.toHaveBeenCalled();
    });

    it("has no Not now button when there is nothing to dismiss", () => {
      render(<CudaTip advice={installCuda} variant="setup" />);

      expect(screen.queryByRole("button", { name: "Not now" })).toBeNull();
    });
  });

  describe("settings variant, installCuda", () => {
    it("says CUDA is not installed and offers both buttons at once", () => {
      render(<CudaTip advice={installCuda} variant="settings" />);

      expect(
        screen.getByText("CUDA isn't installed. Your NVIDIA GeForce RTX 3060 can run Murmur about twice as fast with CUDA 13."),
      ).toBeTruthy();
      expect(screen.getByRole("button", { name: "Get CUDA 13 ↗" })).toBeTruthy();
      expect(screen.getByRole("button", { name: "Restart Murmur" })).toBeTruthy();
      expect(screen.queryByRole("button", { name: "Not now" })).toBeNull();
    });

    it("opens the CUDA download and restarts Murmur", async () => {
      render(<CudaTip advice={installCuda} variant="settings" />);

      fireEvent.click(screen.getByRole("button", { name: "Get CUDA 13 ↗" }));
      fireEvent.click(screen.getByRole("button", { name: "Restart Murmur" }));

      await waitFor(() => expect(callsTo("app.restart")).toHaveLength(1));
      expect(apiClient.call).toHaveBeenCalledWith("app", "openLink", { target: "cudaDownload" });
      expect(apiClient.call).toHaveBeenCalledWith("app", "restart", {});
    });
  });

  describe("updateDriver", () => {
    it("asks for a driver update in the setup variant, with no Restart button", () => {
      render(<CudaTip advice={updateDriver} variant="setup" onDismiss={() => undefined} />);

      expect(screen.getByText("Update your NVIDIA driver to use CUDA.")).toBeTruthy();
      expect(screen.queryByRole("button", { name: "Restart Murmur" })).toBeNull();
    });

    it.each(["setup", "settings"] as const)("opens the NVIDIA driver page in the %s variant", async (variant) => {
      render(<CudaTip advice={updateDriver} variant={variant} />);

      fireEvent.click(screen.getByRole("button", { name: "Update NVIDIA driver ↗" }));

      await waitFor(() => expect(callsTo("app.openLink")).toHaveLength(1));
      expect(apiClient.call).toHaveBeenCalledWith("app", "openLink", { target: "nvidiaDriver" });
    });

    it("adds Restart Murmur in the settings variant", () => {
      render(<CudaTip advice={updateDriver} variant="settings" />);

      expect(screen.getByText("Update your NVIDIA driver to use CUDA.")).toBeTruthy();
      expect(screen.getByRole("button", { name: "Restart Murmur" })).toBeTruthy();
    });
  });

  describe("nothing to say", () => {
    it.each([
      ["notApplicable", { state: "notApplicable", gpuName: null } as GpuAdvice],
      ["cudaInUse", { state: "cudaInUse", gpuName: GPU } as GpuAdvice],
      ["no advice yet", null],
    ])("renders nothing for %s", (_name, advice) => {
      for (const variant of ["setup", "settings"] as const) {
        const { container, unmount } = render(<CudaTip advice={advice} variant={variant} onDismiss={() => undefined} />);
        expect(container.innerHTML).toBe("");
        unmount();
      }
    });
  });

  describe("when a button fails", () => {
    it("says the link could not be opened", async () => {
      vi.mocked(apiClient.call).mockRejectedValue(new Error("no browser"));
      render(<CudaTip advice={installCuda} variant="settings" />);

      fireEvent.click(screen.getByRole("button", { name: "Get CUDA 13 ↗" }));

      expect((await screen.findByRole("alert")).textContent).toBe("Could not open the link (no browser).");
    });

    it("says Murmur could not be restarted", async () => {
      vi.mocked(apiClient.call).mockRejectedValue(new Error("busy"));
      render(<CudaTip advice={installCuda} variant="settings" />);

      fireEvent.click(screen.getByRole("button", { name: "Restart Murmur" }));

      expect((await screen.findByRole("alert")).textContent).toBe("Could not restart Murmur (busy).");
    });
  });
});
