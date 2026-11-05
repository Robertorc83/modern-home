import type { ReactNode } from "react";
import { CalendarClock } from "lucide-react";

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <main className="flex flex-1 items-center justify-center px-4 py-12">
      <div className="w-full max-w-md space-y-6">
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex size-10 items-center justify-center rounded-xl bg-primary text-primary-foreground">
            <CalendarClock className="size-5" />
          </div>
          <h1 className="text-xl font-semibold tracking-tight">Home Showing Scheduler</h1>
          <p className="text-sm text-muted-foreground">
            Book home showings across brokerages, time zones and calendars.
          </p>
        </div>
        {children}
      </div>
    </main>
  );
}
