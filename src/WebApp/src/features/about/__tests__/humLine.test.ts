import { describe, expect, it } from "vitest";
import { humLine } from "../humLine";

/** The About header's frame: 460 wide, a 64 px tile at (198, 20). */
const header = { width: 460, tileX: 198, tileY: 20, tileSize: 64 };
const scale = header.tileSize / 56;
const baseline = header.tileY - 4 * scale + 43 * scale;

describe("humLine", () => {
  it("runs from the left edge to the right edge on the icon's baseline", () => {
    const points = humLine(header, 200);

    expect(points).toHaveLength(200);
    expect(points[0].x).toBeCloseTo(0, 5);
    expect(points[0].y).toBeCloseTo(baseline, 5);
    expect(points[199].x).toBeCloseTo(header.width, 5);
    expect(points[199].y).toBeCloseTo(baseline, 5);
  });

  it("rises into the m inside the tile and nowhere else", () => {
    const points = humLine(header, 400);
    const raised = points.filter((p) => p.y < baseline - 0.5);

    expect(raised.length).toBeGreaterThan(0);
    for (const p of raised) {
      expect(p.x).toBeGreaterThan(header.tileX);
      expect(p.x).toBeLessThan(header.tileX + header.tileSize);
      expect(p.y).toBeGreaterThan(header.tileY);
    }
  });

  it("tops out where the icon's humps do: 21 units above the baseline", () => {
    const top = Math.min(...humLine(header, 800).map((p) => p.y));

    expect(top).toBeCloseTo(baseline - 21 * scale, 1);
  });

  it("spaces its points evenly along the line", () => {
    const points = humLine(header, 300);
    const steps = points.slice(1).map((p, i) => Math.hypot(p.x - points[i].x, p.y - points[i].y));

    // The middle stem is walked down and back up, so two neighbours can be closer there, never further apart.
    expect(Math.max(...steps)).toBeLessThan(steps[0] * 1.01);
  });
});
