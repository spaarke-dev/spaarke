/**
 * The page's launch envelope (ebilling-r1 DEF-069, GitHub #3112). The Rule Review page can open straight on one rule set: from the rule set
 * form's "Rule Review" command (`Xrm.Navigation.navigateTo` with `data=ruleSetId%3D{guid}`), from a URL (`?ruleSetId={guid}`), or from a form
 * command / view row as `id` + `typename=sprk_ruleset`. `parseDataParams` (`@spaarke/ui-components`) decodes both the Xrm data envelope and the
 * raw query string; this module validates its result (the `InvoiceReview` `services/launch.ts` shape: braces stripped, lower case, GUID check).
 *
 * A launch hint is never a gate: a missing id, a malformed id or a `typename` other than `sprk_ruleset` yields `undefined`, and the page opens
 * on the list exactly as it does from the sitemap. There is no error state. Exported for the tests.
 */

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A record id without braces, lower case (ADR-044: bare and lower case). */
export function cleanId(id: string): string {
  return id.replace(/[{}]/g, '').trim().toLowerCase();
}

/** The rule set id to open at launch, or `undefined` to open the list. */
export function parseRuleSetLaunch(params: Record<string, string | undefined>): string | undefined {
  const typename = (params.typename ?? '').trim().toLowerCase();
  if (typename && typename !== 'sprk_ruleset') return undefined;
  const id = cleanId(params.ruleSetId ?? params.ruleSetid ?? params.rulesetid ?? params.id ?? '');
  return GUID.test(id) ? id : undefined;
}
