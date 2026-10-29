import type { Page } from "../types/domain";

const ITEMS: { page: Page; label: string }[] = [
  { page: "history", label: "History" },
  { page: "vocabulary", label: "Vocabulary" },
  { page: "settings", label: "Settings" },
];

interface Props {
  page: Page;
  onNavigate: (page: Page) => void;
}

export default function Sidebar({ page, onNavigate }: Props) {
  return (
    <nav className="sidebar" aria-label="Pages">
      {ITEMS.map((item) => {
        const active = item.page === page;
        return (
          <button
            key={item.page}
            type="button"
            className={active ? "sidebar-item active" : "sidebar-item"}
            aria-current={active ? "page" : undefined}
            onClick={() => onNavigate(item.page)}
          >
            {item.label}
          </button>
        );
      })}
    </nav>
  );
}
