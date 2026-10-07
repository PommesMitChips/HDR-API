#!/usr/bin/env node
// Exporter for SVG strings produced by JS or framework SSR. No JS enters the game.
// node Tools/ExportSvgFrames.mjs ./my-animation.mjs my-animation
import { writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
const [modulePath, name = 'animation'] = process.argv.slice(2);
if (!modulePath || !/^[A-Za-z0-9_-]{1,32}$/.test(name)) throw new Error('Supply a local authoring module and simple output name.');
const animation = await import(pathToFileURL(path.resolve(modulePath)).href);
const fps = animation.fps ?? 10, duration = animation.duration ?? 3;
if (!Number.isFinite(fps) || fps < 1 || fps > 30 || !Number.isFinite(duration) || duration <= 0 || duration > 60 || typeof animation.render !== 'function')
  throw new Error('Module must export render(timeSeconds), fps (1–30), duration (0–60 seconds).');
const count = Math.ceil(fps * duration);
if (count > 300) throw new Error('At most 300 frames; reduce fps or duration.');
const frames = [];
for (let i = 0; i < count; i++) {
  const svg = await animation.render(i / fps);
  if (typeof svg !== 'string' || svg.length > 65536 || !svg.trimStart().startsWith('<svg')) throw new Error(`Invalid SVG frame ${i}.`);
  frames.push(svg);
}
// Keep the PB practical. Geometry parser remains the authority on supported SVG features.
if (frames.reduce((n, s) => n + s.length, 0) > 60000) throw new Error('Frame data exceeds 60k characters; prefer transform animations or packaged assets.');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
await mkdir(path.join(root, 'artifacts'), { recursive: true });
const literal = s => '@"' + s.replaceAll('"', '""') + '"';
const output = `// ${name}: generated SVG frames, ${fps} fps. Append inside PB class.\nconst double SvgFrameRate = ${fps};\nreadonly string[] SvgFrames = new string[] {\n${frames.map(literal).join(',\n')}\n};\n`;
const target = path.join(root, 'artifacts', `${name}.frames.cs`);
await writeFile(target, output);
console.log(`Exported ${count} SVG frames to ${target}`);
