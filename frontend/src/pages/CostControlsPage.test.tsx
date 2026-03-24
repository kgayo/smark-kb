import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { CostControlsPage } from './CostControlsPage';
import * as api from '../api/client';

vi.mock('../api/client', () => ({
  getTokenUsageSummary: vi.fn(),
  getDailyUsage: vi.fn(),
  getCostSettings: vi.fn(),
  updateCostSettings: vi.fn(),
  resetCostSettings: vi.fn(),
  getBudgetCheck: vi.fn(),
}));

vi.mock('../auth/useRoles', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../auth/useRoles')>();
  return { ...actual, useRoles: vi.fn() };
});

import { useRoles } from '../auth/useRoles';
const mockedUseRoles = vi.mocked(useRoles);
const mockedApi = vi.mocked(api);

function renderPage() {
  return render(<MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}><CostControlsPage /></MemoryRouter>);
}

beforeEach(() => {
  vi.clearAllMocks();
});

const sampleSummary = {
  tenantId: 't1',
  periodStart: '2026-03-01T00:00:00Z',
  periodEnd: '2026-03-19T00:00:00Z',
  totalPromptTokens: 150000,
  totalCompletionTokens: 50000,
  totalTokens: 200000,
  totalEmbeddingTokens: 30000,
  totalRequests: 100,
  embeddingCacheHits: 40,
  embeddingCacheMisses: 60,
  totalEstimatedCostUsd: 5.25,
  dailyTokenBudget: 500000,
  monthlyTokenBudget: 10000000,
  dailyBudgetUtilizationPercent: 40,
  monthlyBudgetUtilizationPercent: 2,
};

describe('CostControlsPage', () => {
  it('shows loading state', () => {
    mockedUseRoles.mockReturnValue({ roles: [], loading: true });
    renderPage();
    expect(screen.getByTestId('cost-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin', () => {
    mockedUseRoles.mockReturnValue({ roles: ['SupportAgent'], loading: false });
    renderPage();
    expect(screen.getByTestId('cost-denied')).toBeInTheDocument();
  });

  it('loads and displays usage summary', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockResolvedValue(sampleSummary);
    mockedApi.getDailyUsage.mockResolvedValue([]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('200.0K')).toBeInTheDocument();
      expect(screen.getByText('100')).toBeInTheDocument();
      expect(screen.getByText('$5.25')).toBeInTheDocument();
    });
  });

  it('shows cache hit rate', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockResolvedValue(sampleSummary);
    mockedApi.getDailyUsage.mockResolvedValue([]);
    renderPage();
    await waitFor(() => {
      const cards = screen.getAllByText('40.0%');
      expect(cards.length).toBeGreaterThanOrEqual(1);
    });
  });

  it('tab buttons have aria-labels', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockResolvedValue(sampleSummary);
    mockedApi.getDailyUsage.mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(mockedApi.getTokenUsageSummary).toHaveBeenCalled());
    expect(screen.getByLabelText('Token Usage tab')).toBeInTheDocument();
    expect(screen.getByLabelText('Settings tab')).toBeInTheDocument();
    expect(screen.getByLabelText('Budget Status tab')).toBeInTheDocument();
  });

  it('switches to settings tab', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockResolvedValue(sampleSummary);
    mockedApi.getDailyUsage.mockResolvedValue([]);
    mockedApi.getCostSettings.mockResolvedValue({
      tenantId: 't1',
      dailyTokenBudget: 500000,
      monthlyTokenBudget: 10000000,
      maxPromptTokensPerQuery: null,
      maxEvidenceChunksInPrompt: 10,
      enableEmbeddingCache: true,
      embeddingCacheTtlHours: 24,
      enableRetrievalCompression: false,
      maxChunkCharsCompressed: 1500,
      budgetAlertThresholdPercent: 80,
      hasOverrides: true,
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getTokenUsageSummary).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Settings'));
    await waitFor(() => {
      expect(screen.getByText(/Daily Token Budget/)).toBeInTheDocument();
      expect(screen.getByText('Edit Settings')).toBeInTheDocument();
    });
  });

  it('switches to budget tab', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockResolvedValue(sampleSummary);
    mockedApi.getDailyUsage.mockResolvedValue([]);
    mockedApi.getBudgetCheck.mockResolvedValue({
      allowed: true,
      denialReason: null,
      dailyUtilizationPercent: 40,
      monthlyUtilizationPercent: 2,
      budgetWarning: false,
      warningMessage: null,
    });
    renderPage();
    await waitFor(() => expect(mockedApi.getTokenUsageSummary).toHaveBeenCalled());

    fireEvent.click(screen.getByText('Budget Status'));
    await waitFor(() => {
      expect(screen.getByText('Allowed')).toBeInTheDocument();
    });
  });

  it('shows budget warning', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockResolvedValue(sampleSummary);
    mockedApi.getDailyUsage.mockResolvedValue([]);
    mockedApi.getBudgetCheck.mockResolvedValue({
      allowed: true,
      denialReason: null,
      dailyUtilizationPercent: 85,
      monthlyUtilizationPercent: 50,
      budgetWarning: true,
      warningMessage: 'Approaching daily token budget limit (85%)',
    });
    renderPage();
    fireEvent.click(screen.getByText('Budget Status'));
    await waitFor(() => {
      expect(screen.getByTestId('budget-warning')).toBeInTheDocument();
      expect(screen.getByText(/Approaching daily/)).toBeInTheDocument();
    });
  });

  it('shows error on usage load failure', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockRejectedValue(new Error('Network error'));
    mockedApi.getDailyUsage.mockRejectedValue(new Error('Network error'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('cost-error')).toBeInTheDocument();
    });
  });

  it('has aria-label on usage period select', async () => {
    mockedUseRoles.mockReturnValue({ roles: ['Admin'], loading: false });
    mockedApi.getTokenUsageSummary.mockResolvedValue(sampleSummary);
    mockedApi.getDailyUsage.mockResolvedValue([]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByLabelText('Usage period')).toBeInTheDocument();
    });
  });
});
