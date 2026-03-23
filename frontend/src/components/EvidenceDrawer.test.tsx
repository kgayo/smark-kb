import { render, screen, fireEvent } from '@testing-library/react';
import { EvidenceDrawer } from './EvidenceDrawer';
import type { CitationDto } from '../api/types';

const mockCitation: CitationDto = {
  chunkId: 'chunk_001',
  evidenceId: 'ev_001',
  title: 'Deployment Guide',
  sourceUrl: 'https://dev.azure.com/org/project/_wiki/pages/deploy',
  sourceSystem: 'AzureDevOps',
  snippet: 'To deploy the application, run the following commands...',
  updatedAt: '2026-03-15T10:00:00Z',
  accessLabel: 'Internal',
};

describe('EvidenceDrawer', () => {
  it('does not render when closed', () => {
    render(<EvidenceDrawer citations={[mockCitation]} open={false} onClose={() => {}} />);
    expect(screen.queryByTestId('evidence-drawer')).not.toBeInTheDocument();
  });

  it('renders citation cards when open', () => {
    render(<EvidenceDrawer citations={[mockCitation]} open={true} onClose={() => {}} />);
    expect(screen.getByTestId('evidence-drawer')).toBeInTheDocument();
    expect(screen.getByText('Deployment Guide')).toBeInTheDocument();
    expect(screen.getByText(/To deploy the application/)).toBeInTheDocument();
  });

  it('shows source system and access label', () => {
    render(<EvidenceDrawer citations={[mockCitation]} open={true} onClose={() => {}} />);
    expect(screen.getByText('AzureDevOps')).toBeInTheDocument();
    expect(screen.getByText('Internal')).toBeInTheDocument();
  });

  it('shows formatted date', () => {
    render(<EvidenceDrawer citations={[mockCitation]} open={true} onClose={() => {}} />);
    const drawer = screen.getByTestId('evidence-drawer');
    expect(drawer).toBeInTheDocument();
  });

  it('renders external source URL as link', () => {
    render(<EvidenceDrawer citations={[mockCitation]} open={true} onClose={() => {}} />);
    const link = screen.getByText('Open external');
    expect(link).toHaveAttribute('href', mockCitation.sourceUrl);
    expect(link).toHaveAttribute('target', '_blank');
  });

  it('renders View content button for citation drill-down', () => {
    render(<EvidenceDrawer citations={[mockCitation]} open={true} onClose={() => {}} />);
    expect(screen.getByTestId('view-source-btn')).toBeInTheDocument();
    expect(screen.getByText('View content')).toBeInTheDocument();
  });

  it('shows citation count in header', () => {
    render(<EvidenceDrawer citations={[mockCitation]} open={true} onClose={() => {}} />);
    expect(screen.getByText('Evidence (1)')).toBeInTheDocument();
  });

  it('calls onClose when close button clicked', () => {
    const onClose = vi.fn();
    render(<EvidenceDrawer citations={[mockCitation]} open={true} onClose={onClose} />);
    fireEvent.click(screen.getByTestId('evidence-drawer-close'));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('shows empty state when no citations', () => {
    render(<EvidenceDrawer citations={[]} open={true} onClose={() => {}} />);
    expect(screen.getByText('No citations available.')).toBeInTheDocument();
  });

  it('renders multiple citations', () => {
    const citations = [
      mockCitation,
      { ...mockCitation, chunkId: 'chunk_002', title: 'Troubleshooting FAQ' },
    ];
    render(<EvidenceDrawer citations={citations} open={true} onClose={() => {}} />);
    expect(screen.getAllByTestId('citation-card')).toHaveLength(2);
    expect(screen.getByText('Evidence (2)')).toBeInTheDocument();
  });
});
