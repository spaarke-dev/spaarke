/**
 * AnalysisEditorWidget — Dark mode and NFR tests
 *
 * Task: AIPU-051 — Dark Mode and NFR Verification
 * Covers:
 *   - NFR-01: renders within 200ms (light and dark)
 *   - NFR-04: renders correctly under webDarkTheme
 *   - ADR-021: no hard-coded colors (verified by no-hardcoded-colors.test.ts)
 *
 * Task 025 (spec FR-09) also covers:
 *   - Live edit-state restore: a persisted `isEditing`/`draftSections` restore hint
 *     resumes edit mode with the prior draft, instead of always starting read-only.
 *   - Live edit-state persist: every edit + Save/Cancel reports a patch via
 *     `onDataChange` so the host can persist it into the tab's widgetData
 *     (no silent edit-state loss on tab close/reopen or page refresh).
 */

import React from 'react';
import '@testing-library/jest-dom';
import { fireEvent } from '@testing-library/react';
import AnalysisEditorWidget from '../AnalysisEditorWidget';
import { renderWithTheme, webLightTheme, webDarkTheme, mockAnalysisEditorProps } from '../../__tests__/test-utils';

describe('AnalysisEditorWidget', () => {
  const props = mockAnalysisEditorProps();

  it('renders in light theme without error', () => {
    const { container } = renderWithTheme(<AnalysisEditorWidget {...props} />, webLightTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders in dark theme (webDarkTheme) without error', () => {
    const { container } = renderWithTheme(<AnalysisEditorWidget {...props} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders within 200ms in light theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<AnalysisEditorWidget {...props} />, webLightTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders within 200ms in dark theme (NFR-01)', () => {
    const start = performance.now();
    renderWithTheme(<AnalysisEditorWidget {...props} />, webDarkTheme);
    const elapsed = performance.now() - start;
    expect(elapsed).toBeLessThan(200);
  });

  it('renders section headings', () => {
    const { getByText } = renderWithTheme(<AnalysisEditorWidget {...props} />, webLightTheme);
    expect(getByText('Executive Summary')).toBeTruthy();
    expect(getByText('Key Risks')).toBeTruthy();
  });

  it('renders section body text', () => {
    const { getByText } = renderWithTheme(<AnalysisEditorWidget {...props} />, webLightTheme);
    expect(getByText('This agreement outlines the obligations of both parties.')).toBeTruthy();
  });

  it('renders loading state in dark theme without error', () => {
    const { container } = renderWithTheme(<AnalysisEditorWidget {...props} isLoading={true} />, webDarkTheme);
    expect(container.firstChild).toBeTruthy();
  });
});

describe('AnalysisEditorWidget — live edit-state restore + persist (task 025 / FR-09)', () => {
  const editableProps = {
    data: {
      sections: [
        { heading: 'Executive Summary', body: 'Original summary body.' },
        { heading: 'Key Risks', body: 'Original risks body.' },
      ],
      editable: true,
    },
    isLoading: false,
  };

  it('restores edit mode + the prior draft when data carries isEditing/draftSections restore hints', () => {
    const { getByDisplayValue, queryByText } = renderWithTheme(
      <AnalysisEditorWidget
        data={{
          ...editableProps.data,
          isEditing: true,
          draftSections: [
            { heading: 'Executive Summary', body: 'UNSAVED edit in progress' },
            { heading: 'Key Risks', body: 'Original risks body.' },
          ],
        }}
        isLoading={false}
      />,
      webLightTheme
    );

    // The unsaved draft — NOT the last-saved data.sections body — is what renders.
    expect(getByDisplayValue('UNSAVED edit in progress')).toBeTruthy();
    // Already in edit mode: the "Edit" entry button should not appear (Save/Cancel do).
    expect(queryByText('Edit')).toBeNull();
    expect(queryByText('Save')).toBeTruthy();
    expect(queryByText('Cancel')).toBeTruthy();
  });

  it('reports a live draft patch via onDataChange on every section edit', () => {
    const onDataChange = jest.fn();
    const { getByText, getByDisplayValue } = renderWithTheme(
      <AnalysisEditorWidget {...editableProps} onDataChange={onDataChange} />,
      webLightTheme
    );

    fireEvent.click(getByText('Edit'));
    expect(onDataChange).toHaveBeenLastCalledWith({
      isEditing: true,
      draftSections: [
        { heading: 'Executive Summary', body: 'Original summary body.' },
        { heading: 'Key Risks', body: 'Original risks body.' },
      ],
    });

    fireEvent.change(getByDisplayValue('Original summary body.'), {
      target: { value: 'Edited summary body.' },
    });

    expect(onDataChange).toHaveBeenLastCalledWith({
      isEditing: true,
      draftSections: [
        { heading: 'Executive Summary', body: 'Edited summary body.' },
        { heading: 'Key Risks', body: 'Original risks body.' },
      ],
    });
  });

  it('clears the persisted draft via onDataChange on Save (no stale draft haunts a future restore)', () => {
    const onDataChange = jest.fn();
    const onSave = jest.fn();
    const { getByText } = renderWithTheme(
      <AnalysisEditorWidget {...editableProps} onDataChange={onDataChange} onSave={onSave} />,
      webLightTheme
    );

    fireEvent.click(getByText('Edit'));
    fireEvent.click(getByText('Save'));

    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onDataChange).toHaveBeenLastCalledWith({ isEditing: false, draftSections: undefined });
  });

  it('clears the persisted draft via onDataChange on Cancel (discards the in-progress edit)', () => {
    const onDataChange = jest.fn();
    const { getByText } = renderWithTheme(
      <AnalysisEditorWidget {...editableProps} onDataChange={onDataChange} />,
      webLightTheme
    );

    fireEvent.click(getByText('Edit'));
    fireEvent.click(getByText('Cancel'));

    expect(onDataChange).toHaveBeenLastCalledWith({ isEditing: false, draftSections: undefined });
    // Back to read-only view of the ORIGINAL (unedited) data.
    expect(getByText('Original summary body.')).toBeTruthy();
  });

  it('does not throw when onDataChange is omitted (isolated render context)', () => {
    const { getByText } = renderWithTheme(<AnalysisEditorWidget {...editableProps} />, webLightTheme);

    expect(() => {
      fireEvent.click(getByText('Edit'));
      fireEvent.click(getByText('Save'));
    }).not.toThrow();
  });
});
