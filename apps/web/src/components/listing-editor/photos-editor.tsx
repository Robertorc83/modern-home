"use client";

import { useState } from "react";
import { ChevronLeft, ChevronRight, Plus, X } from "lucide-react";
import { ListingPhoto } from "@/components/listing/listing-photo";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

/** The API only accepts https photo URLs. */
function isHttpsUrl(value: string): boolean {
  try {
    return new URL(value).protocol === "https:";
  } catch {
    return false;
  }
}

/** Ordered photo URLs; the first one is the cover shown on cards. */
export function PhotosEditor({
  photos,
  onChange,
  label,
}: {
  photos: string[];
  onChange: (photos: string[]) => void;
  label: string;
}) {
  const [draft, setDraft] = useState("");
  const [error, setError] = useState<string | null>(null);

  function add(event?: { preventDefault: () => void }) {
    event?.preventDefault();
    const url = draft.trim();
    if (!isHttpsUrl(url)) {
      setError(
        url.startsWith("http://")
          ? "Photo URLs must use https:// (http:// is not accepted)."
          : "Paste a full image URL starting with https://",
      );
      return;
    }
    if (photos.includes(url)) {
      setError("That photo is already added.");
      return;
    }
    onChange([...photos, url]);
    setDraft("");
    setError(null);
  }

  function move(index: number, delta: number) {
    const next = [...photos];
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item);
    onChange(next);
  }

  return (
    <div className="space-y-4">
      <div className="flex gap-2">
        <Input
          type="url"
          aria-label="Photo URL"
          placeholder="https://images.unsplash.com/…"
          value={draft}
          onChange={(e) => {
            setDraft(e.target.value);
            setError(null);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") add(e);
          }}
        />
        <Button type="button" variant="outline" onClick={() => add()} disabled={!draft.trim()}>
          <Plus />
          Add photo
        </Button>
      </div>
      {error ? <p className="text-xs text-destructive">{error}</p> : null}

      {photos.length === 0 ? (
        <div className="max-w-60">
          <ListingPhoto src={null} alt="" label={`${label} · no photos`} />
          <p className="mt-2 text-xs text-muted-foreground">
            Buyers see this placeholder until you add a photo.
          </p>
        </div>
      ) : (
        <ol className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          {photos.map((url, index) => (
            <li key={url} className="space-y-1.5">
              <div className="relative">
                <ListingPhoto src={url} alt={`Photo ${index + 1}`} label={label} width={480} />
                {index === 0 ? (
                  <span className="absolute top-2 left-2 rounded-md bg-black/70 px-1.5 py-0.5 text-xs font-medium text-white">
                    Cover
                  </span>
                ) : null}
                <Button
                  type="button"
                  size="icon-xs"
                  variant="secondary"
                  className="absolute top-2 right-2 shadow-sm"
                  aria-label={`Remove photo ${index + 1}`}
                  onClick={() => onChange(photos.filter((_, i) => i !== index))}
                >
                  <X />
                </Button>
              </div>
              <div className="flex items-center justify-between">
                <Button
                  type="button"
                  size="icon-xs"
                  variant="ghost"
                  aria-label="Move earlier"
                  disabled={index === 0}
                  onClick={() => move(index, -1)}
                >
                  <ChevronLeft />
                </Button>
                <span className="text-xs text-muted-foreground">
                  {index === 0 ? "Cover" : `Photo ${index + 1}`}
                </span>
                <Button
                  type="button"
                  size="icon-xs"
                  variant="ghost"
                  aria-label="Move later"
                  disabled={index === photos.length - 1}
                  onClick={() => move(index, 1)}
                >
                  <ChevronRight />
                </Button>
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
