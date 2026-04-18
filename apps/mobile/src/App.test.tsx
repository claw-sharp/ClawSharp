import { render, screen } from '@testing-library/react';
import App from './App';

describe('Mobile app smoke', () => {
  it('renders the remote runtime shell', () => {
    render(<App />);

    expect(screen.getByText('ClawSharp Mobile')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Connect' })).toBeInTheDocument();
    expect(screen.getByText('Transcript')).toBeInTheDocument();
  });
});
