import { LogLevel } from '@azure/msal-browser';
import { msalConfig } from './msalConfig';

describe('msalConfig loggerCallback', () => {
  const callback = msalConfig.system!.loggerOptions!.loggerCallback!;

  it('logs errors via console.error', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    callback(LogLevel.Error, 'test error', false);
    expect(spy).toHaveBeenCalledWith('[MSAL]', 'test error');
    spy.mockRestore();
  });

  it('logs warnings via console.warn', () => {
    const spy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    callback(LogLevel.Warning, 'test warning', false);
    expect(spy).toHaveBeenCalledWith('[MSAL]', 'test warning');
    spy.mockRestore();
  });

  it('does not log info or verbose messages', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const debugSpy = vi.spyOn(console, 'debug').mockImplementation(() => {});
    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => {});

    callback(LogLevel.Info, 'info msg', false);
    callback(LogLevel.Verbose, 'verbose msg', false);

    expect(errorSpy).not.toHaveBeenCalled();
    expect(warnSpy).not.toHaveBeenCalled();
    expect(debugSpy).not.toHaveBeenCalled();
    expect(logSpy).not.toHaveBeenCalled();

    errorSpy.mockRestore();
    warnSpy.mockRestore();
    debugSpy.mockRestore();
    logSpy.mockRestore();
  });
});
