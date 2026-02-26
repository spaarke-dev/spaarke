/**
 * SprkChatPane -- Application Shell
 *
 * Renders the real SprkChat component from @spaarke/ui-components inside a
 * full-viewport layout designed for the Dataverse side pane (300-600px width).
 *
 * Wires:
 *   - SprkChat component with parsed URL parameters (entityType, entityId, playbookId, sessionId)
 *   - SprkChatBridge instance for cross-pane communication
 *   - Independent authentication via Xrm.Utility.getGlobalContext() (authService)
 *   - Automatic host form context detection via contextService
 *   - Default playbook selection based on entity type
 *   - Session persistence via sessionStorage (keyed by pane ID)
 *   - Context-switch dialog when user navigates to a different record
 *   - Responsive layout via makeStyles with Fluent design tokens
 *   - Host context for entity-scoped AI interactions
 *   - Error boundary for auth failures
 *
 * Authentication flow:
 *   1. App mounts -> calls initializeAuth() from authService
 *   2. authService uses Xrm SDK to acquire a Bearer token for BFF API
 *   3. Token is passed as a string prop to SprkChat
 *   4. Token auto-refreshes via getAccessToken() before each session/stream
 *   5. Auth errors show user-friendly error state
 *
 * Context detection flow (task 014):
 *   1. App mounts -> detectContext() reads URL params, falls back to Xrm APIs
 *   2. Default playbook resolved from configurable entity-type mapping
 *   3. Session state restored from sessionStorage (if pane was reloaded)
 *   4. Context-change polling starts to detect form navigation
 *   5. On mismatch -> ContextSwitchDialog shown (Switch/Keep)
 *   6. On switch -> context_changed emitted via SprkChatBridge
 *
 * Replaces PH-010-A placeholder from task 010.
 * Authentication added by task 013.
 * Context auto-detection and session persistence added by task 014.
 *
 * @see ADR-008 - Endpoint filters for auth
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9; makeStyles; design tokens; dark mode
 */

import { useEffect, useMemo, useRef, useState, useCallback } from "react";
import {
    makeStyles,
    shorthands,
    tokens,
    Spinner,
    Text,
    Button,
} from "@fluentui/react-components";
import {
    ErrorCircle20Regular,
    LockClosed20Regular,
    ArrowClockwise20Regular,
} from "@fluentui/react-icons";
import { SprkChat, SprkChatBridge } from "@spaarke/ui-components";
import type { IHostContext } from "@spaarke/ui-components";
import {
    getAccessToken,
    initializeAuth,
    clearTokenCache,
    isXrmAvailable,
    AuthError,
} from "./services/authService";
import {
    detectContext,
    restoreSession,
    saveSession,
    clearSession,
    startContextChangeDetection,
} from "./services/contextService";
import type { DetectedContext, PersistedSession } from "./services/contextService";
import { ContextSwitchDialog } from "./components/ContextSwitchDialog";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Interval for proactive token refresh (4 minutes).
 * Tokens typically live 1 hour; refreshing every 4 minutes ensures
 * the token passed to SprkChat is always fresh.
 */
const TOKEN_REFRESH_INTERVAL_MS = 4 * 60 * 1000;

/**
 * Default pane ID used for sessionStorage keying.
 * Matches the paneId used in Xrm.App.sidePanes.createPane().
 */
const DEFAULT_PANE_ID = "sprkchat";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface AppProps {
    /** Dataverse entity logical name for record context (from URL params) */
    entityType: string;
    /** Entity record ID for contextual chat (from URL params) */
    entityId: string;
    /** AI playbook ID for guided interactions (from URL params) */
    playbookId: string;
    /** Existing chat session to resume (from URL params) */
    sessionId: string;
    /** BFF API base URL */
    apiBaseUrl: string;
}

// ---------------------------------------------------------------------------
// Auth State
// ---------------------------------------------------------------------------

type AuthState =
    | { status: "loading" }
    | { status: "authenticated"; token: string }
    | { status: "error"; error: AuthError | Error; isXrmUnavailable: boolean };

// ---------------------------------------------------------------------------
// Context-Switch Dialog State
// ---------------------------------------------------------------------------

interface ContextSwitchState {
    /** Whether the dialog is currently shown. */
    open: boolean;
    /** The new context detected from Xrm navigation. */
    newContext: DetectedContext;
}

// ---------------------------------------------------------------------------
// Styles -- responsive full-viewport layout for 300-600px side pane
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        height: "100vh",
        minWidth: "300px",
        maxWidth: "100%",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        overflow: "hidden",
    },
    chatContainer: {
        display: "flex",
        flexDirection: "column",
        flexGrow: 1,
        overflow: "hidden",
        /* Ensure no horizontal scrollbar at any width in 300-600px range */
        overflowX: "hidden",
    },
    authLoading: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flexGrow: 1,
        ...shorthands.gap(tokens.spacingVerticalL),
        ...shorthands.padding(tokens.spacingVerticalXXL, tokens.spacingHorizontalL),
    },
    authError: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        flexGrow: 1,
        ...shorthands.gap(tokens.spacingVerticalM),
        ...shorthands.padding(tokens.spacingVerticalXXL, tokens.spacingHorizontalL),
        textAlign: "center",
    },
    errorIcon: {
        color: tokens.colorPaletteRedForeground1,
        fontSize: "48px",
        marginBottom: tokens.spacingVerticalS,
    },
    lockIcon: {
        color: tokens.colorNeutralForeground3,
        fontSize: "48px",
        marginBottom: tokens.spacingVerticalS,
    },
    errorTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    errorDetail: {
        color: tokens.colorNeutralForeground3,
        maxWidth: "280px",
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const App: React.FC<AppProps> = ({
    entityType: propEntityType,
    entityId: propEntityId,
    playbookId: propPlaybookId,
    sessionId: propSessionId,
    apiBaseUrl,
}) => {
    const styles = useStyles();

    // ── Context detection and state ─────────────────────────────────────
    // Build URL params from props (these came from the data envelope in index.tsx)
    const urlParams = useMemo(() => {
        const params = new URLSearchParams();
        if (propEntityType) params.set("entityType", propEntityType);
        if (propEntityId) params.set("entityId", propEntityId);
        if (propPlaybookId) params.set("playbookId", propPlaybookId);
        return params;
    }, [propEntityType, propEntityId, propPlaybookId]);

    // Resolve initial context: restore session > detect from URL/Xrm > fallback
    const [activeContext, setActiveContext] = useState<DetectedContext>(() => {
        // Try to restore from sessionStorage first (pane reload scenario)
        const restored = restoreSession(DEFAULT_PANE_ID);
        if (restored) {
            console.info("[SprkChatPane] Restored session from sessionStorage:", restored.entityType, restored.entityId);
            return {
                entityType: restored.entityType,
                entityId: restored.entityId,
                playbookId: restored.playbookId,
            };
        }
        // Detect fresh context from URL params or Xrm APIs
        return detectContext(urlParams);
    });

    // Session ID: prefer restored session > URL param > empty (create new)
    const [activeSessionId, setActiveSessionId] = useState<string>(() => {
        const restored = restoreSession(DEFAULT_PANE_ID);
        if (restored?.sessionId) return restored.sessionId;
        return propSessionId;
    });

    // ── Context-switch dialog state ─────────────────────────────────────
    const [contextSwitch, setContextSwitch] = useState<ContextSwitchState>({
        open: false,
        newContext: { entityType: "", entityId: "", playbookId: "" },
    });

    // ── Auth state ──────────────────────────────────────────────────────
    const [authState, setAuthState] = useState<AuthState>({ status: "loading" });

    /**
     * Initialize authentication on mount.
     * Uses Xrm.Utility.getGlobalContext() to acquire the initial token.
     */
    const initAuth = useCallback(async () => {
        setAuthState({ status: "loading" });

        try {
            const token = await initializeAuth();
            setAuthState({ status: "authenticated", token });
        } catch (err) {
            const authErr = err instanceof AuthError ? err : new AuthError(
                err instanceof Error ? err.message : "Authentication failed",
                { isRetryable: true, cause: err }
            );
            setAuthState({
                status: "error",
                error: authErr,
                isXrmUnavailable: authErr instanceof AuthError && authErr.isXrmUnavailable,
            });
        }
    }, []);

    // Initialize auth on mount
    useEffect(() => {
        initAuth();
    }, [initAuth]);

    /**
     * Proactive token refresh interval.
     * Silently refreshes the token before it expires so SprkChat always has
     * a valid token. If refresh fails, the existing token continues to work
     * until it actually expires.
     */
    useEffect(() => {
        if (authState.status !== "authenticated") return;

        const intervalId = setInterval(async () => {
            try {
                const freshToken = await getAccessToken();
                setAuthState({ status: "authenticated", token: freshToken });
            } catch (err) {
                console.warn("[SprkChatPane] Token refresh failed, will retry:", err);
                // Don't set error state -- the existing token may still be valid.
                // The next API call will fail if the token is truly expired,
                // and SprkChat's error handling will display it.
            }
        }, TOKEN_REFRESH_INTERVAL_MS);

        return () => clearInterval(intervalId);
    }, [authState.status]);

    /**
     * Retry authentication after an error.
     * Clears the token cache and re-initializes.
     */
    const handleRetryAuth = useCallback(() => {
        clearTokenCache();
        initAuth();
    }, [initAuth]);

    // ── Session persistence ─────────────────────────────────────────────
    /**
     * Persist session state to sessionStorage whenever context or session changes.
     * This ensures the pane can recover its state after a Code Page re-mount.
     */
    useEffect(() => {
        if (activeSessionId && activeContext.entityType && activeContext.entityId) {
            saveSession(DEFAULT_PANE_ID, {
                sessionId: activeSessionId,
                entityType: activeContext.entityType,
                entityId: activeContext.entityId,
                playbookId: activeContext.playbookId,
                timestamp: new Date().toISOString(),
            });
        }
    }, [activeSessionId, activeContext]);

    // ── Host context ────────────────────────────────────────────────────
    const hostContext: IHostContext | undefined = useMemo(() => {
        if (!activeContext.entityType || !activeContext.entityId) return undefined;
        return {
            entityType: activeContext.entityType,
            entityId: activeContext.entityId,
        };
    }, [activeContext.entityType, activeContext.entityId]);

    // ── SprkChatBridge for cross-pane communication ─────────────────────
    const bridgeRef = useRef<SprkChatBridge | null>(null);

    useEffect(() => {
        const context = activeSessionId || activeContext.entityId || "default";
        const bridge = new SprkChatBridge({ context });
        bridgeRef.current = bridge;

        // Listen for context_changed events from other panes (e.g. workspace navigation)
        const unsubContext = bridge.subscribe("context_changed", (payload) => {
            console.debug("[SprkChatPane] context_changed received from bridge:", payload);
            // If another pane signals a context change, update our context to match
            if (payload.entityType && payload.entityId) {
                setActiveContext({
                    entityType: payload.entityType,
                    entityId: payload.entityId,
                    playbookId: payload.playbookId || activeContext.playbookId,
                });
            }
        });

        return () => {
            unsubContext();
            bridge.disconnect();
            bridgeRef.current = null;
        };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [activeSessionId, activeContext.entityId]);

    // ── Context-change detection (polling) ──────────────────────────────
    useEffect(() => {
        // Only start polling if we have a valid initial context and Xrm is available
        if (!activeContext.entityType || !activeContext.entityId) return;
        if (!isXrmAvailable()) return;

        const stopPolling = startContextChangeDetection(
            activeContext,
            (newContext, _previousContext) => {
                // Show the context-switch dialog
                setContextSwitch({
                    open: true,
                    newContext,
                });
            }
        );

        return stopPolling;
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [activeContext.entityType, activeContext.entityId]);

    // ── Context-switch dialog handlers ──────────────────────────────────

    /**
     * User chose "Switch" -- update context, emit bridge event, clear session.
     */
    const handleContextSwitch = useCallback((newContext: DetectedContext) => {
        setContextSwitch({ open: false, newContext: { entityType: "", entityId: "", playbookId: "" } });

        // Emit context_changed event via SprkChatBridge so other panes are notified
        if (bridgeRef.current && !bridgeRef.current.isDisconnected) {
            bridgeRef.current.emit("context_changed", {
                entityType: newContext.entityType,
                entityId: newContext.entityId,
                playbookId: newContext.playbookId,
            });
        }

        // Clear the old session (user is switching records; start fresh)
        clearSession(DEFAULT_PANE_ID);
        setActiveSessionId("");

        // Update active context -- this triggers SprkChat to re-create the session
        setActiveContext(newContext);

        console.info(
            "[SprkChatPane] Context switched to:",
            newContext.entityType,
            newContext.entityId
        );
    }, []);

    /**
     * User chose "Keep" -- dismiss dialog, continue with current context.
     */
    const handleContextKeep = useCallback(() => {
        setContextSwitch({ open: false, newContext: { entityType: "", entityId: "", playbookId: "" } });
        console.debug("[SprkChatPane] User chose to keep current context");
    }, []);

    /**
     * Callback from SprkChat when a new session is created.
     * Persist the session ID so it survives pane reloads.
     */
    const handleSessionCreated = useCallback((session: { sessionId: string }) => {
        setActiveSessionId(session.sessionId);
    }, []);

    // ── Render ──────────────────────────────────────────────────────────

    // Auth loading state
    if (authState.status === "loading") {
        return (
            <div className={styles.root} role="main" aria-label="SprkChat Pane">
                <div className={styles.authLoading} data-testid="auth-loading">
                    <Spinner size="medium" label="Authenticating..." />
                    <Text size={200} className={styles.errorDetail}>
                        Connecting to Dataverse...
                    </Text>
                </div>
            </div>
        );
    }

    // Auth error: Xrm unavailable (outside Dataverse)
    if (authState.status === "error" && authState.isXrmUnavailable) {
        return (
            <div className={styles.root} role="main" aria-label="SprkChat Pane">
                <div
                    className={styles.authError}
                    role="alert"
                    data-testid="auth-error-xrm"
                >
                    <LockClosed20Regular className={styles.lockIcon} />
                    <Text size={400} className={styles.errorTitle}>
                        Dataverse Required
                    </Text>
                    <Text size={200} className={styles.errorDetail}>
                        SprkChat must be opened from within Dataverse.
                        Please open this pane from a model-driven app form.
                    </Text>
                </div>
            </div>
        );
    }

    // Auth error: token acquisition failure (retryable)
    if (authState.status === "error") {
        return (
            <div className={styles.root} role="main" aria-label="SprkChat Pane">
                <div
                    className={styles.authError}
                    role="alert"
                    data-testid="auth-error-token"
                >
                    <ErrorCircle20Regular className={styles.errorIcon} />
                    <Text size={400} className={styles.errorTitle}>
                        Authentication Failed
                    </Text>
                    <Text size={200} className={styles.errorDetail}>
                        {authState.error.message || "Unable to acquire an authentication token. Please try again."}
                    </Text>
                    <Button
                        appearance="primary"
                        icon={<ArrowClockwise20Regular />}
                        onClick={handleRetryAuth}
                        data-testid="auth-retry-button"
                    >
                        Retry
                    </Button>
                </div>
            </div>
        );
    }

    // Authenticated -- render SprkChat with detected context and session persistence
    return (
        <div className={styles.root} role="main" aria-label="SprkChat Pane">
            <div className={styles.chatContainer}>
                <SprkChat
                    sessionId={activeSessionId || undefined}
                    playbookId={activeContext.playbookId}
                    documentId={undefined}
                    apiBaseUrl={apiBaseUrl}
                    accessToken={authState.token}
                    hostContext={hostContext}
                    onSessionCreated={handleSessionCreated}
                />
            </div>

            {/* Context-switch dialog -- shown when Xrm navigation detected */}
            <ContextSwitchDialog
                open={contextSwitch.open}
                newContext={contextSwitch.newContext}
                currentContext={activeContext}
                onSwitch={handleContextSwitch}
                onKeep={handleContextKeep}
            />
        </div>
    );
};

export default App;
