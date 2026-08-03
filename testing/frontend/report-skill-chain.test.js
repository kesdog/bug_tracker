import { describe, expect, it } from 'vitest';
import { chainBugReportWithImages, decorateReportTextWithImageRefs } from '../../react/src/report_skill_chain';

describe('report skill chain', () => {
  it('maps named image tags to indexed report tokens', () => {
    const images = [
      { name: 'checkout-timeout.png', contentType: 'image/png', dataUrl: 'data:image/png;base64,aaaa' },
      { name: 'console-log.webp', contentType: 'image/webp', dataUrl: 'data:image/webp;base64,bbbb' }
    ];

    const result = decorateReportTextWithImageRefs(
      'Timeout appears in network tab [[img:"checkout-timeout.png"]]. Console has stack [[img:console-log.webp]].',
      images
    );

    expect(result.reportText).toContain('[[img:0]]');
    expect(result.reportText).toContain('[[img:1]]');
    expect(result.unresolvedImageRefs).toEqual([]);
  });

  it('collects unresolved refs and strips invalid tokens', () => {
    const images = [
      { name: 'known.png', contentType: 'image/png', dataUrl: 'data:image/png;base64,aaaa' }
    ];

    const result = decorateReportTextWithImageRefs('Has known [[img:known.png]] and unknown [[img:missing.png]] refs.', images);

    expect(result.reportText).toContain('[[img:0]]');
    expect(result.reportText).not.toContain('missing.png');
    expect(result.unresolvedImageRefs).toEqual(['missing.png']);
  });

  it('appends unused images as evidence lines', () => {
    const images = [
      { name: 'known.png', contentType: 'image/png', dataUrl: 'data:image/png;base64,aaaa' },
      { name: 'extra.png', contentType: 'image/png', dataUrl: 'data:image/png;base64,bbbb' }
    ];

    const result = chainBugReportWithImages('Observed issue [[img:known.png]].', images);

    expect(result.reportText).toContain('[[img:0]]');
    expect(result.reportText).toContain('Evidence [[img:1]]');
  });
});
