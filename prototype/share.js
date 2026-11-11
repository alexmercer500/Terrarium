// Terrarium · one-click sharing (shared by every theme page; load it after the theme script)
// Redraws the current scene at the screen's actual pixel size, then draws a sample icon (folder, document, picture, app)
// at each icon's position, with the name drawn as a white rounded block. No screenshot is taken: icon images and file
// names are never read, only positions (the icon kind and name length are made up pseudo-randomly from the position),
// so no file name can appear in the image, and it doesn't matter if the settings window or other windows cover the desktop.
// Uses from the theme page: cv (canvas), draw(), icons, W, H, WALL; some themes also have renderLabels() and cacheOK
// (text and ground drawings rendered on a 2D canvas, which must be redrawn when the canvas size changes)
(function () {
  // Deterministic random numbers: an icon at the same position always comes out looking the same
  function rnd(x, y, k) {
    let h = Math.imul(Math.round(x) | 0, 374761393) ^ Math.imul(Math.round(y) | 0, 668265263) ^ Math.imul(k + 7, 2246822519);
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
  }
  function roundRect(ctx, x, y, w, h, r) { ctx.beginPath(); addRoundRect(ctx, x, y, w, h, r); }
  function addRoundRect(ctx, x, y, w, h, r) {               // doesn't start a new path: used when several pieces form one shape
    ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath();
  }
  const BADGES = ['#2b6cd4', '#1d7d45', '#c8372c', '#d86f2a', '#7a3fc4'];
  const APPS = [['#29a3e8', '#1474c4'], ['#9b5de5', '#6a35c2'], ['#20c08f', '#0f8f68'], ['#ff7a59', '#e0452c']];
  // One sample icon (g: icon size in physical pixels): folder, document, picture or app; the shadow is a slightly offset translucent shape (blurred shadows are too slow)
  function sampleGlyph(ctx, x, y, g, u) {
    const kind = u(1), sh = Math.max(1, g * 0.03);
    const shape = (fill, f) => { ctx.fillStyle = 'rgba(0,0,0,0.18)'; ctx.save(); ctx.translate(0, sh); f(); ctx.fill(); ctx.restore(); ctx.fillStyle = fill; f(); ctx.fill(); };
    if (kind < 0.36) {                                       // folder: a back panel with a tab, a lighter front panel
      shape('#e3a32f', () => { ctx.beginPath(); addRoundRect(ctx, x + g * 0.06, y + g * 0.2, g * 0.88, g * 0.64, g * 0.07);
        addRoundRect(ctx, x + g * 0.06, y + g * 0.14, g * 0.38, g * 0.2, g * 0.06); });
      ctx.fillStyle = '#fcd25c'; roundRect(ctx, x + g * 0.06, y + g * 0.32, g * 0.88, g * 0.52, g * 0.07); ctx.fill();
      ctx.fillStyle = 'rgba(255,255,255,0.35)'; ctx.fillRect(x + g * 0.12, y + g * 0.36, g * 0.76, g * 0.03);
    } else if (kind < 0.76) {                                // document: white page with a folded corner, a few grey lines, a colored badge at the bottom left
      const px = x + g * 0.2, py = y + g * 0.06, pw = g * 0.6, ph = g * 0.84, c = g * 0.16;
      shape('#f7f9fc', () => { ctx.beginPath(); ctx.moveTo(px, py + g * 0.04); ctx.arcTo(px, py, px + g * 0.04, py, g * 0.04);
        ctx.lineTo(px + pw - c, py); ctx.lineTo(px + pw, py + c); ctx.lineTo(px + pw, py + ph - g * 0.04);
        ctx.arcTo(px + pw, py + ph, px + pw - g * 0.04, py + ph, g * 0.04); ctx.lineTo(px + g * 0.04, py + ph);
        ctx.arcTo(px, py + ph, px, py + ph - g * 0.04, g * 0.04); ctx.closePath(); });
      ctx.fillStyle = '#d9dfe7'; ctx.beginPath(); ctx.moveTo(px + pw - c, py); ctx.lineTo(px + pw - c, py + c); ctx.lineTo(px + pw, py + c); ctx.fill();
      ctx.fillStyle = '#cfd6df';
      for (let i = 0; i < 4; i++) ctx.fillRect(px + pw * 0.38, py + ph * (0.3 + i * 0.13), pw * 0.48, g * 0.035);
      const badge = BADGES[Math.floor(u(2) * BADGES.length)];
      shape(badge, () => { ctx.beginPath(); roundRect(ctx, x + g * 0.08, y + g * 0.44, g * 0.42, g * 0.38, g * 0.06); });
      ctx.fillStyle = 'rgba(255,255,255,0.9)'; roundRect(ctx, x + g * 0.17, y + g * 0.56, g * 0.24, g * 0.14, g * 0.03); ctx.fill();
    } else if (kind < 0.9) {                                 // picture: blue sky, a sun and two green hills in a white frame
      const fx = x + g * 0.1, fy = y + g * 0.16, fw = g * 0.8, fh = g * 0.66, m = g * 0.06;
      shape('#f7f9fc', () => { ctx.beginPath(); roundRect(ctx, fx, fy, fw, fh, g * 0.05); });
      ctx.save(); ctx.beginPath(); ctx.rect(fx + m, fy + m, fw - 2 * m, fh - 2 * m); ctx.clip();
      ctx.fillStyle = '#8fd0f2'; ctx.fillRect(fx + m, fy + m, fw - 2 * m, fh - 2 * m);
      ctx.fillStyle = '#ffd34d'; ctx.beginPath(); ctx.arc(fx + fw * 0.72, fy + fh * 0.32, g * 0.07, 0, 7); ctx.fill();
      ctx.fillStyle = '#5cbf6c'; ctx.beginPath(); ctx.moveTo(fx, fy + fh); ctx.lineTo(fx + fw * 0.36, fy + fh * 0.44); ctx.lineTo(fx + fw * 0.66, fy + fh); ctx.fill();
      ctx.fillStyle = '#3e9f58'; ctx.beginPath(); ctx.moveTo(fx + fw * 0.4, fy + fh); ctx.lineTo(fx + fw * 0.7, fy + fh * 0.58); ctx.lineTo(fx + fw, fy + fh * 0.9); ctx.lineTo(fx + fw, fy + fh); ctx.fill();
      ctx.restore();
    } else {                                                 // app: a rounded square, lighter at the top and darker at the bottom
      const [a, b] = APPS[Math.floor(u(3) * APPS.length)];
      const grad = ctx.createLinearGradient(0, y + g * 0.12, 0, y + g * 0.88);
      grad.addColorStop(0, a); grad.addColorStop(1, b);
      shape(grad, () => { ctx.beginPath(); roundRect(ctx, x + g * 0.12, y + g * 0.12, g * 0.76, g * 0.76, g * 0.18); });
    }
  }
  // Name: one or two lines of white rounded blocks (instead of text) over a slightly offset translucent black, like the shadow under desktop icon text
  function sampleLabel(ctx, cx, y, w, lh) {
    const h = lh * 0.56, r = h / 2, o = Math.max(1, lh * 0.07);
    ctx.fillStyle = 'rgba(0,0,0,0.35)'; roundRect(ctx, cx - w / 2 + o, y + (lh - h) / 2 + o, w, h, r); ctx.fill();
    ctx.fillStyle = 'rgba(255,255,255,0.95)'; roundRect(ctx, cx - w / 2, y + (lh - h) / 2, w, h, r); ctx.fill();
  }
  // Draws one icon (sample glyph + name block) at (px, py) in logical pixels; its look is fixed by where the icon first appeared, so it doesn't change while dragged
  // lift: how far it is picked up (0..1): slightly enlarged and translucent, like an icon being dragged
  function drawIcon(ctx, ic, s, px, py, lift) {
    if (!ic._seed) ic._seed = [ic.x, ic.y];
    const u = k => rnd(ic._seed[0], ic._seed[1], k);
    lift = lift || 0;
    ctx.save();
    if (lift > 0) {
      const sc = 1 + 0.06 * lift, cx = (px + ic.w / 2) * s, cy = (py + ic.h / 2) * s;
      ctx.translate(cx, cy); ctx.scale(sc, sc); ctx.translate(-cx, -cy);
      ctx.globalAlpha = 1 - 0.25 * lift;
    }
    const x = px * s, y = py * s, w = ic.w * s, h = ic.h * s;
    const g = Math.min(48 * s, w - 8 * s, h * 0.62);         // glyph: a centered square at the top
    sampleGlyph(ctx, x + (w - g) / 2, y + 3 * s, g, u);
    const lh = 15 * s, ly = y + 3 * s + g + 4 * s, lines = u(9) < 0.45 ? 2 : 1;   // name: one or two lines below
    if (ly + lh <= y + h + 6 * s)
      for (let i = 0; i < lines; i++) sampleLabel(ctx, x + w / 2, ly + i * lh, w * (i ? 0.3 + 0.35 * u(11) : 0.5 + 0.42 * u(10)), lh);
    ctx.restore();
  }
  // The theme has 2D-canvas content and has already drawn it (the theme's static layer)
  function labelsReady() {
    return typeof renderLabels === 'function' && ((typeof city !== 'undefined' && city) || (typeof world !== 'undefined' && world));
  }
  function drawSampleIcons(ctx, s) { for (const ic of icons) drawIcon(ctx, ic, s, ic.x, ic.y, 0); }
  // Draws one frame at the given scale (default: the screen's actual pixels), adds sample icons and returns a 2D canvas
  function snapshot(scale) {
    scale = scale || window.devicePixelRatio || 1;
    const old = { w: cv.width, h: cv.height, s: cv._scale };
    const hasLabels = labelsReady();
    const hasCache = typeof cacheOK !== 'undefined';
    const box = cv.getBoundingClientRect();                  // sized from the canvas's actual (fractional) box, like the theme's resize(), so the image matches the screen pixels exactly
    cv.width = Math.round((box.width || W) * scale); cv.height = Math.round((box.height || H) * scale); cv._scale = scale;
    if (hasLabels) renderLabels();
    if (hasCache) cacheOK = false;
    draw(performance.now());
    const out = document.createElement('canvas');
    out.width = cv.width; out.height = cv.height;
    const ctx = out.getContext('2d');
    ctx.drawImage(cv, 0, 0);                                  // read back in the same task right after drawing, so preserveDrawingBuffer isn't needed
    cv.width = old.w; cv.height = old.h; cv._scale = old.s;   // redraw at the original size (same task, so nothing flickers on screen)
    if (hasLabels) renderLabels();
    if (hasCache) cacheOK = false;
    draw(performance.now());
    drawSampleIcons(ctx, scale);
    return out;
  }
  function thumbOf(canvas, maxW) {
    const t = document.createElement('canvas');
    t.width = maxW; t.height = Math.round(canvas.height * maxW / canvas.width);
    const c = t.getContext('2d'); c.imageSmoothingQuality = 'high';
    c.drawImage(canvas, 0, 0, t.width, t.height);
    return t.toDataURL('image/jpeg', 0.86);
  }
  window.desklandSnapshot = function (scale) { const c = snapshot(scale); return { canvas: c, thumb: thumbOf(c, 520) }; };
  window.desklandSnapshot.parts = { snapshot, drawSampleIcons, sampleGlyph, sampleLabel, thumbOf };   // for debugging and timing

  /* ---- Share video: records 12 seconds of the theme animation with the current desktop layout; icons and file names are still sample shapes for privacy ----
     No simulated dragging, no temporary icons, no recomputing the icon field. If paused, unpause first and restore the paused state afterwards.
     The drag demo choreography is only kept when the website's offline demo tool explicitly sets __recDemoStyle. */
  // Picks icons to drag and where to drop them: take one from a cluster and drop it on the grid cell farthest from other icons (away from the screen edges, moderate drag distance)
  function planMoves(list, n, only) {
    const H2 = H - TASKBAR, cells = [];
    for (let c = 0; GRID_X0 + (c + 1) * CELL_W <= W; c++)
      for (let r = 0; GRID_Y0 + (r + 1) * CELL_H <= H2; r++) cells.push([GRID_X0 + c * CELL_W, GRID_Y0 + r * CELL_H]);
    const pos = new Map(list.map(ic => [ic, [ic.x, ic.y]]));
    const ctr = ic => { const p = pos.get(ic); return [p[0] + ic.w / 2, p[1] + ic.h / 2]; };
    const moves = [];
    for (let k = 0; k < n; k++) {
      let best = null;
      for (const ic of list) {
        if (only && ic !== only) continue;   // only consider the given icon (avoidance and drop spot still account for all icons)
        const [cx, cy] = ctr(ic);
        let nb = 0;
        for (const o of list) if (o !== ic) { const [ox, oy] = ctr(o); if (Math.hypot(ox - cx, oy - cy) < 170) nb++; }
        for (const [x, y] of cells) {
          const dx = x + ic.w / 2, dy = y + ic.h / 2, d = Math.hypot(dx - cx, dy - cy);
          if (d < 260 || d > 900 || Math.min(dx, dy, W - dx, H2 - dy) < 90) continue;
          let md = 1e9;
          for (const o of list) if (o !== ic) { const [ox, oy] = ctr(o); md = Math.min(md, Math.hypot(ox - dx, oy - dy)); }
          const score = Math.min(md, 420) + Math.min(nb, 6) * 25 + rnd(x, y, 3 + (window.__recSeed || 0)) * 30;
          if (!best || score > best.score) best = { ic, from: pos.get(ic).slice(), to: [x, y], score };
        }
      }
      if (!best) break;
      moves.push(best);
      pos.set(best.ic, best.to);
    }
    return moves;
  }
  // Picks a batch of icons to drag together: box-select around the densest spot (the bounding box of the K nearest icons; everything inside counts)
  // and move the whole batch to the place that fits and is farthest from other icons; if nowhere fits, try again with fewer icons.
  // pos: positions after the earlier drags; fixed: an explicit batch to select (used by the web demo)
  function planGroup(list, pos, fixed) {
    if (list.length < 4) return null;
    const H2 = H - TASKBAR, ctr = ic => { const p = pos.get(ic); return [p[0] + ic.w / 2, p[1] + ic.h / 2]; };
    let cand = [];                                       // batches to try in order (each box is spanned by its batch's icons)
    if (fixed) cand = [fixed];
    else {
      let seed = null, bn = -1;
      for (const ic of list) {
        const [cx, cy] = ctr(ic); let nb = 0;
        for (const o of list) if (o !== ic) { const [ox, oy] = ctr(o); if (Math.hypot(ox - cx, oy - cy) < 220) nb++; }
        if (nb > bn) { bn = nb; seed = ic; }
      }
      const [sx, sy] = ctr(seed);
      const byDist = list.slice().sort((a, b) => { const [ax, ay] = ctr(a), [bx, by] = ctr(b); return Math.hypot(ax - sx, ay - sy) - Math.hypot(bx - sx, by - sy); });
      for (let K = window.__recGroupCount || Math.max(5, Math.min(16, Math.round(list.length * 0.45))); K >= 4; K -= 2) cand.push(byDist.slice(0, K));   // the web demo fixes the count with __recGroupCount; otherwise drag a large batch: a bit over 40% of the icons, at most 16
    }
    for (const batch of cand) {
      let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
      for (const ic of batch) { const p = pos.get(ic); x0 = Math.min(x0, p[0]); y0 = Math.min(y0, p[1]); x1 = Math.max(x1, p[0] + ic.w); y1 = Math.max(y1, p[1] + ic.h); }
      const group = list.filter(ic => { const p = pos.get(ic); return p[0] >= x0 - 1 && p[1] >= y0 - 1 && p[0] + ic.w <= x1 + 1 && p[1] + ic.h <= y1 + 1; });
      const rest = list.filter(ic => !group.includes(ic)).map(ctr);
      const gw = x1 - x0, gh = y1 - y0;
      let best = null;
      for (let ty = 24; ty + gh <= H2 - 24; ty += 24) for (let tx = 24; tx + gw <= W - 24; tx += 24) {
        const dx = tx - x0, dy = ty - y0, d = Math.hypot(dx, dy);
        if (d < 300) continue;
        let md = 1e9;
        for (const [ox, oy] of rest) md = Math.min(md, Math.hypot(Math.max(tx - ox, 0, ox - tx - gw), Math.max(ty - oy, 0, oy - ty - gh)));
        const score = Math.min(md, 300) - d * 0.05 + rnd(tx, ty, 5 + (window.__recSeed || 0)) * 70;
        if (md >= 24 && (!best || score > best.score)) best = { dx, dy, score };   // the drop spot must not touch other icons (at least 24px clear)
      }
      if (best)
        return { kind: 'group', group, from: group.map(ic => pos.get(ic).slice()), delta: [best.dx, best.dy], seed: batch[0], box: [x0, y0, x1, y1] };
    }
    return null;
  }
  const ease = t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  const clamp01 = t => Math.min(1, Math.max(0, t));
  // Selected icon: a light blue rounded background (like a selected icon on the desktop)
  function drawSelected(ctx, ic, x, y, s, a) {
    if (a <= 0) return;
    ctx.save(); ctx.globalAlpha = a;
    roundRect(ctx, x * s, y * s, ic.w * s, ic.h * s, 4 * s);
    ctx.fillStyle = 'rgba(170, 210, 255, 0.38)'; ctx.fill();
    ctx.lineWidth = Math.max(1, s); ctx.strokeStyle = 'rgba(200, 230, 255, 0.9)'; ctx.stroke();
    ctx.restore();
  }
  // Mouse pointer (the classic arrow: white with a black outline)
  function drawCursor(ctx, x, y, s, alpha) {
    if (alpha <= 0) return;
    const k = 1.6 * s, pts = [[0, 0], [0, 16], [4, 12.5], [6.8, 18.6], [9.3, 17.5], [6.6, 11.6], [11.5, 11.6]];
    ctx.save(); ctx.globalAlpha = alpha; ctx.translate(x, y); ctx.scale(k, k);
    ctx.beginPath(); pts.forEach(([px, py], i) => i ? ctx.lineTo(px, py) : ctx.moveTo(px, py)); ctx.closePath();
    ctx.fillStyle = 'rgba(0,0,0,0.25)'; ctx.save(); ctx.translate(0.8, 1.2); ctx.fill(); ctx.restore();
    ctx.fillStyle = '#fff'; ctx.fill(); ctx.lineWidth = 1.1; ctx.strokeStyle = '#111'; ctx.lineJoin = 'round'; ctx.stroke();
    ctx.restore();
  }
  function pickMime() {
    for (const t of ['video/mp4;codecs=avc1.640028', 'video/mp4;codecs=avc1', 'video/mp4', 'video/webm;codecs=vp9', 'video/webm'])
      if (window.MediaRecorder && MediaRecorder.isTypeSupported(t)) return t;
    return null;
  }
  let busy = false;
  // Color range: when the browser records H.264 it encodes "full range" (0-255) and only says so in the MP4 colr box, not in the bitstream.
  // Many players (and chat apps that transcode) only look at the bitstream and play it as "limited range" (16-235), so whites blow out and everything looks brighter.
  // So before recording, squeeze the colors into 16-235 (an SVG filter computed directly in sRGB), and afterwards change the colr "full range" flag to "limited range":
  // bitstream, container and pixels then agree, and it plays correctly whichever convention the player follows
  function limitedRangeFilter() {
    if (document.getElementById('deskland-limited-range')) return 'url(#deskland-limited-range)';
    const ns = 'http://www.w3.org/2000/svg', svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('width', '0'); svg.setAttribute('height', '0');
    svg.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden';
    const fl = document.createElementNS(ns, 'filter');
    fl.id = 'deskland-limited-range';
    fl.setAttribute('color-interpolation-filters', 'sRGB');
    const ct = document.createElementNS(ns, 'feComponentTransfer');
    for (const c of ['R', 'G', 'B']) {
      const fn = document.createElementNS(ns, 'feFunc' + c);
      fn.setAttribute('type', 'linear'); fn.setAttribute('slope', String(219 / 255)); fn.setAttribute('intercept', String(16 / 255));
      ct.appendChild(fn);
    }
    fl.appendChild(ct); svg.appendChild(fl); document.body.appendChild(svg);
    return 'url(#deskland-limited-range)';
  }
  // Clears the "full range" flag in the MP4's colr (nclx) box (only looks in the moov at the start)
  async function markLimitedRange(blob) {
    const head = new Uint8Array(await blob.slice(0, 8192).arrayBuffer());
    for (let i = 0; i + 15 < head.length; i++) {
      if (head[i] === 0x63 && head[i + 1] === 0x6f && head[i + 2] === 0x6c && head[i + 3] === 0x72 &&       // 'colr'
          head[i + 4] === 0x6e && head[i + 5] === 0x63 && head[i + 6] === 0x6c && head[i + 7] === 0x78) {  // 'nclx'
        head[i + 14] &= 0x7f;                                // top bit of the byte after primaries(2) transfer(2) matrix(2) = full range
        return new Blob([head, blob.slice(8192)], { type: blob.type });
      }
    }
    return blob;
  }
  // Records a clip: returns Promise<{ blob, mime, poster }>; onProgress(0..1)
  // Waits for the scene to settle first: if the wallpaper is paused (e.g. the settings window is maximized) let it run for a while, and if the opening trees and houses haven't finished growing, wait for them (up to 6 seconds)
  function record(onProgress) {
    const mime = pickMime();
    if (!mime) return Promise.reject(new Error('This computer does not support video recording'));
    if (busy) return Promise.reject(new Error('The previous clip is still recording'));
    busy = true;
    const wasPaused = typeof paused !== 'undefined' && paused;
    if (wasPaused) paused = false;
    return new Promise(ok => {
      const t0 = performance.now(), f0 = frameN;
      const tick = () => {
        const dt = performance.now() - t0, ran = frameN - f0;
        if ((ran >= 10 && settled()) || dt > 6000 || (dt > 1000 && ran < 2)) ok(); else setTimeout(tick, 100);
      };
      if (!wasPaused && settled()) ok(); else tick();
    }).then(() => recordNow(onProgress, mime, wasPaused));
  }
  function recordNow(onProgress, mime, wasPaused) {
    // Frame size: screen aspect ratio, about 2 megapixels (even width and height, as H.264 requires); web videos can raise this with window.__recMaxPixels
    const rs = Math.min(window.devicePixelRatio || 1, Math.sqrt((window.__recMaxPixels || 2.0e6) / (W * H)));
    const outW = Math.round(W * rs / 2) * 2, outH = Math.round(H * rs / 2) * 2;
    const old = { w: cv.width, h: cv.height, s: cv._scale };
    const hasLabels = typeof renderLabels === 'function';
    const hasCache = typeof cacheOK !== 'undefined';
    const demo = !WALL && !!window.__recDemoStyle;
    const saved = demo ? icons.map(ic => [ic, ic.x, ic.y]) : [];
    // Only the offline demo may add a temporary icon; normal sharing keeps an empty desktop empty.
    const temp = demo && !icons.length ? { x: W * 0.28, y: (H - TASKBAR) * 0.3, w: 76, h: 92 } : null;
    if (temp) { icons.push(temp); commit(); }
    // Web demo choreography (__recDemoStyle): first drag the icon nearest the screen center on its own (the "hero" in the middle of the demo layout),
    // then box-select and drag away a whole block on the left (two rows, 5 icons in the demo layout) - showing both "move one" and "move many"
    let moves = [];
    if (demo && !temp) {
      const cx = W / 2, cy = (H - TASKBAR) / 2;
      const hero = icons.reduce((a, b) => Math.hypot(a.x + a.w / 2 - cx, a.y + a.h / 2 - cy) <= Math.hypot(b.x + b.w / 2 - cx, b.y + b.h / 2 - cy) ? a : b);
      moves = planMoves(icons, 1, hero);
    } else if (demo) moves = planMoves(icons, temp ? 1 : 2);
    const HOLD0 = 0.8, DRAG = 1.5, ADAPT = 2.6, HOLD1 = 0.6;
    // Box-selecting a batch: 0.3-1.1 s drag the selection box, 1.1-1.5 s move the pointer onto one of the icons, 1.6-2.8 s drag them over, then 2.9 s for the scene to adapt after the drop
    const GSEL = 1.1, GDROP = 2.8, GADAPT = 2.9;
    const after = new Map(icons.map(ic => [ic, [ic.x, ic.y]]));
    for (const m of moves) after.set(m.ic, m.to);
    let grp = demo && !temp ? planGroup(icons, after) : null;
    if (demo && !temp) {
      // Web demo: the multi-select always boxes the two short rows on the right (the 6 icons in columns 2-3, first 3 rows of the demo layout)
      const bx0 = GRID_X0 + 2 * CELL_W - 2, bx1 = GRID_X0 + 4 * CELL_W - 2, by1 = GRID_Y0 + 3 * CELL_H - 2;
      const block = icons.filter(ic => ic.x >= bx0 && ic.x < bx1 && ic.y < by1);
      if (block.length) grp = planGroup(icons, after, block);
    }
    if (grp) moves.push(grp);
    let tt = HOLD0;
    for (const m of moves) { m.t0 = tt; m.done = false; tt += m.kind === 'group' ? GDROP + GADAPT : DRAG + ADAPT; }
    const total = demo ? tt + HOLD1 : 12;

    cv.width = outW; cv.height = outH; cv._scale = rs;
    if (hasLabels && labelsReady()) renderLabels();
    if (hasCache) cacheOK = false;
    if (typeof paused !== 'undefined') paused = false;
    const comp = document.createElement('canvas');             // each frame is composed here first (scene + icons + pointer)
    comp.width = outW; comp.height = outH;
    const ctx = comp.getContext('2d');
    const rec = document.createElement('canvas');               // then drawn here with colors squeezed into 16-235; this is the one recorded
    rec.width = outW; rec.height = outH;
    const rctx = rec.getContext('2d');
    const mp4 = mime.indexOf('mp4') >= 0 && !window.__recFullRange;
    if (mp4) rctx.filter = limitedRangeFilter();
    const stream = rec.captureStream(0), track = stream.getVideoTracks()[0];
    const mr = new MediaRecorder(stream, { mimeType: mime, videoBitsPerSecond: window.__recBitrate || 8e6 });
    // Web demo videos use full range (window.__recFullRange): colors aren't squeezed into 16-235, so they stay vivid instead of washed out; shared clips are still squeezed (for chat app compatibility)
    const chunks = [];
    mr.ondataavailable = e => { if (e.data && e.data.size) chunks.push(e.data); };

    return new Promise((resolve, reject) => {
      const origDraw = draw;
      let t0 = null, lastProg = -1, finished = false, poster = null;
      const finish = err => {
        if (finished) return;
        finished = true;
        draw = origDraw;
        for (const [ic, x, y] of saved) { ic.x = x; ic.y = y; }
        if (temp) icons.splice(icons.indexOf(temp), 1);
        cv.width = old.w; cv.height = old.h; cv._scale = old.s;
        if (hasLabels && labelsReady()) renderLabels();
        if (hasCache) cacheOK = false;
        if (demo) commit();                                  // only the demo choreography needs the icon field restored
        if (typeof paused !== 'undefined') paused = hostPause === null ? wasPaused : hostPause;
        busy = false;
        if (err) { try { mr.stop(); } catch (e) { } reject(err); return; }
        mr.onstop = () => {
          const blob = new Blob(chunks, { type: mime.split(';')[0] });
          (mp4 ? markLimitedRange(blob) : Promise.resolve(blob)).then(b => resolve({ blob: b, mime: mime.split(';')[0], poster }));
        };
        mr.stop();
      };
      // Each time the theme finishes a frame (same task, canvas not yet cleared): copy it, draw the icons and pointer, and hand it to the recorder
      draw = function (now) {
        origDraw(now);
        if (finished) return;
        if (t0 === null) { t0 = now; mr.start(); }
        const t = (now - t0) / 1000;
        ctx.drawImage(cv, 0, 0, outW, outH);
        const disp = new Map();                              // icons whose position or look differs from normal in this frame
        let cursor = null, selRect = null;
        for (const m of moves) {
          const dt = t - m.t0;
          if (m.kind === 'group') {
            if (dt >= GDROP && !m.done) {                    // drop: the batch lands together and the scene starts to adapt
              m.done = true;
              m.group.forEach((ic, i) => { ic.x = m.from[i][0] + m.delta[0]; ic.y = m.from[i][1] + m.delta[1]; });
              commit();
            }
            if (dt < 0 || dt > GDROP + 0.8) continue;
            const [bx0, by0, bx1, by1] = m.box, A = [bx0 - 16, by0 - 16], B = [bx1 + 16, by1 + 16];
            const si = m.group.indexOf(m.seed), grab = [m.from[si][0] + m.seed.w * 0.55, m.from[si][1] + m.seed.h * 0.32];
            let cx, cy, off = [0, 0], lift = 0;
            if (dt < GSEL) {                                 // dragging the selection box
              const p = ease(clamp01((dt - 0.3) / (GSEL - 0.3)));
              cx = A[0] + (B[0] - A[0]) * p; cy = A[1] + (B[1] - A[1]) * p;
              if (dt > 0.3) selRect = [A[0], A[1], cx, cy];
            } else if (dt < 1.5) {                           // pointer moves onto one of the icons
              const p = ease((dt - GSEL) / (1.5 - GSEL));
              cx = B[0] + (grab[0] - B[0]) * p; cy = B[1] + (grab[1] - B[1]) * p;
            } else {                                         // hold and drag the whole batch over
              const p = ease(clamp01((dt - 1.6) / (GDROP - 1.6)));
              off = [m.delta[0] * p, m.delta[1] * p - Math.sin(Math.PI * p) * 30];
              lift = dt < GDROP ? Math.min(1, (dt - 1.5) / 0.12) : 0;
              cx = grab[0] + off[0]; cy = grab[1] + off[1];
            }
            m.group.forEach((ic, i) => {
              const x = dt < GDROP ? m.from[i][0] + off[0] : ic.x, y = dt < GDROP ? m.from[i][1] + off[1] : ic.y;
              let sel;
              if (dt < GSEL) {                               // only icons the selection box touches are selected
                sel = selRect && x < Math.max(selRect[0], selRect[2]) && x + ic.w > Math.min(selRect[0], selRect[2]) &&
                  y < Math.max(selRect[1], selRect[3]) && y + ic.h > Math.min(selRect[1], selRect[3]) ? 1 : 0;
              } else sel = dt < GDROP + 0.2 ? 1 : Math.max(0, 1 - (dt - GDROP - 0.2) / 0.4);
              disp.set(ic, { x, y, lift, sel });
            });
            const alpha = Math.min(1, dt / 0.25) * (dt > GDROP + 0.3 ? Math.max(0, 1 - (dt - GDROP - 0.3) / 0.3) : 1);
            cursor = [cx * rs, cy * rs, alpha];
            continue;
          }
          if (dt >= DRAG && !m.done) { m.done = true; m.ic.x = m.to[0]; m.ic.y = m.to[1]; commit(); }   // drop: the icon lands and the scene starts to adapt
          if (dt < 0 || dt > DRAG + 0.6) continue;
          const p = ease(clamp01((dt - 0.4) / (DRAG - 0.4)));
          const x = m.from[0] + (m.to[0] - m.from[0]) * p, y = m.from[1] + (m.to[1] - m.from[1]) * p - Math.sin(Math.PI * p) * 24;
          const lift = dt < 0.35 ? 0 : dt < DRAG ? Math.min(1, (dt - 0.35) / 0.12) : 0;
          if (dt < DRAG) disp.set(m.ic, { x, y, lift, sel: 0 });
          const alpha = Math.min(1, dt / 0.25) * (dt > DRAG + 0.3 ? Math.max(0, 1 - (dt - DRAG - 0.3) / 0.3) : 1);
          cursor = [(x + m.ic.w * 0.55) * rs, (y + m.ic.h * 0.32) * rs, alpha];
        }
        for (const ic of icons) if (!disp.has(ic)) drawIcon(ctx, ic, rs, ic.x, ic.y, 0);
        for (const [ic, d] of disp) { drawSelected(ctx, ic, d.x, d.y, rs, d.sel * (1 - 0.4 * d.lift)); drawIcon(ctx, ic, rs, d.x, d.y, d.lift); }
        if (selRect) {                                       // selection box: a translucent light blue rectangle
          const [ax, ay, bx, by] = selRect.map(v => v * rs);
          ctx.fillStyle = 'rgba(0, 120, 215, 0.2)'; ctx.fillRect(Math.min(ax, bx), Math.min(ay, by), Math.abs(bx - ax), Math.abs(by - ay));
          ctx.lineWidth = Math.max(1, rs); ctx.strokeStyle = 'rgba(0, 120, 215, 0.9)';
          ctx.strokeRect(Math.min(ax, bx) + 0.5, Math.min(ay, by) + 0.5, Math.abs(bx - ax), Math.abs(by - ay));
        }
        if (cursor) drawCursor(ctx, cursor[0], cursor[1], rs, cursor[2]);
        rctx.drawImage(comp, 0, 0);
        track.requestFrame();
        const prog = Math.min(1, t / total);
        if (onProgress && prog - lastProg >= 0.05) { lastProg = prog; onProgress(prog); }
        if (t >= total) { poster = thumbOf(comp, 520); finish(null); }
      };
      setTimeout(() => finish(new Error('The scene never started animating (the wallpaper may be completely covered)')), (total + 8) * 1000);
    });
  }
  window.desklandRecord = record;
  window.desklandRecord.parts = { planMoves, planGroup };   // for debugging


  // Wallpaper mode: the host sends snapshot -> draw it and send it back ("snapshot|thumbnail|full PNG"; the full image is large, so no JSON);
  // the host sends record -> record a clip, sending "recprogress|0..1" along the way and "record|mime|poster|full video" when done (all data URLs)
  // If the wallpaper is paused (e.g. the settings window is maximized): let it run for a while until the scene settles (trees and houses fully grown), draw, then restore the pause state the host last asked for
  const host = WALL && window.chrome && window.chrome.webview;
  let hostPause = null;
  function settled() {
    return typeof trees === 'undefined' || !trees.length || trees.every(t => t.grow === t.want);
  }
  function send() {
    try {
      const r = window.desklandSnapshot();
      host.postMessage('snapshot|' + r.thumb + '|' + r.canvas.toDataURL('image/png'));
    } catch (e) { host.postMessage('snapshot|error|' + (e && e.message || e)); }
  }
  if (host) host.addEventListener('message', ev => {
    let m; try { m = JSON.parse(ev.data); } catch (e) { return; }
    if (!m) return;
    if (m.t === 'pause') { hostPause = !!m.v; return; }
    if (m.t === 'record') {
      record(p => host.postMessage('recprogress|' + p.toFixed(2))).then(r => {
        const fr = new FileReader();
        fr.onload = () => host.postMessage('record|' + r.mime + '|' + r.poster + '|' + fr.result);
        fr.onerror = () => host.postMessage('record|error|Could not read the recorded video');
        fr.readAsDataURL(r.blob);
      }, e => host.postMessage('record|error|' + (e && e.message || e)));
      return;
    }
    if (m.t !== 'snapshot') return;
    if (busy) { host.postMessage('snapshot|error|A video is being recorded; try again when it finishes'); return; }
    if (typeof paused === 'undefined' || !paused) { send(); return; }
    paused = false;
    const t0 = performance.now(), f0 = frameN;
    const wait = () => {
      const dt = performance.now() - t0, ran = frameN - f0;
      // run at least 10 frames; draw once trees and houses are fully grown; wait at most 6 seconds; don't wait at all if the animation isn't running (window fully covered)
      if ((ran >= 10 && settled()) || dt > 6000 || (dt > 1000 && ran < 2)) {
        send();
        paused = hostPause === null ? true : hostPause;
      } else setTimeout(wait, 100);
    };
    setTimeout(wait, 100);
  });
})();
