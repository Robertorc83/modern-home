import type { Metadata } from "next";
import { ShowingsInbox } from "@/components/showings-inbox";

export const metadata: Metadata = { title: "All showings" };

export default function Page() {
  return (
    <ShowingsInbox
      title="All showings"
      description="Every showing across your brokerage's agents. Agents approve or decline their own requests."
      canRespond={false}
      showAgent
    />
  );
}
