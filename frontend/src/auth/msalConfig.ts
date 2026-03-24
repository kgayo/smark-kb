import { type Configuration, LogLevel } from '@azure/msal-browser';

// MSAL configuration for Entra ID authentication.
// Values are loaded from environment variables set at build time.
// In dev, set VITE_ENTRA_CLIENT_ID and VITE_ENTRA_AUTHORITY in .env.local.
export const msalConfig: Configuration = {
  auth: {
    clientId: import.meta.env.VITE_ENTRA_CLIENT_ID ?? '',
    authority:
      import.meta.env.VITE_ENTRA_AUTHORITY ?? 'https://login.microsoftonline.com/common',
    redirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
      loggerCallback: (level, message) => {
        if (level === LogLevel.Error) console.error('[MSAL]', message);
        else if (level === LogLevel.Warning) console.warn('[MSAL]', message);
      },
    },
  },
};

export const loginRequest = {
  scopes: [`${import.meta.env.VITE_ENTRA_CLIENT_ID ?? 'api://smart-kb'}/.default`],
};

export function isMsalConfigured(): boolean {
  return !!import.meta.env.VITE_ENTRA_CLIENT_ID;
}
