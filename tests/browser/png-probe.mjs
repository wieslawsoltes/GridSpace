import { inflateSync } from 'node:zlib';

/** Read an RGB/RGBA scanline from a non-interlaced, 8-bit Playwright PNG. No image dependency or OCR is needed. */
export function pngRow(png, row) {
  if (!png.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))) throw new Error('Not a PNG screenshot.');
  let width = 0, height = 0, channels = 0;
  const chunks = [];
  for (let offset = 8; offset + 12 <= png.length;) {
    const length = png.readUInt32BE(offset);
    if (length > png.length - offset - 12) throw new Error('Truncated PNG screenshot.');
    const kind = png.toString('ascii', offset + 4, offset + 8);
    const bytes = png.subarray(offset + 8, offset + 8 + length);
    if (kind === 'IHDR') {
      width = bytes.readUInt32BE(0); height = bytes.readUInt32BE(4);
      if (bytes[8] !== 8 || ![2, 6].includes(bytes[9]) || bytes[12] !== 0) throw new Error('Unsupported screenshot encoding.');
      channels = bytes[9] === 6 ? 4 : 3;
    } else if (kind === 'IDAT') chunks.push(bytes);
    offset += length + 12;
    if (kind === 'IEND') break;
  }
  if (width <= 0 || height <= row || row < 0 || channels === 0) throw new Error('Invalid screenshot dimensions.');
  const stride = width * channels;
  const raw = inflateSync(Buffer.concat(chunks), { maxOutputLength: (stride + 1) * height });
  let previous = Buffer.alloc(stride);
  let offset = 0;
  for (let y = 0; y <= row; y++) {
    const filter = raw[offset++];
    const current = Buffer.alloc(stride);
    for (let x = 0; x < stride; x++) {
      const a = x >= channels ? current[x - channels] : 0, b = previous[x], c = x >= channels ? previous[x - channels] : 0;
      let prediction = 0;
      if (filter === 1) prediction = a;
      else if (filter === 2) prediction = b;
      else if (filter === 3) prediction = Math.floor((a + b) / 2);
      else if (filter === 4) {
        const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
        prediction = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      } else if (filter !== 0) throw new Error('Invalid PNG scanline filter.');
      current[x] = (raw[offset++] + prediction) & 255;
    }
    previous = current;
  }
  return { pixels: previous, width, channels };
}

/** A visible green title bar distinguishes the rendered application from Uno's splash screen. */
export function workbenchHeaderCoverage(png) {
  const { pixels, width, channels } = pngRow(png, 5);
  let matching = 0;
  for (let x = 0; x < width; x++) {
    const i = x * channels;
    if (Math.abs(pixels[i] - 16) < 8 && Math.abs(pixels[i + 1] - 124) < 8 && Math.abs(pixels[i + 2] - 65) < 8) matching++;
  }
  return matching / width;
}
