import { useState } from 'react';
import type { FieldMappingConfig, FieldMappingRule, FieldTransformType } from '../api/types';

const TRANSFORM_TYPES: FieldTransformType[] = [
  'Direct',
  'Template',
  'Regex',
  'Lookup',
  'Constant',
];

interface FieldMappingEditorProps {
  mapping: FieldMappingConfig | null;
  onChange: (mapping: FieldMappingConfig) => void;
  readOnly?: boolean;
}

export function FieldMappingEditor({
  mapping,
  onChange,
  readOnly = false,
}: FieldMappingEditorProps) {
  const rules = mapping?.rules ?? [];
  const [editIndex, setEditIndex] = useState<number | null>(null);

  function addRule() {
    const newRule: FieldMappingRule = {
      sourceField: '',
      targetField: '',
      transform: 'Direct',
      transformExpression: null,
      isRequired: false,
      defaultValue: null,
    };
    onChange({ rules: [...rules, newRule] });
    setEditIndex(rules.length);
  }

  function updateRule(index: number, patch: Partial<FieldMappingRule>) {
    const updated = rules.map((r, i) => (i === index ? { ...r, ...patch } : r));
    onChange({ rules: updated });
  }

  function removeRule(index: number) {
    onChange({ rules: rules.filter((_, i) => i !== index) });
    if (editIndex === index) setEditIndex(null);
  }

  return (
    <div className="field-mapping-editor" data-testid="field-mapping-editor">
      <div className="mapping-header">
        <h4>Field Mapping</h4>
        {!readOnly && (
          <button className="btn btn-sm" onClick={addRule} data-testid="add-mapping-rule">
            + Add Rule
          </button>
        )}
      </div>

      {rules.length === 0 ? (
        <p className="mapping-empty">No field mappings configured. Default mapping will be used.</p>
      ) : (
        <table className="mapping-table" data-testid="mapping-table">
          <thead>
            <tr>
              <th>Source</th>
              <th>Target</th>
              <th>Transform</th>
              <th>Required</th>
              {!readOnly && <th>Actions</th>}
            </tr>
          </thead>
          <tbody>
            {rules.map((rule, i) => (
              <tr key={i} data-testid={`mapping-row-${i}`}>
                <td>
                  {editIndex === i && !readOnly ? (
                    <input
                      type="text"
                      value={rule.sourceField}
                      onChange={(e) => updateRule(i, { sourceField: e.target.value })}
                      placeholder="source_field"
                      className="mapping-input"
                      data-testid={`source-field-${i}`}
                    />
                  ) : (
                    <span
                      onClick={() => !readOnly && setEditIndex(i)}
                      className="mapping-cell-text"
                    >
                      {rule.sourceField || '(empty)'}
                    </span>
                  )}
                </td>
                <td>
                  {editIndex === i && !readOnly ? (
                    <input
                      type="text"
                      value={rule.targetField}
                      onChange={(e) => updateRule(i, { targetField: e.target.value })}
                      placeholder="target_field"
                      className="mapping-input"
                      data-testid={`target-field-${i}`}
                    />
                  ) : (
                    <span
                      onClick={() => !readOnly && setEditIndex(i)}
                      className="mapping-cell-text"
                    >
                      {rule.targetField || '(empty)'}
                    </span>
                  )}
                </td>
                <td>
                  {editIndex === i && !readOnly ? (
                    <select
                      value={rule.transform}
                      onChange={(e) =>
                        updateRule(i, { transform: e.target.value as FieldTransformType })
                      }
                      className="mapping-select"
                    >
                      {TRANSFORM_TYPES.map((t) => (
                        <option key={t} value={t}>
                          {t}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <span onClick={() => !readOnly && setEditIndex(i)} className="mapping-cell-text">
                      {rule.transform}
                    </span>
                  )}
                </td>
                <td>
                  {editIndex === i && !readOnly ? (
                    <input
                      type="checkbox"
                      checked={rule.isRequired}
                      onChange={(e) => updateRule(i, { isRequired: e.target.checked })}
                    />
                  ) : (
                    <span>{rule.isRequired ? 'Yes' : 'No'}</span>
                  )}
                </td>
                {!readOnly && (
                  <td>
                    <button
                      className="btn btn-sm"
                      onClick={() => removeRule(i)}
                      data-testid={`remove-rule-${i}`}
                    >
                      Remove
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
