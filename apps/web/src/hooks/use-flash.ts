"use client";

import { useCallback, useEffect, useRef, useState } from "react";

/** Tracks ids that should be briefly highlighted (e.g. rows changed by a live event). */
export function useFlash(durationMs = 3000) {
  const [ids, setIds] = useState<Set<string>>(() => new Set());
  const timers = useRef(new Map<string, ReturnType<typeof setTimeout>>());

  const flash = useCallback(
    (id: string) => {
      setIds((prev) => new Set(prev).add(id));
      clearTimeout(timers.current.get(id));
      timers.current.set(
        id,
        setTimeout(() => {
          setIds((prev) => {
            const next = new Set(prev);
            next.delete(id);
            return next;
          });
          timers.current.delete(id);
        }, durationMs),
      );
    },
    [durationMs],
  );

  useEffect(() => {
    const pending = timers.current;
    return () => pending.forEach((t) => clearTimeout(t));
  }, []);

  return { isFlashing: (id: string) => ids.has(id), flash };
}
