import type { Metadata } from "next";
import { AuthLayout } from "@/components/auth/auth-card";
import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Sign in" };

export default async function LoginPage({ searchParams }: PageProps<"/login">) {
  const params = await searchParams;
  const next = typeof params.next === "string" && params.next.startsWith("/") ? params.next : null;
  return (
    <AuthLayout>
      <LoginForm next={next} expired={params.expired === "1"} />
    </AuthLayout>
  );
}
