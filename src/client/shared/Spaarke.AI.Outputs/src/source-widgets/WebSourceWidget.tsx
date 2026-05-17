/**
 * WebSourceWidget
 *
 * Renders an iframe-based web page preview alongside a URL bar and an
 * "Open in browser" link. The iframe is sandboxed to prevent arbitrary
 * script execution and cross-origin attacks.
 *
 * sandbox="allow-scripts allow-same-origin" is the minimum set that allows
 * most pages to render without granting full navigation or form-submit access
 * from the iframe back to the host page.
 *
 * NOT PCF-safe — React 19.
 */

import React from 'react';
import { makeStyles, tokens, Input, Link, Text, mergeClasses } from '@fluentui/react-components';
import { GlobeRegular, OpenRegular } from '@fluentui/react-icons';
import type { SourceWidgetProps } from '../types/widget-types';

// ---------------------------------------------------------------------------
// Payload type
// ---------------------------------------------------------------------------

export interface WebSourceData {
  /** The URL to load in the iframe. */
  url: string;
  /** Optional display title for the source. */
  title?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
  },
  urlBar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke1,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  urlIcon: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
  urlInput: {
    flexGrow: 1,
    // Override input focus ring to avoid redundancy with the container
    '& input': {
      cursor: 'default',
      color: tokens.colorNeutralForeground2,
      fontSize: tokens.fontSizeBase200,
    },
  },
  openLink: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
  },
  iframeContainer: {
    flexGrow: 1,
    overflow: 'hidden',
    position: 'relative',
  },
  iframe: {
    width: '100%',
    height: '100%',
    border: 'none',
    display: 'block',
  },
  fallback: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  errorText: {
    color: tokens.colorPaletteCranberryForeground2,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

function WebSourceWidget(props: SourceWidgetProps<WebSourceData>) {
  const { data, isLoading, error, className } = props;
  const styles = useStyles();

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <GlobeRegular fontSize={40} />
          <Text>Loading web source…</Text>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <GlobeRegular fontSize={40} />
          <Text className={styles.errorText}>{error}</Text>
        </div>
      </div>
    );
  }

  const displayUrl = data?.url ?? '';
  const iframeTitle = data?.title ?? displayUrl;

  return (
    <div className={mergeClasses(styles.root, className)}>
      <div className={styles.urlBar}>
        <GlobeRegular className={styles.urlIcon} fontSize={16} />
        <Input
          className={styles.urlInput}
          value={displayUrl}
          readOnly
          appearance="underline"
          aria-label="Source URL"
          title={displayUrl}
        />
        <Link
          className={styles.openLink}
          href={displayUrl}
          target="_blank"
          rel="noopener noreferrer"
          aria-label="Open source in browser"
        >
          Open
          <OpenRegular fontSize={14} />
        </Link>
      </div>

      <div className={styles.iframeContainer}>
        <iframe
          className={styles.iframe}
          src={displayUrl}
          title={iframeTitle}
          sandbox="allow-scripts allow-same-origin"
        />
      </div>
    </div>
  );
}

export default WebSourceWidget;
