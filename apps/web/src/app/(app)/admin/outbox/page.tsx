import type { Metadata } from "next";
import { Outbox } from "@/components/admin/outbox";

export const metadata: Metadata = { title: "Outbox" };

export default function Page() {
  return <Outbox />;
}
