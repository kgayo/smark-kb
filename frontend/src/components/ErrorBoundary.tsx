import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    console.error('[ErrorBoundary] Uncaught rendering error:', error, errorInfo.componentStack);
  }

  handleReload = (): void => {
    window.location.reload();
  };

  render(): ReactNode {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }
      return (
        <div role="alert" style={{ padding: '2rem', textAlign: 'center' }}>
          <h1>Something went wrong</h1>
          <p>An unexpected error occurred. Please reload the page to continue.</p>
          {this.state.error && (
            <pre style={{ marginTop: '1rem', fontSize: '0.85rem', color: '#888', whiteSpace: 'pre-wrap' }}>
              {this.state.error.message}
            </pre>
          )}
          <button onClick={this.handleReload} style={{ marginTop: '1rem', padding: '0.5rem 1rem' }}>
            Reload page
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}
