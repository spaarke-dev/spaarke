/**
 * FieldGrid — unit tests.
 *
 * Covers acceptance criteria from record-header-and-notepad-r1 task 004:
 *  - Default columns = 3 (grid-template-columns "repeat(3, 1fr)")
 *  - columns={2} respected (grid-template-columns "repeat(2, 1fr)")
 *  - Renders children in DOM order
 *  - Grid gap style present (semantic tokens — value is a CSS var at runtime)
 *  - FieldGrid does NOT set gridColumn on children (renderer's responsibility)
 *  - Extra className is merged
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { FieldGrid } from '../FieldGrid';

/** Grid root selector helper. */
const getGrid = (): HTMLElement => screen.getByRole('group');

describe('FieldGrid', () => {
  // ──────────────────────────────────────────────────────────────────────
  // Default column count = 3 (FR-03)
  // ──────────────────────────────────────────────────────────────────────

  it('defaults to 3 columns when columns prop is omitted', () => {
    renderWithProviders(
      <FieldGrid>
        <div data-testid="child-1">1</div>
      </FieldGrid>,
    );
    const grid = getGrid();
    expect(grid).toBeInTheDocument();
    expect(grid.getAttribute('data-columns')).toBe('3');
    const cs = window.getComputedStyle(grid);
    expect(cs.gridTemplateColumns).toBe('repeat(3, 1fr)');
  });

  // ──────────────────────────────────────────────────────────────────────
  // columns={2} respected (FR-03)
  // ──────────────────────────────────────────────────────────────────────

  it('renders 2 columns when columns={2}', () => {
    renderWithProviders(
      <FieldGrid columns={2}>
        <div>a</div>
      </FieldGrid>,
    );
    const grid = getGrid();
    expect(grid.getAttribute('data-columns')).toBe('2');
    const cs = window.getComputedStyle(grid);
    expect(cs.gridTemplateColumns).toBe('repeat(2, 1fr)');
  });

  // ──────────────────────────────────────────────────────────────────────
  // Explicit columns={3} also respected
  // ──────────────────────────────────────────────────────────────────────

  it('renders 3 columns when columns={3} is passed explicitly', () => {
    renderWithProviders(
      <FieldGrid columns={3}>
        <div>a</div>
      </FieldGrid>,
    );
    const grid = getGrid();
    expect(grid.getAttribute('data-columns')).toBe('3');
    const cs = window.getComputedStyle(grid);
    expect(cs.gridTemplateColumns).toBe('repeat(3, 1fr)');
  });

  // ──────────────────────────────────────────────────────────────────────
  // Children render in DOM order (FR-03)
  // ──────────────────────────────────────────────────────────────────────

  it('renders children in the order they were passed', () => {
    renderWithProviders(
      <FieldGrid columns={3}>
        <div data-testid="field-1">one</div>
        <div data-testid="field-2">two</div>
        <div data-testid="field-3">three</div>
        <div data-testid="field-4">four</div>
        <div data-testid="field-5">five</div>
      </FieldGrid>,
    );

    const grid = getGrid();
    const cells = Array.from(grid.children) as HTMLElement[];
    expect(cells).toHaveLength(5);
    expect(cells[0]).toHaveAttribute('data-testid', 'field-1');
    expect(cells[1]).toHaveAttribute('data-testid', 'field-2');
    expect(cells[2]).toHaveAttribute('data-testid', 'field-3');
    expect(cells[3]).toHaveAttribute('data-testid', 'field-4');
    expect(cells[4]).toHaveAttribute('data-testid', 'field-5');
  });

  // ──────────────────────────────────────────────────────────────────────
  // Grid gap style is present (semantic-token derived)
  // ──────────────────────────────────────────────────────────────────────

  it('sets display:grid on the root', () => {
    renderWithProviders(
      <FieldGrid>
        <div>x</div>
      </FieldGrid>,
    );
    const grid = getGrid();
    const cs = window.getComputedStyle(grid);
    expect(cs.display).toBe('grid');
  });

  it('sets column-gap and row-gap style properties (semantic tokens)', () => {
    renderWithProviders(
      <FieldGrid>
        <div>x</div>
      </FieldGrid>,
    );
    const grid = getGrid();
    const cs = window.getComputedStyle(grid);
    // Griffel resolves tokens.spacingHorizontalM / spacingVerticalS to CSS
    // variables at runtime; we just verify the properties are set (non-empty).
    expect(cs.columnGap).not.toBe('');
    expect(cs.rowGap).not.toBe('');
  });

  // ──────────────────────────────────────────────────────────────────────
  // FieldGrid does NOT set gridColumn on children (FR-03 contract)
  // ──────────────────────────────────────────────────────────────────────

  it('does NOT set gridColumn on child cells — spanning is the child renderer’s responsibility', () => {
    renderWithProviders(
      <FieldGrid columns={3}>
        <div data-testid="child-a">a</div>
        <div data-testid="child-b">b</div>
      </FieldGrid>,
    );
    const childA = screen.getByTestId('child-a');
    const childB = screen.getByTestId('child-b');
    // FieldGrid must not inject an inline gridColumn style on the child.
    expect(childA.style.gridColumn).toBe('');
    expect(childB.style.gridColumn).toBe('');
  });

  // ──────────────────────────────────────────────────────────────────────
  // className merges through
  // ──────────────────────────────────────────────────────────────────────

  it('applies caller-provided className alongside internal classes', () => {
    renderWithProviders(
      <FieldGrid className="caller-class">
        <div>x</div>
      </FieldGrid>,
    );
    const grid = getGrid();
    expect(grid.className).toMatch(/caller-class/);
  });
});
