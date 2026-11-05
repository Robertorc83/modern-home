"use client";

import { useCallback, useEffect, useRef, useState } from "react";

export function useDebounced<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}

/** A stable function that runs the latest `fn` once, `delayMs` after the last call (trailing edge). */
export function useDebouncedCallback(fn: () => void, delayMs = 150): () => void {
  const latest = useRef(fn);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  useEffect(() => {
    latest.current = fn;
  });
  useEffect(() => () => clearTimeout(timer.current), []);
  return useCallback(() => {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => latest.current(), delayMs);
  }, [delayMs]);
}
