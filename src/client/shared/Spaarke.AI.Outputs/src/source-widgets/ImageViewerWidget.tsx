/**
 * ImageViewerWidget
 *
 * Renders an image with pan and zoom controls. Pan is implemented via mouse
 * drag (mousedown/mousemove/mouseup). Zoom is controlled by mouse wheel and
 * explicit zoom-in/zoom-out buttons. Scale is clamped to [1, 4].
 *
 * CSS transform (translate + scale) is used — no canvas or WebGL.
 * State: scale, translateX, translateY applied to the <img> element.
 *
 * NOT PCF-safe — React 19.
 */

import React, { useCallback, useRef, useState } from 'react';
import { makeStyles, tokens, Button, Text, mergeClasses } from '@fluentui/react-components';
import { ZoomInRegular, ZoomOutRegular, ArrowResetRegular, ImageRegular } from '@fluentui/react-icons';
import type { SourceWidgetProps } from '../types/widget-types';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const SCALE_MIN = 1;
const SCALE_MAX = 4;
const SCALE_STEP = 0.5;
const WHEEL_SCALE_FACTOR = 0.001;

// ---------------------------------------------------------------------------
// Payload type
// ---------------------------------------------------------------------------

export interface ImageViewerData {
  /** URL of the image to display. */
  src: string;
  /** Accessible alt text. */
  alt: string;
  /** Optional caption displayed below the image container. */
  caption?: string;
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
  toolbar: {
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
  toolbarSpacer: {
    flexGrow: 1,
  },
  scaleLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    minWidth: '40px',
    textAlign: 'center',
  },
  imageContainer: {
    flexGrow: 1,
    overflow: 'hidden',
    position: 'relative',
    cursor: 'grab',
    userSelect: 'none',
    backgroundColor: tokens.colorNeutralBackground3,
    ':active': {
      cursor: 'grabbing',
    },
  },
  imageContainerGrabbing: {
    cursor: 'grabbing',
  },
  image: {
    position: 'absolute',
    top: '50%',
    left: '50%',
    maxWidth: 'none',
    maxHeight: 'none',
    transformOrigin: 'center center',
    pointerEvents: 'none',
    transition: 'transform 0.05s ease-out',
    // translateX/Y and scale are applied via inline style
  },
  imageNoTransition: {
    transition: 'none',
  },
  caption: {
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    borderTopWidth: '1px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
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

function ImageViewerWidget(props: SourceWidgetProps<ImageViewerData>) {
  const { data, isLoading, error, className } = props;
  const styles = useStyles();

  const [scale, setScale] = useState(1);
  const [translateX, setTranslateX] = useState(0);
  const [translateY, setTranslateY] = useState(0);
  const [isDragging, setIsDragging] = useState(false);
  const [isTransitioning, setIsTransitioning] = useState(false);

  const dragStart = useRef<{ x: number; y: number; tx: number; ty: number } | null>(null);

  // --- Clamp helper ---
  const clampScale = (s: number) => Math.min(SCALE_MAX, Math.max(SCALE_MIN, s));

  // --- Zoom handlers ---
  const handleZoomIn = useCallback(() => {
    setScale(s => clampScale(s + SCALE_STEP));
    setIsTransitioning(true);
  }, []);

  const handleZoomOut = useCallback(() => {
    setScale(s => {
      const newScale = clampScale(s - SCALE_STEP);
      if (newScale === SCALE_MIN) {
        // Reset pan when fully zoomed out
        setTranslateX(0);
        setTranslateY(0);
      }
      return newScale;
    });
    setIsTransitioning(true);
  }, []);

  const handleReset = useCallback(() => {
    setScale(1);
    setTranslateX(0);
    setTranslateY(0);
    setIsTransitioning(true);
  }, []);

  // --- Wheel zoom ---
  const handleWheel = useCallback((e: React.WheelEvent<HTMLDivElement>) => {
    e.preventDefault();
    setIsTransitioning(false);
    const delta = -e.deltaY * WHEEL_SCALE_FACTOR;
    setScale(s => clampScale(s + delta * s));
  }, []);

  // --- Pan handlers ---
  const handleMouseDown = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (e.button !== 0) return; // Left button only
      dragStart.current = {
        x: e.clientX,
        y: e.clientY,
        tx: translateX,
        ty: translateY,
      };
      setIsDragging(true);
      setIsTransitioning(false);
    },
    [translateX, translateY]
  );

  const handleMouseMove = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (!isDragging || !dragStart.current) return;
      const dx = e.clientX - dragStart.current.x;
      const dy = e.clientY - dragStart.current.y;
      setTranslateX(dragStart.current.tx + dx);
      setTranslateY(dragStart.current.ty + dy);
    },
    [isDragging]
  );

  const handleMouseUp = useCallback(() => {
    setIsDragging(false);
    dragStart.current = null;
  }, []);

  const handleMouseLeave = useCallback(() => {
    if (isDragging) {
      setIsDragging(false);
      dragStart.current = null;
    }
  }, [isDragging]);

  // --- Transform style ---
  const imgTransform = `translate(calc(-50% + ${translateX}px), calc(-50% + ${translateY}px)) scale(${scale})`;

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <ImageRegular fontSize={40} />
          <Text>Loading image…</Text>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.fallback}>
          <ImageRegular fontSize={40} />
          <Text className={styles.errorText}>{error}</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Toolbar */}
      <div className={styles.toolbar}>
        <Button
          appearance="subtle"
          icon={<ZoomOutRegular />}
          onClick={handleZoomOut}
          disabled={scale <= SCALE_MIN}
          aria-label="Zoom out"
          size="small"
        />
        <Text className={styles.scaleLabel}>{Math.round(scale * 100)}%</Text>
        <Button
          appearance="subtle"
          icon={<ZoomInRegular />}
          onClick={handleZoomIn}
          disabled={scale >= SCALE_MAX}
          aria-label="Zoom in"
          size="small"
        />
        <div className={styles.toolbarSpacer} />
        <Button
          appearance="subtle"
          icon={<ArrowResetRegular />}
          onClick={handleReset}
          disabled={scale === 1 && translateX === 0 && translateY === 0}
          aria-label="Reset zoom and pan"
          size="small"
        >
          Reset
        </Button>
      </div>

      {/* Image container */}
      <div
        className={mergeClasses(styles.imageContainer, isDragging ? styles.imageContainerGrabbing : undefined)}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseLeave}
        onWheel={handleWheel}
        role="img"
        aria-label={data?.alt}
      >
        <img
          className={mergeClasses(styles.image, !isTransitioning ? styles.imageNoTransition : undefined)}
          src={data?.src}
          alt={data?.alt}
          style={{ transform: imgTransform }}
          draggable={false}
        />
      </div>

      {/* Caption */}
      {data?.caption && <Text className={styles.caption}>{data.caption}</Text>}
    </div>
  );
}

export default ImageViewerWidget;
