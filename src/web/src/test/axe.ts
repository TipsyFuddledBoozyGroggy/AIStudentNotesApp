import axe from "axe-core";

/** Runs axe on a rendered container. Colour contrast is not computable in jsdom, so it needs a real-browser check. */
export async function axeViolations(container: Element): Promise<string[]> {
  const results = await axe.run(container, {
    rules: { "color-contrast": { enabled: false } },
  });
  return results.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.html).join(" | ")}`);
}
