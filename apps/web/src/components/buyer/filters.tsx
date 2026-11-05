"use client";

import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import type { PropertyType } from "@/lib/types";

export interface SearchFilters {
  near: string;
  radiusKm: string;
  city: string;
  maxPrice: string;
  minBeds: string;
  propertyType: PropertyType | "any";
}

export const DEFAULT_FILTERS: SearchFilters = {
  near: "",
  radiusKm: "25",
  city: "",
  maxPrice: "",
  minBeds: "any",
  propertyType: "any",
};

const NEAR_SUGGESTIONS = ["Denver", "LoDo", "Cherry Creek", "Austin", "Toronto"];

export function Filters({
  value,
  onChange,
  nearError,
}: {
  value: SearchFilters;
  onChange: (value: SearchFilters) => void;
  /** The matcher's objection to `near` (an unknown place), shown under the field. */
  nearError?: string | null;
}) {
  const set = <K extends keyof SearchFilters>(key: K, v: SearchFilters[K]) =>
    onChange({ ...value, [key]: v });

  return (
    <div className="grid grid-cols-2 gap-3">
      <div className="col-span-2 space-y-1.5">
        <Label htmlFor="near">Near</Label>
        <Input
          id="near"
          list="near-suggestions"
          placeholder="Denver, LoDo, Toronto…"
          value={value.near}
          aria-invalid={nearError ? true : undefined}
          aria-describedby={nearError ? "near-error" : undefined}
          onChange={(e) => set("near", e.target.value)}
        />
        <datalist id="near-suggestions">
          {NEAR_SUGGESTIONS.map((n) => (
            <option key={n} value={n} />
          ))}
        </datalist>
        {nearError ? (
          <p id="near-error" role="alert" className="text-xs text-destructive">
            {nearError}
          </p>
        ) : null}
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="radius">Radius</Label>
        <Select
          value={value.radiusKm}
          onValueChange={(v) => set("radiusKm", v)}
          disabled={!value.near.trim()}
        >
          <SelectTrigger id="radius" className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {["2", "5", "10", "25", "50", "100"].map((r) => (
              <SelectItem key={r} value={r}>
                {r} km
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="city">City</Label>
        <Input
          id="city"
          placeholder="Any city"
          value={value.city}
          onChange={(e) => set("city", e.target.value)}
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="maxPrice">Max price</Label>
        <Input
          id="maxPrice"
          type="number"
          min={0}
          step={25000}
          placeholder="Any"
          value={value.maxPrice}
          onChange={(e) => set("maxPrice", e.target.value)}
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="minBeds">Beds</Label>
        <Select value={value.minBeds} onValueChange={(v) => set("minBeds", v)}>
          <SelectTrigger id="minBeds" className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="any">Any</SelectItem>
            {["1", "2", "3", "4"].map((b) => (
              <SelectItem key={b} value={b}>
                {b}+
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="col-span-2 space-y-1.5">
        <Label htmlFor="propertyType">Type</Label>
        <Select
          value={value.propertyType}
          onValueChange={(v) => set("propertyType", v as SearchFilters["propertyType"])}
        >
          <SelectTrigger id="propertyType" className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="any">Any</SelectItem>
            <SelectItem value="House">House</SelectItem>
            <SelectItem value="Condo">Condo</SelectItem>
            <SelectItem value="Townhouse">Townhouse</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>
  );
}
