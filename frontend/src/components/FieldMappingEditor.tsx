import { useState } from 'react';
import type { FieldMappingConfig, FieldMappingRule, FieldTransformType, RoutingTagName } from '../api/types';
import { ROUTING_TAG_OPTIONS } from '../api/types';

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
      routingTag: null,
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
    else if (editIndex !== null && index < editIndex) setEditIndex(editIndex - 1);
  }

  function routingTagLabel(tag: RoutingTagName | null): string {
    if (!tag) return '\u2014';
    const opt = ROUTING_TAG_OPTIONS.find((o) => o.value === tag);
    return opt ? opt.label : tag;
  }

  return (
    <div className="field-mapping-editor" data-testid="field-mapping-editor">
      <div className="mapping-header">
        <h4>Field Mapping</h4>
        {!readOnly && (
          <button className="btn btn-sm" onClick={addRule} data-testid="add-mapping-rule" aria-label="Add field mapping rule">
            + Add Rule
          </button>
        )}
      </div>

      {rules.length === 0 ? (
        <p className="mapping-empty">No field mappings configured. Default mapping will be used.</p>
      ) : (
        <table className="mapping-table" data-testid="mapping-table" aria-label="Field mapping rules">
          <thead>
            <tr>
              <th>Source</th>
              <th>Target</th>
              <th>Transform</th>
              <th>Routing Tag</th>
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
                      aria-label={`Source field for rule ${i + 1}`}
                    />
                  ) : (
                    <button
                      type="button"
                      onClick={() => !readOnly && setEditIndex(i)}
                      className="mapping-cell-text"
                      aria-label={`Edit source field for rule ${i + 1}`}
                    >
                      {rule.sourceField || '(empty)'}
                    </button>
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
                      aria-label={`Target field for rule ${i + 1}`}
                    />
                  ) : (
                    <button
                      type="button"
                      onClick={() => !readOnly && setEditIndex(i)}
                      className="mapping-cell-text"
                      aria-label={`Edit target field for rule ${i + 1}`}
                    >
                      {rule.targetField || '(empty)'}
                    </button>
                  )}
                </td>
                <td>
                  {editIndex === i && !readOnly ? (
                    <select
                      value={rule.transform}
                      aria-label={`Transform type for rule ${i + 1}`}
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
                    <button type="button" onClick={() => !readOnly && setEditIndex(i)} className="mapping-cell-text" aria-label={`Edit transform for rule ${i + 1}`}>
                      {rule.transform}
                    </button>
                  )}
                </td>
                <td>
                  {editIndex === i && !readOnly ? (
                    <select
                      value={rule.routingTag ?? ''}
                      aria-label={`Routing tag for rule ${i + 1}`}
                      onChange={(e) =>
                        updateRule(i, {
                          routingTag: (e.target.value as RoutingTagName) || null,
                        })
                      }
                      className="mapping-select"
                      data-testid={`routing-tag-${i}`}
                    >
                      <option value="">None</option>
                      {ROUTING_TAG_OPTIONS.map((opt) => (
                        <option key={opt.value} value={opt.value}>
                          {opt.label}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <button
                      type="button"
                      onClick={() => !readOnly && setEditIndex(i)}
                      className="mapping-cell-text"
                      aria-label={`Edit routing tag for rule ${i + 1}`}
                    >
                      {routingTagLabel(rule.routingTag)}
                    </button>
                  )}
                </td>
                <td>
                  {editIndex === i && !readOnly ? (
                    <input
                      type="checkbox"
                      checked={rule.isRequired}
                      onChange={(e) => updateRule(i, { isRequired: e.target.checked })}
                      aria-label={`Required flag for rule ${i + 1}`}
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
                      aria-label={`Remove rule ${i + 1}`}
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
