import { z } from "zod";

/*
 * Zod 4 compiles object validators with `new Function` after probing whether eval is allowed. The CSP forbids eval
 * (no `unsafe-eval`), so the probe only produced a CSP violation on every page with a form (H-02): validate without it.
 */
z.config({ jitless: true });
