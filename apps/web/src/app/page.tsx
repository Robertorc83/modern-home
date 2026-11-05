import { redirect } from "next/navigation";
import { getCurrentUser, roleHome } from "@/lib/server/session";

export default async function Home() {
  const user = await getCurrentUser();
  redirect(roleHome(user.role));
}
