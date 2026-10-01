import { brandingImage } from "@/lib/bff/handlers";

export const dynamic = "force-dynamic";

export async function GET(
  request: Request,
  context: RouteContext<"/api/branding/[tenant]/[asset]">,
) {
  const { tenant, asset } = await context.params;
  return brandingImage(request, tenant, asset);
}
