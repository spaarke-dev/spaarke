/**
 * followOnElementType — the DETERMINISTIC card-vs-chip element-type resolver
 * (spaarkeai-assistant-enhancements-r3 task 041, FR-14).
 *
 * Per docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md: a per-item action on the
 * user's CURRENT active item is a CARD (persistent, act-on); an overview/query
 * turn-follow-on is a CHIP (ephemeral, grounded in the last turn). This module
 * is the ONE place that decision is made — reading ONLY the registration
 * contract (task 022's `WidgetAssistantContract.perItemCards`/`overviewTools`)
 * and the interaction-pattern field (task 040's `getWidgetInteractionPattern`)
 * — never message text, never a keyword, never a classifier (ADR-039).
 *
 * §11 reuse-first: this does NOT re-implement `EmailPerItemCards.tsx` /
 * `DocumentPerItemCards.tsx` (task 025/026) — those hooks already gate their
 * own rendering on `getWidgetAssistantContract(type).perItemCards`. This
 * module is task 041's OWN new consumption of the registration contract: (a)
 * the outer stack-membership gate `ConversationPane.tsx` applies before
 * folding a card slot into `ProactiveCardStack` (belt-and-suspenders against a
 * future card-producing hook bypassing the registry — see
 * `resolveFollowOnElementType`'s doc below), and (b) the FIRST runtime read of
 * `getWidgetInteractionPattern` (task 040's own note names task 041 as that
 * first consumer).
 *
 * NO KEYWORD HEURISTIC: note the function signature below takes NO message
 * text / chat content parameter at all — it is structurally incapable of
 * mis-firing off a coincidental keyword in the conversation, which is exactly
 * the anti-pattern ASSISTANT-UI-ELEMENT-CRITERIA.md §5 warns against ("DON'T
 * fire a chip set from a keyword heuristic that can match unrelated
 * replies").
 */

import { getWidgetAssistantContract, getWidgetInteractionPattern } from '@spaarke/ai-widgets';

/**
 * The active-item conduit's `type` discriminator (`ActiveItemHandle.type`,
 * `activeItemConduit.tsx`) is a free-form widget/context string ('email',
 * 'document', 'compose', …) — a DIFFERENT vocabulary from the widget-REGISTRY
 * type key the Assistant contract is registered under ('email',
 * 'document-viewer', …). This is the ONE explicit mapping between the two;
 * every follow-on-type call site reads through it rather than re-guessing the
 * registry key per active-item type.
 *
 * @see ../workspace/activeItemConduit.tsx — `ActiveItemHandle.type` docblock
 * @see @spaarke/ai-widgets `DOCUMENT_VIEWER_WIDGET_TYPE` — the 'document-viewer' registry key
 */
export const ACTIVE_ITEM_TYPE_TO_WIDGET_TYPE: Readonly<Record<string, string>> = {
  email: 'email',
  document: 'document-viewer',
};

/** The four possible resolutions. `'none'` = no deterministic follow-on to offer for this widget/state. */
export type FollowOnElementType = 'card' | 'chip' | 'none';

export interface ResolveFollowOnElementTypeParams {
  /**
   * The widget-REGISTRY type key (`WidgetMetadata`'s registration type, e.g.
   * 'email' / 'document-viewer' / 'workspace') for the surface the follow-on
   * would apply to. `undefined` when there is no addressable widget (e.g. no
   * tab open, or the active item's type has no conduit→registry mapping).
   */
  readonly widgetType: string | undefined;
  /**
   * Whether there is currently a single active item of this widget's type
   * (`ActiveItemHandle` present + type-matched) — the active-item conduit's
   * single-active-item invariant (task 001/011/012).
   */
  readonly hasActiveItem: boolean;
}

/**
 * Resolve the deterministic follow-on element type for a widget + active-item
 * state. Reads ONLY the frozen registration contract — `perItemCards` (task
 * 022) and `interactionPattern` (task 040) — via the ONE canonical registry
 * accessor pair. NEVER reads conversation text, a chip label, or any
 * model-produced content.
 *
 * Decision (ASSISTANT-UI-ELEMENT-CRITERIA.md):
 *   1. No registered contract for `widgetType` → `'none'` (nothing
 *      deterministic to offer; also the "no follow-on references an
 *      unmounted tool" guard — an unregistered widget type can never
 *      produce a follow-on here).
 *   2. The widget declares per-item cards AND there is a single active item
 *      of its type → `'card'` — a persistent act-on affordance for the
 *      CURRENT item. True for BOTH 'hybrid' widgets (email — some cards land
 *      chat, some land a composer) and a 'respond' widget whose cards ALL
 *      land chat (document-viewer) — `interactionPattern` governs WHERE a
 *      card's action lands (the per-card `landing` field), never WHETHER the
 *      per-item affordance itself is a card.
 *   3. Otherwise, a 'direct' widget (every action opens another surface, no
 *      standalone chat answer) has no chat "next question" to offer once its
 *      cards aren't showing → `'none'`.
 *   4. Otherwise ('respond' or 'hybrid' with no active item, or no per-item
 *      cards at all) — if the widget declares an overview tool, its
 *      turn-follow-on is a `'chip'` (ephemeral, turn-grounded — the existing
 *      next-step chip strip, `useConsumerChips`).
 *   5. No overview tool either → `'none'`.
 */
export function resolveFollowOnElementType(
  params: ResolveFollowOnElementTypeParams
): FollowOnElementType {
  const { widgetType, hasActiveItem } = params;
  if (!widgetType) return 'none';

  const contract = getWidgetAssistantContract(widgetType);
  const pattern = getWidgetInteractionPattern(widgetType);
  if (!contract || !pattern) return 'none';

  if (contract.perItemCards.length > 0 && hasActiveItem) return 'card';
  if (pattern === 'direct') return 'none';
  if (contract.overviewTools.length > 0) return 'chip';
  return 'none';
}

/**
 * Convenience wrapper for the active-item conduit's own vocabulary — maps
 * `ActiveItemHandle.type` ('email' / 'document' / …) through
 * `ACTIVE_ITEM_TYPE_TO_WIDGET_TYPE` before resolving. Returns `'none'` for an
 * active-item type with no registry mapping (e.g. 'compose' — no Assistant
 * contract registered per task 022's scope note).
 */
export function resolveFollowOnElementTypeForActiveItemType(
  activeItemType: string | null | undefined
): FollowOnElementType {
  if (!activeItemType) return 'none';
  const widgetType = ACTIVE_ITEM_TYPE_TO_WIDGET_TYPE[activeItemType];
  return resolveFollowOnElementType({ widgetType, hasActiveItem: true });
}
