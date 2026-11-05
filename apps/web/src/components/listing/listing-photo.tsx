"use client";

import { useState } from "react";
import { Home } from "lucide-react";
import { cn } from "@/lib/utils";

/** Ask the image CDN for a right-sized rendition when we know how (Unsplash supports w/q params). */
export function sizedPhotoUrl(url: string, width: number): string {
  try {
    const parsed = new URL(url);
    if (parsed.hostname === "images.unsplash.com") {
      parsed.searchParams.set("w", String(width));
      parsed.searchParams.set("q", "75");
      parsed.searchParams.set("auto", "format");
      parsed.searchParams.set("fit", "crop");
      return parsed.toString();
    }
  } catch {
    // Not an absolute URL; use it as-is.
  }
  return url;
}

/**
 * Cover photo with a calm fallback (gradient, house icon, property type) when there is no photo
 * or it fails to load. Plain <img> so it works for any remote host in the standalone build.
 */
export function ListingPhoto({
  src,
  alt,
  label,
  width = 800,
  className,
  rounded = true,
  eager = false,
}: {
  src: string | null | undefined;
  alt: string;
  /** Shown on the fallback, e.g. the property type. */
  label?: string;
  /** Rendition width to request from the CDN. */
  width?: number;
  className?: string;
  rounded?: boolean;
  eager?: boolean;
}) {
  const [failed, setFailed] = useState<string | null>(null);
  const showImage = Boolean(src) && failed !== src;

  return (
    <div
      className={cn(
        "relative aspect-[4/3] w-full overflow-hidden bg-muted",
        rounded && "rounded-lg",
        className,
      )}
    >
      {showImage ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={sizedPhotoUrl(src!, width)}
          alt={alt}
          loading={eager ? "eager" : "lazy"}
          decoding="async"
          onError={() => setFailed(src!)}
          className="absolute inset-0 size-full object-cover"
        />
      ) : (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-1.5 bg-gradient-to-br from-slate-100 via-slate-50 to-stone-200 text-slate-400">
          <Home className="size-8" strokeWidth={1.5} />
          {label ? <span className="text-xs font-medium tracking-wide uppercase">{label}</span> : null}
        </div>
      )}
    </div>
  );
}
