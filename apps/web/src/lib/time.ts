export const TIME_ZONES = [
  "America/Denver",
  "America/Chicago",
  "America/Toronto",
  "America/New_York",
  "America/Los_Angeles",
] as const;

export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
}

/** The fixed zone list plus any extra zone (e.g. the user's), without duplicates. */
export function zoneOptions(...extra: (string | null | undefined)[]): string[] {
  const zones: string[] = [...TIME_ZONES];
  for (const zone of extra) if (zone && !zones.includes(zone)) zones.push(zone);
  return zones;
}

export function formatInZone(
  instant: string | Date,
  timeZone: string,
  options: Intl.DateTimeFormatOptions = {
    weekday: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZoneName: "short",
  },
): string {
  const date = typeof instant === "string" ? new Date(instant) : instant;
  if (Number.isNaN(date.getTime())) return "";
  return new Intl.DateTimeFormat("en-US", { ...options, timeZone }).format(date);
}

export function formatTimeInZone(instant: string, timeZone: string): string {
  return formatInZone(instant, timeZone, { hour: "numeric", minute: "2-digit", timeZoneName: "short" });
}

export function formatRelative(instant: string | null | undefined): string {
  if (!instant) return "Never";
  const date = new Date(instant);
  const seconds = Math.round((date.getTime() - Date.now()) / 1000);
  const rtf = new Intl.RelativeTimeFormat("en", { numeric: "auto" });
  const abs = Math.abs(seconds);
  if (abs < 60) return rtf.format(seconds, "second");
  if (abs < 3600) return rtf.format(Math.round(seconds / 60), "minute");
  if (abs < 86400) return rtf.format(Math.round(seconds / 3600), "hour");
  return rtf.format(Math.round(seconds / 86400), "day");
}

/** "YYYY-MM-DD" for "today" as seen in the given zone. */
export function todayInZone(timeZone: string): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}

export function addDays(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split("-").map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + days));
  return date.toISOString().slice(0, 10);
}

/** 0 = Sunday … 6 = Saturday for a "YYYY-MM-DD" calendar date. */
export function dayOfWeek(isoDate: string): number {
  const [y, m, d] = isoDate.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay();
}

/** The coming Saturday in the zone (today if it is Saturday). */
export function nextSaturday(timeZone: string): string {
  const today = todayInZone(timeZone);
  return addDays(today, (6 - dayOfWeek(today) + 7) % 7);
}

/** "Sat, Nov 7" for a calendar date or a local "YYYY-MM-DDTHH:mm" string. */
export function formatLocalDate(local: string, options?: Intl.DateTimeFormatOptions): string {
  const [y, m, d] = local.slice(0, 10).split("-").map(Number);
  if (!y || !m || !d) return local;
  return new Intl.DateTimeFormat("en-US", {
    timeZone: "UTC",
    weekday: "short",
    month: "short",
    day: "numeric",
    ...options,
  }).format(new Date(Date.UTC(y, m - 1, d)));
}

/** "9:00 AM" for "HH:mm" or a local "YYYY-MM-DDTHH:mm" string. */
export function formatLocalTime(local: string): string {
  const hhmm = local.includes("T") ? local.split("T")[1] : local;
  const [h, min] = hhmm.split(":").map(Number);
  if (Number.isNaN(h) || Number.isNaN(min)) return local;
  const suffix = h >= 12 && h < 24 ? "PM" : "AM";
  const hour12 = h % 12 === 0 ? 12 : h % 12;
  return `${hour12}:${String(min).padStart(2, "0")} ${suffix}`;
}

/** "Sat, Nov 7 · 9:00 AM – 12:00 PM" */
export function formatLocalRange(start: string, end: string): string {
  const sameDay = start.slice(0, 10) === end.slice(0, 10);
  return sameDay
    ? `${formatLocalDate(start)} · ${formatLocalTime(start)} – ${formatLocalTime(end)}`
    : `${formatLocalDate(start)} ${formatLocalTime(start)} – ${formatLocalDate(end)} ${formatLocalTime(end)}`;
}

/** Times of day in 15-minute steps: "00:00" … "23:45", plus "24:00" when includeMidnight. */
export function quarterHours(includeMidnight = false): string[] {
  const times: string[] = [];
  for (let minutes = 0; minutes < 24 * 60; minutes += 15) {
    times.push(
      `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`,
    );
  }
  if (includeMidnight) times.push("24:00");
  return times;
}

export function toMinutes(hhmm: string): number {
  const [h, m] = hhmm.split(":").map(Number);
  return h * 60 + m;
}

export function zoneShortName(timeZone: string): string {
  return timeZone.split("/").pop()?.replace(/_/g, " ") ?? timeZone;
}
