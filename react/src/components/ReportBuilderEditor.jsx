import React from 'react';
import Button from '@mui/material/Button';
import { ALLOWED_IMAGE_TYPES, fileToImageDto, getImageByteSize, MAX_IMAGE_BYTES, MAX_REPORT_IMAGE_BYTES, MAX_REPORT_IMAGES, MAX_REPORT_TEXT_LENGTH } from '../report_builder';
import { useI18n } from '../i18n';

// Shared MIME type guard used by both add and replace actions.
function isAllowedImage(file) {
  return ALLOWED_IMAGE_TYPES.includes(file.type);
}

export default function ReportBuilderEditor({ builder, label = 'Report', submitting = false, error = '', textConflicted = false, imageConflicted = false, textConflictNoteId, imageConflictNoteId, onChange, onError }) {
  const { t } = useI18n();
  const addImageQueueRef = React.useRef(Promise.resolve());
  const pendingAddCountRef = React.useRef(0);
  const pendingAddBytesRef = React.useRef(0);
  const fileInputs = React.useRef(new Map());
  const mountedRef = React.useRef(true);
  const errorId = React.useId();
  const countId = React.useId();

  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      fileInputs.current.clear();
    };
  }, []);

  function openFilePicker(key) {
    fileInputs.current.get(key)?.click();
  }

  // Adds a new image block after the selected block.
  async function handleAddImage(blockId, event) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) {
      return;
    }

    if (!isAllowedImage(file)) {
      onError(t('reportBuilder.invalidImageType', 'Use PNG, JPEG, or WebP images only.'));
      return;
    }

    if (file.size > MAX_IMAGE_BYTES) {
      onError(t('reportBuilder.imageTooLarge', 'Each image must be 4 MiB or smaller.'));
      return;
    }

    if (builder.imageCount + pendingAddCountRef.current >= MAX_REPORT_IMAGES) {
      onError(t('reportBuilder.maxImages', 'Attach up to {{count}} images.', { count: MAX_REPORT_IMAGES }));
      return;
    }

    if (builder.imageBytes + pendingAddBytesRef.current + file.size > MAX_REPORT_IMAGE_BYTES) {
      onError(t('reportBuilder.imagesTooLarge', 'Report images must total 12 MiB or less.'));
      return;
    }

    pendingAddCountRef.current += 1;
    pendingAddBytesRef.current += file.size;

    addImageQueueRef.current = addImageQueueRef.current
      .catch(() => undefined)
      .then(async () => {
        try {
          const image = await fileToImageDto(file);
          if (!mountedRef.current) return;
          onError('');
          onChange((current) => {
            if (current.imageCount >= MAX_REPORT_IMAGES) {
                onError(t('reportBuilder.maxImages', 'Attach up to {{count}} images.', { count: MAX_REPORT_IMAGES }));
              return current;
            }
            if (current.imageBytes + image.sizeBytes > MAX_REPORT_IMAGE_BYTES) {
                onError(t('reportBuilder.imagesTooLarge', 'Report images must total 12 MiB or less.'));
              return current;
            }
            return current.addImageAfter(blockId, image);
          });
        } catch (err) {
          if (mountedRef.current) onError(err.message || t('reportBuilder.loadImageError', 'Unable to load selected image.'));
        } finally {
          pendingAddCountRef.current = Math.max(0, pendingAddCountRef.current - 1);
          pendingAddBytesRef.current = Math.max(0, pendingAddBytesRef.current - file.size);
        }
      });

    await addImageQueueRef.current;
  }

  // Replaces the selected image block while keeping its position.
  async function handleReplaceImage(blockId, event) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) {
      return;
    }

    if (!isAllowedImage(file)) {
      onError(t('reportBuilder.invalidImageType', 'Use PNG, JPEG, or WebP images only.'));
      return;
    }

    if (file.size > MAX_IMAGE_BYTES) {
      onError(t('reportBuilder.imageTooLarge', 'Each image must be 4 MiB or smaller.'));
      return;
    }

    try {
      const image = await fileToImageDto(file);
      onError('');
      onChange((current) => {
        const currentImage = current.blocks.find((block) => block.id === blockId)?.image;
        const nextBytes = current.imageBytes - getImageByteSize(currentImage) + image.sizeBytes;
        if (nextBytes > MAX_REPORT_IMAGE_BYTES) {
          onError(t('reportBuilder.imagesTooLarge', 'Report images must total 12 MiB or less.'));
          return current;
        }
        return current.replaceImage(blockId, image);
      });
    } catch (err) {
      onError(err.message || t('reportBuilder.loadImageError', 'Unable to load selected image.'));
    }
  }

  // Removes a single block from the builder state.
  function removeBlock(blockId) {
    onChange((current) => current.removeBlock(blockId));
  }

  return (
    <div
      className={`report-builder-editor ${textConflicted || imageConflicted ? 'conflict-field' : ''}`}
      role="group"
      aria-label={t('reportBuilder.editor', '{{label}} editor', { label })}
      aria-describedby={[error ? errorId : '', textConflicted ? textConflictNoteId : '', imageConflicted ? imageConflictNoteId : ''].filter(Boolean).join(' ') || undefined}
    >
      {builder.blocks.map((block, index) => (
        <section key={block.id} className="report-block-row">
          <header className="report-block-head">
            <span className="report-block-type">{block.type === 'text' ? t('reportBuilder.textBlock', 'Text block') : t('reportBuilder.imageBlock', 'Image block')}</span>
            <div className="report-block-actions">
              <button type="button" className="tiny-action" disabled={submitting || index === 0} aria-describedby={block.type === 'image' && imageConflicted ? imageConflictNoteId : undefined} onClick={() => onChange((current) => current.moveBlock(block.id, 'up'))}>
                {t('common.up', 'Up')}
              </button>
              <button
                type="button"
                className="tiny-action"
                disabled={submitting || index === builder.blocks.length - 1}
                aria-describedby={block.type === 'image' && imageConflicted ? imageConflictNoteId : undefined}
                onClick={() => onChange((current) => current.moveBlock(block.id, 'down'))}
              >
                {t('common.down', 'Down')}
              </button>
              <button type="button" className="tiny-action" disabled={submitting} aria-describedby={block.type === 'image' && imageConflicted ? imageConflictNoteId : undefined} onClick={() => removeBlock(block.id)}>
                {t('common.remove', 'Remove')}
              </button>
            </div>
          </header>

          {block.type === 'text' ? (
            <textarea
              aria-label={t('reportBuilder.textBlockLabel', '{{label}} text block {{index}}', { label, index: index + 1 })}
              rows="4"
              value={block.text}
              onChange={(event) => onChange((current) => current.updateText(block.id, event.target.value))}
              placeholder={t('reportBuilder.writeText', 'Write report text')}
              maxLength={Math.max(0, MAX_REPORT_TEXT_LENGTH - (builder.textLength - String(block.text || '').length))}
              disabled={submitting}
              aria-describedby={textConflicted ? textConflictNoteId : undefined}
            />
          ) : (
            <div className="report-inline-image-wrap">
              <img className="report-inline-image" src={block.image?.dataUrl} alt={block.image?.name || t('reportBuilder.imageAlt', 'report image')} />
              <p className="report-image-name">{block.image?.name || t('reportBuilder.image', 'image')}</p>
              <Button type="button" size="small" variant="outlined" disabled={submitting} aria-describedby={imageConflicted ? imageConflictNoteId : undefined} onClick={() => openFilePicker(`replace-${block.id}`)}>
                {t('reportBuilder.replaceImage', 'Replace image')}
              </Button>
                <input
                  ref={(node) => node ? fileInputs.current.set(`replace-${block.id}`, node) : fileInputs.current.delete(`replace-${block.id}`)}
                  className="tiny-upload-input"
                  type="file"
                  aria-label={t('reportBuilder.replaceImageLabel', 'Replace image {{image}}', { image: block.image?.name || index + 1 })}
                  aria-describedby={imageConflicted ? imageConflictNoteId : undefined}
                  accept="image/png,image/jpeg,image/webp"
                  onChange={(event) => handleReplaceImage(block.id, event)}
                  disabled={submitting}
                />
            </div>
          )}

          <div className="report-block-add">
            <button type="button" className="tiny-action" disabled={submitting} onClick={() => onChange((current) => current.addTextAfter(block.id))}>
              {t('reportBuilder.addTextBelow', 'Add text below')}
            </button>
            <Button type="button" size="small" variant="outlined" disabled={submitting || builder.imageCount >= MAX_REPORT_IMAGES} aria-describedby={imageConflicted ? imageConflictNoteId : undefined} onClick={() => openFilePicker(`add-${block.id}`)}>
              {t('reportBuilder.addImageBelow', 'Add image below')}
            </Button>
              <input
                ref={(node) => node ? fileInputs.current.set(`add-${block.id}`, node) : fileInputs.current.delete(`add-${block.id}`)}
                className="tiny-upload-input"
                type="file"
                aria-label={t('reportBuilder.addImageBelowLabel', 'Add image below block {{index}}', { index: index + 1 })}
                aria-describedby={imageConflicted ? imageConflictNoteId : undefined}
                accept="image/png,image/jpeg,image/webp"
                onChange={(event) => handleAddImage(block.id, event)}
                disabled={submitting || builder.imageCount >= MAX_REPORT_IMAGES}
              />
          </div>
        </section>
      ))}

      {error ? <p id={errorId} role="alert" className="error-text">{error}</p> : null}
      <p id={countId} className="report-image-count">{t('reportBuilder.count', '{{textLength}} / {{maxTextLength}} characters. {{imageCount}} / {{maxImages}} image block(s).', { textLength: builder.textLength.toLocaleString(), maxTextLength: MAX_REPORT_TEXT_LENGTH.toLocaleString(), imageCount: builder.imageCount, maxImages: MAX_REPORT_IMAGES })}</p>
    </div>
  );
}
