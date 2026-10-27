/**
 * DecisionItemWizard - ONE item in the decision wizard: a `WizardShell` (modal mode, inside `SprkModal`) whose steps are built
 * from the plan: What was found, one step per action in plan order, Next steps, Confirm. The consumer is thin on purpose:
 * the engine is WizardShell, the Next-step cards are FollowOnGrid, and every behaviour here is data-driven (a step per plan
 * action, an exclusion read from the plan's `excludes`), never a switch on a rule, lane or shape.
 *
 * How the stock engine is used (no second engine, no wrapper Dialog):
 *  - **Next takes, Skip leaves out.** The shell's own Skip marks a step 'skipped' (the dashed ring, opt-in `showSkippedSteps`,
 *    D-69) and Next marks it 'completed'. `ShellSync` reads those statuses and records the decision in the draft.
 *  - **Excludes remove later steps.** Taking an action removes the steps it excludes (`removeDynamicStep`); un-taking brings
 *    them back at their plan position (`addDynamicStep` with one canonical order covering every step).
 *  - **Dynamic steps keep no stale closures.** A step added dynamically keeps the config it was added with, so step contents
 *    are components that read the draft from context and `canAdvance` predicates read through refs.
 *  - **Nothing is written here.** Confirm's primary button calls `onRecord`, which posts the one review (task 043).
 *
 * A closed item (opened closed, or just recorded) is read-only: the same steps show what was sent, the footer is
 * Back / Next / Next open item, and the status bar sits above every step.
 */

import * as React from 'react';
import { Button } from '@fluentui/react-components';
import { AddCircleRegular } from '@fluentui/react-icons';
import { WizardShell } from '../Wizard/WizardShell';
import type { IWizardShellHandle, IWizardStepConfig } from '../Wizard/wizardShellTypes';
import type { SprkModalNav } from '../SprkModal/SprkModal.types';
import type { FollowOnCardConfig } from '../WizardFollowOns/followOnTypes';
import { followOnStepId } from '../WizardFollowOns/followOnTypes';
import { StatusBar } from '../ConsoleKit/StatusBar';
import { resolveConsoleState } from '../ConsoleKit/consoleStatus';
import { composeIssueLine } from '../Worklist/composeIssueLine';
import {
  actionScope,
  clearDecision,
  isDismissal,
  nextScope,
  reviewReady,
  stepValid,
  visibleActions,
  withDecision,
} from './decisionDraft';
import type { Decision, ItemDraft, StepScope } from './decisionDraft';
import { DecisionItemContext } from './decisionItemContext';
import type { DecisionItemContextValue, DecisionLookup, DecisionOpenLookupForItem } from './decisionItemContext';
import type {
  DecisionCommitFailure,
  DecisionCommitResult,
  DecisionOpenRecord,
  DecisionPlan,
  DecisionResolution,
  DecisionWizardItem,
  SuppressionStatus,
} from './decisionWizardTypes';
import { ActionStepView } from './steps/ActionStepView';
import { ConfirmStep } from './steps/ConfirmStep';
import { FoundStep } from './steps/FoundStep';
import { NextStepsStep } from './steps/NextStepsStep';

const FOUND = 'found';
const NEXT = 'next';
const CONFIRM = 'confirm';
const ACTION_PREFIX = 'action:';
const FOLLOW_ON_PREFIX = 'followon-';

const actionStepId = (code: string): string => `${ACTION_PREFIX}${code}`;

/** The scope a step's decision is kept under, or null for a step that holds no decision. */
function scopeOfStep(stepId: string): StepScope | null {
  if (stepId.startsWith(ACTION_PREFIX)) return actionScope(stepId.slice(ACTION_PREFIX.length));
  if (stepId.startsWith(FOLLOW_ON_PREFIX)) return nextScope(stepId.slice(FOLLOW_ON_PREFIX.length));
  return null;
}

/**
 * A closed item that a person decided has a record to replay; one that closed itself (superseded, cleared) does not. A decided
 * item whose record was not supplied (`steps` absent: the worklist's closed sets, task 059, come from the Signal read route, which
 * does not return the record) has nothing to replay either, and replaying it would show every action as Skipped.
 */
export function hasRecordedDecision(resolution: DecisionResolution | null | undefined): boolean {
  return (
    (resolution?.resolutionType === 'Acted' || resolution?.resolutionType === 'Dismissed') && resolution.steps != null
  );
}

export interface DecisionItemWizardProps {
  item: DecisionWizardItem;
  plan: DecisionPlan;
  suppression: SuppressionStatus | null;
  suppressionFailed: boolean;
  draft: ItemDraft;
  onDraftChange: (change: (draft: ItemDraft) => ItemDraft) => void;
  /** Present when the item is closed: opened closed, or just recorded (then `justRecorded`). */
  resolution: DecisionResolution | null;
  justRecorded: boolean;
  failure: DecisionCommitFailure | null;
  /**
   * The Failed (or unanswered) attempt that pins the review, while there is one: its id may not change and the route has seen
   * its content, so the steps are read-only until the attempt is repeated or the wizard is closed. `resumable` of THIS failure,
   * not of the latest answer, decides whether the next try resumes it.
   */
  pinnedFailure: DecisionCommitFailure | null;
  /** Items the wizard knows are closed (recorded here, resolved by Also resolve, or opened closed). */
  closedIds: ReadonlySet<string>;
  /** The route's 200 answer for the item just recorded, for its warnings and per-item states. */
  recordResult: DecisionCommitResult | null;
  /** True while the request is in flight. */
  recording: boolean;
  /** Post the one review. Resolves when the attempt is over (recorded or failed); never rejects. */
  onRecord: () => Promise<void>;
  lookup: DecisionLookup | null;
  /** The host's side-pane lookup with the item's core already filled in (D-182); null keeps the inline type-ahead. */
  openLookup?: DecisionOpenLookupForItem | null;
  /** The host's open-record-in-a-modal (D-182); absent: the subject is plain text. */
  openRecord?: DecisionOpenRecord | null;
  /** Other open items left in the browse list after this one closed; null when unknown. */
  openItemsLeft: number | null;
  /** Go to the next open item; undefined when there is none. */
  onNextOpenItem?: () => void;
  nav?: SprkModalNav;
  /** Close the wizard (the host runs its discard check first). */
  onClose: () => void;
  uiScale?: number;
  today: Date;
}

interface SyncPlan {
  readOnly: boolean;
  desiredKey: string;
  desiredIds: string[];
  canonicalOrder: string[];
  makeConfig: (stepId: string) => IWizardStepConfig | null;
  update: (change: (draft: ItemDraft) => ItemDraft) => void;
}

/**
 * Keeps the shell's step list equal to the desired list, and records Next / Skip as taken / skipped. Rendered inside every
 * step's content; it renders nothing. All state comes from a ref because a dynamic step's closure is as old as the click that
 * added it.
 */
const ShellSync: React.FC<{ handle: IWizardShellHandle; syncRef: React.MutableRefObject<SyncPlan> }> = ({
  handle,
  syncRef,
}) => {
  const key = syncRef.current.desiredKey;
  const steps = handle.state.steps;

  // Next takes, Skip leaves out: read the status of every step the user has left.
  React.useEffect(() => {
    const sync = syncRef.current;
    if (sync.readOnly) return;
    const changes: Array<[StepScope, Decision]> = [];
    for (const step of steps) {
      const scope = scopeOfStep(step.id);
      if (!scope) continue;
      if (step.status === 'completed') changes.push([scope, 'taken']);
      else if (step.status === 'skipped') changes.push([scope, 'skipped']);
    }
    if (changes.length > 0)
      sync.update(d => changes.reduce((acc, [scope, decision]) => withDecision(acc, scope, decision), d));
  }, [steps, syncRef]);

  // Excludes and Next-step cards: add and remove steps until the shell matches the plan of this review.
  React.useEffect(() => {
    const sync = syncRef.current;
    const have = new Set(handle.state.steps.map(s => s.id));
    const current = handle.state.steps[handle.state.currentStepIndex]?.id;
    const want = new Set(sync.desiredIds);
    // Only steps this component manages are ever removed.
    const remove = [...have].filter(
      id => !want.has(id) && (id.startsWith(ACTION_PREFIX) || id.startsWith(FOLLOW_ON_PREFIX) || id === NEXT)
    );
    // The steps that are not being viewed go first: one action can exclude several later steps, and removing the step being
    // viewed steps the shell back, so it must be the last removal.
    for (const id of remove.filter(x => x !== current)) handle.removeDynamicStep(id);
    if (current && remove.includes(current)) {
      // Next on an action that excludes the step after it has already moved the shell onto that step. Removing the step being
      // viewed steps the shell back one, so go forward again, onto what follows it.
      handle.removeDynamicStep(current);
      handle.nextStep();
    }
    // A step an exclusion removed has not been decided (it will be asked again if the exclusion lifts).
    if (!sync.readOnly) {
      for (const id of remove) {
        const scope = scopeOfStep(id);
        if (scope) sync.update(d => clearDecision(d, scope));
      }
    }
    for (const id of sync.desiredIds) {
      if (have.has(id)) continue;
      const config = sync.makeConfig(id);
      if (config) handle.addDynamicStep(config, sync.canonicalOrder);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `key` stands for the desired list; `steps` for the shell's list
  }, [key, steps]);

  return null;
};

export const DecisionItemWizard: React.FC<DecisionItemWizardProps> = ({
  item,
  plan,
  suppression,
  suppressionFailed,
  draft,
  onDraftChange,
  resolution,
  justRecorded,
  failure,
  pinnedFailure,
  closedIds,
  recordResult,
  recording,
  onRecord,
  lookup,
  openLookup = null,
  openRecord = null,
  openItemsLeft,
  onNextOpenItem,
  nav,
  onClose,
  uiScale,
  today,
}) => {
  const readOnly = resolution != null;
  const locked = pinnedFailure != null && !readOnly;
  const replayable = readOnly && hasRecordedDecision(resolution);

  // Latest values for predicates and effects that outlive a render (dynamic steps keep their first closure).
  const latest = React.useRef({ plan, draft, resolution, recording, failure, pinnedFailure });
  latest.current = { plan, draft, resolution, recording, failure, pinnedFailure };

  const onDraftRef = React.useRef(onDraftChange);
  onDraftRef.current = onDraftChange;
  const update = React.useCallback((change: (d: ItemDraft) => ItemDraft) => onDraftRef.current(change), []);

  const shellRef = React.useRef<IWizardShellHandle>(null);
  const syncRef = React.useRef<SyncPlan>(null as unknown as SyncPlan);

  // ---- The step list -------------------------------------------------------------------------------
  const nextSteps = plan.nextSteps;
  const followOnCount = resolution?.followOns?.length ?? 0;
  const showNext = readOnly ? followOnCount > 0 : nextSteps.length > 0;

  const canonicalOrder = React.useMemo(
    () => [
      FOUND,
      ...plan.actions.map(a => actionStepId(a.code)),
      NEXT,
      ...plan.nextSteps.map(n => followOnStepId(n.code)),
      CONFIRM,
    ],
    [plan]
  );

  const desiredIds: string[] = [FOUND];
  if (!readOnly || replayable) {
    const actions = readOnly ? plan.actions : visibleActions(plan, draft);
    for (const a of actions) desiredIds.push(actionStepId(a.code));
    if (showNext) desiredIds.push(NEXT);
    if (!readOnly) for (const code of draft.nextSelected) desiredIds.push(followOnStepId(code));
    desiredIds.push(CONFIRM);
  }
  const lastStepId = desiredIds[desiredIds.length - 1];

  const cards: FollowOnCardConfig[] = React.useMemo(
    () =>
      plan.nextSteps.map(n => ({
        id: n.code,
        label: n.label,
        icon: <AddCircleRegular />,
        description: n.effectLines[0] ?? '',
        renderStep: (handle: IWizardShellHandle) => (
          <>
            <ShellSync handle={handle} syncRef={syncRef} />
            <ActionStepView kind="next" code={n.code} />
          </>
        ),
        canAdvance: () => stepValid(n, latest.current.draft, nextScope(n.code)),
        isSkippable: true,
      })),
    [plan.nextSteps]
  );

  const makeConfig = React.useCallback(
    (stepId: string): IWizardStepConfig | null => {
      if (stepId === FOUND) {
        return {
          id: FOUND,
          label: 'What was found',
          canAdvance: () => true,
          renderContent: handle => (
            <>
              <ShellSync handle={handle} syncRef={syncRef} />
              <FoundStep />
            </>
          ),
        };
      }
      if (stepId === NEXT) {
        return {
          id: NEXT,
          label: 'Next steps',
          canAdvance: () => true,
          renderContent: handle => (
            <>
              <ShellSync handle={handle} syncRef={syncRef} />
              <NextStepsStep handle={handle} cards={cards} canonicalOrder={canonicalOrder} />
            </>
          ),
        };
      }
      if (stepId === CONFIRM) {
        return {
          id: CONFIRM,
          label: 'Confirm',
          canAdvance: () => {
            const l = latest.current;
            if (l.resolution || l.recording) return false;
            // A message went out and the first attempt cannot be resumed: the owner decides, not another click (task 043).
            const pinned = l.pinnedFailure ?? l.failure;
            if (pinned?.sentButNotRecorded && !pinned.resumable) return false;
            return reviewReady(l.plan, l.draft);
          },
          renderContent: handle => (
            <>
              <ShellSync handle={handle} syncRef={syncRef} />
              <ConfirmStep />
            </>
          ),
        };
      }
      if (stepId.startsWith(ACTION_PREFIX)) {
        const code = stepId.slice(ACTION_PREFIX.length);
        const action = plan.actions.find(a => a.code === code);
        if (!action) return null;
        return {
          id: stepId,
          label: action.label,
          isSkippable: true,
          canAdvance: () =>
            latest.current.resolution ? true : stepValid(action, latest.current.draft, actionScope(code)),
          renderContent: handle => (
            <>
              <ShellSync handle={handle} syncRef={syncRef} />
              <ActionStepView kind="action" code={code} />
            </>
          ),
        };
      }
      if (stepId.startsWith(FOLLOW_ON_PREFIX)) {
        const card = cards.find(c => followOnStepId(c.id) === stepId);
        return card
          ? {
              id: stepId,
              label: card.stepLabel ?? card.label,
              canAdvance: card.canAdvance ?? (() => true),
              isSkippable: card.isSkippable,
              renderContent: card.renderStep,
            }
          : null;
      }
      return null;
    },
    [plan, cards, canonicalOrder]
  );

  syncRef.current = {
    readOnly,
    desiredKey: desiredIds.join('|'),
    desiredIds,
    canonicalOrder,
    makeConfig,
    update,
  };

  // The base steps of the shell: every step this item can have. The shell builds its list from these once, on mount; the
  // reconciler in `ShellSync` then removes what a draft excludes.
  const baseIds = React.useMemo(() => {
    const ids = [FOUND];
    if (!readOnly || replayable) {
      for (const a of plan.actions) ids.push(actionStepId(a.code));
      if (showNext) ids.push(NEXT);
      ids.push(CONFIRM);
    }
    return ids;
    // eslint-disable-next-line react-hooks/exhaustive-deps -- base steps are fixed for the life of this mounted item
  }, []);
  const stepConfigs = baseIds.map(id => makeConfig(id)).filter((c): c is IWizardStepConfig => c !== null);

  // ---- Focus moves to the status bar when an item is recorded in place ---------------------------------
  const statusBarRef = React.useRef<HTMLDivElement>(null);
  React.useEffect(() => {
    if (justRecorded) statusBarRef.current?.focus();
  }, [justRecorded]);

  const line = composeIssueLine(item.row, today);
  const dismissal = isDismissal(plan, draft);

  const statusBar = resolution ? (
    <StatusBar
      ref={statusBarRef}
      state={resolution.leftOpen ? 'Open' : resolveConsoleState(resolution.resolutionType, resolution.recordClass)}
      note={resolution.leftOpen ? (resolution.note ?? 'Inquiry sent, still open') : undefined}
      recordId={resolution.recordNumber ?? undefined}
      recordClass={resolution.recordClass ?? undefined}
      decidedBy={resolution.decidedByName}
      decidedOn={resolution.decidedOn}
      recorded={justRecorded}
      reason={resolution.reason}
      supersededBy={resolution.supersededBy}
      openItemsLeft={justRecorded ? openItemsLeft : null}
    />
  ) : undefined;

  const contextValue: DecisionItemContextValue = {
    item,
    plan,
    suppression,
    suppressionFailed,
    draft,
    update,
    resolution,
    justRecorded,
    failure,
    locked,
    closedIds,
    recordResult,
    lookup,
    openLookup,
    openRecord,
    openItemsLeft,
    today,
  };

  const baseLabel = dismissal ? 'Dismiss and record' : 'Record decision';
  const finishLabel = (pinnedFailure ?? failure)?.resumable
    ? 'Retry recording'
    : failure?.httpStatus === 0
      ? 'Try again'
      : baseLabel;

  // The in-flight / after-success guard (#1386 K3): the shell leaves Finish enabled with stayOpenOnFinish and no success
  // screen, so the request itself is single-flight (parent) and the button is disabled by `canAdvance` while it runs and
  // once the item is closed.
  const onFinish = React.useCallback(async () => {
    await onRecord();
  }, [onRecord]);

  return (
    <DecisionItemContext.Provider value={contextValue}>
      <WizardShell
        ref={shellRef}
        open
        title={line.headline}
        steps={stepConfigs}
        initialStepId={readOnly ? lastStepId : undefined}
        onClose={onClose}
        onFinish={onFinish}
        finishLabel={finishLabel}
        finishingLabel="Recording..."
        stayOpenOnFinish
        showSkippedSteps
        dismiss="explicit"
        uiScale={uiScale}
        nav={nav}
        statusBar={statusBar}
        footer={
          readOnly
            ? context => ({
                start: (
                  <Button appearance="secondary" onClick={context.close}>
                    Close
                  </Button>
                ),
                end: (
                  <>
                    {!context.isFirstStep ? (
                      <Button appearance="secondary" onClick={context.goBack}>
                        Back
                      </Button>
                    ) : null}
                    {!context.isLastStep ? (
                      <Button appearance="secondary" onClick={context.goNext}>
                        Next
                      </Button>
                    ) : null}
                    {onNextOpenItem ? (
                      <Button appearance="primary" onClick={onNextOpenItem}>
                        Next open item
                      </Button>
                    ) : null}
                  </>
                ),
              })
            : undefined
        }
      />
    </DecisionItemContext.Provider>
  );
};
