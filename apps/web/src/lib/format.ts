import type { DayOfWeek, Listing, ShowingRules } from "./types";
import { formatLocalTime } from "./time";

const usd = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });
const cad = new Intl.NumberFormat("en-CA", { style: "currency", currency: "CAD", maximumFractionDigits: 0 });

/** Prices are stored as plain numbers; Canadian listings (Toronto zone) display in CAD. */
export function formatPrice(price: number, timeZone?: string): string {
  return timeZone === "America/Toronto" ? cad.format(price) : usd.format(price);
}

export function formatBedsBaths(listing: Pick<Listing, "beds" | "baths">): string {
  return `${listing.beds} bd · ${listing.baths} ba`;
}

export function formatDistance(km: number | null | undefined): string | null {
  if (km === null || km === undefined) return null;
  return km < 10 ? `${km.toFixed(1)} km away` : `${Math.round(km)} km away`;
}

const SHORT_DAY: Record<string, string> = {
  Monday: "Mon",
  Tuesday: "Tue",
  Wednesday: "Wed",
  Thursday: "Thu",
  Friday: "Fri",
  Saturday: "Sat",
  Sunday: "Sun",
};

export function shortDay(day: string): string {
  return SHORT_DAY[day] ?? day;
}

/** "Sat 9:00 AM–12:00 PM, Sun 1:00 PM–4:00 PM · 30-min slots · auto-confirm" */
export function summarizeRules(rules: ShowingRules | null | undefined): string {
  if (!rules || rules.weekly.length === 0) return "No showing windows";
  const windows = rules.weekly
    .slice(0, 3)
    .map((w) => `${shortDay(w.dayOfWeek)} ${formatLocalTime(w.start)}–${formatLocalTime(w.end)}`);
  if (rules.weekly.length > 3) windows.push(`+${rules.weekly.length - 3} more`);
  const parts = [windows.join(", "), `${rules.slotMinutes}-min slots`];
  if (rules.autoConfirm) parts.push("auto-confirm");
  return parts.join(" · ");
}

/** The one attribute worth showing on a card: HOA for condos/townhouses, lot size for houses. */
export function keyAttribute(listing: Listing): string | null {
  const a = listing.attributes ?? {};
  if (typeof a.hoaMonthly === "number") return `HOA ${usd.format(a.hoaMonthly)}/mo`;
  if (typeof a.lotSizeSqft === "number") return `${a.lotSizeSqft.toLocaleString()} sq ft lot`;
  return null;
}

const ATTRIBUTE_LABELS: Record<string, string> = {
  hoaMonthly: "HOA / month",
  lotSizeSqft: "Lot size",
  floor: "Floor",
  yearBuilt: "Year built",
  parking: "Parking",
  rooftopDeck: "Rooftop deck",
  sqft: "Interior size",
};

function humanizeKey(key: string): string {
  const spaced = key.replace(/([a-z0-9])([A-Z])/g, "$1 $2").toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function formatAttributeValue(key: string, value: unknown): string {
  if (typeof value === "boolean") return value ? "Yes" : "No";
  if (typeof value === "number") {
    if (key === "hoaMonthly") return usd.format(value);
    if (key === "lotSizeSqft" || key === "sqft") return `${value.toLocaleString()} sq ft`;
    if (key === "yearBuilt") return String(value);
    return value.toLocaleString();
  }
  if (Array.isArray(value)) return value.join(", ");
  if (value && typeof value === "object") return JSON.stringify(value);
  return String(value);
}

/** The flexible `attributes` bag as label/value rows, known keys first. */
export function attributeRows(listing: Listing): { label: string; value: string }[] {
  const entries = Object.entries(listing.attributes ?? {}).filter(
    ([, v]) => v !== null && v !== undefined && v !== "",
  );
  const known = Object.keys(ATTRIBUTE_LABELS);
  entries.sort(([a], [b]) => {
    const ia = known.indexOf(a);
    const ib = known.indexOf(b);
    return (ia === -1 ? 99 : ia) - (ib === -1 ? 99 : ib) || a.localeCompare(b);
  });
  return entries.map(([key, value]) => ({
    label: ATTRIBUTE_LABELS[key] ?? humanizeKey(key),
    value: formatAttributeValue(key, value),
  }));
}

/** "1–4 PM" / "9 AM–12 PM": compact hours for prose. */
function compactRange(start: string, end: string): string {
  const fmt = (hhmm: string) => {
    const [h, m] = hhmm.split(":").map(Number);
    const h12 = h % 12 === 0 ? 12 : h % 12;
    return { text: m === 0 ? `${h12}` : `${h12}:${String(m).padStart(2, "0")}`, pm: h >= 12 };
  };
  const a = fmt(start);
  const b = fmt(end);
  const mer = (pm: boolean) => (pm ? "PM" : "AM");
  return a.pm === b.pm ? `${a.text}–${b.text} ${mer(b.pm)}` : `${a.text} ${mer(a.pm)}–${b.text} ${mer(b.pm)}`;
}

const PLURAL_DAY: Record<DayOfWeek, string> = {
  Monday: "Mondays",
  Tuesday: "Tuesdays",
  Wednesday: "Wednesdays",
  Thursday: "Thursdays",
  Friday: "Fridays",
  Saturday: "Saturdays",
  Sunday: "Sundays",
};

/** "30 min showings · Saturdays 9 AM–12 PM, Sundays 1–4 PM · confirmed by the agent" */
export function rulesSentence(rules: ShowingRules | null | undefined): string {
  if (!rules || rules.weekly.length === 0) return "No regular showing hours";
  const byDay = new Map<DayOfWeek, string[]>();
  for (const w of [...rules.weekly].sort((x, y) => x.start.localeCompare(y.start))) {
    byDay.set(w.dayOfWeek, [...(byDay.get(w.dayOfWeek) ?? []), compactRange(w.start, w.end)]);
  }
  const order: DayOfWeek[] = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
  const days = order.filter((d) => byDay.has(d)).map((d) => `${PLURAL_DAY[d]} ${byDay.get(d)!.join(" & ")}`);
  return [
    `${rules.slotMinutes} min showings`,
    days.join(", "),
    rules.autoConfirm ? "instantly confirmed" : "confirmed by the agent",
  ].join(" · ");
}
