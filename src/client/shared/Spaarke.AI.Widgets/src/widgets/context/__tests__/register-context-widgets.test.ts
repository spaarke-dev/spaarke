/**
 * register-context-widgets — unit tests
 *
 * Covers:
 * - All 6 source widget types are registered in ContextWidgetRegistry after
 *   registerContextWidgets() is called.
 * - Each type resolves to a non-null component (factory loads successfully).
 * - Calling registerContextWidgets() twice is idempotent (no duplicates, no errors).
 *
 * Task: AIPU2-081
 */

import {
  clearContextRegistry,
  getAllContextWidgetTypes,
  hasContextWidget,
  resolveContextWidget,
} from '../../../registry/ContextWidgetRegistry';
import { registerContextWidgets } from '../register-context-widgets';

// ---------------------------------------------------------------------------
// Setup / teardown
// ---------------------------------------------------------------------------

beforeEach(() => {
  clearContextRegistry();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('registerContextWidgets', () => {
  it('registers all 6 source widget types', () => {
    registerContextWidgets();

    const expected = ['DocumentViewer', 'WebSource', 'LegalLibrary', 'Citation', 'ImageViewer', 'CodeViewer'];

    for (const type of expected) {
      expect(hasContextWidget(type)).toBe(true);
    }
  });

  it('registers exactly 6 types (no extras)', () => {
    registerContextWidgets();
    expect(getAllContextWidgetTypes()).toHaveLength(6);
  });

  it('is idempotent — calling twice does not duplicate registrations', () => {
    registerContextWidgets();
    registerContextWidgets();
    expect(getAllContextWidgetTypes()).toHaveLength(6);
  });

  it('resolves DocumentViewer to a non-null component', async () => {
    registerContextWidgets();
    const component = await resolveContextWidget('DocumentViewer');
    expect(component).not.toBeNull();
  });

  it('resolves WebSource to a non-null component', async () => {
    registerContextWidgets();
    const component = await resolveContextWidget('WebSource');
    expect(component).not.toBeNull();
  });

  it('resolves LegalLibrary to a non-null component', async () => {
    registerContextWidgets();
    const component = await resolveContextWidget('LegalLibrary');
    expect(component).not.toBeNull();
  });

  it('resolves Citation to a non-null component', async () => {
    registerContextWidgets();
    const component = await resolveContextWidget('Citation');
    expect(component).not.toBeNull();
  });

  it('resolves ImageViewer to a non-null component', async () => {
    registerContextWidgets();
    const component = await resolveContextWidget('ImageViewer');
    expect(component).not.toBeNull();
  });

  it('resolves CodeViewer to a non-null component', async () => {
    registerContextWidgets();
    const component = await resolveContextWidget('CodeViewer');
    expect(component).not.toBeNull();
  });
});
