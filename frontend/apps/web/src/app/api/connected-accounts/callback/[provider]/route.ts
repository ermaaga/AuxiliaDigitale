import { connectedAccountCallback } from "@/lib/bff/handlers";

export const dynamic = "force-dynamic";

/** Redirect URI of "Sign in with Google / Microsoft" (N05, ADR 0021), registered at the providers per deployment. */
export async function GET(
  request: Request,
  ctx: RouteContext<"/api/connected-accounts/callback/[provider]">,
) {
  const { provider } = await ctx.params;
  return connectedAccountCallback(request, provider);
}
