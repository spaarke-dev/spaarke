/**
 * Centralized logging utility for UniversalDatasetGrid
 * Provides consistent log formatting and log level control
 */

export enum LogLevel {
    DEBUG = 0,
    INFO = 1,
    WARN = 2,
    ERROR = 3
}

class Logger {
    private prefix = '[UniversalDatasetGrid]';
    private logLevel: LogLevel = LogLevel.INFO;

    /**
     * Set the minimum log level to display
     */
    public setLogLevel(level: LogLevel): void {
        this.logLevel = level;
    }

    /**
     * Log debug message
     */
    public debug(component: string, message: string, ...args: unknown[]): void {
        if (this.logLevel <= LogLevel.DEBUG) {
            console.log(`${this.prefix}[${component}] ${message}`, ...args);
        }
    }

    /**
     * Log info message
     */
    public info(component: string, message: string, ...args: unknown[]): void {
        if (this.logLevel <= LogLevel.INFO) {
            console.log(`${this.prefix}[${component}] ${message}`, ...args);
        }
    }

    /**
     * Log warning message
     */
    public warn(component: string, message: string, ...args: unknown[]): void {
        if (this.logLevel <= LogLevel.WARN) {
            console.warn(`${this.prefix}[${component}] ${message}`, ...args);
        }
    }

    /**
     * Log error message
     */
    public error(component: string, message: string, error?: Error | unknown, ...args: unknown[]): void {
        if (this.logLevel <= LogLevel.ERROR) {
            console.error(`${this.prefix}[${component}] ${message}`, error, ...args);
        }
    }
}

// Export singleton instance
export const logger = new Logger();
