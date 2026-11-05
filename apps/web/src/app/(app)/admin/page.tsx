import type { Metadata } from "next";
import { AgentsAdmin } from "@/components/admin/agents";

export const metadata: Metadata = { title: "Agents" };

export default function Page() {
  return <AgentsAdmin />;
}
