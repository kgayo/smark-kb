import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { GoldDatasetPage } from './GoldDatasetPage';
import * as client from '../api/client';
import type { GoldCaseListResponse, GoldCaseDetail } from '../api/types';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
  listGoldCases: vi.fn(),
  getGoldCase: vi.fn(),
  createGoldCase: vi.fn(),
  deleteGoldCase: vi.fn(),
  exportGoldCases: vi.fn(),
}));

const mockedClient = vi.mocked(client);

function renderPage() {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <GoldDatasetPage />
    </MemoryRouter>,
  );
}

const mockCases: GoldCaseListResponse = {
  cases: [
    {
      id: 'id-001',
      caseId: 'eval-00100',
      query: 'How do I reset my password after lockout?',
      responseType: 'final_answer',
      tags: ['auth', 'password'],
      sourceFeedbackId: null,
      createdBy: 'admin-1',
      createdAt: '2026-03-19T10:00:00Z',
      updatedAt: '2026-03-19T10:00:00Z',
    },
    {
      id: 'id-002',
      caseId: 'eval-00200',
      query: 'Billing invoice shows double charges',
      responseType: 'final_answer',
      tags: ['billing'],
      sourceFeedbackId: null,
      createdBy: 'admin-1',
      createdAt: '2026-03-19T11:00:00Z',
      updatedAt: '2026-03-19T11:00:00Z',
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 20,
  hasMore: false,
};

const mockDetail: GoldCaseDetail = {
  id: 'id-001',
  caseId: 'eval-00100',
  query: 'How do I reset my password after lockout?',
  context: null,
  expected: {
    responseType: 'final_answer',
    mustInclude: ['password', 'reset'],
    mustCiteSources: true,
    shouldHaveEvidence: true,
  },
  tags: ['auth', 'password'],
  sourceFeedbackId: null,
  createdBy: 'admin-1',
  updatedBy: null,
  createdAt: '2026-03-19T10:00:00Z',
  updatedAt: '2026-03-19T10:00:00Z',
};

function setupAdminUser() {
  mockedClient.getMe.mockResolvedValue({
    userId: 'u1',
    name: 'Admin',
    tenantId: 't1',
    correlationId: null,
    roles: ['Admin'],
  });
}

describe('GoldDatasetPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading state initially', () => {
    mockedClient.getMe.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByTestId('gold-loading')).toBeInTheDocument();
  });

  it('shows access denied for non-admin', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Agent',
      tenantId: 't1',
      correlationId: null,
      roles: ['SupportAgent'],
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-denied')).toBeInTheDocument();
    });
  });

  it('renders gold dataset page for admin', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-dataset-page')).toBeInTheDocument();
    });
  });

  it('loads and displays cases table', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('cases-table')).toBeInTheDocument();
    });
    expect(screen.getByTestId('case-row-eval-00100')).toBeInTheDocument();
    expect(screen.getByTestId('case-row-eval-00200')).toBeInTheDocument();
  });

  it('shows error on load failure', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockRejectedValue(new Error('Network error'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('cases-error')).toBeInTheDocument();
      expect(screen.getByTestId('cases-error')).toHaveAttribute('role', 'alert');
    });
  });

  it('shows empty state', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue({
      cases: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('No gold cases found.')).toBeInTheDocument();
    });
  });

  it('selects and shows case detail', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    mockedClient.getGoldCase.mockResolvedValue(mockDetail);
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('case-row-eval-00100')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('case-row-eval-00100'));
    await waitFor(() => {
      expect(screen.getByTestId('case-detail')).toBeInTheDocument();
    });
    expect(screen.getByText('password, reset')).toBeInTheDocument();
  });

  it('deletes a case', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    mockedClient.getGoldCase.mockResolvedValue(mockDetail);
    mockedClient.deleteGoldCase.mockResolvedValue(undefined);
    window.confirm = vi.fn(() => true);

    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('case-row-eval-00100')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('case-row-eval-00100'));
    await waitFor(() => {
      expect(screen.getByTestId('delete-case-btn')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('delete-case-btn'));

    await waitFor(() => {
      expect(mockedClient.deleteGoldCase).toHaveBeenCalledWith('id-001');
    });
  });

  it('switches to create tab', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-tabs')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('tab-create'));
    expect(screen.getByTestId('create-panel')).toBeInTheDocument();
  });

  it('creates a gold case', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    mockedClient.createGoldCase.mockResolvedValue(mockDetail);
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-tabs')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-create'));

    fireEvent.change(screen.getByTestId('create-case-id'), { target: { value: 'eval-00100' } });
    fireEvent.change(screen.getByTestId('create-query'), { target: { value: 'How do I reset my password after lockout?' } });
    fireEvent.click(screen.getByTestId('create-submit-btn'));

    await waitFor(() => {
      expect(mockedClient.createGoldCase).toHaveBeenCalled();
    });
    expect(screen.getByTestId('create-success')).toBeInTheDocument();
  });

  it('shows create error', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    mockedClient.createGoldCase.mockRejectedValue(new Error('Duplicate case'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-tabs')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('tab-create'));
    fireEvent.change(screen.getByTestId('create-case-id'), { target: { value: 'eval-00100' } });
    fireEvent.change(screen.getByTestId('create-query'), { target: { value: 'Some query text here' } });
    fireEvent.click(screen.getByTestId('create-submit-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('create-error')).toBeInTheDocument();
      expect(screen.getByTestId('create-error')).toHaveAttribute('role', 'alert');
    });
  });

  it('switches to export tab and downloads', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    mockedClient.exportGoldCases.mockResolvedValue('{"id":"eval-00100"}\n{"id":"eval-00200"}');
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-tabs')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('tab-export'));
    expect(screen.getByTestId('export-panel')).toBeInTheDocument();

    // Mock URL.createObjectURL
    const createObjectURL = vi.fn(() => 'blob:test');
    const revokeObjectURL = vi.fn();
    Object.defineProperty(globalThis, 'URL', {
      value: { createObjectURL, revokeObjectURL },
      writable: true,
    });

    fireEvent.click(screen.getByTestId('export-download-btn'));
    await waitFor(() => {
      expect(mockedClient.exportGoldCases).toHaveBeenCalled();
    });
  });

  it('shows export error', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    mockedClient.exportGoldCases.mockRejectedValue(new Error('Export failed'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-tabs')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('tab-export'));
    fireEvent.click(screen.getByTestId('export-download-btn'));
    await waitFor(() => {
      expect(screen.getByTestId('export-error')).toBeInTheDocument();
      expect(screen.getByTestId('export-error')).toHaveAttribute('role', 'alert');
    });
  });

  it('shows tag filter', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('filter-tag')).toBeInTheDocument();
    });
    fireEvent.change(screen.getByTestId('filter-tag'), { target: { value: 'auth' } });
    await waitFor(() => {
      expect(mockedClient.listGoldCases).toHaveBeenCalledWith('auth', 1, 20);
    });
  });

  it('has navigation links', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Connectors')).toBeInTheDocument();
      expect(screen.getByText('Diagnostics')).toBeInTheDocument();
      expect(screen.getByText('Audit')).toBeInTheDocument();
    });
  });

  it('has aria-label on response type select in create form', async () => {
    setupAdminUser();
    mockedClient.listGoldCases.mockResolvedValue(mockCases);
    renderPage();
    await waitFor(() => {
      expect(screen.getByTestId('gold-tabs')).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId('tab-create'));
    expect(screen.getByLabelText('Expected response type')).toBeInTheDocument();
  });
});
