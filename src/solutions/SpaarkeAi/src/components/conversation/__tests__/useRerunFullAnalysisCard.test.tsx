/**
 * useRerunFullAnalysisCard.test.tsx — UAT round-4 item #9.
 *
 * Covers:
 *   - showFor arms a single card (title/subtitle text, once-per-run — a second showFor call
 *     REPLACES rather than stacking).
 *   - clicking the card's main region fires onRerun(fileId, subDomain) then clears the card.
 *   - dismissing the card clears it WITHOUT firing onRerun.
 *   - resetForSession clears a pending card.
 *   - no card renders until showFor is called (nothing pending → null slot).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { renderHook, act, render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { useRerunFullAnalysisCard } from '../useRerunFullAnalysisCard';

function renderCardSlot(node: React.ReactNode) {
  return render(<FluentProvider theme={webLightTheme}>{node}</FluentProvider>);
}

describe('useRerunFullAnalysisCard', () => {
  it('renders nothing until showFor is called', () => {
    const { result } = renderHook(() => useRerunFullAnalysisCard({ onRerun: jest.fn() }));
    expect(result.current.cardSlot).toBeNull();
  });

  it('showFor renders the card with the specified title + timing subtitle', () => {
    const { result, rerender } = renderHook(() => useRerunFullAnalysisCard({ onRerun: jest.fn() }));

    act(() => {
      result.current.showFor({ fileId: 'file-1', subDomain: 'nda' });
    });
    rerender();

    renderCardSlot(result.current.cardSlot);
    expect(screen.getByTestId('rerun-full-analysis-card')).toBeInTheDocument();
    expect(screen.getByText('Rerun a full analysis')).toBeInTheDocument();
    expect(screen.getByText('(~2–3 min)')).toBeInTheDocument();
  });

  it('a second showFor call REPLACES the pending card — never stacks (single-slot by construction)', () => {
    const { result, rerender } = renderHook(() => useRerunFullAnalysisCard({ onRerun: jest.fn() }));

    act(() => {
      result.current.showFor({ fileId: 'file-1' });
    });
    rerender();
    act(() => {
      result.current.showFor({ fileId: 'file-2' });
    });
    rerender();

    renderCardSlot(result.current.cardSlot);
    // Exactly ONE card, and it reflects the LATEST showFor call (file-2).
    expect(screen.getAllByTestId('rerun-full-analysis-card')).toHaveLength(1);
    expect(screen.getByTestId('suggestion-card-rerun-full-analysis-file-2')).toBeInTheDocument();
  });

  it('clicking the card fires onRerun with the fileId + subDomain, then clears the card', () => {
    const onRerun = jest.fn();
    const { result, rerender } = renderHook(() => useRerunFullAnalysisCard({ onRerun }));

    act(() => {
      result.current.showFor({ fileId: 'file-3', subDomain: 'employment' });
    });
    rerender();
    renderCardSlot(result.current.cardSlot);

    fireEvent.click(screen.getByTestId('suggestion-card-rerun-full-analysis-file-3'));

    expect(onRerun).toHaveBeenCalledTimes(1);
    expect(onRerun).toHaveBeenCalledWith('file-3', 'employment');

    rerender();
    expect(result.current.cardSlot).toBeNull();
  });

  it('clicking the card WITHOUT a subDomain (the direct-chip door) fires onRerun(fileId, undefined)', () => {
    const onRerun = jest.fn();
    const { result, rerender } = renderHook(() => useRerunFullAnalysisCard({ onRerun }));

    act(() => {
      result.current.showFor({ fileId: 'file-4' });
    });
    rerender();
    renderCardSlot(result.current.cardSlot);

    fireEvent.click(screen.getByTestId('suggestion-card-rerun-full-analysis-file-4'));

    expect(onRerun).toHaveBeenCalledWith('file-4', undefined);
  });

  it('dismissing the card clears it WITHOUT calling onRerun', () => {
    const onRerun = jest.fn();
    const { result, rerender } = renderHook(() => useRerunFullAnalysisCard({ onRerun }));

    act(() => {
      result.current.showFor({ fileId: 'file-5' });
    });
    rerender();
    renderCardSlot(result.current.cardSlot);

    fireEvent.click(screen.getByTestId('suggestion-dismiss-rerun-full-analysis-file-5'));

    expect(onRerun).not.toHaveBeenCalled();
    rerender();
    expect(result.current.cardSlot).toBeNull();
  });

  it('resetForSession clears a pending card', () => {
    const { result, rerender } = renderHook(() => useRerunFullAnalysisCard({ onRerun: jest.fn() }));

    act(() => {
      result.current.showFor({ fileId: 'file-6' });
    });
    rerender();
    expect(result.current.cardSlot).not.toBeNull();

    act(() => {
      result.current.resetForSession();
    });
    rerender();
    expect(result.current.cardSlot).toBeNull();
  });

  it('showFor with an empty fileId is a no-op (never arms a card with no target)', () => {
    const { result, rerender } = renderHook(() => useRerunFullAnalysisCard({ onRerun: jest.fn() }));

    act(() => {
      result.current.showFor({ fileId: '' });
    });
    rerender();

    expect(result.current.cardSlot).toBeNull();
  });
});
