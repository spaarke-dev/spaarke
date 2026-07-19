/**
 * useMyAssistant.ts — open-state, cold-start gate, and save/erase orchestration for the My Assistant
 * questionnaire (task 042, FR-F3 / F5). Keeps `ConversationPane` diff-minimal: the pane only renders
 * `<MyAssistantDialog {...dialogProps} />` and wires `onMyAssistant={openDialog}`.
 *
 * COLD-START GATE (FR-F3): on mount (when a Dataverse user id is resolvable) the hook reads the profile
 * once; if `sprk_profilecompletedon` is unset it flags cold-start and auto-opens the questionnaire a
 * single time. Fully DEFENSIVE — when no Xrm/user id is available (jsdom tests, non-MDA hosts) it does
 * nothing: no fetch, no auto-open. So it is inert in suites that don't opt in.
 *
 * MEMORY (ADR-042): on a successful profile SAVE the hook best-effort SEEDs ONE User-scope MemoryItem
 * (`source=user`) via `POST /api/memory/user/seed` (FR-F3 / D-042-01 un-deferral) so the stated profile
 * becomes the baseline the evolvable learned-memory store carries. The subject (systemuserid) is resolved
 * SERVER-side from the auth context — never sent from the client. The seed is best-effort: a failure NEVER
 * blocks the profile save (mirrors the erase's best-effort memory call). This is the ONLY user-initiated
 * memory-WRITE path — `memory.write` (task 057) is AI-initiated only. The ERASURE path honors F5 by calling
 * the EXISTING `DELETE /api/memory/user` governance endpoint (task 052).
 *
 * @see userProfileService.ts — the write/erase path
 * @see MyAssistantDialog.tsx — the questionnaire UI
 */

import * as React from 'react';
import { getCurrentUserId, getXrm } from '@spaarke/ui-components';
import {
  createDataverseUserProfilePort,
  saveMyAssistantProfile,
  eraseMyAssistantProfile,
  buildAssistantMemorySeed,
  isProfileComplete,
  type IUserProfilePort,
  type PracticeArea,
  type UserProfileRow,
  type UserProfileFormValues,
} from './userProfileService';

type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

export interface UseMyAssistantOptions {
  /** `@spaarke/auth` fetch — used ONLY for the F5 memory-erase call to the BFF. */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** BFF base URL — for the `DELETE /api/memory/user` erase call. */
  bffBaseUrl?: string;
  /** Injected port (tests). Defaults to the host Dataverse Web API port. */
  port?: IUserProfilePort;
  /** Injected user-id resolver (tests). Defaults to the Xrm host user id. */
  getUserId?: () => string | undefined;
  /** Injected display-name resolver (tests). Defaults to the Xrm host user name. */
  getDisplayName?: () => string | undefined;
  /** Gate the cold-start prefetch/auto-open (default true). */
  enabled?: boolean;
}

export interface UseMyAssistantResult {
  open: boolean;
  openDialog: () => void;
  closeDialog: () => void;
  coldStart: boolean;
  loading: boolean;
  practiceAreas: PracticeArea[];
  initialValues: Partial<UserProfileFormValues>;
  onSubmit: (values: UserProfileFormValues) => Promise<void>;
  onErase: () => Promise<void>;
  /** True once a Dataverse user id was resolvable and wiring is live (false in non-MDA/jsdom). */
  available: boolean;
}

function defaultDisplayName(): string | undefined {
  try {
    const xrm = getXrm();
    return xrm?.Utility?.getGlobalContext().userSettings.userName;
  } catch {
    return undefined;
  }
}

function rowToInitialValues(row: UserProfileRow | null): Partial<UserProfileFormValues> {
  if (!row) return {};
  return {
    primaryRole: row.primaryRole,
    practiceAreaIds: row.practiceAreaIds,
    focusAreas: row.focusAreas ?? '',
    officeLocation: row.officeLocation ?? '',
    assistantPreferences: row.assistantPreferences ?? '',
  };
}

/**
 * Owns the questionnaire's open-state + data. Returns handlers `MyAssistantDialog` consumes directly.
 */
export function useMyAssistant(options: UseMyAssistantOptions = {}): UseMyAssistantResult {
  const enabled = options.enabled ?? true;
  const systemUserId = React.useMemo(
    () => (options.getUserId ?? getCurrentUserId)(),
    [options.getUserId]
  );
  const available = enabled && !!systemUserId;

  const port = React.useMemo<IUserProfilePort>(
    () => options.port ?? createDataverseUserProfilePort(),
    [options.port]
  );

  const [open, setOpen] = React.useState(false);
  const [coldStart, setColdStart] = React.useState(false);
  const [loading, setLoading] = React.useState(false);
  const [practiceAreas, setPracticeAreas] = React.useState<PracticeArea[]>([]);
  const [existing, setExisting] = React.useState<UserProfileRow | null>(null);
  const autoOpenedRef = React.useRef(false);

  // Cold-start prefetch (once). Inert when no user id / disabled.
  React.useEffect(() => {
    if (!available || !systemUserId) return;
    let cancelled = false;
    setLoading(true);
    (async () => {
      // UAT 2026-07-18: settle each read INDEPENDENTLY. Previously a single Promise.all meant a
      // failure in the profile read (the lookup-alternate-key 400) also discarded the successfully
      // loaded practice areas → an empty dropdown. allSettled lets the practice-areas list populate
      // even if the profile read fails, and vice-versa.
      const [areasResult, profileResult] = await Promise.allSettled([
        port.listPracticeAreas(),
        port.getProfileByUser(systemUserId),
      ]);
      if (cancelled) return;
      if (areasResult.status === 'fulfilled') {
        setPracticeAreas(areasResult.value);
      } else {
        console.warn('[useMyAssistant] practice-area load failed:', areasResult.reason);
      }
      const profile = profileResult.status === 'fulfilled' ? profileResult.value : null;
      if (profileResult.status === 'rejected') {
        console.warn('[useMyAssistant] profile load failed:', profileResult.reason);
      }
      setExisting(profile);
      // Only auto-open the cold-start questionnaire when the profile read SUCCEEDED and is incomplete
      // (a failed read must not spuriously treat the user as brand-new).
      if (profileResult.status === 'fulfilled' && !isProfileComplete(profile)) {
        setColdStart(true);
        if (!autoOpenedRef.current) {
          autoOpenedRef.current = true;
          setOpen(true);
        }
      }
      setLoading(false);
    })();
    return () => {
      cancelled = true;
    };
  }, [available, systemUserId, port]);

  const openDialog = React.useCallback(() => setOpen(true), []);
  const closeDialog = React.useCallback(() => setOpen(false), []);

  const displayName = React.useMemo(
    () => (options.getDisplayName ?? defaultDisplayName)(),
    [options.getDisplayName]
  );

  const onSubmit = React.useCallback(
    async (values: UserProfileFormValues): Promise<void> => {
      if (!systemUserId) throw new Error('No Dataverse user context is available to save the profile.');
      await saveMyAssistantProfile(port, {
        systemUserId,
        displayName,
        form: values,
        existing,
      });

      // Best-effort User-scope memory SEED (FR-F3 / D-042-01): after the profile save succeeds, seed ONE
      // User-scope MemoryItem (source=user) so the stated profile becomes the learned-memory baseline. The
      // subject is resolved SERVER-side from the caller's auth context (never sent from here) — this IS a BFF
      // call (unlike the host-context Dataverse profile write above). A failure NEVER blocks the profile save.
      const { authenticatedFetch, bffBaseUrl } = options;
      const seed = buildAssistantMemorySeed(values);
      if (seed && authenticatedFetch && bffBaseUrl) {
        try {
          await authenticatedFetch(`${bffBaseUrl}/api/memory/user/seed`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(seed),
          });
          // Response status is not consulted — the seed is best-effort; the profile save already succeeded.
        } catch {
          // Swallow — never block the profile save on the memory seed.
        }
      }

      // Optimistically reflect completion so a subsequent open prefills + the cold-start gate clears.
      const completedRow: UserProfileRow = {
        id: existing?.id ?? '',
        profileCompletedOn: new Date().toISOString(),
        primaryRole: values.primaryRole,
        focusAreas: values.focusAreas || null,
        officeLocation: values.officeLocation || null,
        assistantPreferences: values.assistantPreferences || null,
        profileVersion: (existing?.profileVersion ?? 0) + 1,
        practiceAreaIds: values.practiceAreaIds,
      };
      setExisting(completedRow);
      setColdStart(false);
    },
    [port, systemUserId, displayName, existing, options]
  );

  const onErase = React.useCallback(async (): Promise<void> => {
    if (!systemUserId) throw new Error('No Dataverse user context is available to clear the profile.');
    const { authenticatedFetch, bffBaseUrl } = options;
    const eraseUserMemory =
      authenticatedFetch && bffBaseUrl
        ? async () => {
            const resp = await authenticatedFetch(`${bffBaseUrl}/api/memory/user`, { method: 'DELETE' });
            // 204 = erased; 404/others are tolerated (best-effort, caller wraps).
            if (!resp.ok && resp.status !== 404) {
              throw new Error(`memory erase failed: ${resp.status}`);
            }
          }
        : undefined;
    await eraseMyAssistantProfile(port, { systemUserId, eraseUserMemory });
    setExisting(null);
    setColdStart(true);
  }, [port, systemUserId, options]);

  const initialValues = React.useMemo(() => rowToInitialValues(existing), [existing]);

  return {
    open,
    openDialog,
    closeDialog,
    coldStart,
    loading,
    practiceAreas,
    initialValues,
    onSubmit,
    onErase,
    available,
  };
}
