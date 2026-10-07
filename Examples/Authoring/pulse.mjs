export const fps = 4;
export const duration = 2;
// Replace with React renderToStaticMarkup(<YourSvg time={t}/>), or compiled Svelte SSR.
export function render(t) {
  const radius = 20 + 8 * Math.sin(Math.PI * t);
  return `<svg viewBox="0 0 100 100"><circle cx="50" cy="50" r="${radius}" fill="none" stroke="#00dfff" stroke-width="2"/></svg>`;
}
