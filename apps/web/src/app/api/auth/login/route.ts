import { exchangeForSession } from "@/lib/server/auth-route";

export async function POST(request: Request) {
  return exchangeForSession("/auth/login", request);
}
