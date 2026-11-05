import type { Metadata } from "next";
import { AiAssistantSettings } from "@/components/ai-assistant-settings";

export const metadata: Metadata = { title: "Connect an AI assistant" };

// Read per request (not inlined at build time), so one image serves any deployment.
export const dynamic = "force-dynamic";

export default function Page() {
  const mcpUrl = process.env.PUBLIC_MCP_URL ?? "http://localhost:5080/mcp";
  return <AiAssistantSettings mcpUrl={mcpUrl} />;
}
