import { render, screen } from '@testing-library/react';
import { AuthProvider } from './AuthProvider';

// Mock msalConfig module
vi.mock('./msalConfig', () => ({
  msalConfig: {
    auth: { clientId: '', authority: 'https://login.microsoftonline.com/common', redirectUri: 'http://localhost' },
    cache: { cacheLocation: 'sessionStorage', storeAuthStateInCookie: false },
    system: { loggerOptions: { logLevel: 0, loggerCallback: vi.fn() } },
  },
  loginRequest: { scopes: ['api://smart-kb/.default'] },
  isMsalConfigured: vi.fn(),
}));

// Mock MSAL react components
vi.mock('@azure/msal-react', () => ({
  MsalProvider: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="msal-provider">{children}</div>
  ),
  useMsal: () => ({
    instance: { acquireTokenSilent: vi.fn(), loginRedirect: vi.fn() },
    accounts: [],
  }),
  useIsAuthenticated: () => false,
  AuthenticatedTemplate: ({ children }: { children: React.ReactNode }) => null,
  UnauthenticatedTemplate: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

// Mock MSAL browser
vi.mock('@azure/msal-browser', () => ({
  PublicClientApplication: vi.fn().mockImplementation(() => ({
    initialize: vi.fn().mockResolvedValue(undefined),
  })),
  InteractionRequiredAuthError: class extends Error {
    constructor() {
      super('interaction_required');
    }
  },
}));

// Mock the api client setTokenProvider
vi.mock('../api/client', () => ({
  setTokenProvider: vi.fn(),
}));

import { isMsalConfigured } from './msalConfig';
const mockedIsMsalConfigured = vi.mocked(isMsalConfigured);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('AuthProvider', () => {
  it('renders children directly when MSAL is not configured (dev mode)', () => {
    mockedIsMsalConfigured.mockReturnValue(false);

    render(
      <AuthProvider>
        <div data-testid="child-content">App Content</div>
      </AuthProvider>,
    );

    expect(screen.getByTestId('child-content')).toBeInTheDocument();
    expect(screen.queryByTestId('msal-provider')).not.toBeInTheDocument();
  });

  it('wraps children with MsalProvider when MSAL is configured', () => {
    mockedIsMsalConfigured.mockReturnValue(true);

    render(
      <AuthProvider>
        <div data-testid="child-content">App Content</div>
      </AuthProvider>,
    );

    expect(screen.getByTestId('msal-provider')).toBeInTheDocument();
  });

  it('shows sign-in UI when MSAL is configured and user is unauthenticated', () => {
    mockedIsMsalConfigured.mockReturnValue(true);

    render(
      <AuthProvider>
        <div data-testid="child-content">App Content</div>
      </AuthProvider>,
    );

    expect(screen.getByText('Sign In')).toBeInTheDocument();
    expect(screen.getByText('Sign in with your organization account to continue.')).toBeInTheDocument();
  });

  it('renders Smart KB heading in auth gate', () => {
    mockedIsMsalConfigured.mockReturnValue(true);

    render(
      <AuthProvider>
        <div>App</div>
      </AuthProvider>,
    );

    expect(screen.getByText('Smart KB')).toBeInTheDocument();
  });
});
