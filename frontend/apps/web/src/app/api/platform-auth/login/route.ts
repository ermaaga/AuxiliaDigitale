import { login } from "@/lib/bff/handlers";

export const dynamic = "force-dynamic";

export function POST(request: Request) {
  return login(request, "platform");
}
