/**
 * StageTransitionRules.ts — Pure stage-determination logic for the four-stage
 * pane lifecycle in the SpaarkeAi three-pane shell.
 *
 * This module is intentionally side-effect free and React-independent so it can
 * be unit-tested without a DOM or React environment. It is consumed by
 * ShellStageManager in ThreePaneShell.tsx, which passes the current session state
 * and receives the derived ShellStage back.
 *
 * Stage definitions (matches design.md Section 2.3):
 *
 *   'welcome'       Stage 1 — Landing: no active session or playbook.
 *                   Conversation: welcome message + prompt buttons.
 *                   Workspace:    "What would you like to work on?" + recent work.
 *                   Context:      Playbook gallery.
 *
 *   'loading'       Stage 2 — Playbook Selected: playbook chosen, gathering context.
 *                   Conversation: chat initialized with chosen agent, awaiting entity.
 *                   Workspace:    document/entity selection (Upload / Browse).
 *                   Context:      entity info widget or loading spinner.
 *
 *   'active-chat'   Stage 3 — Active Work: first document/widget loaded, all panes live.
 *                   Conversation: SprkChat with ongoing exchange.
 *                   Workspace:    single active widget (document viewer, report, etc.).
 *                   Context:      findings, citations, sources.
 *
 *   'review'        Stage 4 — Multi-Task: second workspace tab opened, tab bar visible.
 *                   Conversation: chat stays stable.
 *                   Workspace:    tabbed widget view (WorkspaceTabManagerComponent).
 *                   Context:      adapts to the active workspace tab via tab_change events.
 *
 * Transition rules (from design.md Section 2.3 + task AIPU2-105):
 *
 *   welcome → loading      playbook selected OR first message sent
 *   loading → active-chat  first widget loaded in workspace OR entity context resolved
 *   active-chat → review   second workspace tab opened (tabCount >= 2)
 *   review → active-chat   all but one workspace tab closed (tabCount === 1)
 *   any → welcome          session cleared / deleted (hasSession === false,
 *                          hasWidget === false, hasEntity === false)
 *
 * @see ThreePaneShell — ShellStageManager wires this into React state
 * @see design.md Section 2.3 — authoritative stage diagrams
 */

// ---------------------------------------------------------------------------
// ShellStage — the four lifecycle stages
// ---------------------------------------------------------------------------

/**
 * The four lifecycle stages of the SpaarkeAi three-pane shell.
 *
 * Named to match existing ShellStage values in ThreePaneShell.tsx so the
 * context value type remains unchanged. Consumers reading currentStage from
 * ShellStageContext receive one of these four strings.
 */
export type PaneStage = 'welcome' | 'loading' | 'active-chat' | 'review';

// ---------------------------------------------------------------------------
// SessionState — input to determineStage()
// ---------------------------------------------------------------------------

/**
 * Snapshot of session state used to compute the current PaneStage.
 *
 * All fields are required so callers are explicit about the current state;
 * no field is inferred or defaulted. This prevents subtle bugs where an
 * undefined hasWidget is treated as false (which it would be, but explicitly).
 */
export interface SessionState {
  /**
   * True when an AI chat session is active (chatSessionId is non-null) OR a
   * playbook has been selected (playbookId is non-null). Either condition
   * advances beyond the welcome stage.
   */
  hasSession: boolean;

  /**
   * True when at least one workspace widget has finished loading (i.e. the
   * workspace tab list has at least one resolved tab). This advances from
   * 'loading' to 'active-chat'.
   */
  hasWidget: boolean;

  /**
   * Number of open workspace tabs. When this reaches 2 the shell advances to
   * 'review' (multi-task). When it drops back to 1 the shell reverts to
   * 'active-chat'.
   */
  tabCount: number;

  /**
   * True when an entity context has been resolved (entityLogicalName +
   * entityId are both present). Entity context resolving is an alternative
   * trigger to widget loading for the loading → active-chat transition.
   */
  hasEntity: boolean;
}

// ---------------------------------------------------------------------------
// determineStage — pure transition function
// ---------------------------------------------------------------------------

/**
 * Compute the correct PaneStage from the current session state.
 *
 * This function is pure: same inputs always produce the same output. There are
 * no side effects, no React calls, no global state reads.
 *
 * Priority order (checked top to bottom, first match wins):
 *   1. No session → 'welcome'
 *   2. tabCount >= 2 → 'review'
 *   3. hasWidget || hasEntity → 'active-chat'
 *   4. hasSession (but no widget yet) → 'loading'
 *   5. Fallback → 'welcome' (should not be reached given the guards above)
 *
 * @param state - Current session/workspace state snapshot.
 * @returns The PaneStage that should be displayed.
 *
 * @example
 * // Landing — no session
 * determineStage({ hasSession: false, hasWidget: false, tabCount: 0, hasEntity: false })
 * // → 'welcome'
 *
 * @example
 * // Playbook selected, no widget yet
 * determineStage({ hasSession: true, hasWidget: false, tabCount: 0, hasEntity: false })
 * // → 'loading'
 *
 * @example
 * // First document loaded → active work
 * determineStage({ hasSession: true, hasWidget: true, tabCount: 1, hasEntity: false })
 * // → 'active-chat'
 *
 * @example
 * // Second tab opened → multi-task
 * determineStage({ hasSession: true, hasWidget: true, tabCount: 2, hasEntity: false })
 * // → 'review'
 *
 * @example
 * // Entity context resolved without explicit widget (e.g. entity launch)
 * determineStage({ hasSession: true, hasWidget: false, tabCount: 0, hasEntity: true })
 * // → 'active-chat'
 */
export function determineStage(state: SessionState): PaneStage {
  const { hasSession, hasWidget, tabCount, hasEntity } = state;

  // Stage 1: no active session → always welcome regardless of other flags.
  if (!hasSession) {
    return 'welcome';
  }

  // Stage 4: two or more workspace tabs → multi-task view.
  // Checked before widget guard so that multi-tab always wins over active-chat.
  if (tabCount >= 2) {
    return 'review';
  }

  // Stage 3: at least one widget loaded, or entity context resolved.
  // Either condition means the user has a document / entity to work with.
  if (hasWidget || hasEntity) {
    return 'active-chat';
  }

  // Stage 2: session exists (playbook chosen / message sent) but no content yet.
  return 'loading';
}

// ---------------------------------------------------------------------------
// shouldReset — convenience predicate for the "any → welcome" transition
// ---------------------------------------------------------------------------

/**
 * Returns true when the session state represents a full reset back to the
 * welcome stage, regardless of any previous stage.
 *
 * Use this in event handlers that process "session cleared" or "new session"
 * events to short-circuit the normal determineStage() computation.
 *
 * @param state - Current session/workspace state snapshot.
 */
export function shouldReset(state: SessionState): boolean {
  return !state.hasSession && !state.hasWidget && state.tabCount === 0 && !state.hasEntity;
}
