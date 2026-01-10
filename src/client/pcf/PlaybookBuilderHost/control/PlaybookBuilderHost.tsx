/**
 * PlaybookBuilderHost React Component
 *
 * Embeds the React 18 Playbook Builder app in an iframe.
 * Handles bidirectional communication via postMessage.
 *
 * Message Protocol:
 * - Host → Builder: INIT (playbook data), SAVE_RESPONSE
 * - Builder → Host: READY, DIRTY_CHANGE, SAVE_REQUEST, CANVAS_UPDATE
 */

import * as React from 'react';
import {
  Button,
  Spinner,
  Text,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface PlaybookBuilderHostProps {
  playbookId: string;
  playbookName: string;
  canvasJson: string;
  builderBaseUrl: string;
  onDirtyChange: (isDirty: boolean) => void;
  onSave: (canvasJson: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Host → Builder Messages
// ─────────────────────────────────────────────────────────────────────────────

interface InitMessage {
  type: 'INIT';
  payload: {
    playbookId: string;
    playbookName: string;
    canvasJson: string;
    authToken?: string;
  };
}

interface SaveSuccessMessage {
  type: 'SAVE_SUCCESS';
  payload: { timestamp: number };
}

interface SaveErrorMessage {
  type: 'SAVE_ERROR';
  payload: { error: string; code?: string };
}

interface ThemeChangeMessage {
  type: 'THEME_CHANGE';
  payload: { theme: 'light' | 'dark' };
}

type HostToBuilderMessage = InitMessage | SaveSuccessMessage | SaveErrorMessage | ThemeChangeMessage;

// ─────────────────────────────────────────────────────────────────────────────
// Builder → Host Messages
// ─────────────────────────────────────────────────────────────────────────────

interface ReadyMessage {
  type: 'READY';
}

interface DirtyChangeMessage {
  type: 'DIRTY_CHANGE';
  payload: { isDirty: boolean };
}

interface SaveRequestMessage {
  type: 'SAVE_REQUEST';
  payload: { canvasJson: string };
}

interface CanvasUpdateMessage {
  type: 'CANVAS_UPDATE';
  payload: { canvasJson: string };
}

interface RequestTokenMessage {
  type: 'REQUEST_TOKEN';
}

type BuilderToHostMessage = ReadyMessage | DirtyChangeMessage | SaveRequestMessage | CanvasUpdateMessage | RequestTokenMessage;

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    minHeight: '800px', // Match PCF container minHeight - needed because height:100% doesn't work with parent minHeight
    overflow: 'hidden',
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  iframeContainer: {
    flex: 1,
    position: 'relative',
    overflow: 'hidden',
  },
  iframe: {
    position: 'absolute',
    top: 0,
    left: 0,
    width: '100%',
    height: '100%',
    ...shorthands.border('none'),
  },
  loading: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  error: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    ...shorthands.padding(tokens.spacingVerticalXXL),
    textAlign: 'center',
    color: tokens.colorPaletteRedForeground1,
  },
  footer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-end',
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    fontSize: '11px',
    color: tokens.colorNeutralForeground3,
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderTop(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
  },
  dirtyIndicator: {
    color: tokens.colorPaletteMarigoldForeground1,
    marginRight: tokens.spacingHorizontalS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const PlaybookBuilderHost: React.FC<PlaybookBuilderHostProps> = ({
  playbookId,
  playbookName,
  canvasJson,
  builderBaseUrl,
  onDirtyChange,
  onSave,
}) => {
  const styles = useStyles();
  const iframeRef = React.useRef<HTMLIFrameElement>(null);

  const [isLoading, setIsLoading] = React.useState(true);
  const [isReady, setIsReady] = React.useState(false);
  const [isDirty, setIsDirty] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [currentCanvasJson, setCurrentCanvasJson] = React.useState(canvasJson);

  // Build iframe URL
  const iframeSrc = React.useMemo(() => {
    // Append origin for postMessage security
    const url = new URL(builderBaseUrl, window.location.origin);
    url.searchParams.set('embedded', 'true');
    if (playbookId) {
      url.searchParams.set('playbookId', playbookId);
    }
    return url.toString();
  }, [builderBaseUrl, playbookId]);

  // Send message to iframe
  const sendMessage = React.useCallback((message: HostToBuilderMessage) => {
    if (iframeRef.current?.contentWindow) {
      iframeRef.current.contentWindow.postMessage(message, '*');
      console.debug('[PlaybookBuilderHost] Sent to builder:', message.type);
    }
  }, []);

  // Handle messages from iframe
  const handleMessage = React.useCallback(
    (event: MessageEvent<BuilderToHostMessage>) => {
      // Validate message origin - allow messages from the builder iframe
      // The builderBaseUrl determines the expected origin
      const builderUrl = new URL(builderBaseUrl, window.location.origin);
      const allowedOrigins = [
        window.location.origin,
        builderUrl.origin,
        'http://localhost:5173',
        'http://localhost:3001',
        'https://spe-api-dev-67e2xz.azurewebsites.net', // Azure App Service
      ];
      if (!allowedOrigins.includes(event.origin)) {
        console.warn('[PlaybookBuilderHost] Ignoring message from unknown origin:', event.origin);
        return;
      }

      const message = event.data;
      if (!message || typeof message.type !== 'string') return;

      console.info('[PlaybookBuilderHost] Received message:', message.type);

      switch (message.type) {
        case 'READY':
          setIsReady(true);
          setIsLoading(false);
          // Send initial data to builder
          sendMessage({
            type: 'INIT',
            payload: {
              playbookId,
              playbookName,
              canvasJson: currentCanvasJson,
            },
          });
          break;

        case 'DIRTY_CHANGE':
          setIsDirty(message.payload.isDirty);
          onDirtyChange(message.payload.isDirty);
          break;

        case 'SAVE_REQUEST':
          setCurrentCanvasJson(message.payload.canvasJson);
          // Call parent save handler and send response
          try {
            onSave(message.payload.canvasJson);
            setIsDirty(false);
            // Send success response to builder
            sendMessage({
              type: 'SAVE_SUCCESS',
              payload: { timestamp: Date.now() },
            });
          } catch (error) {
            // Send error response to builder
            sendMessage({
              type: 'SAVE_ERROR',
              payload: {
                error: error instanceof Error ? error.message : 'Save failed',
              },
            });
          }
          break;

        case 'CANVAS_UPDATE':
          setCurrentCanvasJson(message.payload.canvasJson);
          break;

        case 'REQUEST_TOKEN':
          // TODO: Implement token refresh when auth is wired up
          console.info('[PlaybookBuilderHost] Token requested - not implemented yet');
          break;

        default:
          console.warn('[PlaybookBuilderHost] Unknown message type:', (message as { type: string }).type);
      }
    },
    [playbookId, playbookName, currentCanvasJson, sendMessage, onDirtyChange, onSave]
  );

  // Set up message listener
  React.useEffect(() => {
    window.addEventListener('message', handleMessage);
    return () => {
      window.removeEventListener('message', handleMessage);
    };
  }, [handleMessage]);

  // Handle iframe load
  const handleIframeLoad = React.useCallback(() => {
    console.info('[PlaybookBuilderHost] Iframe loaded');
    // Builder will send READY message when initialized
  }, []);

  // Handle iframe error
  const handleIframeError = React.useCallback(() => {
    setError('Failed to load the Playbook Builder. Please check your connection and try again.');
    setIsLoading(false);
  }, []);

  // Handle save button click
  const handleSaveClick = React.useCallback(() => {
    if (iframeRef.current?.contentWindow) {
      iframeRef.current.contentWindow.postMessage({ type: 'SAVE_REQUEST' }, '*');
    }
  }, []);

  // Handle reload button click
  const handleReloadClick = React.useCallback(() => {
    setIsLoading(true);
    setIsReady(false);
    setError(null);
    if (iframeRef.current) {
      iframeRef.current.src = iframeSrc;
    }
  }, [iframeSrc]);

  // Error state
  if (error) {
    return (
      <div className={styles.container}>
        <div className={styles.error}>
          <Text size={400} weight="semibold">
            Unable to Load Builder
          </Text>
          <Text size={300}>{error}</Text>
          <Button
            appearance="primary"
            onClick={handleReloadClick}
          >
            Retry
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Toolbar */}
      <div className={styles.toolbar}>
        {isDirty && (
          <Text className={styles.dirtyIndicator} size={200}>
            Unsaved changes
          </Text>
        )}
        <Button
          appearance="primary"
          disabled={!isReady || !isDirty}
          onClick={handleSaveClick}
        >
          Save
        </Button>
      </div>

      {/* Iframe Container */}
      <div className={styles.iframeContainer}>
        {isLoading && (
          <div className={styles.loading}>
            <Spinner size="medium" />
            <Text>Loading Playbook Builder...</Text>
          </div>
        )}
        <iframe
          ref={iframeRef}
          className={styles.iframe}
          src={iframeSrc}
          title="Playbook Builder"
          onLoad={handleIframeLoad}
          onError={handleIframeError}
          style={{ visibility: isLoading ? 'hidden' : 'visible' }}
          sandbox="allow-scripts allow-same-origin allow-forms"
        />
      </div>

      {/* Footer with version */}
      <div className={styles.footer}>
        <Text size={100}>v1.2.4</Text>
      </div>
    </div>
  );
};
