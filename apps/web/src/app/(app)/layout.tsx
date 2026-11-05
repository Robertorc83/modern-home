import { AppHeader } from "@/components/app-header";
import { UserProvider } from "@/components/user-context";
import { getCurrentUser } from "@/lib/server/session";

export default async function AppLayout({ children }: LayoutProps<"/">) {
  const user = await getCurrentUser();
  return (
    <UserProvider user={user}>
      <AppHeader />
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8">{children}</main>
    </UserProvider>
  );
}
