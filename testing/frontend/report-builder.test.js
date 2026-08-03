import { describe, expect, it } from 'vitest';
import { ReportBuilder } from '../../react/src/report_builder';

describe('report builder', () => {
  it('round-trips text and images while preserving block order', () => {
    // Arrange: one inline token splits text into two blocks around one image block.
    const builder = ReportBuilder.fromSerialized('Line 1 [[img:0]] Line 2', [
      { name: 'inline.png', contentType: 'image/png', dataUrl: 'data:image/png;base64,aGVsbG8=' }
    ]);

    // Act: convert back to persisted payload format.
    const payload = builder.toPayload();

    // Assert: serialization keeps token location and image identity stable.
    expect(payload.text).toBe('Line 1 \n\n[[img:0]]\n\n Line 2');
    expect(payload.images).toHaveLength(1);
    expect(payload.images[0].name).toBe('inline.png');
  });

  it('moves blocks and updates payload token positions', () => {
    // Arrange: builder starts with text then image then text.
    const original = ReportBuilder.fromSerialized('Alpha [[img:0]] Omega', [
      { name: 'diagram.png', contentType: 'image/png', dataUrl: 'data:image/png;base64,d29ybGQ=' }
    ]);
    const imageBlockId = original.blocks.find((block) => block.type === 'image')?.id;

    // Act: move image to the end.
    const moved = original.moveBlock(imageBlockId, 'down');
    const payload = moved.toPayload();

    // Assert: output text reflects block reordering.
    expect(payload.text).toBe('Alpha \n\n Omega\n\n[[img:0]]');
    expect(payload.images).toHaveLength(1);
  });

  it('separates consecutive text blocks with blank lines', () => {
    const first = ReportBuilder.createTextBlock('First update');
    const second = ReportBuilder.createTextBlock('Second update');
    const payload = new ReportBuilder([first, second]).toPayload();

    expect(payload.text).toBe('First update\n\nSecond update');
  });
});
