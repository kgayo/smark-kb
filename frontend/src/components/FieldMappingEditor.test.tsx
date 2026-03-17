import { render, screen, fireEvent } from '@testing-library/react';
import { FieldMappingEditor } from './FieldMappingEditor';
import type { FieldMappingConfig } from '../api/types';

const mockMapping: FieldMappingConfig = {
  rules: [
    {
      sourceField: 'System.Title',
      targetField: 'title',
      transform: 'Direct',
      transformExpression: null,
      isRequired: true,
      defaultValue: null,
    },
  ],
};

describe('FieldMappingEditor', () => {
  it('renders empty state when no mapping', () => {
    render(<FieldMappingEditor mapping={null} onChange={() => {}} />);
    expect(screen.getByText(/No field mappings configured/)).toBeInTheDocument();
  });

  it('renders mapping table with rules', () => {
    render(<FieldMappingEditor mapping={mockMapping} onChange={() => {}} />);
    expect(screen.getByTestId('mapping-table')).toBeInTheDocument();
    expect(screen.getByText('System.Title')).toBeInTheDocument();
    expect(screen.getByText('title')).toBeInTheDocument();
    expect(screen.getByText('Direct')).toBeInTheDocument();
    expect(screen.getByText('Yes')).toBeInTheDocument();
  });

  it('adds a new rule when + Add Rule clicked', () => {
    const onChange = vi.fn();
    render(<FieldMappingEditor mapping={mockMapping} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('add-mapping-rule'));
    expect(onChange).toHaveBeenCalledWith({
      rules: expect.arrayContaining([
        expect.objectContaining({ sourceField: '', targetField: '' }),
      ]),
    });
  });

  it('removes a rule', () => {
    const onChange = vi.fn();
    render(<FieldMappingEditor mapping={mockMapping} onChange={onChange} />);
    fireEvent.click(screen.getByTestId('remove-rule-0'));
    expect(onChange).toHaveBeenCalledWith({ rules: [] });
  });

  it('hides actions in readOnly mode', () => {
    render(<FieldMappingEditor mapping={mockMapping} onChange={() => {}} readOnly />);
    expect(screen.queryByTestId('add-mapping-rule')).not.toBeInTheDocument();
    expect(screen.queryByTestId('remove-rule-0')).not.toBeInTheDocument();
  });
});
