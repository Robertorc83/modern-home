"use client";

import { ErrorState } from "@/components/states";

export default function AppError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  // Server errors arrive redacted in production (only a digest), so say what usually causes them.
  const message = error.digest
    ? "This page could not be loaded. Check that the API is running, then try again."
    : error.message || "Something went wrong while loading this page.";
  return (
    <div className="mx-auto max-w-xl py-16">
      <ErrorState message={message} onRetry={reset} />
    </div>
  );
}
