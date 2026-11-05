import type { Metadata } from "next";
import { AuthLayout } from "@/components/auth/auth-card";
import { safeNextPath } from "@/lib/utils";
import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Sign in" };

export default async function LoginPage({ searchParams }: PageProps<"/login">) {
  const params = await searchParams;
  const next = safeNextPath(params.next);
  return (
    <AuthLayout>
      <LoginForm next={next} expired={params.expired === "1"} />
    </AuthLayout>
  );
}
