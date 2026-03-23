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
      routingTag: null,
    },
  ],
};

const mockMappingWithRoutingTag: FieldMappingConfig = {
  rules: [
    {
      sourceField: 'System.AreaPath',
      targetField: 'ProductArea',
      transform: 'Lookup',
      transformExpression: 'Auth=Authentication',
      isRequired: false,
      defaultValue: null,
      routingTag: 'product_area',
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
        expect.objectContaining({ sourceField: '', targetField: '', routingTag: null }),
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

  it('renders Routing Tag column header', () => {
    render(<FieldMappingEditor mapping={mockMapping} onChange={() => {}} />);
    expect(screen.getByText('Routing Tag')).toBeInTheDocument();
  });

  it('shows dash for null routing tag in read mode', () => {
    render(<FieldMappingEditor mapping={mockMapping} onChange={() => {}} readOnly />);
    expect(screen.getByText('\u2014')).toBeInTheDocument();
  });

  it('shows routing tag label for product_area', () => {
    render(<FieldMappingEditor mapping={mockMappingWithRoutingTag} onChange={() => {}} readOnly />);
    expect(screen.getByText('Product Area')).toBeInTheDocument();
  });

  it('renders routing tag select in edit mode', () => {
    render(<FieldMappingEditor mapping={mockMappingWithRoutingTag} onChange={() => {}} />);
    // Click to enter edit mode
    fireEvent.click(screen.getByText('Product Area'));
    expect(screen.getByTestId('routing-tag-0')).toBeInTheDocument();
  });

  it('updates routing tag via select', () => {
    const onChange = vi.fn();
    render(<FieldMappingEditor mapping={mockMappingWithRoutingTag} onChange={onChange} />);
    // Click to enter edit mode
    fireEvent.click(screen.getByText('Product Area'));
    fireEvent.change(screen.getByTestId('routing-tag-0'), {
      target: { value: 'module' },
    });
    expect(onChange).toHaveBeenCalledWith({
      rules: [expect.objectContaining({ routingTag: 'module' })],
    });
  });

  it('clears routing tag when None is selected', () => {
    const onChange = vi.fn();
    render(<FieldMappingEditor mapping={mockMappingWithRoutingTag} onChange={onChange} />);
    fireEvent.click(screen.getByText('Product Area'));
    fireEvent.change(screen.getByTestId('routing-tag-0'), {
      target: { value: '' },
    });
    expect(onChange).toHaveBeenCalledWith({
      rules: [expect.objectContaining({ routingTag: null })],
    });
  });
});
