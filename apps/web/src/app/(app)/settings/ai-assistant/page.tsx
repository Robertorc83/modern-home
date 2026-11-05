import type { Metadata } from "next";
import { AiAssistantSettings } from "@/components/ai-assistant-settings";

export const metadata: Metadata = { title: "Connect an AI assistant" };

export default function Page() {
  return <AiAssistantSettings />;
}
