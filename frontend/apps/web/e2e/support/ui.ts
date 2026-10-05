import AxeBuilder from "@axe-core/playwright";
import { expect, type Page } from "@playwright/test";

import itMessages from "../../messages/it.json";

/**
 * Italian text of a translation key (static bundle generated from the seeds, as served to the `demo` tenant, whose
 * default language is Italian): tests find controls by what the user reads, without copying the wording.
 */
export function t(
  key: keyof typeof itMessages,
  values: Record<string, string | number> = {},
): string {
  return Object.entries(values).reduce(
    (text, [name, value]) => text.replaceAll(`{${name}}`, String(value)),
    itMessages[key] as string,
  );
}

/** Every page visited by the suite: no serious or critical WCAG 2.2 AA violation (skill auxilia-testing). */
export async function expectAccessible(page: Page, name: string): Promise<void> {
  // The pointer left by the previous action may hover a control (e.g. a button rendered where it was): measure
  // the page at rest. Hover colours are a separate concern of the UI kit, not of each page.
  await page.mouse.move(0, 0);
  // Colours caught mid-transition (a toast fading out, a button changing colour) are not the page's real contrast.
  await page.evaluate(() =>
    Promise.all(
      document
        .getAnimations()
        .filter(
          (animation) => animation.effect?.getTiming().iterations !== Number.POSITIVE_INFINITY,
        )
        .map((animation) => animation.finished.catch(() => undefined)),
    ),
  );
  // The metadata (title) is streamed after a client navigation: wait for it instead of reporting a race.
  await expect(page).toHaveTitle(/\S/);
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
    .analyze();
  const blocking = results.violations
    .filter((violation) => violation.impact === "serious" || violation.impact === "critical")
    .map(
      (violation) =>
        `${violation.id}: ${violation.nodes
          .map(
            (node) => `${node.target.join(" ")} (${node.any[0]?.message ?? node.failureSummary})`,
          )
          .join("; ")}`,
    );
  expect(blocking, `axe on ${name}`).toEqual([]);
}

/**
 * Mobile check: the page fits a 360 px screen (no horizontal scrolling). Polled: after a viewport change, resizing
 * content (charts) settles in a moment.
 */
export async function expectNoHorizontalScroll(page: Page): Promise<void> {
  await expect
    .poll(
      () =>
        page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        ),
      { timeout: 5_000 },
    )
    .toBeLessThanOrEqual(0);
}
