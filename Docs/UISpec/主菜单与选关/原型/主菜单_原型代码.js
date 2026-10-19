(() => {
  'use strict';
  const A = window.__MENU2;
  const VW = 1920, VH = 1080, G = 3600;
  const view = document.getElementById('view'), ctx = view.getContext('2d');
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  const mix3 = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const rgb = (c, a = 1) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`;
  const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
  let seed = 7;
  const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
  const rr = (a, b) => a + (b - a) * rnd();
  const HEADLESS = !!window.__menuHeadless;
  const CALM = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const FONT = '"Noto Sans SC", "Noto Sans CJK SC", "Source Han Sans SC", "PingFang SC", "Microsoft YaHei", sans-serif';
  const IMG = {};

  // ---------------- palette ----------------
  const INK = [24, 22, 46];                       // outlines
  const PAPER = [242, 239, 255];                  // selected text
  const PINK = [244, 168, 184], CYAN = [63, 214, 224], WARM = [255, 186, 104];

  // ---------------- pixel text: rendered small, alpha cut to 1 bit, 1px outline ----------------
  const ptCache = new Map();
  function pixText(str, size, weight, fill, outline, spacing = 1) {
    const key = [str, size, weight, fill, outline, spacing].join('|');
    if (ptCache.has(key)) return ptCache.get(key);
    const m = document.createElement('canvas').getContext('2d'); m.font = `${weight} ${size}px ${FONT}`;
    const chars = [...str], ws = chars.map((c) => m.measureText(c).width);
    const w = ws.reduce((a, b) => a + b, 0) + spacing * (chars.length - 1);
    const pad = 2, cw = Math.ceil(w) + pad * 2, ch = Math.ceil(size * 1.25) + pad * 2;
    const c = document.createElement('canvas'); c.width = cw; c.height = ch;
    const g = c.getContext('2d'); g.font = m.font; g.textBaseline = 'top'; g.fillStyle = '#fff';
    let x = pad; chars.forEach((chr, i) => { g.fillText(chr, Math.round(x), pad); x += ws[i] + spacing; });
    const src = g.getImageData(0, 0, cw, ch).data, mask = new Uint8Array(cw * ch);
    for (let i = 0; i < cw * ch; i++) mask[i] = src[i * 4 + 3] > 120 ? 1 : 0;
    const out = g.createImageData(cw, ch), d = out.data;
    for (let y = 0; y < ch; y++) for (let x2 = 0; x2 < cw; x2++) {
      const i = y * cw + x2; let col = null;
      if (mask[i]) col = fill;
      else if (outline) {
        let near = false;
        for (let dy = -1; dy <= 1 && !near; dy++) for (let dx = -1; dx <= 1; dx++) {
          const xx = x2 + dx, yy = y + dy;
          if (xx >= 0 && yy >= 0 && xx < cw && yy < ch && mask[yy * cw + xx]) { near = true; break; }
        }
        if (near) col = outline;
      }
      if (col) { d[i * 4] = col[0]; d[i * 4 + 1] = col[1]; d[i * 4 + 2] = col[2]; d[i * 4 + 3] = 255; }
    }
    g.clearRect(0, 0, cw, ch); g.putImageData(out, 0, 0);
    ptCache.set(key, c); return c;
  }
  function drawPix(img, x, y, s, align = 'center') {  // draws an art-res canvas scaled by s, x is centre (or left)
    ctx.imageSmoothingEnabled = false;
    const w = img.width * s, h = img.height * s;
    ctx.drawImage(img, Math.round(align === 'center' ? x - w / 2 : align === 'right' ? x - w : x), Math.round(y), w, h);
  }
  // pixel sprite from a character map
  function spriteFrom(rows, pal) {
    const c = document.createElement('canvas'); c.width = rows[0].length; c.height = rows.length;
    const g = c.getContext('2d');
    rows.forEach((row, y) => [...row].forEach((ch, x) => { if (pal[ch]) { g.fillStyle = rgb(pal[ch]); g.fillRect(x, y, 1, 1); } }));
    return c;
  }

  // ---------------- worlds ----------------
  const WORLDS = [
    { key: 'maze', name: '迷宫', en: 'THE MAZE', frame: [112, 118, 182], lo: [44, 42, 84], inner: [33, 31, 62], accent: PINK, text: [232, 228, 255], rail: [92, 90, 160], hi: [150, 150, 220], gem: [150, 140, 240] },
    { key: 'mine', name: '矿坑', en: 'THE MINE', frame: [176, 122, 70], lo: [70, 46, 28], inner: [40, 28, 20], accent: [255, 196, 96], text: [252, 236, 214], rail: [128, 88, 52], hi: [214, 160, 96], gem: [240, 166, 74] },
    { key: 'lava', name: '岩浆', en: 'THE FORGE', frame: [214, 92, 60], lo: [90, 26, 24], inner: [44, 14, 16], accent: [255, 196, 96], text: [255, 232, 216], rail: [150, 56, 44], hi: [255, 140, 90], gem: [255, 120, 60] },
    { key: 'sky', name: '天空', en: 'THE SKY', frame: [255, 255, 255], lo: [150, 182, 226], inner: [214, 232, 252], accent: [255, 176, 64], text: [44, 64, 110], rail: [255, 255, 255], hi: [255, 255, 255], gem: [90, 170, 245] },
  ];
  const NAMES = { Maze: ['起点', '回廊', '激光', '之字', '甩尾', '闪光闸', '大回环', '翻天'], Mine: ['落石', '塌方', '矿车', '木板'], Lava: ['封口', '追击', '晃开', '冲门'], Sky: ['跷跷板', '气球雷', '散云', '气球平台'] };
  let num = 0;
  [['Maze', 0], ['Mine', 1], ['Lava', 2], ['Sky', 3]].forEach(([p, wi]) => {
    WORLDS[wi].levels = NAMES[p].map((name, i) => { num++; return { scene: `${p}_0${i + 1}`, num, name, cleared: num <= 5 }; });
  });

  // ---------------- background: flat pixel blobs (metaballs on a 480x270 grid, 4px pixels) ----------------
  const P = 4, BW = VW / P, BH = VH / P, TH = 0.25;
  const fA = new Float32Array(BW * BH), fB = new Float32Array(BW * BH);
  const bcan = document.createElement('canvas'); bcan.width = BW; bcan.height = BH;
  const bctx = bcan.getContext('2d'), bimg = bctx.createImageData(BW, BH), bd = bimg.data;
  function splat(f, cx, cy, r, w) {
    if (r <= 0.5 || w <= 0.001) return;
    const R = r * 1.4142, R2 = R * R, inv = 1 / R2;
    const x0 = Math.max(0, Math.floor(cx - R)), x1 = Math.min(BW - 1, Math.ceil(cx + R));
    const y0 = Math.max(0, Math.floor(cy - R)), y1 = Math.min(BH - 1, Math.ceil(cy + R));
    for (let y = y0; y <= y1; y++) {
      const dy = y + 0.5 - cy, dy2 = dy * dy; if (dy2 >= R2) continue;
      let i = y * BW + x0;
      for (let x = x0; x <= x1; x++, i++) { const dx = x + 0.5 - cx, d2 = dx * dx + dy2; if (d2 < R2) { const q = 1 - d2 * inv; f[i] += w * q * q; } }
    }
  }
  const THEMES = {
    maze: { base: [52, 46, 108], back: [68, 62, 138], front: [100, 94, 190], lamps: true },
    mine: { base: [56, 40, 30], back: [74, 52, 36], front: [112, 80, 50], lamps: true },
    lava: { base: [54, 18, 22], back: [86, 28, 30], front: [222, 92, 44], lamps: false },
    sky: { base: [112, 172, 230], back: [146, 196, 240], front: [246, 250, 255], lamps: false },
  };
  // a scene = slow "masses" hugging the top and bottom edges + "movers" that show the world's gravity
  class BlobScene {
    constructor(kind) {
      this.kind = kind; this.masses = []; this.movers = []; this.spawn = 0.4;
      const s0 = seed; seed = { maze: 11, mine: 23, lava: 37, sky: 51 }[kind];
      const add = (L, x0, y0, r0, k = 1) => this.masses.push({ L, x0, y0, r0, ax: rr(3, 8) * k, ay: rr(2, 6) * k, fx: rr(0.12, 0.3), fy: rr(0.15, 0.35), ph: rr(0, 6.28), ar: rr(1.5, 4) * k, fr: rr(0.2, 0.45) });
      if (kind === 'sky') {
        for (let x = -20; x <= BW + 20; x += 40) add(1, x + rr(-6, 6), BH + rr(4, 16), rr(30, 44));         // cloud bank
        for (let x = 0; x <= BW; x += 56) add(0, x + rr(-8, 8), BH - rr(26, 40), rr(22, 34));              // paler clouds behind
        for (let x = -10; x <= BW + 10; x += 70) add(0, x, rr(-30, -20), rr(26, 34));
      } else if (kind === 'lava') {
        for (let x = -30; x <= BW + 30; x += 42) add(0, x + rr(-8, 8), rr(-24, -12), rr(34, 48));         // dark rock ceiling
        for (let x = -20; x <= BW + 20; x += 36) add(1, x + rr(-6, 6), BH + rr(6, 18), rr(30, 42), 1.4);  // the lava pool
        for (let x = -30; x <= BW + 30; x += 50) add(0, x + rr(-8, 8), BH - rr(30, 40), rr(26, 36));
      } else {
        for (let x = -30; x <= BW + 30; x += 42) add(0, x + rr(-8, 8), rr(-24, -12), rr(34, 48));
        for (let x = -30; x <= BW + 30; x += 46) add(0, x + rr(-8, 8), BH + rr(10, 22), rr(40, 54));
        for (let x = -10; x <= BW + 10; x += 58) add(1, x + rr(-8, 8), rr(-24, -16), rr(30, 38));
        for (let x = -10; x <= BW + 10; x += 52) add(1, x + rr(-8, 8), BH + rr(22, 32), rr(34, 46));
        [[26, 104, 15, 1], [458, 146, 17, 1], [64, 190, 9, 0], [420, 66, 11, 0], [8, 36, 22, 0], [470, 232, 20, 0], [446, 40, 8, 1], [36, 236, 12, 1]]
          .forEach(([x, y, r, L]) => add(L, x, y, r * (kind === 'mine' ? 1.15 : 1)));
        [[46, 126, 10, 1], [14, 150, 12, 1], [440, 168, 11, 1], [468, 120, 9, 1], [96, 70, 7, 1], [392, 210, 8, 1]]
          .forEach(([x, y, r, L]) => add(L, x, y, r, 2.2));
      }
      seed = s0;
    }
    update(dt, t) {
      this.spawn -= dt;
      const sideX = () => (rnd() < 0.5 ? rr(14, 130) : rr(350, 466));
      if (this.spawn <= 0) {
        if (this.kind === 'maze') { this.movers.push({ x: sideX(), y: 19, r: 0, rm: rr(4, 7), vy: 0, st: 'grow', age: 0 }); this.spawn = rr(0.9, 1.7); }
        if (this.kind === 'mine') { this.movers.push({ x: sideX(), y: 14, r: 0, rm: rr(3, 4.5), vy: 0, st: 'grow', age: 0, fast: 1 }); this.spawn = rr(1.6, 2.8); }
        if (this.kind === 'lava') { this.movers.push({ x: sideX(), y: BH - 14, r: 0, rm: rr(5, 10), vy: 0, st: 'grow', age: 0, wob: rr(0, 6) }); this.spawn = rr(0.7, 1.3); }
        if (this.kind === 'sky') { const y = rr(40, 210), r = rr(9, 16); this.movers.push({ x: -40, y, r, rm: r, vx: rr(6, 14), st: 'cloud', age: 0, L: rnd() < 0.5 ? 0 : 1, parts: [[0, 0, 1], [r * 0.9, 2, 0.8], [-r * 0.9, 3, 0.7], [r * 0.3, -r * 0.5, 0.75]] }); this.spawn = rr(3, 6); }
      }
      for (const m of this.movers) {
        m.age += dt;
        if (m.st === 'grow') { m.r = m.rm * clamp(m.age / 0.9, 0, 1); if (m.age > 0.9) m.st = 'go'; if (this.kind === 'lava') m.y = BH - 14 - m.age * 6; }
        else if (m.st === 'go') {
          if (this.kind === 'lava') { m.vy = Math.max(m.vy - 40 * dt, -34); m.y += m.vy * dt; m.x += Math.sin(m.age * 2 + m.wob) * 6 * dt; if (m.y < 70) m.st = 'pop'; }
          else { m.vy = Math.min(m.vy + (m.fast ? 160 : 60) * dt, m.fast ? 120 : 70); m.y += m.vy * dt; if (m.y > BH - 20) m.st = 'pop'; }
        } else if (m.st === 'pop') { m.r -= m.rm * dt / 0.3; if (this.kind !== 'lava') m.y += m.vy * dt * 0.5; }
        else if (m.st === 'cloud') { m.x += m.vx * dt; }
      }
      this.movers = this.movers.filter((m) => !(m.st === 'pop' && m.r <= 0.3) && m.x < BW + 60);
    }
    splat(w, t) {
      for (const b of this.masses) {
        splat(b.L ? fB : fA, b.x0 + Math.sin(t * b.fx + b.ph) * b.ax, b.y0 + Math.sin(t * b.fy + b.ph * 1.3) * b.ay, b.r0 + Math.sin(t * b.fr + b.ph * 0.7) * b.ar, w);
      }
      for (const m of this.movers) {
        if (m.st === 'cloud') { for (const [dx, dy, k] of m.parts) splat(m.L ? fB : fA, m.x + dx, m.y + dy, m.r * k, w); }
        else splat(this.kind === 'lava' || this.kind === 'maze' || this.kind === 'mine' ? fB : fA, m.x, m.y, m.r, w);
      }
    }
  }
  const scenes = { maze: new BlobScene('maze'), mine: new BlobScene('mine'), lava: new BlobScene('lava'), sky: new BlobScene('sky') };
  const bgState = { from: 'maze', to: 'maze', k: 1 };          // crossfade between two scenes (fields blend = shapes morph)
  function bgTo(kind) { if (kind === bgState.to) return; bgState.from = bgState.k < 0.5 ? bgState.from : bgState.to; bgState.to = kind; bgState.k = 0; }
  function renderBlobs(t) {
    fA.fill(0); fB.fill(0);
    const k = ease(clamp(bgState.k, 0, 1));
    if (k < 1) scenes[bgState.from].splat(1 - k, t);
    scenes[bgState.to].splat(k, t);
    const pa = THEMES[bgState.from], pb = THEMES[bgState.to];
    const base = mix3(pa.base, pb.base, k), back = mix3(pa.back, pb.back, k), front = mix3(pa.front, pb.front, k);
    // lamp glows: stepped warm rings, in blob pixels
    const glows = lamps.filter((l) => l.vis() > 0).map((l) => { const p = l.body(); return { x: p.x / P, y: p.y / P, f: l.flick, a: l.vis() }; });
    for (let y = 0, i = 0; y < BH; y++) {
      for (let x = 0; x < BW; x++, i++) {
        let c = fB[i] > TH ? front : fA[i] > TH ? back : base;
        let r = c[0], g = c[1], b = c[2];
        for (const gl of glows) {
          const dx = x + 0.5 - gl.x, dy = y + 0.5 - gl.y, d2 = dx * dx + dy * dy, f = gl.f;
          const s = d2 < 64 * f ? 0.26 : d2 < 196 * f ? 0.15 : d2 < 484 * f ? 0.08 : d2 < 900 * f ? 0.04 : 0;
          if (s) { const q = s * gl.a; r += (WARM[0] - r) * q; g += (WARM[1] - g) * q; b += (WARM[2] - b) * q; }
        }
        const o = i * 4; bd[o] = r; bd[o + 1] = g; bd[o + 2] = b; bd[o + 3] = 255;
      }
    }
    bctx.putImageData(bimg, 0, 0);
    ctx.imageSmoothingEnabled = false; ctx.drawImage(bcan, 0, 0, VW, VH);
  }

  // ---------------- lamps (the maze's hanging lights) ----------------
  const LAMP_PAL = { K: [30, 26, 56], k: [86, 80, 140], g: [255, 226, 168], f: [255, 150, 64], Y: [255, 250, 220], w: [80, 54, 40] };
  const LAMP_ROWS = [
    ['....KKK....', '....K.K....', '...KKKKK...', '..KkkkkkK..', '.KKKKKKKKK.', '.KggggfggK.', '.KgggffggK.', '.KggfYfggK.', '.KggfYYfgK.', '.KgggwgggK.', '.KKKKKKKKK.', '..KkkkkkK..', '...KKKKK...', '.....K.....'],
    ['....KKK....', '....K.K....', '...KKKKK...', '..KkkkkkK..', '.KKKKKKKKK.', '.KgggfgggK.', '.KggfffggK.', '.KggfYfggK.', '.KggfYfggK.', '.KgggwgggK.', '.KKKKKKKKK.', '..KkkkkkK..', '...KKKKK...', '.....K.....'],
    ['....KKK....', '....K.K....', '...KKKKK...', '..KkkkkkK..', '.KKKKKKKKK.', '.KgggggggK.', '.KgggfgggK.', '.KggffYggK.', '.KggfYfggK.', '.KgggwgggK.', '.KKKKKKKKK.', '..KkkkkkK..', '...KKKKK...', '.....K.....'],
  ];
  const LS = 4;                                          // lamp pixel size
  class Lamp {
    constructor(px, Ltitle, Lsel) { Object.assign(this, { px, Ltitle, Lsel, L: -160, Lt: -160, Lv: 0, ang: 0, av: 0, ph: rnd() * 10, flick: 1, fr: 0, wait: 0, next: null }); }
    vis() { return clamp((this.L + 40) / 60, 0, 1); }
    body() { const len = this.L + 7 * LS; return { x: this.px + Math.sin(this.ang) * len, y: -6 + Math.cos(this.ang) * len, a: -this.ang }; }
    update(dt, t) {
      if (this.next !== null) { this.wait -= dt; if (this.wait <= 0) { this.Lt = this.next; this.next = null; this.av += (rnd() - 0.5) * 0.4; } }
      this.Lv += (70 * (this.Lt - this.L) - 9 * this.Lv) * dt; this.L += this.Lv * dt;
      const w2 = 1000 / Math.max(80, this.L + 60);
      this.av += (-w2 * Math.sin(this.ang) - 0.55 * this.av + Math.sin(t * 0.6 + this.ph) * 0.06) * dt; this.ang += this.av * dt;
      const n = Math.sin(t * 13 + this.ph) * Math.sin(t * 7.3 + this.ph * 2);
      this.flick = 1 + 0.08 * n; this.fr = n > 0.35 ? 1 : n < -0.35 ? 2 : 0;
    }
    draw() {
      if (this.vis() <= 0) return;
      const b = this.body(), top = -6;
      // chain: small links along the line
      const len = this.L;
      for (let s = 0, k = 0; s < len; s += 3 * LS, k++) {
        const x = this.px + Math.sin(this.ang) * s, y = top + Math.cos(this.ang) * s;
        ctx.fillStyle = rgb(k % 2 ? LAMP_PAL.k : LAMP_PAL.K);
        ctx.fillRect(Math.round(x / LS) * LS - (k % 2 ? LS : LS / 2), Math.round(y / LS) * LS, k % 2 ? 2 * LS : LS, 3 * LS);
      }
      ctx.save(); ctx.translate(b.x, b.y); ctx.rotate(b.a); ctx.imageSmoothingEnabled = false;
      const sp = IMG['lamp' + this.fr]; ctx.drawImage(sp, -sp.width * LS / 2, -7 * LS, sp.width * LS, sp.height * LS);
      ctx.restore();
    }
    kick(v) { this.av += v; }
  }
  const lamps = [new Lamp(176, 190, 96), new Lamp(330, 330, 150), new Lamp(1622, 270, 120), new Lamp(1772, 150, 74)];
  function lampsTo(mode, delay = 0) {   // 'title' | 'select' | 'gone'
    const order = [0, 3, 1, 2];
    lamps.forEach((l, i) => { const nl = mode === 'title' ? l.Ltitle : mode === 'select' ? l.Lsel : -170; if (Math.abs(nl - (l.next ?? l.Lt)) > 1) { l.next = nl; l.wait = delay + order.indexOf(i) * 0.12; } });
  }

  // ---------------- the emblem: a maze room that turns, the girl falls inside ----------------
  const RS = 4, ROOM = 72, RW = 6;                         // art px
  const GIRL_PAL = { H: [244, 168, 184], h: [206, 122, 152], S: [255, 226, 210], E: [44, 34, 70], W: [242, 240, 255], w: [196, 192, 232], C: [150, 102, 74], c: [108, 72, 52], D: [36, 32, 60] };
  const GIRL = {
    stand: ['...HHH...', '..HHHHH..', '.HHHHHHH.', '.HHSSSHh.', '.HSSESHh.', '.hHSSSHh.', '..CWWWC.h', '.CWWwWWC.', '.CWWWWWC.', '.cCDDDCc.', '..cDDDc..', '...S.S...', '...S.S...', '...S.S...', '...D.D...', '..DD.DD..'],
    fall: ['.S.H.H.S.', '.SHHHHHS.', '.HHHHHHH.', '.HHSSSHH.', '.HSSESSH.', '..HSSSH..', 'C.WWWWW.C', 'CCWWwWWCC', '.CWWWWWC.', '.cCDDDCc.', '..cDDDc..', '..S...S..', '..S...S..', '.S.....S.', '.D.....D.', '.........'],
    land: ['.........', '.........', '...HHH...', '..HHHHH..', '.HHHHHHH.', '.HHSSSHh.', '.HSSESHh.', '.hHSSSHh.', '.CWWWWWCh', 'CCWWwWWCC', '.cCDDDCc.', '..cDDDc..', '..S...S..', '..SS.SS..', '..D...D..', '.DD...DD.'],
  };
  function buildRoom() {
    const c = document.createElement('canvas'); c.width = ROOM; c.height = ROOM; const g = c.getContext('2d');
    const px = (x, y, w, h, col) => { g.fillStyle = rgb(col); g.fillRect(x, y, w, h); };
    const lite = [112, 118, 182], mid = [73, 77, 126], dark = [39, 39, 68];
    px(0, 0, ROOM, ROOM, INK);
    px(1, 1, ROOM - 2, ROOM - 2, lite); px(2, 2, ROOM - 4, ROOM - 4, mid);
    for (let i = 4; i < ROOM - 4; i += 6) { px(i, 1, 1, 1, dark); px(i, ROOM - 2, 1, 1, dark); px(1, i, 1, 1, dark); px(ROOM - 2, i, 1, 1, dark); }
    for (let i = 3; i < ROOM - 4; i += 4) { px(i, 3, 2, 1, dark); px(i, ROOM - 4, 2, 1, dark); px(3, i, 1, 2, dark); px(ROOM - 4, i, 1, 2, dark); }
    px(RW - 1, RW - 1, ROOM - 2 * RW + 2, ROOM - 2 * RW + 2, lite);
    px(RW, RW, ROOM - 2 * RW, ROOM - 2 * RW, dark);
    px(RW, RW, ROOM - 2 * RW, 1, [30, 30, 54]); px(RW, RW, 1, ROOM - 2 * RW, [30, 30, 54]);
    // faint ring ornament in the middle (the maze floor tile)
    const cx = ROOM / 2, cy = ROOM / 2, orn = [52, 54, 96];
    for (let a = 0; a < 16; a++) { const an = a / 16 * Math.PI * 2; px(Math.round(cx + Math.cos(an) * 9) - 1, Math.round(cy + Math.sin(an) * 9) - 1, 2, 2, orn); }
    px(cx - 2, cy - 2, 4, 4, orn); px(cx - 1, cy - 1, 2, 2, [70, 74, 124]);
    for (const [x, y] of [[2, 2], [ROOM - 4, 2], [2, ROOM - 4], [ROOM - 4, ROOM - 4]]) px(x, y, 2, 2, [170, 176, 230]);
    return c;
  }
  const room = {
    f: null, ang: 0, from: 0, to: 0, phase: 'hold', timer: 2.6, door: 1, doorT: 1,
    girl: { x: -14, y: 30 - 8, vx: 0, vy: 0, frame: 'stand', landT: 0, onG: true },
  };
  const GR = 8, GL = 400;                                   // girl radius (art px), gravity in art px/s^2
  function roomUpdate(dt) {
    room.timer -= dt;
    if (room.phase === 'hold' && room.timer <= 0 && room.f.mode === 'rest') {
      room.phase = 'turn'; room.timer = 1.0; room.from = room.ang; room.to = room.ang + Math.PI / 2;
      lamps.forEach((l, i) => l.kick(i % 2 ? 0.22 : -0.22));
    } else if (room.phase === 'turn') {
      room.ang = lerp(room.from, room.to, ease(clamp(1 - room.timer / 1.0, 0, 1)));
      if (room.timer <= 0) { room.ang = room.to; room.phase = 'hold'; room.timer = 2.4; }
    }
    // the door opens when gravity points into it (the door sits on the room's local floor)
    const total = room.ang + room.f.a, down = Math.cos(total);
    room.door = down > 0.94 ? 1 : 0;
    room.doorT = clamp(room.doorT + (room.door ? dt : -dt) / 0.45, 0, 1);
    // girl, simulated in the room's frame; gravity turns as the room turns
    const gm = room.girl, gx = GL * Math.sin(total), gy = GL * Math.cos(total), lim = ROOM / 2 - RW - GR;
    const sub = 4;
    for (let s = 0; s < sub; s++) {
      const h = dt / sub;
      gm.vx += gx * h; gm.vy += gy * h; gm.x += gm.vx * h; gm.y += gm.vy * h;
      let hit = 0, impact = 0;
      if (gm.x < -lim) { impact = Math.max(impact, -gm.vx); gm.x = -lim; gm.vx = gm.vx < 0 ? -gm.vx * 0.12 : gm.vx; hit |= 1; }
      if (gm.x > lim) { impact = Math.max(impact, gm.vx); gm.x = lim; gm.vx = gm.vx > 0 ? -gm.vx * 0.12 : gm.vx; hit |= 1; }
      if (gm.y < -lim) { impact = Math.max(impact, -gm.vy); gm.y = -lim; gm.vy = gm.vy < 0 ? -gm.vy * 0.12 : gm.vy; hit |= 2; }
      if (gm.y > lim) { impact = Math.max(impact, gm.vy); gm.y = lim; gm.vy = gm.vy > 0 ? -gm.vy * 0.12 : gm.vy; hit |= 2; }
      if (hit & 1) gm.vy *= 1 - Math.min(1, 9 * h);            // friction along the wall
      if (hit & 2) gm.vx *= 1 - Math.min(1, 9 * h);
      if (impact > 90) { gm.landT = 0.18; }
      gm.onG = hit !== 0 && Math.hypot(gm.vx, gm.vy) < 20;
    }
    gm.landT = Math.max(0, gm.landT - dt);
    gm.frame = gm.landT > 0 ? 'land' : gm.onG ? 'stand' : 'fall';
  }
  function roomDraw() {
    const f = room.f; if (!f.visible) return;
    const total = room.ang + f.a, s = ROOM * RS;
    ctx.save(); ctx.translate(f.x, f.y); ctx.rotate(total); ctx.imageSmoothingEnabled = false;
    // flat drop shadow then the room
    ctx.fillStyle = 'rgba(16,14,34,0.35)'; ctx.fillRect(-s / 2 + 10, -s / 2 + 14, s, s);
    ctx.drawImage(IMG.room, -s / 2, -s / 2, s, s);
    // door on the local floor (right half)
    const open = room.doorT, fi = open > 0 ? Math.min(3, Math.floor(open * 3.999)) : -1;
    const dx = -s / 2 + 34 * RS, dy = -s / 2 + (ROOM - RW - 32) * RS;
    if (fi >= 0) ctx.drawImage(IMG.doorOpen, fi * 32, 0, 32, 32, dx, dy, 32 * RS, 32 * RS);
    else { const k = Math.floor(S.t * 8) % 6; ctx.drawImage(IMG.door, k * 32, 0, 32, 32, dx, dy, 32 * RS, 32 * RS); }
    if (open > 0.5) {   // light spilling from the open door, flat steps
      ctx.fillStyle = rgb(CYAN, 0.10 * open); ctx.fillRect(dx - 6 * RS, dy - 10 * RS, 44 * RS, 42 * RS);
      ctx.fillStyle = rgb(CYAN, 0.10 * open); ctx.fillRect(dx, dy - 4 * RS, 32 * RS, 36 * RS);
    }
    ctx.restore();
  }

  // ---------------- falling bodies ----------------
  class Faller {
    constructor(x, y) { Object.assign(this, { tx: x, ty: y, x, y, vy: 0, vx: 0, a: 0, av: 0, mode: 'hidden', delay: 0, bounces: 0 }); }
    dropIn(height, delay) { this.hopping = false; this.mode = 'in'; this.x = this.tx; this.y = this.ty - height; this.vy = 0; this.vx = 0; this.a = (rnd() - 0.5) * 0.25; this.av = 0; this.delay = delay; this.bounces = 0; }
    hop(v, delay) { if (this.mode !== 'rest') return; this.mode = 'in'; this.vy = -v; this.delay = delay; this.bounces = 0; this.hopping = true; }
    fallAway(delay) { this.mode = 'out'; this.vy = -(120 + rnd() * 260); this.vx = (rnd() - 0.5) * 220; this.av = (rnd() - 0.5) * 1.6; this.delay = delay; }
    update(dt) {
      if (this.mode === 'hidden' || this.mode === 'gone') return;
      if (this.delay > 0) { this.delay -= dt; return; }
      if (this.mode === 'in') {
        this.vy += G * dt; this.y += this.vy * dt; this.a *= Math.exp(-6 * dt);
        if (this.y >= this.ty) {
          this.y = this.ty;
          if (this.vy > 260 && this.bounces < 3) { this.vy = -this.vy * 0.28; this.bounces++; if (this.onLand && this.bounces === 1 && !this.hopping) this.onLand(); }
          else { this.vy = 0; this.mode = 'rest'; this.a = 0; this.hopping = false; }
        }
      } else if (this.mode === 'out') {
        this.vy += G * dt; this.x += this.vx * dt; this.y += this.vy * dt; this.a += this.av * dt;
        if (this.y > VH + 700) this.mode = 'gone';
      }
    }
    get visible() { return this.mode !== 'gone' && this.mode !== 'hidden' && !(this.mode === 'in' && this.delay > 0 && this.y < this.ty - 1); }
  }
  // rope-hung card: one-sided stiff spring + pendulum
  class Dangle {
    constructor(px, py, rope, h) { Object.assign(this, { px, py, rope, h, o: 0, vo: 0, ang: 0, av: 0, pull: 0, mode: 'hang', fx: 0, fy: 0, fvx: 0, fvy: 0, fang: 0, fav: 0, caught: true, delay: 0 }); }
    drop(height, delay) { this.mode = 'drop'; this.o = -height; this.vo = 0; this.delay = delay; this.caught = false; this.ang = (rnd() - 0.5) * 0.3; this.av = 0; }
    release(vx, vy, av) { const p = this.center(); this.mode = 'fall'; this.fx = p.x; this.fy = p.y; this.fang = this.ang; this.fvx = vx; this.fvy = vy; this.fav = av; }
    center() { const L = this.rope + this.o + this.h / 2; return { x: this.px + Math.sin(-this.ang) * L, y: this.py + Math.cos(this.ang) * L, a: this.ang }; }
    update(dt) {
      if (this.mode === 'gone') return;
      if (this.mode === 'fall') { this.fvy += G * dt; this.fx += this.fvx * dt; this.fy += this.fvy * dt; this.fang += this.fav * dt; if (this.fy > VH + 500) this.mode = 'gone'; return; }
      if (this.delay > 0) { this.delay -= dt; return; }
      let acc = G;
      if (this.o > this.pull) { acc += -1500 * (this.o - this.pull) - 26 * this.vo; if (!this.caught) { this.caught = true; this.av += (rnd() - 0.5) * 1.5; if (this.onCatch) this.onCatch(); } }
      this.vo += acc * dt; if (this.mode === 'drop' && !this.caught) this.vo = Math.min(this.vo, 1700);
      this.o += this.vo * dt;
      if (this.caught && Math.abs(this.o - this.pull) < 0.5 && Math.abs(this.vo) < 8) this.mode = 'hang';
      const Lp = this.rope + this.h / 2; this.av += (-(G / Lp) * Math.sin(this.ang) - 3.0 * this.av) * dt; this.ang += this.av * dt;
    }
    pos() { return this.mode === 'fall' || this.mode === 'gone' ? { x: this.fx, y: this.fy, a: this.fang } : this.center(); }
  }

  // ---------------- state ----------------
  const S = { railA: 0, screen: 'title', t: 0, menu: 0, world: 0, sel: 0, mouse: [0.5, 0.5], busy: 0, fade: 0, fadeText: '', toast: null, shake: 0, hopT: 6 };
  const MENU = ['开始游戏', '选择关卡', '设置', '退出'];
  const title = { pieces: [], items: [] };
  let cards = [], sign = null, oldCards = [], oldSign = null;
  const parts = [];
  const LOGO_S = 3, LOGO_Y = 474, MENU_Y = 700, MENU_GAP = 66, ROOM_Y = 236;
  function buildTitle() {
    const [lw] = A.logoSize, x0 = VW / 2 - (lw * LOGO_S) / 2;
    title.pieces = A.logoPieces.map((p, i) => Object.assign(new Faller(x0 + p.x * LOGO_S, LOGO_Y + p.y * LOGO_S), { img: IMG['lp' + i] }));
    title.items = MENU.map((s, i) => Object.assign(new Faller(VW / 2, MENU_Y + i * MENU_GAP), { label: s }));
    room.f = new Faller(VW / 2, ROOM_Y);
  }
  function titleIn(first) {
    const b = first ? 0.2 : 0.1;
    room.f.dropIn(700, b); room.f.onLand = () => { S.shake = Math.max(S.shake, 3); lamps.forEach((l) => l.kick((rnd() - 0.5) * 0.3)); };
    title.pieces.forEach((p, i) => { p.dropIn(900 + rnd() * 200, b + 0.35 + i * 0.06); p.onLand = () => { S.shake = Math.max(S.shake, 1.5); }; });
    title.items.forEach((it, i) => it.dropIn(520, b + 0.85 + i * 0.07));
    room.phase = 'hold'; room.timer = 2.2; S.hopT = 6;
    lampsTo('title', first ? 1.0 : 0.3);
  }
  function titleAway() {
    [...title.pieces, ...title.items].forEach((f) => f.fallAway(rnd() * 0.12));
    room.f.fallAway(0.05);
  }
  const CARD_W = 288, CARD_H = 264, ROPE = 40;
  function railsOf(wi) { return WORLDS[wi].levels.length > 4 ? [300, 650] : [430]; }
  function layoutCards(wi) {
    const W = WORLDS[wi], rails = railsOf(wi), cols = 4, gap = 72, x0 = (VW - (cols * CARD_W + (cols - 1) * gap)) / 2;
    return W.levels.map((lv, i) => {
      const r = Math.floor(i / cols), c = i % cols;
      return Object.assign(new Dangle(x0 + c * (CARD_W + gap) + CARD_W / 2, rails[r], ROPE, CARD_H), { lv, i, row: r, col: c, world: wi, canvas: null, canvasSel: null });
    });
  }
  function cardsDropIn(list, base) {
    const rows = Math.max(...list.map((d) => d.row)) + 1;
    list.forEach((d) => { d.drop(d.py + ROPE + 300 + rnd() * 120, base + (rows - 1 - d.row) * 0.2 + d.col * 0.06); d.onCatch = () => { S.shake = Math.max(S.shake, 1.2); lamps.forEach((l) => l.kick((rnd() - 0.5) * 0.05)); }; });
  }
  function makeSign(wi) { const s = new Dangle(VW / 2, -20, 64, 102); s.world = wi; s.flip = 1; return s; }
  const worldBg = (wi) => WORLDS[wi].key;
  const worldLamps = (wi) => (THEMES[WORLDS[wi].key].lamps ? 'select' : 'gone');

  // ---------------- card / sign art (flat pixel) ----------------
  const PX = 3;
  function pxRect(g, x, y, w, h, c) { g.fillStyle = rgb(c); g.fillRect(x * PX, y * PX, w * PX, h * PX); }
  function cardCanvas(d, selected) {
    const key = selected ? 'canvasSel' : 'canvas'; if (d[key]) return d[key];
    const W = WORLDS[d.world], c = document.createElement('canvas'); c.width = CARD_W; c.height = CARD_H;
    const g = c.getContext('2d'); g.imageSmoothingEnabled = false;
    const fr = selected ? W.accent : W.frame, sky = W.key === 'sky';
    pxRect(g, 0, 0, 96, 88, sky ? [120, 150, 200] : INK);
    pxRect(g, 1, 1, 94, 86, fr);
    pxRect(g, 3, 3, 90, 82, W.inner);
    pxRect(g, 5, 5, 86, 58, sky ? [150, 176, 214] : W.lo);
    pxRect(g, 6, 6, 84, 56, sky ? [196, 222, 246] : W.lo.map((v) => v * 0.8));
    const ti = A.thumbIndex[d.lv.scene];
    g.drawImage(IMG.thumbs, ti[0], ti[1], ti[2], ti[3], 6 * PX, 6 * PX, ti[2] * PX, ti[3] * PX);
    pxRect(g, 45, 0, 6, 2, fr); pxRect(g, 46, 1, 4, 1, sky ? [120, 150, 200] : INK);   // hook
    const numImg = pixText(String(d.lv.num).padStart(2, '0'), 15, 700, selected ? (sky ? [176, 110, 20] : W.accent) : W.text, null, 1);
    g.drawImage(numImg, 5 * PX, 64 * PX, numImg.width * PX, numImg.height * PX);
    const nameImg = pixText(d.lv.name, 12, 500, W.text, null, 1);
    g.drawImage(nameImg, (5 + numImg.width + 1) * PX, 67 * PX, nameImg.width * PX, nameImg.height * PX);
    if (d.lv.cleared) {
      const ck = sky ? [40, 160, 170] : CYAN, cx = 80, cy = 71;
      [[0, 2], [1, 3], [2, 4], [3, 3], [4, 2], [5, 1], [6, 0]].forEach(([dx, dy]) => pxRect(g, cx + dx, cy + dy, 1, 2, ck));
    }
    d[key] = c; return c;
  }
  function signCanvas(wi) {
    const key = 'sign' + wi; if (IMG[key]) return IMG[key];
    const W = WORLDS[wi], c = document.createElement('canvas'); c.width = 480; c.height = 102;
    const g = c.getContext('2d'); g.imageSmoothingEnabled = false; const sky = W.key === 'sky';
    pxRect(g, 0, 0, 160, 34, sky ? [120, 150, 200] : INK); pxRect(g, 1, 1, 158, 32, W.frame); pxRect(g, 3, 3, 154, 28, W.inner);
    const t1 = pixText(W.name, 16, 700, W.text, null, 3), t2 = pixText(W.en, 8, 700, sky ? [90, 120, 170] : W.hi, null, 2);
    g.drawImage(t1, Math.round(80 - t1.width / 2) * PX, 4 * PX, t1.width * PX, t1.height * PX);
    g.drawImage(t2, Math.round(80 - t2.width / 2) * PX, 23 * PX, t2.width * PX, t2.height * PX);
    IMG[key] = c; return c;
  }
  const DIAMOND = ['..A..', '.AaA.', 'AaaaA', '.AaA.', '..A..'];
  function diamond(col) { const k = 'dia' + col; if (!IMG[k]) IMG[k] = spriteFrom(DIAMOND, { A: col, a: mix3(col, [255, 255, 255], 0.45) }); return IMG[k]; }
  const GEM = ['....O....', '...OAO...', '..OAaAO..', '.OAaaaAO.', 'OAaaaaaAO', '.OAaaaAO.', '..OAaAO..', '...OAO...', '....O....'];
  function gem(col, ring) { const k = 'gem' + col + ring; if (!IMG[k]) IMG[k] = spriteFrom(GEM, { O: ring, A: col, a: mix3(col, [255, 255, 255], 0.4) }); return IMG[k]; }

  // ---------------- particles (few: lamp sparks, motes, embers) ----------------
  function stepParticles(dt) {
    const k = worldBg(S.world), sel = S.screen === 'select';
    if (rnd() < dt * 2.2) for (const l of lamps) if (l.vis() > 0.5 && rnd() < 0.4) { const b = l.body(); parts.push({ x: b.x + rr(-8, 8), y: b.y - 10, vx: rr(-6, 6), vy: rr(-30, -14), life: 0, max: rr(1.2, 2.2), c: WARM, s: 4 }); }
    if (rnd() < dt * 3 && (!sel || k === 'maze')) parts.push({ x: rr(0, VW), y: rr(0, VH), vx: rr(-4, 4), vy: rr(-10, -3), life: 0, max: rr(3, 5), c: [170, 166, 236], s: 4 });
    if (sel && k === 'mine' && rnd() < dt * 4) parts.push({ x: rr(0, VW), y: rr(0, VH), vx: rr(-8, 8), vy: rr(-3, 3), life: 0, max: rr(3, 5), c: [220, 180, 130], s: 4 });
    if (sel && k === 'lava' && rnd() < dt * 7) parts.push({ x: rr(0, VW), y: VH - rr(0, 120), vx: rr(-10, 10), vy: rr(-90, -40), life: 0, max: rr(2, 4), c: rnd() < 0.5 ? [255, 190, 90] : [255, 120, 50], s: 4 });
    for (const p of parts) { p.life += dt; p.x += p.vx * dt; p.y += p.vy * dt; }
    for (let i = parts.length - 1; i >= 0; i--) if (parts[i].life > parts[i].max) parts.splice(i, 1);
  }
  function drawParticles() {
    for (const p of parts) {
      const a = clamp(Math.min(p.life / 0.4, (p.max - p.life) / 0.6), 0, 1);
      ctx.fillStyle = rgb(p.c, 0.8 * a); ctx.fillRect(Math.round(p.x / 4) * 4, Math.round(p.y / 4) * 4, p.s, p.s);
    }
  }

  // ---------------- drawing: title UI ----------------
  function drawLogo() {
    for (const p of title.pieces) {
      if (!p.visible) continue;
      const w = p.img.width * LOGO_S, h = p.img.height * LOGO_S;
      ctx.save(); ctx.translate(p.x + w / 2, p.y + h / 2); ctx.rotate(p.a); ctx.imageSmoothingEnabled = false;
      ctx.drawImage(IMG['lpS' + title.pieces.indexOf(p)], -w / 2 - LOGO_S, -h / 2 - LOGO_S + 3 * LOGO_S, w + 2 * LOGO_S, h + 2 * LOGO_S);   // flat shadow
      ctx.drawImage(IMG['lpO' + title.pieces.indexOf(p)], -w / 2 - LOGO_S, -h / 2 - LOGO_S, w + 2 * LOGO_S, h + 2 * LOGO_S);              // outline + fill
      ctx.restore();
    }
  }
  function drawMenu() {
    title.items.forEach((it, i) => {
      if (!it.visible) return;
      const sel = i === S.menu && S.screen === 'title';
      const img = pixText(it.label, 13, 700, sel ? PAPER : [176, 172, 226], INK, 2);
      ctx.save(); ctx.translate(it.x, it.y); ctx.rotate(it.a);
      drawPix(img, 0, -img.height * 3 / 2, 3);
      if (sel) {
        const dm = diamond(PINK), w = img.width * 3, b = Math.abs(Math.sin(S.t * 5.2)), sq = b < 0.15 ? 1 : 0;
        for (const sx of [-1, 1]) {
          const x = sx * (w / 2 + 30), y = -6 - b * 18;
          ctx.drawImage(dm, Math.round(x - 10 - sq * 2), Math.round(y - 10 + sq * 4), 20 + sq * 4, 20 - sq * 4);
        }
      }
      ctx.restore();
    });
  }
  function drawHints() {
    const sky = S.screen === 'select' && WORLDS[S.world].key === 'sky';
    const t = S.screen === 'title' ? '↑↓ 选择    Enter 确定' : '←→↑↓ 选择    Q E 换世界    Enter 进入    Esc 返回';
    const img = pixText(t, 12, 500, sky ? [60, 84, 130] : [170, 166, 220], sky ? null : INK, 1);
    drawPix(img, VW / 2, VH - 62, 2);
    if (S.screen === 'title') { const v = pixText('v0.1', 11, 500, [140, 136, 196], INK, 1); drawPix(v, 28, VH - 54, 2, 'left'); }
  }

  // ---------------- drawing: level select ----------------
  function drawRails(wi, alpha) {
    const W = WORLDS[wi]; ctx.globalAlpha = alpha; const sky = W.key === 'sky';
    for (const y of railsOf(wi)) {
      const x0 = 210, x1 = VW - 210;
      ctx.fillStyle = sky ? 'rgba(60,90,150,0.25)' : 'rgba(10,8,20,0.35)'; ctx.fillRect(x0 + 8, y + 9, x1 - x0, 9);
      ctx.fillStyle = rgb(sky ? [120, 150, 200] : INK); ctx.fillRect(x0 - 3, y - 9, x1 - x0 + 6, 18);
      ctx.fillStyle = rgb(W.rail); ctx.fillRect(x0, y - 6, x1 - x0, 12);
      ctx.fillStyle = rgb(W.hi); ctx.fillRect(x0, y - 6, x1 - x0, 3);
      for (const xx of [x0 - 15, x1 + 3]) { ctx.fillStyle = rgb(sky ? [120, 150, 200] : INK); ctx.fillRect(xx, y - 18, 12, 36); ctx.fillStyle = rgb(W.rail); ctx.fillRect(xx + 3, y - 15, 6, 30); }
    }
    ctx.globalAlpha = 1;
  }
  function drawCard(d, isSel) {
    if (d.mode === 'gone' || (d.delay > 0 && d.mode === 'drop')) return;
    const W = WORLDS[d.world], p = d.pos(), sky = W.key === 'sky';
    const hx = p.x + Math.sin(p.a) * d.h / 2, hy = p.y - Math.cos(p.a) * d.h / 2;
    if (d.mode !== 'fall') {
      if (hy > d.py + 4) { ctx.strokeStyle = rgb(isSel ? W.accent : sky ? [120, 150, 200] : W.rail); ctx.lineWidth = 6; ctx.beginPath(); ctx.moveTo(d.px, d.py); ctx.lineTo(hx, hy); ctx.stroke(); }
      ctx.fillStyle = rgb(sky ? [120, 150, 200] : INK); ctx.fillRect(d.px - 9, d.py - 9, 18, 18); ctx.fillStyle = rgb(W.hi); ctx.fillRect(d.px - 6, d.py - 6, 12, 12);
    } else if (d.snapT !== undefined && S.t - d.snapT < 0.4) {
      const k = 1 - (S.t - d.snapT) / 0.4; ctx.strokeStyle = rgb(W.rail); ctx.lineWidth = 6; ctx.beginPath(); ctx.moveTo(d.px, d.py); ctx.lineTo(d.px + 10 * k, d.py + 30 * k); ctx.stroke();
    }
    const c = cardCanvas(d, isSel);
    ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.a); ctx.imageSmoothingEnabled = Math.abs(p.a) > 0.004;
    ctx.fillStyle = sky ? 'rgba(60,90,150,0.22)' : 'rgba(10,8,20,0.32)'; ctx.fillRect(-CARD_W / 2 + 9, -CARD_H / 2 + 15, CARD_W, CARD_H);   // flat shadow
    if (isSel) { ctx.fillStyle = rgb(W.accent, 0.16); ctx.fillRect(-CARD_W / 2 - 15, -CARD_H / 2 - 15, CARD_W + 30, CARD_H + 30); ctx.fillStyle = rgb(W.accent, 0.16); ctx.fillRect(-CARD_W / 2 - 7, -CARD_H / 2 - 7, CARD_W + 14, CARD_H + 14); }
    ctx.drawImage(c, -CARD_W / 2, -CARD_H / 2);
    ctx.restore();
    if (isSel && d.mode !== 'fall') {
      const b = Math.abs(Math.sin(S.t * 5.2)), dm = diamond(W.key === 'sky' ? [255, 176, 64] : W.accent);
      ctx.imageSmoothingEnabled = false; ctx.drawImage(dm, Math.round(d.px - 10), Math.round(d.py - 40 - b * 12), 20, 20);
    }
  }
  function drawSign(s) {
    if (!s || s.mode === 'gone' || (s.mode === 'drop' && s.delay > 0)) return;
    const W = WORLDS[s.world], p = s.pos(), sky = W.key === 'sky';
    if (s.mode !== 'fall') for (const ox of [-170, 170]) {
      const cx = p.x + ox, cy = p.y - 51, topX = s.px + ox * 0.9, n = 7;
      for (let k = 0; k < n; k++) {
        const t = (k + 0.5) / n, x = lerp(topX, cx, t), y = lerp(-10, cy, t);
        ctx.fillStyle = rgb(sky ? [120, 150, 200] : INK); ctx.fillRect(Math.round(x / 3) * 3 - 6, Math.round(y / 3) * 3 - 7, 12, 14);
        ctx.fillStyle = rgb(W.frame); ctx.fillRect(Math.round(x / 3) * 3 - 3, Math.round(y / 3) * 3 - 4, 6, 8);
      }
    }
    const c = signCanvas(s.world);
    ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.a); ctx.scale(1, s.flip); ctx.imageSmoothingEnabled = Math.abs(p.a) > 0.004 || s.flip !== 1;
    ctx.fillStyle = sky ? 'rgba(60,90,150,0.22)' : 'rgba(10,8,20,0.32)'; ctx.fillRect(-240 + 9, -51 + 14, 480, 102);
    ctx.drawImage(c, -240, -51);
    const dimg = IMG['door_' + W.key], fr = Math.floor(S.t * 7) % 6; ctx.imageSmoothingEnabled = false;
    if (dimg) for (const x of [-240 + 21, 240 - 21 - 64]) ctx.drawImage(dimg, fr * 32, 0, 32, 32, x, -34, 64, 64);
    ctx.restore();
  }
  function drawWorldGems(alpha) {
    ctx.globalAlpha = alpha; const y = 214, sky = WORLDS[S.world].key === 'sky';
    WORLDS.forEach((W, i) => {
      const x = VW / 2 + (i - 1.5) * 56, cur = i === S.world, s = cur ? 45 : 27;
      const dm = gem(W.gem, cur ? (sky ? [255, 255, 255] : PAPER) : (sky ? [120, 150, 200] : INK));
      ctx.imageSmoothingEnabled = false;
      ctx.globalAlpha = alpha * (cur ? 1 : 0.6); ctx.drawImage(dm, Math.round(x - s / 2), Math.round(y - s / 2), s, s); ctx.globalAlpha = alpha;
    });
    const col = sky ? [60, 84, 130] : [200, 196, 240];
    drawPix(pixText('Q ◀', 12, 700, col, sky ? null : INK, 2), VW / 2 - 170, y - 20, 3);
    drawPix(pixText('▶ E', 12, 700, col, sky ? null : INK, 2), VW / 2 + 170, y - 20, 3);
    ctx.globalAlpha = 1;
  }

  // ---------------- transitions ----------------
  function setSel(i, silent) {
    i = clamp(i, 0, cards.length - 1); cards.forEach((d) => { d.pull = 0; });
    if (!silent && cards[S.sel] && i !== S.sel) cards[S.sel].av -= 0.9 * (Math.sign(i - S.sel) || 1);
    S.sel = i; if (cards[i]) { cards[i].pull = 18; if (!silent) cards[i].av += (rnd() - 0.5) * 1.6; }
  }
  function toSelect() {
    if (S.busy > 0) return;
    S.screen = 'select'; S.busy = 1.1; titleAway();
    bgTo(worldBg(S.world)); lampsTo(worldLamps(S.world));
    cards = layoutCards(S.world); cardsDropIn(cards, 0.45);
    sign = makeSign(S.world); sign.drop(380, 0.3); sign.onCatch = () => { S.shake = Math.max(S.shake, 2); };
    setSel(clamp(S.sel, 0, cards.length - 1), true);
  }
  function toTitle() {
    if (S.busy > 0) return;
    S.screen = 'title'; S.busy = 1.2;
    cards.forEach((d) => d.release((rnd() - 0.5) * 240, -(80 + rnd() * 200), (rnd() - 0.5) * 2));
    if (sign) sign.release(0, -150, (rnd() - 0.5) * 0.8);
    oldCards = cards; oldSign = sign; cards = []; sign = null;
    bgTo('maze'); titleIn(false);
  }
  function switchWorld(dir) {
    if (S.busy > 0 || S.screen !== 'select') return;
    const nw = (S.world + dir + WORLDS.length) % WORLDS.length; S.busy = 0.75;
    cards.forEach((d) => d.release((rnd() - 0.5) * 300 + dir * -120, -(60 + rnd() * 160), (rnd() - 0.5) * 2.4));
    oldCards = cards; S.world = nw; S.flipT = 0; S.flipTo = nw;
    if (sign) sign.av += 1.4 * dir;
    bgTo(worldBg(nw)); lampsTo(worldLamps(nw));
    cards = layoutCards(nw); cardsDropIn(cards, 0.28);
    setSel(dir > 0 ? 0 : Math.min(cards.length - 1, 3), true);
  }
  function enterLevel() {
    if (S.busy > 0 || !cards[S.sel]) return;
    const d = cards[S.sel]; S.busy = 2.2;
    d.release(0, 120, (rnd() - 0.5) * 3); d.snapT = S.t; S.shake = 4;
    cards.forEach((o) => { if (o !== d) o.av += (rnd() - 0.5) * 2.5; });
    S.fadeTo = { t: 0, text: `进入 ${d.lv.scene}  ${d.lv.name}`, after: 'select', delay: 0.45 };
  }
  function startGame() { if (S.busy > 0) return; S.busy = 2.4; titleAway(); S.fadeTo = { t: 0, text: '进入第一关  Maze_01', after: 'title', delay: 0.5 }; }
  function toast(text) { S.toast = { text, t: 0 }; }
  function confirm() {
    if (S.screen === 'title') { if (S.menu === 0) startGame(); else if (S.menu === 1) toSelect(); else if (S.menu === 2) toast('设置界面这次不改'); else toast('原型里不退出'); }
    else enterLevel();
  }
  function back() { if (S.screen === 'select') toTitle(); }
  function move(dx, dy) {
    if (S.screen === 'title') { if (dy) S.menu = (S.menu + dy + MENU.length) % MENU.length; return; }
    if (!cards.length) return;
    if (dx) { const ni = S.sel + dx; if (ni < 0) return switchWorld(-1); if (ni >= cards.length) return switchWorld(1); setSel(ni); }
    if (dy) { const ni = S.sel + dy * 4; if (ni >= 0 && ni < cards.length) setSel(ni); }
  }

  // ---------------- main loop ----------------
  function step(dt) { const n = Math.max(1, Math.ceil(dt / 0.004 - 1e-9)); for (let i = 0; i < n; i++) stepOnce(dt / n); }
  function stepOnce(dt) {
    S.t += dt; if (S.busy > 0) S.busy -= dt;
    for (const f of [...title.pieces, ...title.items, room.f]) f.update(dt);
    roomUpdate(dt);
    for (const l of lamps) l.update(dt, S.t);
    for (const d of cards) d.update(dt); for (const d of oldCards) d.update(dt);
    if (sign) sign.update(dt); if (oldSign) oldSign.update(dt);
    oldCards = oldCards.filter((d) => d.mode !== 'gone'); if (oldSign && oldSign.mode === 'gone') oldSign = null;
    if (bgState.k < 1) bgState.k = Math.min(1, bgState.k + dt / 0.7);
    S.selAge = S.screen === 'select' ? (S.selAge || 0) + dt : 0;          // rails and gems wait for the title to fall out of the way
    S.railA = S.screen === 'select' ? clamp((S.selAge - 0.35) / 0.35, 0, 1) : Math.max(0, S.railA - dt * 5);
    if (S.flipT !== undefined && S.flipT < 1 && sign) { S.flipT = Math.min(1, S.flipT + dt / 0.45); sign.flip = Math.cos(S.flipT * Math.PI); if (S.flipT >= 0.5) { sign.world = S.flipTo; sign.flip = Math.abs(sign.flip); } }
    for (const d of cards) if (d.mode === 'hang' || d.mode === 'drop') d.av += Math.sin(S.t * 0.9 + d.i * 1.7) * 0.3 * dt;
    // every few seconds the title letters hop in a ripple
    if (S.screen === 'title' && title.pieces.every((p) => p.mode === 'rest')) { S.hopT -= dt; if (S.hopT <= 0) { title.pieces.forEach((p, i) => p.hop(330, i * 0.055)); S.hopT = 7; } }
    if (S.fadeTo) {
      const f = S.fadeTo;
      if (f.delay > 0) f.delay -= dt;
      else {
        f.t += dt; S.fade = clamp(f.t / 0.35, 0, 1); S.fadeText = f.text;
        if (f.t > 1.5) {
          S.fadeTo = null; S.fadeOut = 0.4;
          if (f.after === 'select') { cards = layoutCards(S.world); cardsDropIn(cards, 0.15); setSel(S.sel, true); S.busy = 0.6; }
          else { titleIn(false); S.busy = 1.2; }
        }
      }
    } else if (S.fadeOut > 0) { S.fadeOut -= dt; S.fade = clamp(S.fadeOut / 0.4, 0, 1); }
    if (S.toast) { S.toast.t += dt; if (S.toast.t > 1.6) S.toast = null; }
    S.shake *= Math.exp(-10 * dt);
    for (const k in scenes) if (k === bgState.to || (k === bgState.from && bgState.k < 1)) scenes[k].update(dt, S.t);
    stepParticles(dt);
  }
  function render() {
    ctx.setTransform(view.width / VW, 0, 0, view.height / VH, 0, 0);
    const sh = S.shake > 0.3 && !CALM ? [Math.round((rnd() - 0.5) * S.shake * 2), Math.round((rnd() - 0.5) * S.shake * 2)] : [0, 0];
    ctx.save(); ctx.translate(sh[0], sh[1]);
    renderBlobs(S.t);
    drawParticles();
    for (const l of lamps) l.draw();
    if (S.screen === 'select' || oldCards.length || oldSign) {
      drawRails(S.world, S.railA);
      for (const d of oldCards) drawCard(d, false);
      cards.forEach((d, i) => drawCard(d, i === S.sel));
      drawSign(oldSign); drawSign(sign);
      if (S.railA > 0) drawWorldGems(S.railA);
    }
    roomDraw(); drawLogo(); drawMenu(); drawHints();
    if (S.toast) {
      const a = clamp(Math.min(S.toast.t / 0.15, (1.6 - S.toast.t) / 0.3), 0, 1); ctx.globalAlpha = a;
      const img = pixText(S.toast.text, 13, 700, PINK, INK, 2);
      ctx.fillStyle = rgb(INK, 0.75); ctx.fillRect(VW / 2 - img.width * 1.5 - 30, VH - 170, img.width * 3 + 60, img.height * 3 + 24);
      drawPix(img, VW / 2, VH - 158, 3); ctx.globalAlpha = 1;
    }
    if (S.fade > 0) {
      ctx.fillStyle = rgb([16, 14, 32], S.fade); ctx.fillRect(-10, -10, VW + 20, VH + 20);
      ctx.globalAlpha = S.fade;
      drawPix(pixText(S.fadeText, 14, 700, PAPER, null, 2), VW / 2, VH / 2 - 40, 3);
      drawPix(pixText('（原型：这里切进关卡场景）', 12, 500, [150, 146, 200], null, 1), VW / 2, VH / 2 + 30, 2);
      ctx.globalAlpha = 1;
    }
    ctx.restore();
  }

  // ---------------- input ----------------
  const KEYS = { ArrowUp: [0, -1], KeyW: [0, -1], ArrowDown: [0, 1], KeyS: [0, 1], ArrowLeft: [-1, 0], KeyA: [-1, 0], ArrowRight: [1, 0], KeyD: [1, 0] };
  window.addEventListener('keydown', (e) => {
    if (KEYS[e.code]) { e.preventDefault(); move(...KEYS[e.code]); }
    else if (e.code === 'Enter' || e.code === 'Space' || e.code === 'KeyZ') { e.preventDefault(); confirm(); }
    else if (e.code === 'Escape' || e.code === 'Backspace' || e.code === 'KeyX') { e.preventDefault(); back(); }
    else if (e.code === 'KeyQ' || e.code === 'PageUp') switchWorld(-1);
    else if (e.code === 'KeyE' || e.code === 'PageDown') switchWorld(1);
  });
  const toVirtual = (e) => { const r = view.getBoundingClientRect(); return [(e.clientX - r.left) / r.width * VW, (e.clientY - r.top) / r.height * VH]; };
  const menuHit = (x, y) => title.items.findIndex((it) => Math.abs(y - it.ty) < 32 && Math.abs(x - VW / 2) < 220);
  view.addEventListener('pointermove', (e) => {
    const [x, y] = toVirtual(e); S.mouse = [x / VW, y / VH]; if (S.busy > 0) return;
    if (S.screen === 'title') { const i = menuHit(x, y); if (i >= 0) S.menu = i; }
    else cards.forEach((d, i) => { const p = d.pos(); if (Math.abs(x - p.x) < CARD_W / 2 && Math.abs(y - p.y) < CARD_H / 2 && i !== S.sel) setSel(i); });
  });
  view.addEventListener('pointerdown', (e) => {
    view.focus({ preventScroll: true });
    const [x, y] = toVirtual(e);
    if (S.screen === 'title') { const i = menuHit(x, y); if (i >= 0) { S.menu = i; confirm(); } return; }
    if (Math.abs(y - 214) < 30 && Math.abs(x - (VW / 2 - 170)) < 60) return switchWorld(-1);
    if (Math.abs(y - 214) < 30 && Math.abs(x - (VW / 2 + 170)) < 60) return switchWorld(1);
    let hit = -1; cards.forEach((d, i) => { const p = d.pos(); if (Math.abs(x - p.x) < CARD_W / 2 && Math.abs(y - p.y) < CARD_H / 2) hit = i; });
    if (hit >= 0) { setSel(hit, true); enterLevel(); } else if (y > VH - 100) back();
  });
  let padPrev = {};
  function pollPad() {
    let pads = []; try { pads = navigator.getGamepads ? navigator.getGamepads() : []; } catch (e) { return; }
    const p = [...pads].find((q) => q); if (!p) return;
    const ax = p.axes[0] || 0, ay = p.axes[1] || 0, b = (i) => !!(p.buttons[i] && p.buttons[i].pressed);
    const now = { up: b(12) || ay < -0.6, down: b(13) || ay > 0.6, left: b(14) || ax < -0.6, right: b(15) || ax > 0.6, a: b(0), b: b(1), lb: b(4), rb: b(5) };
    const edge = (k) => now[k] && !padPrev[k];
    if (edge('up')) move(0, -1); if (edge('down')) move(0, 1); if (edge('left')) move(-1, 0); if (edge('right')) move(1, 0);
    if (edge('a')) confirm(); if (edge('b')) back(); if (edge('lb')) switchWorld(-1); if (edge('rb')) switchWorld(1);
    padPrev = now;
  }

  // ---------------- boot ----------------
  function resize() { const r = view.getBoundingClientRect(), dpr = Math.min(window.devicePixelRatio || 1, 2); view.width = Math.max(320, Math.round(r.width * dpr)); view.height = Math.max(180, Math.round(r.height * dpr)); }
  let last = 0;
  function frame(ts) { requestAnimationFrame(frame); const now = ts / 1000, dt = Math.min(0.05, last ? now - last : 0.016); last = now; if (!HEADLESS) { pollPad(); step(dt); } render(); }
  const load = (src) => new Promise((res, rej) => { const im = new Image(); im.onload = () => res(im); im.onerror = rej; im.src = src; });
  const GLYPHS = [...new Set((MENU.join('') + WORLDS.map((W) => W.name + W.en + W.levels.map((l) => l.name).join('')).join('') +
    '0123456789QE◀▶←→↑↓ 选择确定换世界进入返回第一关原型这里切进关卡场景设置界面这次不改不退出（）：_MazeMinLvSkyv.').split(''))].join('');
  function tintLogo(i, img) {    // outline (1 art px, ink) + fill, and a flat shadow copy
    const w = img.width + 2, h = img.height + 2;
    const mk = (col, fillCol) => {
      const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d');
      const t = document.createElement('canvas'); t.width = img.width; t.height = img.height; const tg = t.getContext('2d');
      tg.drawImage(img, 0, 0); tg.globalCompositeOperation = 'source-in'; tg.fillStyle = rgb(col); tg.fillRect(0, 0, t.width, t.height);
      for (const [dx, dy] of [[0, 0], [1, 0], [2, 0], [0, 1], [2, 1], [0, 2], [1, 2], [2, 2]]) g.drawImage(t, dx, dy);
      if (fillCol) { const f = document.createElement('canvas'); f.width = img.width; f.height = img.height; const fg = f.getContext('2d'); fg.drawImage(img, 0, 0); fg.globalCompositeOperation = 'source-in'; fg.fillStyle = rgb(fillCol); fg.fillRect(0, 0, f.width, f.height); g.drawImage(f, 1, 1); }
      return c;
    };
    IMG['lpO' + i] = mk(INK, PAPER); IMG['lpS' + i] = mk([30, 28, 64]);
  }
  Promise.all([
    load(A.door).then((im) => { IMG.door = im; }), ...['maze', 'mine', 'lava', 'sky'].map((w) => load(A['door_' + w]).then((im) => { IMG['door_' + w] = im; })), load(A.doorOpen).then((im) => { IMG.doorOpen = im; }), load(A.thumbs).then((im) => { IMG.thumbs = im; }),
    ...A.logoPieces.map((p, i) => load(p.src).then((im) => { IMG['lp' + i] = im; })),
    ...(document.fonts ? ['700', '500'].map((w) => document.fonts.load(`${w} 13px "Noto Sans SC"`, GLYPHS).catch(() => null)) : []),
  ]).then(() => {
    A.logoPieces.forEach((p, i) => tintLogo(i, IMG['lp' + i]));
    LAMP_ROWS.forEach((rows, i) => { IMG['lamp' + i] = spriteFrom(rows, LAMP_PAL); });
    for (const k in GIRL) IMG['girl_' + k] = spriteFrom(GIRL[k], GIRL_PAL);
    IMG.room = buildRoom();
    resize(); window.addEventListener('resize', resize);
    buildTitle(); titleIn(true);
    window.__menuReady = true;
    requestAnimationFrame(frame);
  });
  window.__menu = { S, step, render, move, confirm, back, switchWorld, toSelect, toTitle, enterLevel, get cards() { return cards; }, room, lamps, scenes, WORLDS };
})();
