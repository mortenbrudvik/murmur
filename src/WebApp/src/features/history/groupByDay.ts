import type { HistoryEntry } from "../../types/domain";

export interface DayGroup {
  label: string;
  entries: HistoryEntry[];
}

function dayKey(date: Date): string {
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
}

function dateLabel(date: Date): string {
  // Some runtimes write "Thu, 22 Oct"; the label is always "Thu 22 Oct".
  return date
    .toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short" })
    .replace(/,/g, "");
}

/**
 * Groups entries by local calendar day as "Today", "Yesterday" or "Thu 22 Oct".
 * Entries keep their order, and groups come in the order their first entry appears.
 */
export function groupByDay(entries: HistoryEntry[], now: Date): DayGroup[] {
  const todayKey = dayKey(now);
  const yesterdayKey = dayKey(new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1));

  const groups = new Map<string, DayGroup>();
  for (const entry of entries) {
    const date = new Date(entry.timestamp);
    const key = dayKey(date);

    let group = groups.get(key);
    if (!group) {
      const label = key === todayKey ? "Today" : key === yesterdayKey ? "Yesterday" : dateLabel(date);
      group = { label, entries: [] };
      groups.set(key, group);
    }
    group.entries.push(entry);
  }

  return [...groups.values()];
}
