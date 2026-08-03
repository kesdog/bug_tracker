import { sanitizeImageName } from './report_builder';

const NAMED_IMAGE_TOKEN_REGEX = /\[\[img:(?:"([^"]+)"|'([^']+)'|([^\]]+))\]\]/g;
const INDEXED_IMAGE_TOKEN_REGEX = /\[\[img:(\d+)\]\]/g;

function normalizeImageName(name) {
  if (typeof name !== 'string') {
    return '';
  }

  return sanitizeImageName(name).toLowerCase();
}

function collectUsedIndexes(text, imageCount) {
  const used = new Set();
  INDEXED_IMAGE_TOKEN_REGEX.lastIndex = 0;
  let match = INDEXED_IMAGE_TOKEN_REGEX.exec(text);

  while (match) {
    const index = Number(match[1]);
    if (Number.isInteger(index) && index >= 0 && index < imageCount) {
      used.add(index);
    }
    match = INDEXED_IMAGE_TOKEN_REGEX.exec(text);
  }

  return used;
}

function buildImageIndexMap(images) {
  const map = new Map();
  images.forEach((image, index) => {
    const normalized = normalizeImageName(image?.name);
    if (!normalized || map.has(normalized)) {
      return;
    }

    map.set(normalized, index);
  });
  return map;
}

export function decorateReportTextWithImageRefs(reportText, reportImages = []) {
  const safeText = typeof reportText === 'string' ? reportText : '';
  const images = Array.isArray(reportImages) ? reportImages : [];
  const imageIndexByName = buildImageIndexMap(images);
  const unresolvedImageRefs = [];

  NAMED_IMAGE_TOKEN_REGEX.lastIndex = 0;
  const transformedText = safeText.replace(NAMED_IMAGE_TOKEN_REGEX, (_, quotedDouble, quotedSingle, rawValue) => {
    const rawRef = (quotedDouble || quotedSingle || rawValue || '').trim();
    if (!rawRef) {
      unresolvedImageRefs.push(rawRef);
      return '';
    }

    if (/^\d+$/.test(rawRef)) {
      const index = Number(rawRef);
      if (index >= 0 && index < images.length) {
        return `[[img:${index}]]`;
      }

      unresolvedImageRefs.push(rawRef);
      return '';
    }

    const normalized = normalizeImageName(rawRef);
    if (!normalized || !imageIndexByName.has(normalized)) {
      unresolvedImageRefs.push(rawRef);
      return '';
    }

    return `[[img:${imageIndexByName.get(normalized)}]]`;
  });

  return {
    reportText: transformedText,
    reportImages: images,
    unresolvedImageRefs
  };
}

export function chainBugReportWithImages(reportText, reportImages = []) {
  const decorated = decorateReportTextWithImageRefs(reportText, reportImages);
  const usedIndexes = collectUsedIndexes(decorated.reportText, decorated.reportImages.length);
  let mergedText = decorated.reportText.trim();

  for (let index = 0; index < decorated.reportImages.length; index += 1) {
    if (usedIndexes.has(index)) {
      continue;
    }

    const evidenceLine = `Evidence [[img:${index}]]`;
    mergedText = mergedText ? `${mergedText}\n\n${evidenceLine}` : evidenceLine;
  }

  return {
    reportText: mergedText,
    reportImages: decorated.reportImages,
    unresolvedImageRefs: decorated.unresolvedImageRefs
  };
}
