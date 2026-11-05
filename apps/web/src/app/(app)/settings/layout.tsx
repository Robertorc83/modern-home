import { requireRole } from "@/lib/server/session";

export default async function SettingsLayout({ children }: LayoutProps<"/settings">) {
  await requireRole("Buyer", "Agent");
  return children;
}
