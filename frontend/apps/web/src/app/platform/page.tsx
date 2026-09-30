import { redirect } from "next/navigation";

import { PLATFORM_HOME } from "@/lib/href";

/** `/platform` → the tenant list (the console layout sends signed-out users to the sign-in page). */
export default function PlatformRoot() {
  redirect(PLATFORM_HOME);
}
