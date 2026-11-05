import { requireRole } from "@/lib/server/session";

export default async function AgentLayout({ children }: LayoutProps<"/agent">) {
  await requireRole("Agent");
  return children;
}
