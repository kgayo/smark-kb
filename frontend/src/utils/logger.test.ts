import { logger } from './logger';

describe('logger', () => {
  it('delegates warn to console.warn in non-production', () => {
    const spy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    logger.warn('[Test]', 'message');
    expect(spy).toHaveBeenCalledWith('[Test]', 'message');
    spy.mockRestore();
  });

  it('delegates error to console.error in non-production', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    logger.error('[Test]', 'error message');
    expect(spy).toHaveBeenCalledWith('[Test]', 'error message');
    spy.mockRestore();
  });

  it('delegates info to console.info in non-production', () => {
    const spy = vi.spyOn(console, 'info').mockImplementation(() => {});
    logger.info('[Test]', 'info message');
    expect(spy).toHaveBeenCalledWith('[Test]', 'info message');
    spy.mockRestore();
  });
});
