import { type FormEvent, type KeyboardEvent, useRef, useState } from 'react';

interface MessageInputProps {
  onSend: (query: string) => void;
  disabled: boolean;
}

export function MessageInput({ onSend, disabled }: MessageInputProps) {
  const [value, setValue] = useState('');
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = value.trim();
    if (!trimmed || disabled) return;
    onSend(trimmed);
    setValue('');
    textareaRef.current?.focus();
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit(e);
    }
  }

  return (
    <form className="message-input" onSubmit={handleSubmit} data-testid="message-input">
      <textarea
        ref={textareaRef}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="Ask a question..."
        disabled={disabled}
        rows={1}
        aria-label="Message input"
        data-testid="message-textarea"
      />
      <button
        type="submit"
        disabled={disabled || !value.trim()}
        className="btn btn-primary"
        data-testid="send-button"
      >
        {disabled ? 'Thinking...' : 'Send'}
      </button>
    </form>
  );
}
