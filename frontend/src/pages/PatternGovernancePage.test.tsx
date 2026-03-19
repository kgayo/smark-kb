import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { PatternGovernancePage } from './PatternGovernancePage';
import * as api from '../api/client';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
  getGovernanceQueue: vi.fn(),
  getPatternDetail: vi.fn(),
  reviewPattern: vi.fn(),
  approvePattern: vi.fn(),
  deprecatePattern: vi.fn(),
  getPatternUsage: vi.fn(),
  getPatternHistory: vi.fn(),
}));

vi.mock('../auth/useRoles', () => {
  const actual = vi.importActual('../auth/useRoles');
  return {
    ...actual,
    useRoles: vi.fn(),
  };
});

import { useRoles } from '../auth/useRoles';
const mockedUseRoles = vi.mocked(useRoles);
const mockedApi = vi.mocked(api);

function renderPage() {
  return render(
    <MemoryRouter>
      <PatternGovernancePage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockedApi.getPatternUsage.mockResolvedValue({
    patternId: '', totalCitations: 0, citationsLast7Days: 0,
    citationsLast30Days: 0, citationsLast90Days: 0, uniqueUsers: 0,
    averageConfidence: 0, lastCitedAt: null, firstCitedAt: null, dailyBreakdown: [],
  });
  mockedApi.getPatternHistory.mockResolvedValue({
    patternId: '', entries: [], totalCount: 0,
  });
});

describe('PatternGovernancePage', () => {
  it('shows loading state when roles are loading', () => {
    mockedUseRoles.mockReturnValue({ roles: [], loading: true });
    renderPage();
    expect(screen.getByTestId('governance-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin/non-lead roles', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['SupportAgent'], loading: false });
    renderPage();
    expect(screen.getByTestId('governance-denied')).toBeInTheDocument();
    expect(screen.getByText('Access Denied')).toBeInTheDocument();
  });

  it('shows access denied for empty roles', () => {
    mockedUseRoles.mockReturnValue({ roles: [], loading: false });
    renderPage();
    expect(screen.getByTestId('governance-denied')).toBeInTheDocument();
  });

  it('allows Admin role to access governance page', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getGovernanceQueue.mockResolvedValue({
      patterns: [],
      totalCount: 0,
      page: 1,
      hasMore: false,
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('governance-page')).toBeInTheDocument();
    });
  });

  it('allows SupportLead role to access governance page', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['SupportLead'], loading: false });
    mockedApi.getGovernanceQueue.mockResolvedValue({
      patterns: [],
      totalCount: 0,
      page: 1,
      hasMore: false,
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('governance-page')).toBeInTheDocument();
    });
  });

  it('loads and displays patterns', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getGovernanceQueue.mockResolvedValue({
      patterns: [
        {
          patternId: 'p1',
          title: 'DNS Timeout Fix',
          trustLevel: 'Draft',
          productArea: 'Networking',
          usageCount: 5,
          createdAt: '2026-03-15T00:00:00Z',
        },
      ],
      totalCount: 1,
      page: 1,
      hasMore: false,
    });

    renderPage();
    await waitFor(() => {
      expect(screen.getByText('DNS Timeout Fix')).toBeInTheDocument();
    });
  });

  it('shows error banner when pattern load fails', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getGovernanceQueue.mockRejectedValue(new Error('Server down'));

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('governance-error')).toBeInTheDocument();
      expect(screen.getByText('Server down')).toBeInTheDocument();
    });
  });

  it('selects a pattern and shows detail view', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getGovernanceQueue.mockResolvedValue({
      patterns: [
        {
          patternId: 'p1',
          title: 'DNS Timeout Fix',
          trustLevel: 'Draft',
          productArea: 'Networking',
          usageCount: 5,
          createdAt: '2026-03-15T00:00:00Z',
        },
      ],
      totalCount: 1,
      page: 1,
      hasMore: false,
    });
    mockedApi.getPatternDetail.mockResolvedValue({
      patternId: 'p1',
      title: 'DNS Timeout Fix',
      trustLevel: 'Draft',
      productArea: 'Networking',
      symptoms: ['timeout'],
      problems: ['DNS resolution failure'],
      resolutions: ['Flush DNS cache'],
      diagnosisSteps: [],
      resolutionSteps: [],
      verificationSteps: [],
      escalationCriteria: [],
      tags: [],
      relatedEvidenceIds: ['ev1'],
      usageCount: 5,
      createdAt: '2026-03-15T00:00:00Z',
      updatedAt: '2026-03-16T00:00:00Z',
      governanceHistory: [],
    } as any);

    renderPage();
    await waitFor(() => expect(screen.getByText('DNS Timeout Fix')).toBeInTheDocument());
    fireEvent.click(screen.getByText('DNS Timeout Fix'));

    await waitFor(() => {
      expect(mockedApi.getPatternDetail).toHaveBeenCalledWith('p1');
    });
  });

  it('shows error when pattern detail load fails', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getGovernanceQueue.mockResolvedValue({
      patterns: [
        {
          patternId: 'p1',
          title: 'Pattern A',
          trustLevel: 'Draft',
          productArea: 'Billing',
          usageCount: 0,
          createdAt: '2026-03-15T00:00:00Z',
        },
      ],
      totalCount: 1,
      page: 1,
      hasMore: false,
    });
    mockedApi.getPatternDetail.mockRejectedValue(new Error('Not found'));

    renderPage();
    await waitFor(() => expect(screen.getByText('Pattern A')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Pattern A'));

    await waitFor(() => {
      expect(screen.getByTestId('governance-error')).toHaveTextContent('Not found');
    });
  });

  it('renders navigation links', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getGovernanceQueue.mockResolvedValue({
      patterns: [],
      totalCount: 0,
      page: 1,
      hasMore: false,
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('back-to-chat')).toBeInTheDocument();
      expect(screen.getByText('Connectors')).toBeInTheDocument();
    });
  });
});
