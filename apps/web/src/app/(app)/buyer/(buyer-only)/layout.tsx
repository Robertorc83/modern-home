import { requireRole } from "@/lib/server/session";

export default async function BuyerLayout({ children }: LayoutProps<"/">) {
  await requireRole("Buyer");
  return children;
}
