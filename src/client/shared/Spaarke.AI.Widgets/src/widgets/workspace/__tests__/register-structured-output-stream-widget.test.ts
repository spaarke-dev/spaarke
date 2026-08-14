/**
 * register-structured-output-stream-widget — unit tests (R3 task 022, FR-08)
 *
 * Verifies the StructuredOutputStream widget is wired into
 * WorkspaceWidgetRegistry via its dedicated side-effect registration file,
 * so dispatching `widget_load` with widgetType: 'structured-output-stream'
 * resolves to the expected component (and NOT the GenericTextWidget
 * fallback).
 *
 * Mirrors register-document-viewer-widget.test.ts (R4 task 042) and
 * register-search-criteria-result-widget.test.ts (R4 task 043). Added by
 * task 022 (FR-08 + FR-15 SHAPE) alongside that task's widget-type ↔
 * context-type enumeration + Assistant-contract completeness assertions —
 * no prior test file existed for this registration site.
 */

import {
  hasWorkspaceWidget,
  getWorkspaceWidgetMetadata,
  resolveWorkspaceWidget,
} from '../../../registry/WorkspaceWidgetRegistry';
import { STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE } from '../register-structured-output-stream-widget';

// Side-effect import: ensure the registration has run before the assertions.
// The package barrel does this in production; tests import directly so the
// registry state is set up regardless of test-runner module-load order.
import '../register-structured-output-stream-widget';

describe('register-structured-output-stream-widget', () => {
  it('registers the structured-output-stream widget type', () => {
    expect(hasWorkspaceWidget(STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE)).toBe(true);
  });

  it('exposes the expected display name in registry metadata', () => {
    const meta = getWorkspaceWidgetMetadata(STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE);
    expect(meta).toBeDefined();
    expect(meta!.displayName).toBe('Structured AI Output');
    expect(meta!.category).toBe('analysis');
    expect(meta!.allowMultiple).toBe(true);
  });

  it('resolveWorkspaceWidget returns a component (not the GenericTextWidget fallback)', async () => {
    const Component = await resolveWorkspaceWidget(STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE);
    expect(Component).toBeDefined();
    expect(typeof Component).toBe('function');
  });

  it('exports STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE as a stable string constant', () => {
    // Guard against accidental renames — dispatchers reference this constant
    // (e.g. ConversationPane in SpaarkeAi, task 020 / D2-11).
    expect(STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE).toBe('structured-output-stream');
  });

  // FR-08 enumeration (task 022) → FR-15 ENFORCEMENT (task 050): this widget
  // has no honest fit among the six WidgetContextType values (contextType stays
  // `undefined`), and is outside R3's overview/per-item scope — so it declares
  // an EXPLICIT assistantContract opt-out marker (required post-050), not a
  // silent absence.
  it('has no contextType and an EXPLICIT assistantContract opt-out (task 022 enumeration → task 050 FR-15)', () => {
    const meta = getWorkspaceWidgetMetadata(STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE);
    expect(meta).toBeDefined();
    expect(meta!.contextType).toBeUndefined();
    const declared = meta!.assistantContract as { optOut?: boolean; reason?: string };
    expect(declared.optOut).toBe(true);
    expect(typeof declared.reason).toBe('string');
    expect(declared.reason!.length).toBeGreaterThan(0);
  });
});
