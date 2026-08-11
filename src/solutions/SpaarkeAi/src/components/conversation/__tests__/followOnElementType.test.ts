/**
 * followOnElementType.test.ts — spaarkeai-assistant-enhancements-r3 task 041 (FR-14).
 *
 * Proves the DETERMINISTIC card-vs-chip resolver:
 *   (a) a per-item widget (email) WITH an active item → 'card'.
 *   (b) an overview-only widget (a grid / 'workspace') → 'chip'.
 *   (c) the SAME input always produces the SAME output (determinism) — and,
 *       critically, that NO keyword/text signal can change the outcome,
 *       because the resolver's signature carries no message-text parameter
 *       at all (ASSISTANT-UI-ELEMENT-CRITERIA.md §5's "DON'T fire a chip set
 *       from a keyword heuristic" is structurally impossible to violate here).
 *   (d) an unregistered/unknown widget type → 'none' (the "no follow-on may
 *       reference an unmounted tool" guard — nothing deterministic to derive).
 *   (e) no active item for a per-item widget → falls through to the widget's
 *       overview signal (or 'none' if it has none).
 *
 * Side-effect imports (`register-workspace-widgets` / `register-document-viewer-widget`)
 * register the REAL widget catalog at module-evaluation time (mirrors
 * `WorkspaceWidgetRegistry.interactionPattern.test.ts`'s own pattern — see
 * `registerWorkspaceWidgets()`'s own docblock: the exported function is a
 * documentation-only no-op sentinel, all registration is a TOP-LEVEL side
 * effect of importing the module, so this suite deliberately does NOT call
 * `clearWorkspaceRegistry()` — clearing after the side-effect import already
 * ran would empty the registry with nothing left to repopulate it).
 */
import '@spaarke/ai-widgets/widgets/workspace/register-workspace-widgets';
import '@spaarke/ai-widgets/widgets/workspace/register-document-viewer-widget';
import {
  resolveFollowOnElementType,
  resolveFollowOnElementTypeForActiveItemType,
  ACTIVE_ITEM_TYPE_TO_WIDGET_TYPE,
} from '../followOnElementType';

describe('resolveFollowOnElementType (task 041, FR-14)', () => {
  it('resolves a per-item widget (email) WITH an active item to CARD', () => {
    expect(resolveFollowOnElementType({ widgetType: 'email', hasActiveItem: true })).toBe('card');
  });

  it('resolves a per-item widget (document-viewer) WITH an active item to CARD (interactionPattern=respond, but it still has cards)', () => {
    // document-viewer's interactionPattern is 'respond' (every card lands 'chat') yet the widget
    // STILL declares perItemCards — proving element-type selection is driven by perItemCards
    // presence + active-item state, not merely the interactionPattern label.
    expect(
      resolveFollowOnElementType({ widgetType: 'document-viewer', hasActiveItem: true })
    ).toBe('card');
  });

  it('resolves an overview-only widget (a matter grid / dashboard) with NO active item to CHIP', () => {
    expect(resolveFollowOnElementType({ widgetType: 'workspace', hasActiveItem: false })).toBe('chip');
    expect(resolveFollowOnElementType({ widgetType: 'matters-list', hasActiveItem: false })).toBe('chip');
  });

  it('resolves a per-item widget (email) with NO active item to CHIP or NONE (falls through — email declares no overviewTools of its own today, so NONE)', () => {
    // email declares perItemCards but they only apply WITH an active item; without one there is no
    // per-item follow-on, so this falls through to the widget's overview-tool declaration (empty for
    // 'email' today — its overview lives on the sibling grid — see register-workspace-widgets.ts).
    const result = resolveFollowOnElementType({ widgetType: 'email', hasActiveItem: false });
    expect(['chip', 'none']).toContain(result);
  });

  it('resolves an unregistered widget type to NONE (no follow-on may reference an unmounted tool)', () => {
    expect(resolveFollowOnElementType({ widgetType: 'totally-unregistered-type', hasActiveItem: true })).toBe(
      'none'
    );
  });

  it('resolves an undefined widget type to NONE', () => {
    expect(resolveFollowOnElementType({ widgetType: undefined, hasActiveItem: true })).toBe('none');
  });

  it('is deterministic — repeated calls with the SAME input always produce the SAME output', () => {
    const params = { widgetType: 'email', hasActiveItem: true } as const;
    const results = Array.from({ length: 10 }, () => resolveFollowOnElementType(params));
    expect(new Set(results).size).toBe(1);
    expect(results[0]).toBe('card');
  });

  it(
    'NO KEYWORD MIS-FIRE: the resolver signature carries no message-text parameter — a caller ' +
      'simulating a chat turn whose text coincidentally contains a card-triggering keyword ' +
      '("reply", "forward") for an OVERVIEW-ONLY widget with no active item still resolves to CHIP, ' +
      'never CARD, because the function structurally cannot read the text at all',
    () => {
      // Simulate a caller that has "reply"/"forward" in scope (e.g. the last assistant message text)
      // but does NOT thread it through the resolver — proving no code path exists by which it could.
      const coincidentalChatText =
        'You could reply to this, forward it, or draft a memo about it — want me to summarize instead?';
      void coincidentalChatText; // deliberately unused by the resolver call below

      const result = resolveFollowOnElementType({ widgetType: 'workspace', hasActiveItem: false });
      expect(result).toBe('chip');

      // Same assertion holds even with an active item present on an UNRELATED (unregistered) widget —
      // still 'none', never a spuriously-produced 'card' from the coincidental keywords above.
      expect(
        resolveFollowOnElementType({ widgetType: 'not-a-real-widget', hasActiveItem: true })
      ).toBe('none');
    }
  );

  it('a caller cannot smuggle a text/keyword field through the params object — extra fields are ignored', () => {
    const withExtraField = {
      widgetType: 'workspace',
      hasActiveItem: false,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      text: 'reply reply reply forward forward',
    } as any;
    expect(resolveFollowOnElementType(withExtraField)).toBe('chip');
  });
});

describe('resolveFollowOnElementTypeForActiveItemType (active-item-conduit vocabulary mapping)', () => {
  it('maps the conduit type "email" through to the registry key "email" → CARD', () => {
    expect(ACTIVE_ITEM_TYPE_TO_WIDGET_TYPE.email).toBe('email');
    expect(resolveFollowOnElementTypeForActiveItemType('email')).toBe('card');
  });

  it('maps the conduit type "document" through to the registry key "document-viewer" → CARD', () => {
    expect(ACTIVE_ITEM_TYPE_TO_WIDGET_TYPE.document).toBe('document-viewer');
    expect(resolveFollowOnElementTypeForActiveItemType('document')).toBe('card');
  });

  it('an active-item type with no registry mapping (e.g. "compose") resolves to NONE', () => {
    expect(resolveFollowOnElementTypeForActiveItemType('compose')).toBe('none');
  });

  it('null/undefined active-item type (tab closed / deselected) resolves to NONE', () => {
    expect(resolveFollowOnElementTypeForActiveItemType(null)).toBe('none');
    expect(resolveFollowOnElementTypeForActiveItemType(undefined)).toBe('none');
  });
});
