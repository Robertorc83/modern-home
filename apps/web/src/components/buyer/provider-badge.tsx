"use client";

import { Sparkles } from "lucide-react";
import { Badge } from "@/components/ui/badge";

export function providerLabel(provider: string | undefined): string {
  if (!provider) return "Checking…";
  const p = provider.toLowerCase();
  if (p.includes("haiku")) return "Claude Haiku";
  if (p.includes("claude")) return "Claude";
  if (p.includes("rule")) return "Rule-based (no API key)";
  return provider;
}

export function ProviderBadge({ provider }: { provider: string | undefined }) {
  const isModel = provider?.toLowerCase().includes("claude") ?? false;
  return (
    <Badge
      variant="outline"
      title={
        isModel
          ? "Parsed by an LLM with a strict schema, validated server-side"
          : "Deterministic parser; set ANTHROPIC_API_KEY on the API to use Claude"
      }
    >
      {isModel ? <Sparkles /> : null}
      {providerLabel(provider)}
    </Badge>
  );
}
