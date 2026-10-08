import { beginTwoFactorSetup } from "@/lib/bff/handlers";

export const dynamic = "force-dynamic";

export function POST(request: Request) {
  return beginTwoFactorSetup(request);
}
