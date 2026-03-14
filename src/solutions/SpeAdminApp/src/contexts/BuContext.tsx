import * as React from "react";
import type { BusinessUnit, SpeContainerTypeConfig, SpeEnvironment } from "../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// localStorage Keys
// ─────────────────────────────────────────────────────────────────────────────

const LS_KEY_BU_ID = "speadmin_selectedBuId";
const LS_KEY_CONFIG_ID = "speadmin_selectedConfigId";
const LS_KEY_ENV_ID = "speadmin_selectedEnvironmentId";

// ─────────────────────────────────────────────────────────────────────────────
// Context Shape
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Shape of the BuContext value provided to the component tree.
 *
 * The context is the primary state driver for BU/config/environment selection
 * across the SPE Admin App (spec.md FR-01).
 */
export interface BuContextValue {
  // ── Selected entities ──────────────────────────────────────────────────────

  /** Currently selected Dataverse Business Unit, or null if none selected. */
  selectedBu: BusinessUnit | null;

  /** Currently selected Container Type Config (scoped to selectedBu), or null. */
  selectedConfig: SpeContainerTypeConfig | null;

  /** Currently selected SPE Environment (linked to selectedConfig), or null. */
  selectedEnvironment: SpeEnvironment | null;

  // ── Setters ────────────────────────────────────────────────────────────────

  /**
   * Select a Business Unit. Clears selectedConfig and selectedEnvironment
   * since they are scoped to the BU.
   */
  setSelectedBu: (bu: BusinessUnit | null) => void;

  /**
   * Select a Container Type Config. Also clears selectedEnvironment
   * since the environment is linked through the config.
   */
  setSelectedConfig: (config: SpeContainerTypeConfig | null) => void;

  /** Select an SPE Environment. */
  setSelectedEnvironment: (env: SpeEnvironment | null) => void;

  // ── Helpers ────────────────────────────────────────────────────────────────

  /**
   * Clear all selections and remove persisted state from localStorage.
   * Useful when the administrator wants to start fresh or after a logout.
   */
  clearSelection: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Context Creation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BuContext — React context for BU/config/environment selection state.
 *
 * Do NOT consume this context directly. Use the `useBuContext` hook instead,
 * which validates that you are inside a BuProvider.
 */
const BuContext = React.createContext<BuContextValue | null>(null);

// ─────────────────────────────────────────────────────────────────────────────
// localStorage Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Safely read a JSON value from localStorage.
 * Returns null if the key is missing, the value is not valid JSON,
 * or localStorage is unavailable (e.g. private browsing with restrictions).
 *
 * Stale data (e.g. from a deleted BU) returns whatever was stored and
 * consumers must validate against fresh API data — the context does not
 * auto-validate against the server (acceptance criteria: "handled gracefully").
 */
function readFromStorage<T>(key: string): T | null {
  try {
    const raw = localStorage.getItem(key);
    if (raw === null) return null;
    return JSON.parse(raw) as T;
  } catch {
    // Corrupted JSON or unavailable storage — silently clear and continue
    try {
      localStorage.removeItem(key);
    } catch {
      // Storage also unavailable for removal — ignore
    }
    return null;
  }
}

/**
 * Safely write a JSON value to localStorage.
 * Silently ignores errors (e.g. storage quota exceeded, private browsing).
 */
function writeToStorage<T>(key: string, value: T | null): void {
  try {
    if (value === null) {
      localStorage.removeItem(key);
    } else {
      localStorage.setItem(key, JSON.stringify(value));
    }
  } catch {
    // Quota exceeded or unavailable — ignore (state still works in memory)
  }
}

/**
 * Clear all SPE Admin selection keys from localStorage.
 */
function clearStorage(): void {
  try {
    localStorage.removeItem(LS_KEY_BU_ID);
    localStorage.removeItem(LS_KEY_CONFIG_ID);
    localStorage.removeItem(LS_KEY_ENV_ID);
  } catch {
    // Ignore storage errors
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// BuProvider Component
// ─────────────────────────────────────────────────────────────────────────────

interface BuProviderProps {
  children: React.ReactNode;
  /**
   * Optional initial BU ID to pre-select (e.g. from URL params passed via
   * Xrm.Navigation.navigateTo data param — see App.tsx SpeAdminParams.buId).
   *
   * If provided, the provider will attempt to restore a stored BU whose ID
   * matches this value. The actual BusinessUnit object is still loaded from
   * localStorage (if previously stored). The consuming component (e.g.
   * BuContextPicker) is responsible for fetching and validating fresh data.
   */
  initialBuId?: string | null;
  /**
   * Optional initial config ID to pre-select (from URL params — configId).
   */
  initialConfigId?: string | null;
}

/**
 * BuProvider — manages BU/config/environment selection state.
 *
 * Place this provider inside FluentProvider but above any component that
 * needs BU context. All state is persisted to localStorage and restored
 * on mount.
 *
 * Stale localStorage data (e.g. a deleted BU) does NOT cause crashes;
 * consumers are expected to validate selections against fresh API data
 * and call setSelectedBu(null) / setSelectedConfig(null) if the selection
 * is no longer valid.
 */
export const BuProvider: React.FC<BuProviderProps> = ({
  children,
  initialBuId,
  initialConfigId,
}) => {
  // ── State — initialised from localStorage (runs once) ────────────────────

  const [selectedBu, setSelectedBuState] = React.useState<BusinessUnit | null>(
    () => {
      const stored = readFromStorage<BusinessUnit>(LS_KEY_BU_ID);
      // If an initialBuId was provided but doesn't match stored BU, clear stored
      if (initialBuId && stored && stored.businessUnitId !== initialBuId) {
        return null;
      }
      return stored;
    }
  );

  const [selectedConfig, setSelectedConfigState] =
    React.useState<SpeContainerTypeConfig | null>(() => {
      const stored = readFromStorage<SpeContainerTypeConfig>(LS_KEY_CONFIG_ID);
      // If an initialConfigId was provided but doesn't match stored config, clear stored
      if (initialConfigId && stored && stored.id !== initialConfigId) {
        return null;
      }
      return stored;
    });

  const [selectedEnvironment, setSelectedEnvironmentState] =
    React.useState<SpeEnvironment | null>(() =>
      readFromStorage<SpeEnvironment>(LS_KEY_ENV_ID)
    );

  // ── Setters with localStorage persistence ────────────────────────────────

  const setSelectedBu = React.useCallback((bu: BusinessUnit | null) => {
    setSelectedBuState(bu);
    writeToStorage(LS_KEY_BU_ID, bu);
    // Changing BU invalidates config and environment — cascade clear
    setSelectedConfigState(null);
    writeToStorage(LS_KEY_CONFIG_ID, null);
    setSelectedEnvironmentState(null);
    writeToStorage(LS_KEY_ENV_ID, null);
  }, []);

  const setSelectedConfig = React.useCallback(
    (config: SpeContainerTypeConfig | null) => {
      setSelectedConfigState(config);
      writeToStorage(LS_KEY_CONFIG_ID, config);
      // Changing config invalidates environment selection — cascade clear
      setSelectedEnvironmentState(null);
      writeToStorage(LS_KEY_ENV_ID, null);
    },
    []
  );

  const setSelectedEnvironment = React.useCallback(
    (env: SpeEnvironment | null) => {
      setSelectedEnvironmentState(env);
      writeToStorage(LS_KEY_ENV_ID, env);
    },
    []
  );

  const clearSelection = React.useCallback(() => {
    setSelectedBuState(null);
    setSelectedConfigState(null);
    setSelectedEnvironmentState(null);
    clearStorage();
  }, []);

  // ── Context value (stable reference via useMemo) ─────────────────────────

  const value = React.useMemo<BuContextValue>(
    () => ({
      selectedBu,
      selectedConfig,
      selectedEnvironment,
      setSelectedBu,
      setSelectedConfig,
      setSelectedEnvironment,
      clearSelection,
    }),
    [
      selectedBu,
      selectedConfig,
      selectedEnvironment,
      setSelectedBu,
      setSelectedConfig,
      setSelectedEnvironment,
      clearSelection,
    ]
  );

  return <BuContext.Provider value={value}>{children}</BuContext.Provider>;
};

// ─────────────────────────────────────────────────────────────────────────────
// useBuContext Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * useBuContext — consume BU/config/environment selection state.
 *
 * Throws a descriptive error when used outside BuProvider (acceptance criterion).
 *
 * @example
 * const { selectedBu, setSelectedBu } = useBuContext();
 */
export function useBuContext(): BuContextValue {
  const ctx = React.useContext(BuContext);
  if (ctx === null) {
    throw new Error(
      "[SpeAdminApp] useBuContext must be used inside a <BuProvider>. " +
        "Wrap your component tree with <BuProvider> in App.tsx."
    );
  }
  return ctx;
}
