# @auxilia/ui

Design system: shadcn/ui components (source copied here) on Radix + Tailwind v4, design tokens in `src/styles/tokens.css`.

- Add a component: `cd frontend/packages/ui && pnpm dlx shadcn@latest add <name>` (if `ui.shadcn.com` is not reachable, copy the source from `shadcn-ui/ui`, `apps/v4/registry/new-york-v4/ui/<name>.tsx`).
- **After adding**, make imports inside `src/` relative (`../lib/utils`, `./button`), because consuming apps resolve `@/` to their own `src`; never import the npm package `cn`. Replace any hard-coded text with a prop (e.g. `closeLabel` of `DialogContent`/`SheetContent`): texts come from the app's translations. Check `package.json` for unexpected new dependencies (allowlist: `auxilia-dependency-policy`).
- Apps import `@auxilia/ui/globals.css` once (root layout) and components as `@auxilia/ui/components/<name>`, helpers as `@auxilia/ui/lib/<name>`.
- Root layout: `ThemeProvider` (next-themes, `class` strategy, light/dark/system), `TooltipProvider`, `Toaster` (sonner). `ThemeToggle` takes its labels as props.

## Tokens and tenant branding
- Components use tokens only (`bg-primary`, `text-muted-foreground`, `bg-brand`…), never hex colours.
- Brand tokens (`--primary`, `--primary-foreground`, `--primary-text`, `--accent`, `--accent-foreground`, `--ring`, `--sidebar-primary*`, `--brand-gradient`) default to the legacy theme #667eea → #764ba2 made accessible.
- A tenant brand (`{ primaryColor, accentColor }`, public branding endpoint in S-02) is applied by `<BrandingStyle branding={…} nonce={…} />` in the tenant layout. `lib/branding.ts` derives light and dark tokens and guarantees WCAG 2.2 AA: text ≥ 4.5:1 (a failing brand colour is replaced by its closest accessible shade), filled components and focus ring ≥ 3:1 against the page. `--primary-text` is the brand as text on the page (links), because a light brand can fill a button with dark text but cannot be text on white.
- `pnpm test` (vitest) checks the contrast rules over a grid of colours and that `tokens.css` holds exactly the computed default brand.
- Preview: `pnpm dev`, then `/design-system` (development only; `?primary=%23e91e63&accent=%23ff9800` previews a tenant brand).
