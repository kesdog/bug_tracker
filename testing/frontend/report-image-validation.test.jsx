import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import ReportBuilderEditor from '../../react/src/components/ReportBuilderEditor';
import { ReportBuilder } from '../../react/src/report_builder';

const NativeImage = global.Image;

afterEach(() => {
  global.Image = NativeImage;
  vi.restoreAllMocks();
});

function EditorHarness() {
  const [builder, setBuilder] = React.useState(() => ReportBuilder.fromSerialized('', []));
  const [error, setError] = React.useState('');
  return <ReportBuilderEditor builder={builder} error={error} onChange={setBuilder} onError={setError} />;
}

describe('report image validation', () => {
  it('rejects unsupported images without reading them', async () => {
    const readSpy = vi.spyOn(FileReader.prototype, 'readAsDataURL');
    render(<EditorHarness />);
    fireEvent.change(screen.getByLabelText(/add image below block 1/i), {
      target: { files: [new File(['gif'], 'proof.gif', { type: 'image/gif' })] }
    });
    expect(await screen.findByRole('alert')).toHaveTextContent(/png, jpeg, or webp/i);
    expect(readSpy).not.toHaveBeenCalled();
  });

  it('rejects a file over 4 MiB before FileReader', async () => {
    const readSpy = vi.spyOn(FileReader.prototype, 'readAsDataURL');
    render(<EditorHarness />);
    const oversized = new File([new Uint8Array(4 * 1024 * 1024 + 1)], 'large.png', { type: 'image/png' });
    await userEvent.upload(screen.getByLabelText(/add image below block 1/i), oversized);
    expect(await screen.findByRole('alert')).toHaveTextContent(/4 mib or smaller/i);
    expect(readSpy).not.toHaveBeenCalled();
  });

  it('rejects corrupt image bytes after reading', async () => {
    global.Image = class CorruptImage {
      set src(value) {
        this._src = value;
        queueMicrotask(() => this.onerror?.(new Error('decode failed')));
      }
    };
    render(<EditorHarness />);
    await userEvent.upload(screen.getByLabelText(/add image below block 1/i), new File(['not-an-image'], 'broken.png', { type: 'image/png' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/corrupt or cannot be decoded/i);
    expect(screen.getByText(/0\s*\/\s*3 image block/i)).toBeInTheDocument();
  });

  it('rejects decoded dimensions beyond the orientation-neutral 3840 by 2160 limit', async () => {
    global.Image = class OversizedImage {
      naturalWidth = 2000;
      naturalHeight = 4000;
      set src(value) {
        this._src = value;
        queueMicrotask(() => this.onload?.());
      }
    };
    render(<EditorHarness />);
    await userEvent.upload(screen.getByLabelText(/add image below block 1/i), new File(['valid-container'], 'portrait.png', { type: 'image/png' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/3840 × 2160 or smaller/i);
  });

  it('reserves queued image slots so rapid selections cannot exceed three', async () => {
    global.Image = class PendingImage {
      set src(value) { this._src = value; }
    };
    const readSpy = vi.spyOn(FileReader.prototype, 'readAsDataURL');
    render(<EditorHarness />);
    const input = screen.getByLabelText(/add image below block 1/i);

    for (let index = 1; index <= 4; index += 1) {
      fireEvent.change(input, {
        target: { files: [new File([String(index)], `${index}.png`, { type: 'image/png' })] }
      });
    }

    expect(await screen.findByRole('alert')).toHaveTextContent(/up to 3 images/i);
    expect(readSpy).toHaveBeenCalledTimes(1);
  });

  it('exposes report limits and errors to assistive technology', async () => {
    render(<EditorHarness />);
    const editor = screen.getByRole('group', { name: /report editor/i });
    const text = screen.getByRole('textbox', { name: /report text block 1/i });
    expect(text).toHaveAttribute('maxlength', '20000');
    expect(screen.getByText(/20,000 characters/i)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/add image below block 1/i), {
      target: { files: [new File(['gif'], 'bad.gif', { type: 'image/gif' })] }
    });
    const alert = await screen.findByRole('alert');
    expect(editor.getAttribute('aria-describedby')).toContain(alert.id);
  });
});
