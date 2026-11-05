import type { Metadata } from "next";
import { AuthLayout } from "@/components/auth/auth-card";
import { RegisterForm } from "./register-form";

export const metadata: Metadata = { title: "Create buyer account" };

export default function RegisterPage() {
  return (
    <AuthLayout>
      <RegisterForm />
    </AuthLayout>
  );
}
