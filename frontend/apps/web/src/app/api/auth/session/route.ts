import { sessionInfo } from "@/lib/bff/handlers";

export const dynamic = "force-dynamic";

export function GET(request: Request) {
  return sessionInfo(request, "tenant");
}
