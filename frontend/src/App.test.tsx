import { render, screen } from '@testing-library/react';
import { App } from './App';

describe('App', () => {
  it('renders the chat page by default', () => {
    render(<App />);
    expect(screen.getByText('Smart KB - Support Assistant')).toBeInTheDocument();
  });
});
