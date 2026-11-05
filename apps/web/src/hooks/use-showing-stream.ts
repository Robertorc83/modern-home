"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { ShowingStreamEvent } from "@/lib/types";

export type StreamState = "connecting" | "live" | "offline";

const STREAM_URL = "/api/proxy/showings/stream";
const BACKOFF_MIN_MS = 1000;
const BACKOFF_MAX_MS = 30_000;

/**
 * Subscribes to the API's change stream of showings (SSE, via the BFF proxy).
 *
 * EventSource reconnects on its own only while the connection stays "SSE-shaped". When the proxy
 * answers with something else (a 502 while the API restarts, a 401 after the session ends) the
 * browser closes it for good, so this hook reopens it with exponential backoff. `onReconnect`
 * runs after every recovered connection so the caller can refetch what it missed meanwhile.
 */
export function useShowingStream(
  onEvent: (event: ShowingStreamEvent) => void,
  onReconnect?: () => void,
): StreamState {
  const [state, setState] = useState<StreamState>("connecting");
  const handlers = useRef({ onEvent, onReconnect });

  useEffect(() => {
    handlers.current = { onEvent, onReconnect };
  });

  useEffect(() => {
    let source: EventSource | null = null;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let backoff = BACKOFF_MIN_MS;
    let lost = false; // a connection was live (or failed) before: the next open is a recovery
    let disposed = false;

    function connect() {
      source = new EventSource(STREAM_URL);
      source.onopen = () => {
        backoff = BACKOFF_MIN_MS;
        setState("live");
        if (lost) handlers.current.onReconnect?.();
        lost = false;
      };
      source.onerror = () => {
        lost = true;
        if (source?.readyState !== EventSource.CLOSED) {
          // The browser retries by itself (it honours the server's `retry:` hint).
          setState("connecting");
          return;
        }
        // Closed for good: find out why. A 401 makes the API client redirect to /login itself.
        setState("offline");
        void api.me().catch(() => undefined);
        timer = setTimeout(() => {
          if (!disposed) connect();
        }, backoff);
        backoff = Math.min(backoff * 2, BACKOFF_MAX_MS);
      };
      source.addEventListener("showing", (message) => {
        try {
          handlers.current.onEvent(JSON.parse((message as MessageEvent<string>).data) as ShowingStreamEvent);
        } catch {
          // Ignore malformed events; the next refetch will reconcile.
        }
      });
    }

    connect();
    return () => {
      disposed = true;
      clearTimeout(timer);
      source?.close();
    };
  }, []);

  return state;
}
