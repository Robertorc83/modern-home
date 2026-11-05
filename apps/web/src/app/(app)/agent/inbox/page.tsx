import type { Metadata } from "next";
import { ShowingsInbox } from "@/components/showings-inbox";

export const metadata: Metadata = { title: "Inbox" };

export default function Page() {
  return (
    <ShowingsInbox
      title="Showings inbox"
      description="Requests for your listings. Times are shown in each listing's own time zone."
      canRespond
    />
  );
}
