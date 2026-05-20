/**
 * TemplateStep — unit tests
 *
 * Covers FR-14 + FR-25 backwards-compatibility invariants for the optional
 * `templateFilter` prop:
 *   (a) When `templateFilter` is absent (prop not passed) → all 9 canonical templates
 *       are rendered. This is the standalone LegalWorkspace backwards-compat path
 *       per FR-25 / NFR-10.
 *   (b) When `templateFilter` is passed the 6-ID subset used by SpaarkeAi's
 *       `WorkspacePaneMenu`, exactly 6 templates render — and they are the listed IDs.
 *   (c) When `templateFilter` is empty `[]` → zero templates render (degenerate case
 *       but covered for completeness — defensive contract).
 *   (d) Filter preserves canonical order from `LAYOUT_TEMPLATES`, NOT the order of
 *       the filter array itself (deterministic UX).
 *
 * Test harness: jest + @testing-library/react, matching the project-wide
 * conventions used in `src/solutions/SpaarkeAi/jest.config.ts` and
 * `src/client/shared/Spaarke.UI.Components/jest.config.js`.
 *
 * @see TemplateStep — component under test
 * @see ../../../client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/layoutTemplates.ts — canonical 9-template definitions
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { LAYOUT_TEMPLATES, type LayoutTemplateId } from '@spaarke/ui-components';
import { TemplateStep } from '../TemplateStep';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const ALL_TEMPLATE_IDS: readonly LayoutTemplateId[] = LAYOUT_TEMPLATES.map((t) => t.id);

/**
 * The 6-template subset that SpaarkeAi's `WorkspacePaneMenu` passes when launching
 * the wizard via Xrm.Navigation.navigateTo (task 032). Keep this list in sync with
 * the spec's FR-14 listing.
 */
const SPAARKEAI_TEMPLATE_FILTER: readonly LayoutTemplateId[] = [
  '2-col-equal',
  '3-row-mixed',
  'hero-2x2',
  'sidebar-main',
  'single-column',
  'single-column-5',
];

describe('TemplateStep — templateFilter prop (FR-14 + FR-25)', () => {
  // ─────────────────────────────────────────────────────────────────────
  // (a) FR-25 backwards-compat: no prop → all 9 templates
  // ─────────────────────────────────────────────────────────────────────

  it('render_NoTemplateFilter_RendersAllNineCanonicalTemplates', () => {
    renderWithProvider(
      <TemplateStep selectedTemplateId={null} onSelect={() => {}} />,
    );

    // The TemplateStep renders one element with role="radio" per template card.
    const cards = screen.getAllByRole('radio');
    expect(cards).toHaveLength(9);
  });

  it('render_NoTemplateFilter_RendersEachCanonicalTemplateName', () => {
    renderWithProvider(
      <TemplateStep selectedTemplateId={null} onSelect={() => {}} />,
    );

    // Every canonical template's display name should appear in the DOM.
    for (const template of LAYOUT_TEMPLATES) {
      expect(screen.getByText(template.name)).toBeInTheDocument();
    }
  });

  it('render_TemplateFilterUndefined_RendersAllNineTemplates', () => {
    // Explicit undefined behaves identically to omitting the prop.
    renderWithProvider(
      <TemplateStep
        selectedTemplateId={null}
        onSelect={() => {}}
        templateFilter={undefined}
      />,
    );

    expect(screen.getAllByRole('radio')).toHaveLength(9);
  });

  // ─────────────────────────────────────────────────────────────────────
  // (b) FR-14: 6-template subset from SpaarkeAi
  // ─────────────────────────────────────────────────────────────────────

  it('render_TemplateFilterSixIds_RendersExactlySixTemplates', () => {
    renderWithProvider(
      <TemplateStep
        selectedTemplateId={null}
        onSelect={() => {}}
        templateFilter={SPAARKEAI_TEMPLATE_FILTER}
      />,
    );

    const cards = screen.getAllByRole('radio');
    expect(cards).toHaveLength(6);
  });

  it('render_TemplateFilterSixIds_RendersOnlyListedTemplateNames', () => {
    renderWithProvider(
      <TemplateStep
        selectedTemplateId={null}
        onSelect={() => {}}
        templateFilter={SPAARKEAI_TEMPLATE_FILTER}
      />,
    );

    const allowedNames = LAYOUT_TEMPLATES.filter((t) =>
      SPAARKEAI_TEMPLATE_FILTER.includes(t.id),
    ).map((t) => t.name);
    const excludedNames = LAYOUT_TEMPLATES.filter(
      (t) => !SPAARKEAI_TEMPLATE_FILTER.includes(t.id),
    ).map((t) => t.name);

    for (const name of allowedNames) {
      expect(screen.getByText(name)).toBeInTheDocument();
    }
    for (const name of excludedNames) {
      expect(screen.queryByText(name)).not.toBeInTheDocument();
    }
  });

  it('render_TemplateFilterSixIds_PreservesCanonicalOrder', () => {
    // The wizard must render filtered templates in the canonical LAYOUT_TEMPLATES
    // order, NOT the order of the filter array. This keeps UX deterministic regardless
    // of how callers construct their filter list.
    const reorderedFilter: readonly LayoutTemplateId[] = [
      // intentionally reverse alphabetical to prove canonical order wins
      'single-column-5',
      'single-column',
      'sidebar-main',
      'hero-2x2',
      '3-row-mixed',
      '2-col-equal',
    ];

    renderWithProvider(
      <TemplateStep
        selectedTemplateId={null}
        onSelect={() => {}}
        templateFilter={reorderedFilter}
      />,
    );

    const cards = screen.getAllByRole('radio');
    const renderedIdsInOrder = cards.map((c) => c.getAttribute('aria-label') ?? '');

    const expectedNamesInCanonicalOrder = LAYOUT_TEMPLATES.filter((t) =>
      reorderedFilter.includes(t.id),
    ).map((t) => t.name);

    // Each radio's aria-label starts with the template name; verify canonical order.
    for (let i = 0; i < expectedNamesInCanonicalOrder.length; i++) {
      expect(renderedIdsInOrder[i]).toContain(expectedNamesInCanonicalOrder[i]);
    }
  });

  // ─────────────────────────────────────────────────────────────────────
  // (c) Defensive: empty filter array renders zero templates
  // ─────────────────────────────────────────────────────────────────────

  it('render_TemplateFilterEmptyArray_RendersZeroTemplates', () => {
    renderWithProvider(
      <TemplateStep
        selectedTemplateId={null}
        onSelect={() => {}}
        templateFilter={[]}
      />,
    );

    // Empty array is distinct from `undefined` — caller explicitly opted into a
    // zero-template view. Spec defines this as a no-op rendering.
    expect(screen.queryAllByRole('radio')).toHaveLength(0);
  });

  // ─────────────────────────────────────────────────────────────────────
  // Sanity: canonical list has exactly 9 templates (catches regressions
  // in the shared lib if a template is added/removed unexpectedly)
  // ─────────────────────────────────────────────────────────────────────

  it('canonicalLayoutTemplates_AreExactlyNine', () => {
    expect(LAYOUT_TEMPLATES).toHaveLength(9);
    expect(ALL_TEMPLATE_IDS).toHaveLength(9);
  });
});
