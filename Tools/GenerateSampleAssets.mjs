import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { packAnimation } from './PackAnimation.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.join(root, 'artifacts', 'SvgSamples');
await mkdir(directory, { recursive: true });
const wrap = content => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 480 480"><g transform="scale(4.8)">${content}</g></svg>`;
const navigation = wrap(`
<circle cx="50" cy="50" r="43" fill="#071e2c" fill-opacity="0.8"/>
<g fill="none" stroke="#26dce8" stroke-width="0.8">
 <circle cx="50" cy="50" r="43"/><circle cx="50" cy="50" r="34" stroke-opacity="0.4"/>
 <path d="M50 4 V13 M50 87 V96 M4 50 H13 M87 50 H96 M19 19 L24 24 M76 76 L81 81 M19 81 L24 76 M76 24 L81 19"/>
 <path d="M50 25 V36 M50 64 V75 M25 50 H36 M64 50 H75" stroke-opacity="0.5"/>
</g>
<polygon points="50,17 57,43 50,39 43,43" fill="#ffb458"/>
<polygon points="50,83 43,57 50,61 57,57" fill="#20b6ce" fill-opacity="0.75"/>
<polygon points="17,50 43,43 39,50 43,57" fill="#20b6ce" fill-opacity="0.75"/>
<polygon points="83,50 57,57 61,50 57,43" fill="#20b6ce" fill-opacity="0.75"/>
<polygon points="50,44 56,50 50,56 44,50" fill="#c8fbff"/>
<g fill="#26dce8"><rect x="38" y="90" width="5" height="1.5"/><rect x="47.5" y="90" width="5" height="1.5"/><rect x="57" y="90" width="5" height="1.5"/></g>`);
const schematic = wrap(`
<rect x="7" y="7" width="86" height="86" fill="#071e2c" fill-opacity="0.8"/>
<g fill="none" stroke="#26dce8" stroke-width="0.6" stroke-opacity="0.3">
 <path d="M20 12 V88 M35 12 V88 M50 12 V88 M65 12 V88 M80 12 V88 M12 20 H88 M12 35 H88 M12 50 H88 M12 65 H88 M12 80 H88"/>
</g>
<g stroke="#8cf4ff" stroke-width="0.8">
 <polygon points="50,16 59,34 59,67 54,77 46,77 41,67 41,34" fill="#174b61"/>
 <polygon points="41,43 25,54 17,71 41,64" fill="#103547"/>
 <polygon points="59,43 75,54 83,71 59,64" fill="#103547"/>
 <rect x="26" y="59" width="7" height="19" fill="#245e74"/>
 <rect x="67" y="59" width="7" height="19" fill="#245e74"/>
 <polygon points="50,23 54,35 46,35" fill="#35bfd0"/>
</g>
<g fill="#ffb458"><rect x="27" y="74" width="5" height="8"/><rect x="68" y="74" width="5" height="8"/><rect x="46" y="74" width="8" height="9"/></g>
<g fill="none" stroke="#26dce8" stroke-width="0.8">
 <path d="M12 32 V12 H32 M68 12 H88 V32 M12 68 V88 H32 M68 88 H88 V68"/>
 <path d="M36 28 H24 V22 M65 42 H78 V32 M61 82 H78" stroke-opacity="0.7"/>
</g>
<g fill="#26dce8"><circle cx="36" cy="28" r="1.3"/><circle cx="65" cy="42" r="1.3"/><circle cx="61" cy="82" r="1.3"/></g>`);
const radarBase = `<circle cx="50" cy="50" r="43" fill="#071e2c" fill-opacity="0.9"/>
<g fill="none" stroke="#26dce8" stroke-width="0.7" stroke-opacity="0.65">
 <circle cx="50" cy="50" r="43"/><circle cx="50" cy="50" r="29"/><circle cx="50" cy="50" r="15"/>
 <path d="M50 7 V93 M7 50 H93" stroke-opacity="0.3"/>
</g>`;
const beam = `<polygon points="50,50 89.1,31.8 92.3,42.5 93,50" fill="#26dce8" fill-opacity="0.16"/>
 <path d="M50 50 H93" fill="none" stroke="#8cf4ff" stroke-width="1.4"/>`;
const contacts = `<g fill="#ffb458"><circle cx="70" cy="31" r="2"/><circle cx="24" cy="64" r="1.6"/></g>
 <g fill="none" stroke="#ffb458" stroke-width="0.8"><path d="M66 25 H74 M70 21 V29"/><polygon points="76,70 80,74 76,78 72,74"/></g>
 <circle cx="50" cy="50" r="2" fill="#c8fbff"/>`;
const frames = Array.from({ length: 32 }, (_, i) => [wrap(`${radarBase}<g transform="rotate(${i * 360 / 32} 50 50)">${beam}</g>${contacts}`)]);
const animatedPreview = wrap(`${radarBase}<g>${beam}<animateTransform attributeName="transform" type="rotate" from="0 50 50" to="360 50 50" dur="8s" repeatCount="indefinite"/></g>${contacts}`);
const samples = [
 { name: 'navigation-compass', svg: navigation, frames: [[navigation]], fps: 1, type: 'static' },
 { name: 'ship-schematic', svg: schematic, frames: [[schematic]], fps: 1, type: 'static' },
 { name: 'radar-sweep', svg: animatedPreview, frames, fps: 4, type: 'animated' }
];
const stats = [];
for (const sample of samples) {
  const packed = packAnimation(sample.frames.map(f => f.map(s => s.replace(/\s+/g, ' ').trim())), sample.fps);
  await writeFile(path.join(directory, `${sample.name}.svg`), sample.svg);
  await writeFile(path.join(directory, `${sample.name}.customdata.txt`), packed.encoded);
  await writeFile(path.join(directory, `${sample.name}.animation.txt`), packed.plain);
  stats.push({ name: sample.name, type: sample.type, ...packed.stats });
}
await writeFile(path.join(directory, 'stats.json'), JSON.stringify(stats, null, 2));
console.log(stats);
