// Offline conversion of the user's Pelican & Pedals scene. No source JS is executed.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const input = process.argv[2];
if (!input) throw new Error('Usage: node Tools/PackPelican.mjs <physics/index.html>');
const html = await readFile(input, 'utf8');
const raw = html.match(/<svg\b[^>]*id="scene"[^>]*>[\s\S]*?<\/svg>/)?.[0];
if (!raw) throw new Error('Expected SVG scene with id="scene".');
// Parse only this known SVG asset, strip metadata, expand local use elements.
const root = { tag: 'root', attrs: {}, children: [] }, stack = [root], ids = new Map();
for (const token of raw.replace(/<!--[\s\S]*?-->/g, '').matchAll(/<([^>]+)>/g)) {
  let body = token[1].trim();
  if (body.startsWith('/')) { stack.pop(); continue; }
  const self = body.endsWith('/'); body = body.replace(/\/$/, '');
  const tag = body.match(/^\S+/)[0], attrs = {};
  for (const m of body.matchAll(/([\w:-]+)\s*=\s*(["'])(.*?)\2/g)) attrs[m[1]] = m[3];
  const node = { tag, attrs, children: [] }; stack.at(-1).children.push(node);
  if (attrs.id) ids.set(attrs.id, node);
  if (!self) stack.push(node);
}
const scene = root.children[0];
if (!ids.has('pelican') || !ids.has('bicycle')) throw new Error('Expected pelican and bicycle groups.');
const clone = n => structuredClone(n);
function expand(n, depth = 0) {
  if (depth > 16) throw new Error('Recursive use definition.');
  if (n.tag === 'use') {
    const target = ids.get((n.attrs.href ?? '').slice(1)); if (!target) throw new Error('Unknown local SVG use.');
    const attrs = { ...n.attrs }; delete attrs.href;
    const x = attrs.x ?? 0, y = attrs.y ?? 0; delete attrs.x; delete attrs.y;
    attrs.transform = `${attrs.transform ?? ''} translate(${x} ${y})`;
    return expand({ tag: 'g', attrs, children: [clone(target)] }, depth + 1);
  }
  n.children = n.children.filter(c => !['defs', 'title', 'desc', 'text'].includes(c.tag)).map(c => expand(c, depth + 1));
  if (n.attrs.class === 'outline') Object.assign(n.attrs, { stroke: '#284d43', 'stroke-width': n.attrs['stroke-width'] ?? '3' });
  if (n.attrs.class === 'fine') Object.assign(n.attrs, { fill: 'none', stroke: '#284d43', 'stroke-width': n.attrs['stroke-width'] ?? '2' });
  if (n.tag==='path' && (n.attrs.d?.match(/[Mm]/g)?.length??0)>1 && !n.attrs.fill) n.attrs.fill='none';
  for (const key of Object.keys(n.attrs)) if (['class', 'role', 'aria-labelledby', 'aria-hidden', 'stroke-linecap', 'stroke-linejoin'].includes(key)) delete n.attrs[key];
  return n;
}
// Retain bike + legs + pelican; omit the drifting scenery, bell text and page chrome.
const foreground = scene.children.filter(n => n.attrs.id === 'bicycle' || n.attrs.id === 'pelican' ||
  (n.tag === 'g' && n.children.some(c => c.attrs.id === 'near-leg'))).map(n => expand(clone(n)));
const rounded = s => s.replace(/[-+]?\d*\.\d+(?:e[-+]?\d+)?/gi, n => String(Math.round(Number(n) * 100) / 100));
const escape = s => s.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
function serialize(n) {
  return `<${n.tag}${Object.entries(n.attrs).filter(([k]) => k !== 'id').map(([k, v]) => ` ${k}="${escape(rounded(String(v)))}"`).join('')}>${n.children.map(serialize).join('')}</${n.tag}>`;
}
function indexNodes(nodes) { const map = new Map(); function visit(n) { if(n.attrs.id) map.set(n.attrs.id,n); n.children.forEach(visit); } nodes.forEach(visit); return map; }
function leg(hip, foot, bend) {
  const dx=foot.x-hip.x,dy=foot.y-hip.y,d=Math.hypot(dx,dy),along=(79*79-86*86+d*d)/(2*d),perp=Math.sqrt(Math.max(0,79*79-along*along));
  return `M${hip.x} ${hip.y} L${hip.x+along*dx/d-bend*perp*dy/d} ${hip.y+along*dy/d+bend*perp*dx/d} L${foot.x} ${foot.y}`;
}
const fps=4, duration=8*Math.PI*2/2.8, count=Math.round(duration*fps);
const frames=[];
for(let i=0;i<count;i++) {
  const nodes=foreground.map(clone), parts=indexNodes(nodes), angle=8*Math.PI*2*i/count, bob=Math.sin(angle*2)*2, degrees=angle*180/Math.PI;
  const attr=(id,k,v)=>{ const n=parts.get(id); if(!n)throw new Error(`Missing ${id}`);n.attrs[k]=v; };
  attr('rear-wheel','transform',`rotate(${degrees*1.75})`);attr('front-wheel','transform',`rotate(${degrees*1.75})`);
  attr('pelican','transform',`translate(0 ${bob})`);attr('crank','transform',`rotate(${degrees} 549 473)`);
  const near={x:549+Math.cos(angle)*30,y:473+Math.sin(angle)*30},far={x:549-Math.cos(angle)*30,y:473-Math.sin(angle)*30};
  const nearPath=leg({x:521,y:350+bob},{x:near.x-3,y:near.y-7},1);
  attr('near-leg','d',nearPath);attr('near-leg-color','d',nearPath);attr('far-leg','d',leg({x:536,y:350+bob},{x:far.x-3,y:far.y-7},-1));
  attr('near-foot','d',`M${near.x-13} ${near.y-6}q11-7 22-3l14 9h-36Z`);attr('far-foot','d',`M${far.x-10} ${far.y-5}h28`);
  attr('near-pedal','d',`M${near.x-13} ${near.y+2}h36`);attr('far-pedal','d',`M${far.x-13} ${far.y+2}h36`);
  const flutter=Math.sin(angle*2.5)*7;
  attr('scarf-tail','d',`M577 235C535 ${214+flutter} 497 ${242-flutter} 455 ${222+flutter}L466 ${245+flutter} 449 ${253+flutter}C495 ${268-flutter} 540 ${243+flutter} 580 256Z`);
  attr('scarf-seam','d',`M464 ${240+flutter}Q518 ${254-flutter} 575 244`);
  // Make the blink loop along with the exported scene.
  attr('eye','transform',`translate(631 146) scale(1 ${i%24===23?0.12:1})`);
  // One SVG per foreground group keeps meshes bounded and preserves inherited styles.
  frames.push(nodes.map(n=>`<svg viewBox="330 100 480 480">${serialize(n)}</svg>`));
}
const plain=Buffer.from(`HMA1\n${fps}\n${count}\n${foreground.length}\n${frames.flat().join('\n')}\n`,'utf8');
// LZSS: flag byte + eight tokens. 16-bit distance, length 3–258. Bounded game decoder.
function compress(data) {
  const output=[], chains=new Map();let p=0;
  const key=i=>i+2<data.length?(data[i]<<16)|(data[i+1]<<8)|data[i+2]:-1;
  function remember(i){const k=key(i);if(k<0)return;let list=chains.get(k);if(!list)chains.set(k,list=[]);list.push(i);while(list.length>64||i-list[0]>65535)list.shift();}
  while(p<data.length){const flagIndex=output.length;output.push(0);let flags=0;
    for(let bit=0;bit<8&&p<data.length;bit++){
      let length=0,distance=0;const candidates=chains.get(key(p))??[];
      for(let j=candidates.length-1;j>=0;j--){const q=candidates[j],d=p-q;if(d>65535)break;let n=0;while(n<258&&p+n<data.length&&data[q+n]===data[p+n])n++;if(n>length){length=n;distance=d;}if(length===258)break;}
      if(length>=3){output.push(distance&255,distance>>8,length-3);for(let j=0;j<length;j++)remember(p+j);p+=length;}
      else{flags|=1<<bit;output.push(data[p]);remember(p++);}
    }output[flagIndex]=flags;
  }return Buffer.from(output);
}
const compressed=compress(plain),header=Buffer.alloc(8);header.write('HMC1');header.writeUInt32LE(plain.length,4);
const customData='HoloMapAnimation1:'+Buffer.concat([header,compressed]).toString('base64');
const taskRoot=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');await mkdir(path.join(taskRoot,'artifacts'),{recursive:true});
await writeFile(path.join(taskRoot,'artifacts/pelican.customdata.txt'),customData);
await writeFile(path.join(taskRoot,'artifacts/pelican.animation.txt'),plain);
await writeFile(path.join(taskRoot,'artifacts/pelican.preview.svg'),`<svg xmlns="http://www.w3.org/2000/svg" viewBox="330 100 480 480">${foreground.map(serialize).join('')}</svg>`);
const stats={sourceBytes:Buffer.byteLength(html),decodedBytes:plain.length,compressedBytes:compressed.length,customDataCharacters:customData.length,fps,frames:count,parts:foreground.length,duration:count/fps};
await writeFile(path.join(taskRoot,'artifacts/pelican.stats.json'),JSON.stringify(stats,null,2));console.log(stats);
if(customData.length>60000)throw new Error('Compressed animation exceeds the 60k transport budget; reduce sampling.');
