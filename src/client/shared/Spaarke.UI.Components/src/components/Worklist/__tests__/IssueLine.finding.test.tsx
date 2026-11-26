/**
 * IssueLine layout (owner decision D-227): the finding on line 1, the subject's name on line 2, timing and chevron on the right.
 *  - a row shows the finding, then the subject
 *  - a row whose finding fell back to the subject (no rule short name, no title) shows ONE line, no duplicate
 *  - the rule short name is not shown separately when it is the finding
 *  - the whole line is still one native button with nothing interactive inside
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { MatterCard } from '..';
import type { WorklistItem } from '..';
import { TODAY, matterCore, pathBItem } from '../__fixtures__/worklistFixtures';

const renderCard = (item: WorklistItem) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <MatterCard core={matterCore} items={[item]} today={TODAY} />
    </FluentProvider>
  );

const occurrences = (text: string | null, part: string) => (text ?? '').split(part).length - 1;

describe('IssueLine: finding first, then the subject', () => {
  it('shows the finding on line 1 and the subject name on line 2', () => {
    renderCard(pathBItem);
    const finding = screen.getByTestId('issue-finding');
    const subject = screen.getByTestId('issue-subject');
    expect(finding).toHaveTextContent('Fee or scope change');
    expect(subject).toHaveTextContent('Invoice 4411 from Acme LLP');
    expect(finding.compareDocumentPosition(subject) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // The rule short name is the finding, so it is not shown a second time as separate rule text.
    expect(occurrences(screen.getByTestId('issue-line').textContent, 'Fee or scope change')).toBe(1);
  });

  it('shows one line, no duplicate, when there is no rule short name and no title', () => {
    renderCard({
      signalId: 's1',
      lane: 'Decide',
      subjectName: 'Invoice 4411 from Acme LLP',
      raisedOn: pathBItem.raisedOn,
    });
    expect(screen.getByTestId('issue-finding')).toHaveTextContent('Invoice 4411 from Acme LLP');
    expect(screen.queryByTestId('issue-subject')).toBeNull();
    expect(occurrences(screen.getByTestId('issue-line').textContent, 'Invoice 4411 from Acme LLP')).toBe(1);
  });

  it('leads with the stored title, then the subject, when the rule has no short name', () => {
    renderCard({ ...pathBItem, ruleShortName: null });
    expect(screen.getByTestId('issue-finding')).toHaveTextContent('Fee or scope change with no budget revision');
    expect(screen.getByTestId('issue-subject')).toHaveTextContent('Invoice 4411 from Acme LLP');
  });

  it('keeps the timing on the right and the whole line one native button with nothing interactive inside', () => {
    renderCard(pathBItem);
    const line = screen.getByTestId('issue-line');
    expect(line.tagName).toBe('BUTTON');
    expect(line.querySelectorAll('button, a, input, select, textarea, [role="button"], [role="link"]')).toHaveLength(0);
    expect(screen.getByTestId('issue-timing')).toHaveTextContent('3d');
  });
});
