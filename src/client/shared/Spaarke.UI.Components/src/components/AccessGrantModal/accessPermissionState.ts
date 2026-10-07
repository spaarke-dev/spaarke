/**
 * The host's mapping from a record's raw Access Permission + Secure flag to the
 * modal's semantic {@link AccessPermissionState} (unified-access-control-r2 task
 * 138; owner round 2 item 3, binding).
 *
 * A pure function so the fail-closed rules are pinned by tests. It stays
 * entity-agnostic (ADR-012 / FR-14): the HOST supplies its own option integers
 * through {@link IAccessPermissionValues} and reads its own secure column — the
 * shared library never learns a Dataverse value or column name.
 */

import type { AccessPermissionState } from './types';

/** The host's raw Access Permission option values (Standard is "anything else"). */
export interface IAccessPermissionValues {
  limited: number;
  restricted: number;
}

/**
 * Resolves the semantic state, failing CLOSED:
 * - Restricted → `'restricted'`, regardless of the secure flag (Restricted wins).
 * - Limited → `'limited'`.
 * - Standard or unset, on a SECURE record → `'limited'` (Secure implies Limited
 *   for contacts: only named, direct grants).
 * - Standard or unset, when the secure flag could NOT be read (`null` /
 *   `undefined` — a failed read, a read still in flight, or a value hidden by
 *   field-level security) → `'limited'`. An unknown flag must never widen what
 *   the dialog offers.
 * - Standard or unset, on a record READ as not secure → `'standard'`.
 *
 * @param value      The record's raw Access Permission value (`null` = unset = Standard).
 * @param isSecure   The record's Secure flag: `true`, `false`, or `null`/`undefined` when unreadable.
 * @param values     The host's raw option values for Limited and Restricted.
 */
export function resolveAccessPermissionState(
  value: number | null | undefined,
  isSecure: boolean | null | undefined,
  values: IAccessPermissionValues
): AccessPermissionState {
  if (value === values.restricted) return 'restricted';
  if (value === values.limited) return 'limited';
  return isSecure === false ? 'standard' : 'limited';
}
