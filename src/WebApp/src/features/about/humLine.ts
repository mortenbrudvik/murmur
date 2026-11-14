export interface Point {
  x: number;
  y: number;
}

/** Where the icon sits in a drawing: the tile's corner and size, and how wide the drawing is. */
export interface HumFrame {
  width: number;
  tileX: number;
  tileY: number;
  tileSize: number;
}

/** The icon's tile is 56 units wide on a 64-unit grid, with its corner at (4, 4). See murmur-icon.svg. */
const TILE_UNITS = 56;
const TILE_ORIGIN = 4;
const BASELINE = 43;

type Segment =
  | { kind: "line"; from: Point; to: Point; length: number }
  | { kind: "arc"; centre: Point; radius: number; start: number; sweep: number; length: number };

const line = (x1: number, y1: number, x2: number, y2: number): Segment => ({
  kind: "line",
  from: { x: x1, y: y1 },
  to: { x: x2, y: y2 },
  length: Math.hypot(x2 - x1, y2 - y1),
});

/** Angles in radians, with y pointing down as on screen: 0 is right, PI/2 is down. */
const arc = (cx: number, cy: number, radius: number, start: number, sweep: number): Segment => ({
  kind: "arc",
  centre: { x: cx, y: cy },
  radius,
  start,
  sweep,
  length: Math.abs(sweep) * radius,
});

/**
 * The icon's line in icon units, from x0 to x1 along the baseline with the m in between. The same path as in
 * murmur-icon.svg: M0 43 H15 a4 4 0 0 0 4 -4 V28.5 a6.5 6.5 0 0 1 13 0 V43 V28.5 a6.5 6.5 0 0 1 13 0 V39 a4 4 0 0 0 4 4 H64.
 */
function segments(x0: number, x1: number): Segment[] {
  const half = Math.PI / 2;
  return [
    line(x0, BASELINE, 15, BASELINE),
    arc(15, 39, 4, half, -half),
    line(19, 39, 19, 28.5),
    arc(25.5, 28.5, 6.5, Math.PI, Math.PI),
    line(32, 28.5, 32, BASELINE),
    line(32, BASELINE, 32, 28.5),
    arc(38.5, 28.5, 6.5, Math.PI, Math.PI),
    line(45, 28.5, 45, 39),
    arc(49, 39, 4, Math.PI, -half),
    line(49, BASELINE, x1, BASELINE),
  ];
}

function pointOn(segment: Segment, t: number): Point {
  if (segment.kind === "line") {
    return {
      x: segment.from.x + (segment.to.x - segment.from.x) * t,
      y: segment.from.y + (segment.to.y - segment.from.y) * t,
    };
  }

  const angle = segment.start + segment.sweep * t;
  return {
    x: segment.centre.x + segment.radius * Math.cos(angle),
    y: segment.centre.y + segment.radius * Math.sin(angle),
  };
}

/**
 * The icon's line placed in a drawing: `count` points spaced evenly along it, from the drawing's left edge to its
 * right edge, rising into the m where the tile is.
 */
export function humLine(frame: HumFrame, count: number): Point[] {
  const scale = frame.tileSize / TILE_UNITS;
  const offsetX = frame.tileX - TILE_ORIGIN * scale;
  const offsetY = frame.tileY - TILE_ORIGIN * scale;
  const parts = segments(-offsetX / scale, (frame.width - offsetX) / scale);
  const total = parts.reduce((sum, part) => sum + part.length, 0);

  const points: Point[] = [];
  let index = 0;
  let before = 0;
  for (let i = 0; i < count; i++) {
    const distance = (total * i) / (count - 1);
    while (index < parts.length - 1 && distance > before + parts[index].length) {
      before += parts[index].length;
      index++;
    }

    const part = parts[index];
    const p = pointOn(part, part.length > 0 ? Math.min(1, (distance - before) / part.length) : 0);
    points.push({ x: offsetX + p.x * scale, y: offsetY + p.y * scale });
  }

  return points;
}

/** The points as an SVG path. */
export function toPath(points: Point[]): string {
  let d = "";
  for (let i = 0; i < points.length; i++) d += `${i ? "L" : "M"}${points[i].x.toFixed(1)} ${points[i].y.toFixed(1)}`;
  return d;
}
