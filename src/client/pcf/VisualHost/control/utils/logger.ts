/**
 * Logger utility for Visual Host PCF
 * Provides structured logging with component context
 */

type LogLevel = "debug" | "info" | "warn" | "error";

interface ILogger {
  debug: (component: string, message: string, data?: unknown) => void;
  info: (component: string, message: string, data?: unknown) => void;
  warn: (component: string, message: string, data?: unknown) => void;
  error: (component: string, message: string, data?: unknown) => void;
}

const LOG_PREFIX = "[VisualHost]";

// Enable debug logs in development
const isDebugEnabled =
  process.env.NODE_ENV === "development" ||
  window.location.search.includes("debug=true");

function formatMessage(
  level: LogLevel,
  component: string,
  message: string
): string {
  return `${LOG_PREFIX} [${component}] ${message}`;
}

export const logger: ILogger = {
  debug: (component: string, message: string, data?: unknown) => {
    if (isDebugEnabled) {
      if (data !== undefined) {
        console.debug(formatMessage("debug", component, message), data);
      } else {
        console.debug(formatMessage("debug", component, message));
      }
    }
  },

  info: (component: string, message: string, data?: unknown) => {
    if (data !== undefined) {
      console.info(formatMessage("info", component, message), data);
    } else {
      console.info(formatMessage("info", component, message));
    }
  },

  warn: (component: string, message: string, data?: unknown) => {
    if (data !== undefined) {
      console.warn(formatMessage("warn", component, message), data);
    } else {
      console.warn(formatMessage("warn", component, message));
    }
  },

  error: (component: string, message: string, data?: unknown) => {
    if (data !== undefined) {
      console.error(formatMessage("error", component, message), data);
    } else {
      console.error(formatMessage("error", component, message));
    }
  },
};
