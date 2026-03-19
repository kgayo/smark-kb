import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { RoutingAnalyticsPage } from './RoutingAnalyticsPage';
import * as api from '../api/client';

vi.mock('../api/client', () => ({
  getRoutingAnalytics: vi.fn(),
  listRoutingRules: vi.fn(),
  createRoutingRule: vi.fn(),
  updateRoutingRule: vi.fn(),
  deleteRoutingRule: vi.fn(),
  listRoutingRecommendations: vi.fn(),
  generateRoutingRecommendations: vi.fn(),
  applyRoutingRecommendation: vi.fn(),
  dismissRoutingRecommendation: vi.fn(),
}));

vi.mock('../auth/useRoles', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../auth/useRoles')>();
  return { ...actual, useRoles: vi.fn() };
});

import { useRoles } from '../auth/useRoles';
const mockedUseRoles = vi.mocked(useRoles);
const mockedApi = vi.mocked(api);

function renderPage() {
  return render(<MemoryRouter><RoutingAnalyticsPage /></MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('RoutingAnalyticsPage', () => {
  it('shows loading state', () => {
    mockedUseRoles.mockReturnValue({ roles: [], loading: true });
    renderPage();
    expect(screen.getByTestId('routing-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin', () => {
    mockedUseRoles.mockReturnValue({ roles: ['SupportAgent'], loading: false });
    renderPage();
    expect(screen.getByTestId('routing-denied')).toBeInTheDocument();
  });

  it('loads analytics on mount for admin', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getRoutingAnalytics.mockResolvedValue({
      tenantId: 't1',
      totalOutcomes: 50,
      totalEscalations: 10,
      totalReroutes: 3,
      totalResolvedWithoutEscalation: 37,
      overallAcceptanceRate: 0.8,
      overallRerouteRate: 0.06,
      selfResolutionRate: 0.74,
      teamMetrics: [{
        targetTeam: 'Engineering',
        totalEscalations: 10,
        acceptedCount: 8,
        reroutedCount: 2,
        pendingCount: 0,
        acceptanceRate: 0.8,
        rerouteRate: 0.2,
        avgTimeToAssign: null,
        avgTimeToResolve: null,
      }],
      productAreaMetrics: [],
      computedAt: '2026-03-19T00:00:00Z',
      windowStart: null,
      windowEnd: null,
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('50')).toBeInTheDocument();
      expect(screen.getByText('74.0%')).toBeInTheDocument();
    });
    expect(screen.getByTestId('team-metrics-table')).toBeInTheDocument();
  });

  it('shows error on analytics load failure', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getRoutingAnalytics.mockRejectedValue(new Error('Network error'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('routing-error')).toBeInTheDocument();
    });
  });

  it('switches to rules tab and loads rules', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getRoutingAnalytics.mockResolvedValue({
      tenantId: 't1', totalOutcomes: 0, totalEscalations: 0, totalReroutes: 0,
      totalResolvedWithoutEscalation: 0, overallAcceptanceRate: 0, overallRerouteRate: 0,
      selfResolutionRate: 0, teamMetrics: [], productAreaMetrics: [],
      computedAt: '2026-03-19T00:00:00Z', windowStart: null, windowEnd: null,
    });
    mockedApi.listRoutingRules.mockResolvedValue({
      rules: [{
        ruleId: 'r1', productArea: 'Auth', targetTeam: 'Security',
        escalationThreshold: 0.4, minSeverity: 'P2', isActive: true,
        createdAt: '2026-03-19T00:00:00Z', updatedAt: '2026-03-19T00:00:00Z',
      }],
      totalCount: 1,
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getRoutingAnalytics).toHaveBeenCalled());

    fireEvent.click(screen.getByText(/^Rules/));
    await waitFor(() => {
      expect(screen.getByText('Auth')).toBeInTheDocument();
      expect(screen.getByText('Security')).toBeInTheDocument();
    });
  });

  it('switches to recommendations tab', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getRoutingAnalytics.mockResolvedValue({
      tenantId: 't1', totalOutcomes: 0, totalEscalations: 0, totalReroutes: 0,
      totalResolvedWithoutEscalation: 0, overallAcceptanceRate: 0, overallRerouteRate: 0,
      selfResolutionRate: 0, teamMetrics: [], productAreaMetrics: [],
      computedAt: '2026-03-19T00:00:00Z', windowStart: null, windowEnd: null,
    });
    mockedApi.listRoutingRecommendations.mockResolvedValue({
      recommendations: [{
        recommendationId: 'rec1', recommendationType: 'TeamChange', productArea: 'Auth',
        currentTargetTeam: 'Engineering', suggestedTargetTeam: 'Security',
        currentThreshold: null, suggestedThreshold: null,
        reason: 'High reroute rate', confidence: 0.75, supportingOutcomeCount: 12,
        status: 'Pending', createdAt: '2026-03-19T00:00:00Z', appliedAt: null, appliedBy: null,
      }],
      totalCount: 1,
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getRoutingAnalytics).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Recommendations'));
    await waitFor(() => {
      expect(screen.getByText('TeamChange')).toBeInTheDocument();
      expect(screen.getByText('Team: Security')).toBeInTheDocument();
    });
  });

  it('renders navigation links', () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getRoutingAnalytics.mockResolvedValue({
      tenantId: 't1', totalOutcomes: 0, totalEscalations: 0, totalReroutes: 0,
      totalResolvedWithoutEscalation: 0, overallAcceptanceRate: 0, overallRerouteRate: 0,
      selfResolutionRate: 0, teamMetrics: [], productAreaMetrics: [],
      computedAt: '2026-03-19T00:00:00Z', windowStart: null, windowEnd: null,
    });
    renderPage();
    expect(screen.getByText('Connectors')).toBeInTheDocument();
    expect(screen.getByText('Diagnostics')).toBeInTheDocument();
    expect(screen.getByText('Playbooks')).toBeInTheDocument();
  });
});
