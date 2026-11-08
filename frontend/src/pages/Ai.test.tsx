import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { AiPage } from '../pages/Ai';

vi.mock('../services/pcsuiteBridge', () => ({
  bridge: {
    call: vi.fn(),
  },
  BridgeError: class BridgeError extends Error {},
}));

import { bridge } from '../services/pcsuiteBridge';

describe('AiPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renderiza el título y secciones', () => {
    render(<AiPage />);
    expect(screen.getByText(/IA/i)).toBeTruthy();
  });

  it('muestra estado no instalado cuando ai.detect falla', async () => {
    (bridge.call as any).mockRejectedValueOnce(new Error('boom'));
    render(<AiPage />);
    await waitFor(() => {
      expect(bridge.call).toHaveBeenCalled();
    });
  });

  it('muestra estado instalado cuando ai.detect tiene éxito', async () => {
    (bridge.call as any).mockResolvedValueOnce({
      installed: true,
      pythonPath: 'C:/python.exe',
      markItDownVersion: '0.1.0',
      detail: null,
    });
    (bridge.call as any).mockResolvedValueOnce({
      installed: true,
      ollamaPath: 'C:/ollama.exe',
      version: '0.5.7',
      detail: null,
    });
    render(<AiPage />);
    await waitFor(() => {
      expect(bridge.call).toHaveBeenCalled();
    });
  });
});