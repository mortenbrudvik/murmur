import { useEffect, useRef, useState, type MouseEvent } from "react";
import "./AboutDialog.css";
import HumHeader from "./HumHeader";
import { buildDetails, describeDictated, describeRunningOn } from "./aboutText";
import { useAbout } from "./useAbout";
import { openLink } from "../settings/appActions";
import type { EngineStatus, LinkTarget } from "../../types/domain";

const TAGLINE = "Hold a key, talk, and the words land where you were typing. Nothing you say leaves this PC.";
const COPY_LABEL = "Copy details";
/** How long "Copied" stays on the button. */
const COPIED_MS = 1600;

const LINKS: { target: LinkTarget; label: string }[] = [
  { target: "repository", label: "Source on GitHub" },
  { target: "privacyPolicy", label: "Privacy policy" },
  { target: "license", label: "MIT License" },
];

interface Props {
  open: boolean;
  /** The speech engine's status; null until it is known. The model and runtime rows follow it while the dialog is open. */
  engineStatus: EngineStatus | null;
  onClose: () => void;
}

/** What is running, and where the user's words go. Opened from the sidebar and from the tray menu. */
export default function AboutDialog({ open, engineStatus, onClose }: Props) {
  const { info, failed } = useAbout(open);
  const [copyLabel, setCopyLabel] = useState(COPY_LABEL);
  const [linkError, setLinkError] = useState(false);
  const dialog = useRef<HTMLDivElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  // Focus moves into the dialog when it opens and goes back to whatever had it (the sidebar's About) when it closes.
  useEffect(() => {
    if (!open) return;

    const before = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeButton.current?.focus();
    return () => {
      setCopyLabel(COPY_LABEL);
      setLinkError(false);
      if (before?.isConnected) before.focus();
    };
  }, [open]);

  useEffect(() => {
    if (copyLabel === COPY_LABEL) return;

    const timer = setTimeout(() => setCopyLabel(COPY_LABEL), COPIED_MS);
    return () => clearTimeout(timer);
  }, [copyLabel]);

  // The keys are heard on the document, not on the dialog: a click on the dialog's text (the tagline, the path) leaves
  // the focus on the page's body, and a handler on the dialog would then hear nothing. Modal: Esc closes from
  // anywhere, and Tab goes round the dialog's own controls and never out to the page behind it.
  useEffect(() => {
    if (!open) return;

    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        e.stopPropagation();
        onClose();
        return;
      }

      if (e.key !== "Tab") return;

      const controls = [...(dialog.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)") ?? [])];
      if (controls.length === 0) return;

      const first = controls[0];
      const last = controls[controls.length - 1];
      const active = document.activeElement;
      const outside = !dialog.current?.contains(active);
      if (e.shiftKey && (outside || active === first)) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && (outside || active === last)) {
        e.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  function onScrimMouseDown(e: MouseEvent<HTMLDivElement>) {
    if (e.target === e.currentTarget) onClose();
  }

  async function follow(target: LinkTarget) {
    setLinkError(false);
    try {
      await openLink(target);
    } catch {
      setLinkError(true);
    }
  }

  async function copyDetails() {
    if (!info) return;

    try {
      await navigator.clipboard.writeText(buildDetails(info, engineStatus));
      setCopyLabel("Copied");
    } catch {
      setCopyLabel("Could not copy");
    }
  }

  const pending = failed ? "Unavailable" : "…";
  const dictated = info ? describeDictated(info) : null;

  return (
    <div className="about-scrim" onMouseDown={onScrimMouseDown}>
      <div className="about" role="dialog" aria-modal="true" aria-labelledby="about-name" ref={dialog}>
        <HumHeader />
        <div className="about-main">
          <div className="about-id">
            <h2 id="about-name">Murmur</h2>
            <p className="about-version">
              {info ? `Version ${info.version} · ${info.channel}` : failed ? "Version unavailable" : " "}
            </p>
          </div>
          <p className="about-tagline">{TAGLINE}</p>
          <dl className="about-facts">
            <dt>Speech model</dt>
            <dd>{engineStatus?.modelName || "Not chosen yet"}</dd>
            <dt>Running on</dt>
            <dd>{describeRunningOn(engineStatus)}</dd>
            <dt>Dictated so far</dt>
            <dd>
              {dictated ? dictated.main : pending}
              {dictated?.detail && <small>{dictated.detail}</small>}
            </dd>
            <dt>Your data</dt>
            <dd>
              <span className="about-path">{info ? info.dataFolder : pending}</span>
              <button type="button" className="about-link" onClick={() => void follow("dataFolder")}>
                Open folder
              </button>
            </dd>
          </dl>
          <div className="about-links">
            {LINKS.map(({ target, label }) => (
              <button type="button" className="about-link" key={target} onClick={() => void follow(target)}>
                {label}
              </button>
            ))}
          </div>
          {linkError && (
            <p className="page-error about-error" role="alert">
              Could not open the link.
            </p>
          )}
        </div>
        <div className="about-foot">
          <span className="about-copyright">
            © {__BUILD_YEAR__}{" "}
            <button type="button" className="about-link" onClick={() => void follow("homePage")}>
              Morten Brudvik
            </button>
          </span>
          <div className="about-actions">
            <button type="button" className="about-button" disabled={!info} onClick={() => void copyDetails()}>
              {copyLabel}
            </button>
            <button type="button" className="about-button about-button-primary" ref={closeButton} onClick={onClose}>
              Close
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
