import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { SynonymManagementPage } from './SynonymManagementPage';
import * as client from '../api/client';

vi.mock('../api/client', () => ({
  getMe: vi.fn(),
  listSynonymRules: vi.fn(),
  createSynonymRule: vi.fn(),
  updateSynonymRule: vi.fn(),
  deleteSynonymRule: vi.fn(),
  syncSynonymMaps: vi.fn(),
  seedSynonymRules: vi.fn(),
  listStopWords: vi.fn(),
  createStopWord: vi.fn(),
  updateStopWord: vi.fn(),
  deleteStopWord: vi.fn(),
  seedStopWords: vi.fn(),
  listSpecialTokens: vi.fn(),
  createSpecialToken: vi.fn(),
  updateSpecialToken: vi.fn(),
  deleteSpecialToken: vi.fn(),
  seedSpecialTokens: vi.fn(),
}));

const mockedClient = vi.mocked(client);

const adminUser = {
  userId: 'u1',
  name: 'Admin',
  tenantId: 't1',
  correlationId: null,
  roles: ['Admin'],
};

function renderWithRouter() {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <SynonymManagementPage />
    </MemoryRouter>,
  );
}

describe('SynonymManagementPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedClient.listSynonymRules.mockResolvedValue({ rules: [], totalCount: 0, groups: [] });
    mockedClient.listStopWords.mockResolvedValue({ words: [], totalCount: 0, groups: [] });
    mockedClient.listSpecialTokens.mockResolvedValue({ tokens: [], totalCount: 0, categories: [] });
  });

  it('shows loading state initially', () => {
    mockedClient.getMe.mockReturnValue(new Promise(() => {}));
    renderWithRouter();
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('shows access denied for non-admin users', async () => {
    mockedClient.getMe.mockResolvedValue({
      userId: 'u1',
      name: 'Agent',
      tenantId: 't1',
      correlationId: null,
      roles: ['SupportAgent'],
    });
    renderWithRouter();
    await waitFor(() => {
      expect(screen.getByText(/Access denied/)).toBeInTheDocument();
    });
  });

  it('renders tabs for Synonyms, Stop Words, Special Tokens', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText('Synonyms')).toBeInTheDocument();
    });
    expect(screen.getByText('Stop Words')).toBeInTheDocument();
    expect(screen.getByText('Special Tokens')).toBeInTheDocument();
  });

  it('shows Synonyms tab by default with synonym rules', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    mockedClient.listSynonymRules.mockResolvedValue({
      rules: [
        {
          id: 'rule-1',
          tenantId: 't1',
          groupName: 'general',
          rule: 'crash, BSOD, blue screen',
          description: 'Crash synonyms',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          updatedAt: '2024-01-01T00:00:00Z',
          createdBy: 'admin-1',
          updatedBy: null,
        },
      ],
      totalCount: 1,
      groups: ['general'],
    });

    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText('crash, BSOD, blue screen')).toBeInTheDocument();
    });
    expect(screen.getByText('Crash synonyms')).toBeInTheDocument();
  });

  it('shows empty state for synonyms with seed prompt', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText(/No synonym rules found/)).toBeInTheDocument();
    });
  });

  it('renders navigation links', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText('Connectors')).toBeInTheDocument();
    });
    expect(screen.getByText('Patterns')).toBeInTheDocument();
    expect(screen.getByText('Diagnostics')).toBeInTheDocument();
    expect(screen.getByText('Chat')).toBeInTheDocument();
  });

  // ── Stop Words Tab ──

  it('switches to Stop Words tab and shows empty state', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText('Stop Words')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Stop Words'));

    await waitFor(() => {
      expect(screen.getByText(/No stop words configured/)).toBeInTheDocument();
    });
  });

  it('displays stop words in table', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    mockedClient.listStopWords.mockResolvedValue({
      words: [
        {
          id: 'sw-1',
          tenantId: 't1',
          word: 'hello',
          groupName: 'greeting',
          isActive: true,
          createdAt: '2024-01-01T00:00:00Z',
          updatedAt: '2024-01-01T00:00:00Z',
          createdBy: 'admin-1',
        },
      ],
      totalCount: 1,
      groups: ['greeting'],
    });

    renderWithRouter();
    await waitFor(() => { expect(screen.getByText('Stop Words')).toBeInTheDocument(); });
    fireEvent.click(screen.getByText('Stop Words'));

    await waitFor(() => {
      expect(screen.getByText('hello')).toBeInTheDocument();
    });
  });

  it('seeds stop words on button click', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    mockedClient.seedStopWords.mockResolvedValue({ seeded: 15 });
    mockedClient.listStopWords.mockResolvedValue({ words: [], totalCount: 0, groups: [] });

    renderWithRouter();
    await waitFor(() => { expect(screen.getByText('Stop Words')).toBeInTheDocument(); });
    fireEvent.click(screen.getByText('Stop Words'));

    await waitFor(() => {
      const seedButtons = screen.getAllByText('Seed Defaults');
      expect(seedButtons.length).toBeGreaterThan(0);
    });

    const seedButtons = screen.getAllByText('Seed Defaults');
    fireEvent.click(seedButtons[0]);

    await waitFor(() => {
      expect(mockedClient.seedStopWords).toHaveBeenCalledWith(false);
    });
  });

  // ── Special Tokens Tab ──

  it('switches to Special Tokens tab and shows empty state', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();

    await waitFor(() => {
      expect(screen.getByText('Special Tokens')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Special Tokens'));

    await waitFor(() => {
      expect(screen.getByText(/No special tokens configured/)).toBeInTheDocument();
    });
  });

  it('displays special tokens in table', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    mockedClient.listSpecialTokens.mockResolvedValue({
      tokens: [
        {
          id: 'st-1',
          tenantId: 't1',
          token: 'HTTP 500',
          category: 'http-status',
          boostFactor: 2,
          isActive: true,
          description: 'Internal Server Error',
          createdAt: '2024-01-01T00:00:00Z',
          updatedAt: '2024-01-01T00:00:00Z',
          createdBy: 'admin-1',
        },
      ],
      totalCount: 1,
      categories: ['http-status'],
    });

    renderWithRouter();
    await waitFor(() => { expect(screen.getByText('Special Tokens')).toBeInTheDocument(); });
    fireEvent.click(screen.getByText('Special Tokens'));

    await waitFor(() => {
      expect(screen.getByText('HTTP 500')).toBeInTheDocument();
    });
    expect(screen.getByText('Internal Server Error')).toBeInTheDocument();
    expect(screen.getByText('2x')).toBeInTheDocument();
  });

  it('shows create form for special tokens', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();
    await waitFor(() => { expect(screen.getByText('Special Tokens')).toBeInTheDocument(); });
    fireEvent.click(screen.getByText('Special Tokens'));

    await waitFor(() => {
      const addButtons = screen.getAllByText('Add Token');
      expect(addButtons.length).toBeGreaterThan(0);
    });

    const addButtons = screen.getAllByText('Add Token');
    fireEvent.click(addButtons[0]);

    await waitFor(() => {
      expect(screen.getByText('New Special Token')).toBeInTheDocument();
    });
  });

  it('seeds special tokens on button click', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    mockedClient.seedSpecialTokens.mockResolvedValue({ seeded: 14 });
    mockedClient.listSpecialTokens.mockResolvedValue({ tokens: [], totalCount: 0, categories: [] });

    renderWithRouter();
    await waitFor(() => { expect(screen.getByText('Special Tokens')).toBeInTheDocument(); });
    fireEvent.click(screen.getByText('Special Tokens'));

    await waitFor(() => {
      const seedButtons = screen.getAllByText('Seed Defaults');
      expect(seedButtons.length).toBeGreaterThan(0);
    });

    const seedButtons = screen.getAllByText('Seed Defaults');
    fireEvent.click(seedButtons[0]);

    await waitFor(() => {
      expect(mockedClient.seedSpecialTokens).toHaveBeenCalledWith(false);
    });
  });

  it('shows description text for Stop Words tab', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();
    await waitFor(() => { expect(screen.getByText('Stop Words')).toBeInTheDocument(); });
    fireEvent.click(screen.getByText('Stop Words'));

    await waitFor(() => {
      expect(screen.getByText(/Stop words are removed from search queries/)).toBeInTheDocument();
    });
  });

  it('shows description text for Special Tokens tab', async () => {
    mockedClient.getMe.mockResolvedValue(adminUser);
    renderWithRouter();
    await waitFor(() => { expect(screen.getByText('Special Tokens')).toBeInTheDocument(); });
    fireEvent.click(screen.getByText('Special Tokens'));

    await waitFor(() => {
      expect(screen.getByText(/Special tokens.*are preserved during query preprocessing/)).toBeInTheDocument();
    });
  });
});
