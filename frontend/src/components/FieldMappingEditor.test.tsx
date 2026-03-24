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

  it('interactive elements have aria-labels', () => {
    render(<FieldMappingEditor mapping={mockMapping} onChange={() => {}} />);
    expect(screen.getByTestId('add-mapping-rule')).toHaveAttribute('aria-label', 'Add field mapping rule');
    expect(screen.getByTestId('remove-rule-0')).toHaveAttribute('aria-label', 'Remove rule 1');
    // Cell buttons in non-edit mode
    expect(screen.getByRole('button', { name: 'Edit source field for rule 1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit target field for rule 1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit transform for rule 1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit routing tag for rule 1' })).toBeInTheDocument();
  });

  it('adjusts editIndex when removing a row before the edited row', () => {
    const threeRuleMapping: FieldMappingConfig = {
      rules: [
        { sourceField: 'A', targetField: 'a', transform: 'Direct', transformExpression: null, isRequired: false, defaultValue: null, routingTag: null },
        { sourceField: 'B', targetField: 'b', transform: 'Direct', transformExpression: null, isRequired: false, defaultValue: null, routingTag: null },
        { sourceField: 'C', targetField: 'c', transform: 'Direct', transformExpression: null, isRequired: false, defaultValue: null, routingTag: null },
      ],
    };
    let currentMapping = threeRuleMapping;
    const onChange = vi.fn((m: FieldMappingConfig) => { currentMapping = m; });
    const { rerender } = render(<FieldMappingEditor mapping={currentMapping} onChange={onChange} />);

    // Click cell on row 2 (rule C) to enter edit mode on that row
    fireEvent.click(screen.getByRole('button', { name: 'Edit source field for rule 3' }));
    // Row 2 should now show an input for source field
    expect(screen.getByTestId('source-field-2')).toHaveValue('C');

    // Remove row 0 (rule A) — editIndex should shift from 2 to 1
    fireEvent.click(screen.getByTestId('remove-rule-0'));
    rerender(<FieldMappingEditor mapping={currentMapping} onChange={onChange} />);

    // After removal, rule C is now at index 1. The edit input should still be on rule C.
    expect(screen.getByTestId('source-field-1')).toHaveValue('C');
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
