import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { Web } from '../pages/Web';

vi.mock('../services/pcsuiteBridge', () => ({
  bridge: { call: vi.fn() },
  BridgeError: class BridgeError extends Error {},
}));

import { bridge } from '../services/pcsuiteBridge';

describe('Web page', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renderiza el inspector web', () => {
    render(<Web />);
    expect(screen.getByText(/Inspector web/i)).toBeTruthy();
  });

  it('inspecciona URL al pulsar', async () => {
    (bridge.call as any).mockResolvedValueOnce({
      host: 'example.com',
      scripts: ['Latin'],
      suspicious: [],
      punycode: false,
      mixedScripts: false,
      verdict: 'safe',
      typosquatCandidates: [],
    });
    render(<Web />);
    const input = screen.getByPlaceholderText('https://ejemplo.com');
    fireEvent.change(input, { target: { value: 'https://example.com' } });
    const btn = screen.getByRole('button', { name: /Inspeccionar/i });
    fireEvent.click(btn);
    await waitFor(() => {
      expect(bridge.call).toHaveBeenCalled();
    });
  });
});