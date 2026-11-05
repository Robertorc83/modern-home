import { requireRole } from "@/lib/server/session";

export default async function AdminLayout({ children }: LayoutProps<"/admin">) {
  await requireRole("BrokerageAdmin");
  return children;
}
