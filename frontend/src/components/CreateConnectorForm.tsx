import { useState } from 'react';
import type {
  ConnectorType,
  CreateConnectorRequest,
  SecretAuthType,
} from '../api/types';
import { SourceConfigEditor } from './SourceConfigEditor';

const CONNECTOR_TYPES: { value: ConnectorType; label: string }[] = [
  { value: 'AzureDevOps', label: 'Azure DevOps' },
  { value: 'SharePoint', label: 'SharePoint' },
  { value: 'HubSpot', label: 'HubSpot' },
  { value: 'ClickUp', label: 'ClickUp' },
];

const AUTH_TYPES: { value: SecretAuthType; label: string }[] = [
  { value: 'Pat', label: 'Personal Access Token' },
  { value: 'OAuth', label: 'OAuth 2.0' },
  { value: 'ServiceAccount', label: 'Service Account' },
  { value: 'PrivateKey', label: 'Private Key' },
];

type WizardStep = 'type' | 'auth' | 'config' | 'review';

interface CreateConnectorFormProps {
  onSubmit: (req: CreateConnectorRequest) => Promise<void>;
  onCancel: () => void;
  submitting: boolean;
  error: string | null;
}

export function CreateConnectorForm({
  onSubmit,
  onCancel,
  submitting,
  error,
}: CreateConnectorFormProps) {
  const [step, setStep] = useState<WizardStep>('type');
  const [name, setName] = useState('');
  const [connectorType, setConnectorType] = useState<ConnectorType>('AzureDevOps');
  const [authType, setAuthType] = useState<SecretAuthType>('Pat');
  const [keyVaultSecretName, setKeyVaultSecretName] = useState('');
  const [sourceConfig, setSourceConfig] = useState('');
  const [scheduleCron, setScheduleCron] = useState('');

  function canAdvance(): boolean {
    switch (step) {
      case 'type':
        return name.trim().length > 0;
      case 'auth':
        return true;
      case 'config':
        return true;
      case 'review':
        return true;
    }
  }

  function nextStep() {
    switch (step) {
      case 'type':
        setStep('auth');
        break;
      case 'auth':
        setStep('config');
        break;
      case 'config':
        setStep('review');
        break;
    }
  }

  function prevStep() {
    switch (step) {
      case 'auth':
        setStep('type');
        break;
      case 'config':
        setStep('auth');
        break;
      case 'review':
        setStep('config');
        break;
    }
  }

  async function handleSubmit() {
    const req: CreateConnectorRequest = {
      name: name.trim(),
      connectorType,
      authType,
    };
    if (keyVaultSecretName.trim()) req.keyVaultSecretName = keyVaultSecretName.trim();
    if (sourceConfig.trim()) req.sourceConfig = sourceConfig.trim();
    if (scheduleCron.trim()) req.scheduleCron = scheduleCron.trim();
    await onSubmit(req);
  }

  const steps: WizardStep[] = ['type', 'auth', 'config', 'review'];
  const stepIndex = steps.indexOf(step);

  return (
    <div className="create-connector-form" data-testid="create-connector-form">
      <div className="wizard-header">
        <h2>New Connector</h2>
        <div className="wizard-steps">
          {steps.map((s, i) => (
            <span
              key={s}
              className={`wizard-step ${i <= stepIndex ? 'wizard-step-active' : ''} ${i === stepIndex ? 'wizard-step-current' : ''}`}
            >
              {i + 1}. {s.charAt(0).toUpperCase() + s.slice(1)}
            </span>
          ))}
        </div>
      </div>

      <div className="wizard-body">
        {step === 'type' && (
          <div className="wizard-section" data-testid="wizard-step-type">
            <div className="draft-field">
              <label className="draft-field-label">Connector Name</label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. Production ADO"
                data-testid="connector-name-input"
                aria-label="Connector name"
              />
            </div>
            <div className="draft-field">
              <label className="draft-field-label">Connector Type</label>
              <div className="connector-type-grid">
                {CONNECTOR_TYPES.map((ct) => (
                  <label
                    key={ct.value}
                    className={`connector-type-card ${connectorType === ct.value ? 'selected' : ''}`}
                  >
                    <input
                      type="radio"
                      name="connectorType"
                      value={ct.value}
                      checked={connectorType === ct.value}
                      onChange={() => setConnectorType(ct.value)}
                    />
                    <span>{ct.label}</span>
                  </label>
                ))}
              </div>
            </div>
          </div>
        )}

        {step === 'auth' && (
          <div className="wizard-section" data-testid="wizard-step-auth">
            <div className="draft-field">
              <label className="draft-field-label">Authentication Type</label>
              <select
                value={authType}
                onChange={(e) => setAuthType(e.target.value as SecretAuthType)}
                data-testid="auth-type-select"
                aria-label="Authentication type"
              >
                {AUTH_TYPES.map((at) => (
                  <option key={at.value} value={at.value}>
                    {at.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="draft-field">
              <label className="draft-field-label">Key Vault Secret Name</label>
              <input
                type="text"
                value={keyVaultSecretName}
                onChange={(e) => setKeyVaultSecretName(e.target.value)}
                placeholder="e.g. ado-pat-production"
                data-testid="secret-name-input"
                aria-label="Key Vault secret name"
              />
              <span className="field-hint">
                Name of the secret in Azure Key Vault containing the credential.
              </span>
            </div>
          </div>
        )}

        {step === 'config' && (
          <div className="wizard-section" data-testid="wizard-step-config">
            <div className="draft-field">
              <label className="draft-field-label">Source Configuration</label>
              <SourceConfigEditor
                connectorType={connectorType}
                value={sourceConfig}
                onChange={setSourceConfig}
              />
            </div>
            <div className="draft-field">
              <label className="draft-field-label">Schedule (Cron Expression)</label>
              <input
                type="text"
                value={scheduleCron}
                onChange={(e) => setScheduleCron(e.target.value)}
                placeholder="0 */6 * * * (every 6 hours)"
                data-testid="schedule-input"
                aria-label="Sync schedule cron expression"
              />
              <span className="field-hint">
                Sync runs on this schedule. Leave empty for webhook/manual only.
              </span>
            </div>
          </div>
        )}

        {step === 'review' && (
          <div className="wizard-section" data-testid="wizard-step-review">
            <h3>Review Configuration</h3>
            <div className="review-grid">
              <div className="review-item">
                <span className="review-label">Name</span>
                <span className="review-value">{name}</span>
              </div>
              <div className="review-item">
                <span className="review-label">Type</span>
                <span className="review-value">{connectorType}</span>
              </div>
              <div className="review-item">
                <span className="review-label">Auth</span>
                <span className="review-value">{authType}</span>
              </div>
              {keyVaultSecretName && (
                <div className="review-item">
                  <span className="review-label">Secret</span>
                  <span className="review-value">{keyVaultSecretName}</span>
                </div>
              )}
              {scheduleCron && (
                <div className="review-item">
                  <span className="review-label">Schedule</span>
                  <span className="review-value">{scheduleCron}</span>
                </div>
              )}
              {sourceConfig && (
                <div className="review-item review-item-full">
                  <span className="review-label">Source Config</span>
                  <pre className="review-value review-pre">{sourceConfig}</pre>
                </div>
              )}
            </div>
          </div>
        )}

        {error && (
          <div className="error-banner" role="alert" data-testid="create-error">
            {error}
          </div>
        )}
      </div>

      <div className="wizard-actions">
        <button className="btn" onClick={onCancel} disabled={submitting} aria-label="Cancel connector creation">
          Cancel
        </button>
        <div className="wizard-actions-right">
          {step !== 'type' && (
            <button className="btn" onClick={prevStep} disabled={submitting} aria-label="Go to previous step">
              Back
            </button>
          )}
          {step !== 'review' ? (
            <button
              className="btn btn-primary"
              onClick={nextStep}
              disabled={!canAdvance()}
              data-testid="wizard-next-btn"
              aria-label="Go to next step"
            >
              Next
            </button>
          ) : (
            <button
              className="btn btn-primary"
              onClick={handleSubmit}
              disabled={submitting || !canAdvance()}
              data-testid="wizard-create-btn"
              aria-label="Create connector"
            >
              {submitting ? 'Creating...' : 'Create Connector'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
