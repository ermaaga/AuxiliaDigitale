import { proxy } from "@/lib/bff/handlers";

export const dynamic = "force-dynamic";

async function handle(request: Request, context: RouteContext<"/api/platform-bff/[...path]">) {
  const { path } = await context.params;
  return proxy(request, "platform", path);
}

export { handle as GET, handle as POST, handle as PUT, handle as PATCH, handle as DELETE };
