import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { downloadFile } from './downloadFile';

describe('downloadFile', () => {
  let appendChildSpy: ReturnType<typeof vi.spyOn>;
  let removeChildSpy: ReturnType<typeof vi.spyOn>;
  let createObjectURLSpy: ReturnType<typeof vi.fn>;
  let revokeObjectURLSpy: ReturnType<typeof vi.fn>;
  let clickSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    clickSpy = vi.fn();
    vi.spyOn(document, 'createElement').mockReturnValue({
      href: '',
      download: '',
      click: clickSpy,
    } as unknown as HTMLAnchorElement);
    appendChildSpy = vi.spyOn(document.body, 'appendChild').mockImplementation((node) => node);
    removeChildSpy = vi.spyOn(document.body, 'removeChild').mockImplementation((node) => node);
    createObjectURLSpy = vi.fn().mockReturnValue('blob:mock-url');
    revokeObjectURLSpy = vi.fn();
    globalThis.URL.createObjectURL = createObjectURLSpy;
    globalThis.URL.revokeObjectURL = revokeObjectURLSpy;
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('creates object URL from blob and triggers download', () => {
    const blob = new Blob(['test'], { type: 'text/plain' });
    downloadFile(blob, 'test.txt');

    expect(createObjectURLSpy).toHaveBeenCalledWith(blob);
    expect(clickSpy).toHaveBeenCalled();
    expect(appendChildSpy).toHaveBeenCalled();
    expect(removeChildSpy).toHaveBeenCalled();
    expect(revokeObjectURLSpy).toHaveBeenCalledWith('blob:mock-url');
  });

  it('sets download filename on the anchor element', () => {
    const blob = new Blob(['data'], { type: 'application/json' });
    const createSpy = vi.spyOn(document, 'createElement');
    const anchor = { href: '', download: '', click: vi.fn() } as unknown as HTMLAnchorElement;
    createSpy.mockReturnValue(anchor);

    downloadFile(blob, 'export-2026.json');
    expect((anchor as unknown as { download: string }).download).toBe('export-2026.json');
  });

  it('revokes object URL after click to prevent memory leak', () => {
    const blob = new Blob(['x']);
    downloadFile(blob, 'f.bin');

    expect(revokeObjectURLSpy).toHaveBeenCalledTimes(1);
    expect(revokeObjectURLSpy).toHaveBeenCalledWith('blob:mock-url');
  });
});
