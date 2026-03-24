import { type ReactNode, useCallback, useEffect, useMemo } from 'react';
import {
  MsalProvider,
  useMsal,
  useIsAuthenticated,
  AuthenticatedTemplate,
  UnauthenticatedTemplate,
} from '@azure/msal-react';
import { PublicClientApplication, InteractionRequiredAuthError } from '@azure/msal-browser';
import { msalConfig, loginRequest, isMsalConfigured } from './msalConfig';
import { setTokenProvider } from '../api/client';

let msalInstance: PublicClientApplication | null = null;

function getMsalInstance(): PublicClientApplication {
  if (!msalInstance) {
    msalInstance = new PublicClientApplication(msalConfig);
  }
  return msalInstance;
}

function TokenProviderSetup(): null {
  const { instance, accounts } = useMsal();

  const acquireToken = useCallback(async (): Promise<string | null> => {
    if (accounts.length === 0) return null;
    try {
      const result = await instance.acquireTokenSilent({
        ...loginRequest,
        account: accounts[0],
      });
      return result.accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await instance.acquireTokenRedirect(loginRequest);
      }
      return null;
    }
  }, [instance, accounts]);

  useEffect(() => {
    setTokenProvider(acquireToken);
  }, [acquireToken]);

  return null;
}

function AuthGate({ children }: { children: ReactNode }) {
  const { instance } = useMsal();
  const isAuthenticated = useIsAuthenticated();

  const handleLogin = useCallback(() => {
    instance.loginRedirect(loginRequest);
  }, [instance]);

  return (
    <>
      <TokenProviderSetup />
      <AuthenticatedTemplate>{children}</AuthenticatedTemplate>
      <UnauthenticatedTemplate>
        <div className="auth-gate">
          <h1>Smart KB</h1>
          <p>Sign in with your organization account to continue.</p>
          <button onClick={handleLogin} className="btn btn-primary" aria-label="Sign in with your organization account">
            Sign In
          </button>
          {!isAuthenticated && null}
        </div>
      </UnauthenticatedTemplate>
    </>
  );
}

/**
 * If MSAL is configured (VITE_ENTRA_CLIENT_ID set), wraps children with
 * MsalProvider + AuthGate. Otherwise renders children directly for local dev.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const configured = useMemo(() => isMsalConfigured(), []);

  if (!configured) {
    return <>{children}</>;
  }

  return (
    <MsalProvider instance={getMsalInstance()}>
      <AuthGate>{children}</AuthGate>
    </MsalProvider>
  );
}
