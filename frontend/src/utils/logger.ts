/**
 * Environment-aware logger that suppresses output in production builds.
 * Replaces direct console.warn/console.error calls throughout the frontend
 * to prevent implementation details from leaking to browser DevTools in production.
 */

const isProduction = import.meta.env.PROD;

function noop(): void {}

export const logger = {
  warn: isProduction ? noop : (...args: unknown[]) => console.warn(...args),
  error: isProduction ? noop : (...args: unknown[]) => console.error(...args),
  info: isProduction ? noop : (...args: unknown[]) => console.info(...args),
};
