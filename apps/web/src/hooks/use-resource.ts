"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api";

export interface Resource<T> {
  data: T | undefined;
  error: ApiError | undefined;
  loading: boolean;
  /** Refetch; keeps showing the current data while loading (no flash). */
  reload: () => Promise<T | undefined>;
  setData: (data: T | undefined) => void;
}

/**
 * Minimal data hook: runs `load` on mount and whenever `deps` change; `reload` refetches.
 * Stale responses from earlier calls are discarded.
 */
export function useResource<T>(load: () => Promise<T>, deps: unknown[] = []): Resource<T> {
  const [data, setData] = useState<T>();
  const [error, setError] = useState<ApiError>();
  const [loading, setLoading] = useState(true);
  const generation = useRef(0);
  const loadRef = useRef(load);

  useEffect(() => {
    loadRef.current = load;
  });

  const reload = useCallback(async (): Promise<T | undefined> => {
    const current = ++generation.current;
    setLoading(true);
    try {
      const result = await loadRef.current();
      if (current !== generation.current) return undefined;
      setData(result);
      setError(undefined);
      return result;
    } catch (err) {
      if (current !== generation.current) return undefined;
      setError(err instanceof ApiError ? err : new ApiError(0, { detail: String(err) }));
      return undefined;
    } finally {
      if (current === generation.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    // Fetching on mount / when inputs change is exactly what this hook is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  return { data, error, loading, reload, setData };
}
