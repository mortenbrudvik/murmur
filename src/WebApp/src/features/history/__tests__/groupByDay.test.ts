import { describe, it, expect, vi } from "vitest";
import { groupByDay } from "../groupByDay";
import type { HistoryEntry } from "../../../types/domain";

function entry(id: string, when: Date, text = id): HistoryEntry {
  return { id, timestamp: when.toISOString(), text, targetApp: "Claude", pasted: true };
}

// Local-time constructors keep these tests independent of the machine's time zone.
const now = new Date(2026, 9, 29, 12, 0);

describe("groupByDay", () => {
  it("labels today, yesterday and older days", () => {
    const groups = groupByDay(
      [
        entry("a", new Date(2026, 9, 29, 10)),
        entry("b", new Date(2026, 9, 28, 23)),
        entry("c", new Date(2026, 9, 22, 9)),
      ],
      now,
    );

    expect(groups.map((g) => g.label)).toEqual(["Today", "Yesterday", "Thu 22 Oct"]);
  });

  it("keeps newest-first order inside a group", () => {
    const groups = groupByDay(
      [
        entry("late", new Date(2026, 9, 29, 11)),
        entry("mid", new Date(2026, 9, 29, 9)),
        entry("early", new Date(2026, 9, 29, 1)),
      ],
      now,
    );

    expect(groups).toHaveLength(1);
    expect(groups[0].entries.map((e) => e.id)).toEqual(["late", "mid", "early"]);
  });

  it("puts the first minute of today and the last minute of yesterday in different groups", () => {
    const groups = groupByDay(
      [entry("a", new Date(2026, 9, 29, 0, 0)), entry("b", new Date(2026, 9, 28, 23, 59))],
      now,
    );

    expect(groups.map((g) => g.label)).toEqual(["Today", "Yesterday"]);
  });

  it("labels yesterday correctly across a month boundary", () => {
    const groups = groupByDay([entry("a", new Date(2026, 9, 31, 8))], new Date(2026, 10, 1, 9));

    expect(groups.map((g) => g.label)).toEqual(["Yesterday"]);
  });

  it("writes the older-day label without a comma", () => {
    const [group] = groupByDay([entry("a", new Date(2026, 0, 5, 8))], now);

    expect(group.label).toBe("Mon 5 Jan");
  });

  it("removes the comma some runtimes put after the weekday", () => {
    const spy = vi.spyOn(Date.prototype, "toLocaleDateString").mockReturnValue("Thu, 22 Oct");
    try {
      const [group] = groupByDay([entry("a", new Date(2026, 9, 22, 9))], now);

      expect(group.label).toBe("Thu 22 Oct");
    } finally {
      spy.mockRestore();
    }
  });

  it("returns no groups for no entries", () => {
    expect(groupByDay([], now)).toEqual([]);
  });
});
