"use client";

import { useEffect, useRef, useState } from "react";
import type { ShowingStreamEvent } from "@/lib/types";

export type StreamState = "connecting" | "live" | "offline";

/**
 * Subscribes to the API's change stream of showings (SSE, via the BFF proxy).
 * EventSource reconnects on its own and resends the last event id, so the API can resume.
 */
export function useShowingStream(onEvent: (event: ShowingStreamEvent) => void): StreamState {
  const [state, setState] = useState<StreamState>("connecting");
  const handler = useRef(onEvent);

  useEffect(() => {
    handler.current = onEvent;
  });

  useEffect(() => {
    const source = new EventSource("/api/proxy/showings/stream");
    source.onopen = () => setState("live");
    source.onerror = () => setState(source.readyState === EventSource.CLOSED ? "offline" : "connecting");
    source.addEventListener("showing", (message) => {
      try {
        handler.current(JSON.parse((message as MessageEvent<string>).data) as ShowingStreamEvent);
      } catch {
        // Ignore malformed events; the next refetch will reconcile.
      }
    });
    return () => source.close();
  }, []);

  return state;
}
