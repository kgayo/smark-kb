import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { SourceViewerPanel } from './SourceViewerPanel';
import type { EvidenceContentResponse } from '../api/types';
import * as api from '../api/client';

vi.mock('../api/client');

const mockContent: EvidenceContentResponse = {
  chunkId: 'ev1_chunk_0',
  evidenceId: 'ev1',
  title: 'Auth Token Cache Reset',
  sourceUrl: 'https://dev.azure.com/org/project/_workitems/edit/42',
  sourceSystem: 'AzureDevOps',
  sourceType: 'Ticket',
  chunkText: 'Resolution: Reset the auth token cache by running...',
  chunkContext: 'Root > Auth > Token Management',
  rawContent: null,
  contentType: null,
  updatedAt: '2026-03-15T10:00:00Z',
  accessLabel: 'Internal',
  productArea: 'Authentication',
  tags: ['auth', 'cache'],
};

describe('SourceViewerPanel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows loading state initially', () => {
    vi.mocked(api.getEvidenceContent).mockReturnValue(new Promise(() => {}));
    render(<SourceViewerPanel chunkId="chunk1" onBack={() => {}} />);
    expect(screen.getByTestId('source-viewer-loading')).toBeInTheDocument();
    expect(screen.getByText('Loading evidence content...')).toBeInTheDocument();
  });

  it('renders content after loading', async () => {
    vi.mocked(api.getEvidenceContent).mockResolvedValue(mockContent);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-title')).toBeInTheDocument();
    });

    expect(screen.getByText('Auth Token Cache Reset')).toBeInTheDocument();
    expect(screen.getByText(/Resolution: Reset the auth token cache/)).toBeInTheDocument();
    expect(screen.getByText('Ticket')).toBeInTheDocument();
    expect(screen.getByText('AzureDevOps')).toBeInTheDocument();
    expect(screen.getByText('Internal')).toBeInTheDocument();
    expect(screen.getByText('Authentication')).toBeInTheDocument();
  });

  it('renders tags', async () => {
    vi.mocked(api.getEvidenceContent).mockResolvedValue(mockContent);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-tags')).toBeInTheDocument();
    });

    expect(screen.getByText('auth')).toBeInTheDocument();
    expect(screen.getByText('cache')).toBeInTheDocument();
  });

  it('renders chunk context section path', async () => {
    vi.mocked(api.getEvidenceContent).mockResolvedValue(mockContent);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-context')).toBeInTheDocument();
    });

    expect(screen.getByText(/Root > Auth > Token Management/)).toBeInTheDocument();
  });

  it('shows error state on API failure', async () => {
    vi.mocked(api.getEvidenceContent).mockRejectedValue(new Error('Network error'));
    render(<SourceViewerPanel chunkId="bad-chunk" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-error')).toBeInTheDocument();
    });

    expect(screen.getByText('Network error')).toBeInTheDocument();
  });

  it('calls onBack when back button clicked and has aria-label', async () => {
    vi.mocked(api.getEvidenceContent).mockResolvedValue(mockContent);
    const onBack = vi.fn();
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={onBack} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-title')).toBeInTheDocument();
    });

    const backBtn = screen.getByTestId('source-viewer-back');
    expect(backBtn).toHaveAttribute('aria-label', 'Back to citations');
    fireEvent.click(backBtn);
    expect(onBack).toHaveBeenCalledOnce();
  });

  it('renders copy citation link button', async () => {
    vi.mocked(api.getEvidenceContent).mockResolvedValue(mockContent);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('copy-citation-link')).toBeInTheDocument();
    });

    expect(screen.getByText('Copy citation link')).toBeInTheDocument();
  });

  it('renders open external link with aria-label', async () => {
    vi.mocked(api.getEvidenceContent).mockResolvedValue(mockContent);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('open-external')).toBeInTheDocument();
    });

    const link = screen.getByTestId('open-external');
    expect(link).toHaveAttribute('href', mockContent.sourceUrl);
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('aria-label', 'Open external source (opens in new tab)');
  });

  it('renders rawContent when available', async () => {
    const withRaw = {
      ...mockContent,
      rawContent: 'Full raw document text here...',
      contentType: 'text/plain',
    };
    vi.mocked(api.getEvidenceContent).mockResolvedValue(withRaw);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-content')).toBeInTheDocument();
    });

    expect(screen.getByText('Full raw document text here...')).toBeInTheDocument();
  });

  it('renders WikiPage with wiki content class', async () => {
    const wikiContent = { ...mockContent, sourceType: 'WikiPage' };
    vi.mocked(api.getEvidenceContent).mockResolvedValue(wikiContent);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-content')).toBeInTheDocument();
    });

    const contentDiv = screen.getByTestId('source-viewer-content');
    expect(contentDiv.className).toContain('content-wiki');
  });

  it('does not render tags section when no tags', async () => {
    const noTags = { ...mockContent, tags: [] };
    vi.mocked(api.getEvidenceContent).mockResolvedValue(noTags);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-title')).toBeInTheDocument();
    });

    expect(screen.queryByTestId('source-viewer-tags')).not.toBeInTheDocument();
  });

  it('does not render context when null', async () => {
    const noContext = { ...mockContent, chunkContext: null };
    vi.mocked(api.getEvidenceContent).mockResolvedValue(noContext);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-title')).toBeInTheDocument();
    });

    expect(screen.queryByTestId('source-viewer-context')).not.toBeInTheDocument();
  });

  it('clears copy timer on unmount to prevent memory leak', async () => {
    vi.mocked(api.getEvidenceContent).mockResolvedValue(mockContent);
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    });

    const { unmount } = render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('copy-citation-link')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId('copy-citation-link'));

    await waitFor(() => {
      expect(screen.getByText('Copied!')).toBeInTheDocument();
    });

    // Unmount before the 2s timer fires — should not throw or warn about state updates on unmounted component
    unmount();
  });

  it('does not render open external when no sourceUrl', async () => {
    const noUrl = { ...mockContent, sourceUrl: '' };
    vi.mocked(api.getEvidenceContent).mockResolvedValue(noUrl);
    render(<SourceViewerPanel chunkId="ev1_chunk_0" onBack={() => {}} />);

    await waitFor(() => {
      expect(screen.getByTestId('source-viewer-title')).toBeInTheDocument();
    });

    expect(screen.queryByTestId('open-external')).not.toBeInTheDocument();
  });
});
