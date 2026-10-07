// Reusable, data-only HMC1 / HMA1 encoder for Console Custom Data.
export function packAnimation(frames, fps = 4) {
  if (!Array.isArray(frames) || frames.length < 1 || frames.length > 128 || !Number.isFinite(fps) || fps < 1 || fps > 10)
    throw new Error('Supply 1–128 frames, 1–10 fps. Each frame is an array of SVG parts.');
  const parts = frames[0]?.length;
  if (!Number.isInteger(parts) || parts < 1 || parts > 8) throw new Error('Each frame requires 1–8 parts.');
  for (const frame of frames) {
    if (!Array.isArray(frame) || frame.length !== parts) throw new Error('Frame part counts must match.');
    for (const svg of frame) if (typeof svg !== 'string' || svg.length < 1 || svg.length > 65536 || /[^\x01-\x7f]|[\r\n]/.test(svg))
      throw new Error('Parts must be single-line ASCII SVG strings, at most 65536 characters.');
  }
  const plain = Buffer.from(`HMA1\n${fps}\n${frames.length}\n${parts}\n${frames.flat().join('\n')}\n`, 'ascii');
  if (plain.length > 2 * 1024 * 1024) throw new Error('Decoded-size budget exceeded.');
  const output = [], chains = new Map(); let p = 0;
  const key = i => i + 2 < plain.length ? (plain[i] << 16) | (plain[i + 1] << 8) | plain[i + 2] : -1;
  function remember(i) {
    const k = key(i); if (k < 0) return;
    let list = chains.get(k); if (!list) chains.set(k, list = []);
    list.push(i); while (list.length > 64 || i - list[0] > 65535) list.shift();
  }
  while (p < plain.length) {
    const flagIndex = output.length; output.push(0); let flags = 0;
    for (let bit = 0; bit < 8 && p < plain.length; bit++) {
      let length = 0, distance = 0; const candidates = chains.get(key(p)) ?? [];
      for (let j = candidates.length - 1; j >= 0; j--) {
        const q = candidates[j], d = p - q; if (d > 65535) break;
        let n = 0; while (n < 258 && p + n < plain.length && plain[q + n] === plain[p + n]) n++;
        if (n > length) { length = n; distance = d; } if (length === 258) break;
      }
      if (length >= 3) {
        output.push(distance & 255, distance >> 8, length - 3);
        for (let j = 0; j < length; j++) remember(p + j); p += length;
      } else { flags |= 1 << bit; output.push(plain[p]); remember(p++); }
    }
    output[flagIndex] = flags;
  }
  const header = Buffer.alloc(8); header.write('HMC1'); header.writeUInt32LE(plain.length, 4);
  const encoded = 'HoloMapAnimation1:' + Buffer.concat([header, Buffer.from(output)]).toString('base64');
  if (encoded.length > 60000) throw new Error('Encoded-size budget exceeded.');
  return { encoded, plain, stats: { frames: frames.length, parts, fps, decodedBytes: plain.length, customDataCharacters: encoded.length } };
}
