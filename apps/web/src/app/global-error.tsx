"use client";

export default function GlobalError({ error, reset }: { error: Error; reset: () => void }) {
  return (
    <html lang="en">
      <body style={{ fontFamily: "system-ui, sans-serif", padding: 48, textAlign: "center" }}>
        <h1 style={{ fontSize: 20, fontWeight: 600 }}>Something went wrong</h1>
        <p style={{ color: "#666", marginTop: 8 }}>{error.message}</p>
        <button onClick={reset} style={{ marginTop: 16, padding: "6px 12px" }}>
          Try again
        </button>
      </body>
    </html>
  );
}
