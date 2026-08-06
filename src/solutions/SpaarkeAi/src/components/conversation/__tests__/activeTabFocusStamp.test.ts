/**
 * activeTabFocusStamp tests — task 010 (FR-A1).
 *
 * `ConversationPane`'s existing `usePaneEvent('workspace', …)` subscription is extended with an
 * `active_widget_changed` branch that stamps the focused tab into a REF (not state, to avoid
 * re-render churn). `deriveActiveTabFocusStamp` is the pure mapping logic that branch delegates to
 * — this is the seam test for that subscriber logic (mirrors this codebase's convention of unit
 * testing pure helpers like `reorderChipsForDisplay` / `normalizeReviewDepth` rather than rendering
 * the full pane to poke a private ref).
 *
 * Covers:
 *   - active_widget_changed → maps to the exact FR-A1 shape
 *     { widgetType, contextType, tabId, displayName, compactState }
 *   - contextType is always undefined here (FR-B1's closed set lands in a later task, 020)
 *   - compactState passes widgetData through verbatim (undefined when absent)
 *   - displayName falls back to widgetType when the event omits it
 *   - missing tabId/widgetType default to "" rather than throwing
 *   - every OTHER workspace event type (e.g. widget_load, tab_change) is ignored → returns null,
 *     so the subscriber's existing branches (compose widget_load, etc.) are left untouched
 */

import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import { deriveActiveTabFocusStamp } from '../activeTabFocusStamp';

describe('deriveActiveTabFocusStamp', () => {
  it('maps an active_widget_changed event to the FR-A1 shape', () => {
    const event = {
      type: 'active_widget_changed',
      widgetType: 'document-viewer',
      tabId: 'tab-123',
      displayName: 'Contract.pdf',
      widgetData: { fileName: 'Contract.pdf', pageCount: 4 },
    } as unknown as WorkspacePaneEvent;

    expect(deriveActiveTabFocusStamp(event)).toEqual({
      widgetType: 'document-viewer',
      contextType: undefined,
      tabId: 'tab-123',
      displayName: 'Contract.pdf',
      compactState: { fileName: 'Contract.pdf', pageCount: 4 },
    });
  });

  it('falls back displayName to widgetType when the event omits it', () => {
    const event = {
      type: 'active_widget_changed',
      widgetType: 'email',
      tabId: 'tab-email-1',
    } as unknown as WorkspacePaneEvent;

    const stamp = deriveActiveTabFocusStamp(event);
    expect(stamp?.displayName).toBe('email');
  });

  it('defaults missing tabId/widgetType to empty strings rather than throwing', () => {
    const event = { type: 'active_widget_changed' } as unknown as WorkspacePaneEvent;

    expect(deriveActiveTabFocusStamp(event)).toEqual({
      widgetType: '',
      contextType: undefined,
      tabId: '',
      displayName: '',
      compactState: undefined,
    });
  });

  it('passes through the registry-resolved widgetContextType (FR-B1/FR-C3, task 020)', () => {
    const event = {
      type: 'active_widget_changed',
      widgetType: 'email',
      widgetContextType: 'email',
      tabId: 'tab-email-1',
      displayName: 'Email',
    } as unknown as WorkspacePaneEvent;

    expect(deriveActiveTabFocusStamp(event)?.contextType).toBe('email');
  });

  it('leaves compactState undefined when the event carries no widgetData', () => {
    const event = {
      type: 'active_widget_changed',
      widgetType: 'matter-grid',
      tabId: 'tab-mg-1',
      displayName: 'Matters',
    } as unknown as WorkspacePaneEvent;

    expect(deriveActiveTabFocusStamp(event)?.compactState).toBeUndefined();
  });

  it('ignores every other workspace event type (returns null — the compose widget_load branch stays untouched)', () => {
    const otherTypes: WorkspacePaneEvent['type'][] = [
      'widget_load',
      'widget_update',
      'tab_change',
      'tab_count_change',
      'session_reset',
    ];

    for (const type of otherTypes) {
      const event = { type, widgetType: 'compose', tabId: 'tab-x' } as unknown as WorkspacePaneEvent;
      expect(deriveActiveTabFocusStamp(event)).toBeNull();
    }
  });
});
