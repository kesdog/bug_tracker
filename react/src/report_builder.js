const IMAGE_TOKEN_REGEX = /\[\[img:(\d+)\]\]/g;

export const MAX_REPORT_IMAGES = 3;
export const MAX_IMAGE_BYTES = 4 * 1024 * 1024;
export const MAX_REPORT_IMAGE_BYTES = 12 * 1024 * 1024;
export const MAX_IMAGE_LONG_SIDE = 3840;
export const MAX_IMAGE_SHORT_SIDE = 2160;
export const MAX_IMAGE_PIXELS = 8_294_400;
export const MAX_REPORT_TEXT_LENGTH = 20_000;
export const ALLOWED_IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/webp'];

let nextBlockId = 1;

// Generates deterministic-looking IDs for local UI blocks.
function createBlockId() {
  nextBlockId += 1;
  return `rb-${nextBlockId}`;
}

// Creates a safe copy of image data before storing it in blocks/payloads.
function cloneImage(image) {
  if (!image) {
    return null;
  }

  return {
    name: image.name,
    contentType: image.contentType,
    dataUrl: image.dataUrl,
    ...(Number.isFinite(image.sizeBytes) ? { sizeBytes: image.sizeBytes } : {})
  };
}

// Ensures the builder always has at least one editable text block.
function normalizeBlocks(blocks) {
  if (!Array.isArray(blocks) || blocks.length === 0) {
    return [ReportBuilder.createTextBlock('')];
  }

  return blocks;
}

// Normalizes file names to backend-safe characters and length.
export function sanitizeImageName(name) {
  const cleaned = name.trim().replace(/[^a-zA-Z0-9._-]+/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
  if (!cleaned) {
    return 'image';
  }

  return cleaned.length > 80 ? cleaned.slice(0, 80) : cleaned;
}

// Converts a browser File to a base64 data URL used by the API payload.
export function readFileAsDataUrl(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ''));
    reader.onerror = () => reject(new Error('Unable to read selected image.'));
    reader.readAsDataURL(file);
  });
}

function decodeImageDimensions(dataUrl) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve({ width: image.naturalWidth || image.width, height: image.naturalHeight || image.height });
    image.onerror = () => reject(new Error('The selected image is corrupt or cannot be decoded.'));
    image.src = dataUrl;
  });
}

export function getImageByteSize(image) {
  if (Number.isFinite(image?.sizeBytes)) return image.sizeBytes;
  const encoded = String(image?.dataUrl || '').split(',')[1] || '';
  if (!encoded) return 0;
  const padding = encoded.endsWith('==') ? 2 : encoded.endsWith('=') ? 1 : 0;
  return Math.max(0, Math.floor(encoded.length * 3 / 4) - padding);
}

// Transforms a browser File into the report image DTO shape.
export async function fileToImageDto(file) {
  if (!ALLOWED_IMAGE_TYPES.includes(file.type)) {
    throw new Error('Use PNG, JPEG, or WebP images only.');
  }
  if (file.size > MAX_IMAGE_BYTES) {
    throw new Error('Each image must be 4 MiB or smaller.');
  }

  const dataUrl = await readFileAsDataUrl(file);
  const { width, height } = await decodeImageDimensions(dataUrl);
  const longSide = Math.max(width, height);
  const shortSide = Math.min(width, height);
  if (!width || !height || longSide > MAX_IMAGE_LONG_SIDE || shortSide > MAX_IMAGE_SHORT_SIDE || width * height > MAX_IMAGE_PIXELS) {
    throw new Error('Images must be 3840 × 2160 or smaller and no more than 8,294,400 pixels.');
  }

  return {
    name: sanitizeImageName(file.name),
    contentType: file.type,
    dataUrl,
    sizeBytes: file.size
  };
}

// Block-based report model that supports text/image composition and reordering.
export class ReportBuilder {
  constructor(blocks) {
    this.blocks = normalizeBlocks(blocks);
  }

  // Factory for plain text blocks.
  static createTextBlock(text = '') {
    return { id: createBlockId(), type: 'text', text };
  }

  // Factory for image blocks.
  static createImageBlock(image) {
    return { id: createBlockId(), type: 'image', image: cloneImage(image) };
  }

  // Parses serialized text + image arrays into ordered UI blocks.
  static fromSerialized(text = '', images = []) {
    const blocks = [];
    const imageList = Array.isArray(images) ? images : [];
    const usedIndexes = new Set();
    const source = typeof text === 'string' ? text : '';
    IMAGE_TOKEN_REGEX.lastIndex = 0;
    let cursor = 0;
    let match = IMAGE_TOKEN_REGEX.exec(source);

    while (match) {
      const start = match.index;
      const segment = source.slice(cursor, start);
      if (segment.length > 0 || blocks.length === 0) {
        blocks.push(ReportBuilder.createTextBlock(segment));
      }

      const imageIndex = Number(match[1]);
      if (Number.isInteger(imageIndex) && imageIndex >= 0 && imageIndex < imageList.length) {
        blocks.push(ReportBuilder.createImageBlock(imageList[imageIndex]));
        usedIndexes.add(imageIndex);
      }

      cursor = start + match[0].length;
      match = IMAGE_TOKEN_REGEX.exec(source);
    }

    const tail = source.slice(cursor);
    if (tail.length > 0 || blocks.length === 0) {
      blocks.push(ReportBuilder.createTextBlock(tail));
    }

    imageList.forEach((image, index) => {
      if (!usedIndexes.has(index)) {
        blocks.push(ReportBuilder.createImageBlock(image));
      }
    });

    return new ReportBuilder(blocks);
  }

  // Counts image blocks for validation (max image limit).
  get imageCount() {
    return this.blocks.filter((block) => block.type === 'image').length;
  }

  get imageBytes() {
    return this.blocks.reduce((total, block) => total + (block.type === 'image' ? getImageByteSize(block.image) : 0), 0);
  }

  get textLength() {
    return this.blocks.reduce((total, block) => total + (block.type === 'text' ? String(block.text || '').length : 0), 0);
  }

  // Converts blocks back into API payload shape with image tokens in text.
  toPayload() {
    const images = [];
    const parts = [];

    this.blocks.forEach((block) => {
      if (block.type === 'image') {
        const index = images.length;
        const image = cloneImage(block.image);
        images.push({ name: image.name, contentType: image.contentType, dataUrl: image.dataUrl });
        parts.push(`[[img:${index}]]`);
        return;
      }

      parts.push(block.text || '');
    });

    return {
      text: parts.join('\n\n'),
      images
    };
  }

  // Updates a specific text block.
  updateText(blockId, text) {
    const blocks = this.blocks.map((block) => (block.id === blockId && block.type === 'text' ? { ...block, text } : block));
    return new ReportBuilder(blocks);
  }

  // Replaces the image content of an existing image block.
  replaceImage(blockId, image) {
    const blocks = this.blocks.map((block) => (block.id === blockId && block.type === 'image' ? { ...block, image: cloneImage(image) } : block));
    return new ReportBuilder(blocks);
  }

  // Inserts a new text block directly after the target block.
  addTextAfter(blockId) {
    const index = this.blocks.findIndex((block) => block.id === blockId);
    const nextBlocks = [...this.blocks];
    const at = index >= 0 ? index + 1 : nextBlocks.length;
    nextBlocks.splice(at, 0, ReportBuilder.createTextBlock(''));
    return new ReportBuilder(nextBlocks);
  }

  // Inserts a new image block directly after the target block.
  addImageAfter(blockId, image) {
    const index = this.blocks.findIndex((block) => block.id === blockId);
    const nextBlocks = [...this.blocks];
    const at = index >= 0 ? index + 1 : nextBlocks.length;
    nextBlocks.splice(at, 0, ReportBuilder.createImageBlock(image));
    return new ReportBuilder(nextBlocks);
  }

  // Removes one block from the report.
  removeBlock(blockId) {
    const nextBlocks = this.blocks.filter((block) => block.id !== blockId);
    return new ReportBuilder(nextBlocks);
  }

  // Moves a block up/down while preserving order of other blocks.
  moveBlock(blockId, direction) {
    const index = this.blocks.findIndex((block) => block.id === blockId);
    if (index < 0) {
      return new ReportBuilder(this.blocks);
    }

    const target = direction === 'up' ? index - 1 : index + 1;
    if (target < 0 || target >= this.blocks.length) {
      return new ReportBuilder(this.blocks);
    }

    const nextBlocks = [...this.blocks];
    const [item] = nextBlocks.splice(index, 1);
    nextBlocks.splice(target, 0, item);
    return new ReportBuilder(nextBlocks);
  }
}
