import { useEffect, useId, useMemo, useRef } from "react";
import { humLine, toPath, type HumFrame } from "./humLine";

/** The header's drawing: the icon's tile in the middle, its line running the dialog's width. */
const FRAME: HumFrame = { width: 460, tileX: 198, tileY: 20, tileSize: 64 };
const HEIGHT = 104;
const POINTS = 260;
/** How far from the tile the line is free to move; inside the tile it is the icon, and stays still. */
const FALL_OFF = 46;

const scale = FRAME.tileSize / 56;
const strokeWidth = 5.8 * scale;
const cornerRadius = 15 * scale;

function prefersReducedMotion(): boolean {
  return typeof window.matchMedia !== "function" || window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/**
 * The About dialog's header. Outside the tile the line hums: a slow wave a few pixels high, for as long as the header
 * is mounted. Still when the system asks for reduced motion.
 */
export default function HumHeader() {
  const id = useId();
  const dim = useRef<SVGPathElement>(null);
  const lit = useRef<SVGPathElement>(null);
  const rest = useMemo(() => humLine(FRAME, POINTS), []);
  const restPath = useMemo(() => toPath(rest), [rest]);

  useEffect(() => {
    if (prefersReducedMotion()) return;

    const moving = rest.map((p) => ({ ...p }));
    let frame = 0;
    const draw = (now: number) => {
      const t = now / 1000;
      for (let i = 0; i < rest.length; i++) {
        const { x, y } = rest[i];
        const outside = x < FRAME.tileX ? FRAME.tileX - x : x > FRAME.tileX + FRAME.tileSize ? x - FRAME.tileX - FRAME.tileSize : 0;
        const fall = Math.min(1, outside / FALL_OFF);
        moving[i].y = y + fall * (2.6 * Math.sin(x * 0.06 - t * 2.2) + 1.3 * Math.sin(x * 0.13 + t * 3.1));
      }

      const path = toPath(moving);
      dim.current?.setAttribute("d", path);
      lit.current?.setAttribute("d", path);
      frame = requestAnimationFrame(draw);
    };

    frame = requestAnimationFrame(draw);
    return () => cancelAnimationFrame(frame);
  }, [rest]);

  const centreX = FRAME.tileX + FRAME.tileSize / 2;
  const centreY = FRAME.tileY + FRAME.tileSize / 2;

  return (
    <svg className="about-hum" viewBox={`0 0 ${FRAME.width} ${HEIGHT}`} aria-hidden="true" focusable="false">
      <defs>
        <linearGradient
          id={`${id}-tile`}
          gradientUnits="userSpaceOnUse"
          x1={FRAME.tileX}
          y1={FRAME.tileY}
          x2={FRAME.tileX + FRAME.tileSize}
          y2={FRAME.tileY + FRAME.tileSize}
        >
          <stop offset="0" stopColor="#56A0FF" />
          <stop offset=".55" stopColor="#7A78FF" />
          <stop offset="1" stopColor="#AE62F2" />
        </linearGradient>
        <linearGradient id={`${id}-dim`} gradientUnits="userSpaceOnUse" x1="0" y1="0" x2={FRAME.width} y2="0">
          <stop offset="0" stopColor="#8FB3FF" stopOpacity="0" />
          <stop offset=".14" stopColor="#8FB3FF" />
          <stop offset=".86" stopColor="#B59BFF" />
          <stop offset="1" stopColor="#B59BFF" stopOpacity="0" />
        </linearGradient>
        <linearGradient id={`${id}-lit`} gradientUnits="userSpaceOnUse" x1={FRAME.tileX} y1="0" x2={FRAME.tileX + FRAME.tileSize} y2="0">
          <stop offset="0" stopColor="#fff" stopOpacity=".3" />
          <stop offset=".24" stopColor="#fff" />
          <stop offset=".76" stopColor="#fff" />
          <stop offset="1" stopColor="#fff" stopOpacity=".3" />
        </linearGradient>
        <radialGradient id={`${id}-glow`}>
          <stop offset="0" stopColor="#7A78FF" stopOpacity=".5" />
          <stop offset="1" stopColor="#7A78FF" stopOpacity="0" />
        </radialGradient>
        <clipPath id={`${id}-clip`}>
          <rect x={FRAME.tileX} y={FRAME.tileY} width={FRAME.tileSize} height={FRAME.tileSize} rx={cornerRadius} />
        </clipPath>
      </defs>
      <circle cx={centreX} cy={centreY} r={FRAME.tileSize * 1.35} fill={`url(#${id}-glow)`} />
      <path
        ref={dim}
        d={restPath}
        fill="none"
        stroke={`url(#${id}-dim)`}
        strokeWidth={strokeWidth}
        strokeLinecap="round"
        strokeLinejoin="round"
        opacity=".38"
      />
      <rect
        x={FRAME.tileX}
        y={FRAME.tileY}
        width={FRAME.tileSize}
        height={FRAME.tileSize}
        rx={cornerRadius}
        fill={`url(#${id}-tile)`}
      />
      <g clipPath={`url(#${id}-clip)`}>
        <path
          ref={lit}
          d={restPath}
          fill="none"
          stroke={`url(#${id}-lit)`}
          strokeWidth={strokeWidth}
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </g>
    </svg>
  );
}
