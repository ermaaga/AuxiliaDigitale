import { proxy } from "@/lib/bff/handlers";

export const dynamic = "force-dynamic";

async function handle(request: Request, context: RouteContext<"/api/bff/[...path]">) {
  const { path } = await context.params;
  return proxy(request, "tenant", path);
}

export { handle as GET, handle as POST, handle as PUT, handle as PATCH, handle as DELETE };
