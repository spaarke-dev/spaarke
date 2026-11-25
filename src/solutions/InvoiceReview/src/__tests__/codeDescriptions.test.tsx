import '@testing-library/jest-dom';
import { render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { LineReviewPane } from '../components/LineReviewPane';
import type { ReviewCode, ReviewLine } from '../services/invoiceReviewApi';
import { line } from './testKit';

// Owner review 2026-11-25: the pane shows a UTBMS code with its description ("L190 · Pleadings"), the bare code when there is none.

function renderPane(l: ReviewLine) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <LineReviewPane
        line={l}
        invoiceFindings={[]}
        canWrite={false}
        busy={false}
        corrections={[]}
        toDecideCount={0}
        onStart={jest.fn()}
        onDecide={jest.fn()}
        onCorrect={jest.fn()}
        onConfirm={jest.fn()}
        onShowField={jest.fn()}
      />
    </FluentProvider>
  );
}

const withCodes = (task: Partial<ReviewCode>, activity: Partial<ReviewCode> = {}): ReviewLine => {
  const base = line(1);
  return {
    ...base,
    codes: {
      ...base.codes,
      task: { ...base.codes.task, ...task },
      activity: { ...base.codes.activity, ...activity },
    },
  };
};

describe('LineReviewPane: UTBMS code descriptions', () => {
  it('must-fire: shows "code · description" for a code that has a description', () => {
    renderPane(withCodes({ code: 'L190', description: 'Pleadings' }, { code: 'A101', description: 'Plan and prepare for' }));

    expect(within(screen.getByTestId('read-TaskCode')).getByText('L190 · Pleadings')).toBeInTheDocument();
    expect(within(screen.getByTestId('read-ActivityCode')).getByText('A101 · Plan and prepare for')).toBeInTheDocument();
  });

  it('must-not-fire: shows the bare code when the description is null or absent', () => {
    renderPane(withCodes({ code: 'L190', description: null }, { code: 'A101' }));

    const task = screen.getByTestId('read-TaskCode');
    expect(within(task).getByText('L190')).toBeInTheDocument();
    expect(within(task).queryByText(/^L190 ·/)).not.toBeInTheDocument();
    expect(within(screen.getByTestId('read-ActivityCode')).getByText('A101')).toBeInTheDocument();
  });

  it("shows the firm's code with its description only when it differs from the AI's code", () => {
    const { unmount } = renderPane(
      withCodes({ code: 'L190', description: 'Pleadings', firmCode: 'L110', firmDescription: 'Fact Investigation', mismatch: true })
    );
    expect(screen.getByTestId('firm-TaskCode')).toHaveTextContent('Firm billed: L110 · Fact Investigation');
    unmount();

    renderPane(withCodes({ code: 'L190', description: 'Pleadings', firmCode: 'L190', firmDescription: 'Pleadings', mismatch: false }));
    expect(screen.queryByTestId('firm-TaskCode')).not.toBeInTheDocument();
  });
});
