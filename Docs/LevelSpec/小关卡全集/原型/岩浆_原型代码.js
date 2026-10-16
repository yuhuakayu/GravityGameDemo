(() => {
  'use strict';
  const A = window.__ASSETS;
  const DEG = Math.PI / 180;
  // Player rules ported from the game's PlayerController / WorldRotator (fixed step 0.02, no own horizontal speed)
  const G = 9.81, PR = 0.4, PC = [0.4, 1.13];
  const DT = 0.02, SUB = 4, SKIN = 0.02, PIVOT_LAG = 8, VMAX = 16;
  const P = { rotSpeed: 180, lavaDrag: 0.25, sloshHold: 0.3, slowmo: true, react: 0.12, intro: 1.2 };
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
  const mk = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };

  // ---------------- the four small levels (tiles, y down) ----------------
  // One lava trick each. The door opens only while it stands upright on screen (within 20°).
  const LEVELS = [
    {
      id: '2-1', name: '封口', w: 21, h: 16,
      tip: '一转，池底的岩浆就顺着墙爬向出口。抢在它封口前钻出去。',
      carve: [[2, 2, 11, 12], [11, 2, 20, 5], [18, 5, 20, 15], [4, 13, 18, 15]],
      fill: [[4, 4, 7, 5]],
      start: { feet: [5.5, 4.0] },
      door: { bx: 4, by: 14, dir: -90 },
      pools: [[2, 9.3, 11, 12, 0, false]],
      vent: { x: 2.9, y: 2, dx: 0, dy: 1, rate: 22, max: 150 },
      plan: [
        { t: 90, cap: '顺时针转 90°：往右冲', sub: '岩浆顺着墙爬向出口，抢在它封口前钻出去', next: () => lp()[0] > LV.carve[1][2] - 1 && S.grounded },
        { t: 0, cap: '落地马上逆时针转回来', sub: '往下掉；岩浆灌进管子追过来了', react: 0.4, next: () => lp()[1] > 14 && S.grounded },
        { t: -90, cap: '继续逆时针：冲进门', sub: '岩浆从头顶浇下来', react: 0.4, next: () => false },
      ],
    },
    {
      id: '2-2', name: '追击', w: 20, h: 20,
      tip: '你一走，身后那一池冷却的岩浆就活了，像浪一样追着你。落地别停。',
      carve: [[1, 1, 7, 5], [7, 3, 19, 5], [17, 5, 19, 14], [6, 12, 17, 14], [6, 14, 8, 19]],
      fill: [],
      start: { feet: [6.5, 5.0] },
      door: { bx: 7, by: 19, dir: 0 },
      pools: [[1, 1.3, 5.2, 5, 0, true]],
      wakes: [{ g: 0, test: (l) => l[0] > 8 }],
      dragMul: 0.9,
      plan: [
        { t: 90, cap: '顺时针转 90°：往右冲', sub: '身后的岩浆活了，像浪一样追过来', next: () => lp()[0] > 18 && S.grounded },
        { t: 0, cap: '逆时针：往下', sub: '浪拐弯倒下来了', react: 0.38, next: () => lp()[1] > 13 && S.grounded },
        { t: -90, cap: '落地马上继续逆时针：往左', sub: '浪从头顶浇下来', react: 0.42, next: () => lp()[0] < 7 && S.grounded },
        { t: 0, cap: '顺时针转正，掉进门', sub: '浪追进来了', react: 0.42, next: () => false },
      ],
    },
    {
      id: '2-3', name: '晃开', w: 25, h: 13,
      tip: '出口那边会被岩浆封住。先斜 45° 把岩浆晃到另一头，再猛打方向冲过去。',
      carve: [[8, 1, 24, 11], [1, 1, 8, 4], [1, 4, 4, 12]],
      fill: [[16, 3, 19, 4]],
      start: { feet: [17.5, 3.0] },
      door: { bx: 2, by: 12, dir: 0 },
      pools: [[8, 8.0, 24, 11, 0, false]],
      plan: [
        { t: 45, cap: '顺时针 45°：把岩浆晃到右下角', sub: '台子会粘住你，岩浆流开，左边的出口露出来', hold: true, next: () => S.tiltT > P.sloshHold },
        { t: -90, cap: '逆时针猛打到 −90°：冲向左边出口', sub: '岩浆马上倒流回来，抢在它前面', next: () => lp()[0] < 2 && S.grounded },
        { t: 0, cap: '顺时针转回来：掉进门', sub: '岩浆追进管子了', react: 0.3, next: () => false },
      ],
    },
    {
      id: '2-4', name: '冲门', w: 19, h: 19,
      tip: '你一动，旁边那池冷却的岩浆就活了，跟着你灌进竖井。落到底马上转，门一转正就冲进去。',
      carve: [[2, 2, 14, 5], [2, 5, 5, 14], [2, 14, 17, 18]],
      fill: [],
      start: { feet: [5.6, 5.0] },
      door: { bx: 17, by: 17, dir: 90 },
      pools: [[10.4, 2.15, 14, 5, 0, true]],
      wakes: [{ g: 0, test: (l) => l[0] < 4.2 }],
      plan: [
        { t: -90, cap: '逆时针转 90°：往左', sub: '旁边那池冷却的岩浆裂开了', next: () => lp()[0] < 3 && S.grounded },
        { t: 0, cap: '顺时针转回来：往下掉', sub: '岩浆跟着灌进竖井', react: 0.4, next: () => lp()[1] > 17 && S.grounded },
        { t: 90, cap: '落地马上顺时针：冲进门', sub: '门一转正就冲进去', react: 0.45, next: () => false },
      ],
    },
  ];

  // ---------------- current level (rebuilt by setupLevel) ----------------
  let LI = 0, LV = LEVELS[0], MW = 1, MH = 1, PLAN = LV.plan;
  let rock = new Uint8Array(1), DOOR = null, VENT = null, COOL = [], POOLS = [], WAKES = [];
  const TS = 16;
  const solidAt = (cx, cy) => cx < 0 || cy < 0 || cx >= MW || cy >= MH || rock[cy * MW + cx] === 1;
  const inCool = (x, y) => { for (const r of COOL) if (x >= r[0] && x < r[2] && y >= r[1] && y < r[3]) return true; return false; };

  // ---------------- lava: particle fluid in level space (double density relaxation) ----------------
  const LQ = { SP: 0.3, H: 0.62, K: 150, KN: 250, COH: 0.3, DAMP: 0.3, SIG: 0.3, BETA: 0.05, FRIC: 0.1, RP: 0.12, VCAP: 13 };
  const MAXP = 1500;
  const L = { n: 0, made: 0, vk: 0, ventAcc: 0, vented: 0, x: new Float32Array(MAXP), y: new Float32Array(MAXP), vx: new Float32Array(MAXP), vy: new Float32Array(MAXP), c: new Uint8Array(MAXP), g: new Uint8Array(MAXP), fz: new Uint8Array(MAXP) };
  const LOX = new Float32Array(MAXP), LOY = new Float32Array(MAXP), LRHO = new Float32Array(MAXP), LISO = new Float32Array(MAXP);
  const LH = LQ.H;
  let GW = 1, GH = 1, GN = 1, cst = new Int32Array(2), cfl = new Int32Array(2);
  const cof = new Int32Array(MAXP), srt = new Int32Array(MAXP);
  const gcx = (x) => clamp(Math.floor(x / LH) + 1, 0, GW - 1), gcy = (y) => clamp(Math.floor(y / LH) + 1, 0, GH - 1);
  function lavaGrid() {
    cst.fill(0);
    for (let i = 0; i < L.n; i++) { const c = gcy(L.y[i]) * GW + gcx(L.x[i]); cof[i] = c; cst[c + 1]++; }
    for (let c = 0; c < GN; c++) cst[c + 1] += cst[c];
    cfl.set(cst);
    for (let i = 0; i < L.n; i++) srt[cfl[cof[i]]++] = i;
  }
  const RHO0 = (() => {
    let r = 0;
    for (let j = -4; j <= 4; j++) for (let i = -4; i <= 4; i++) {
      const x = i * LQ.SP + (j % 2 ? LQ.SP / 2 : 0), y = j * LQ.SP * 0.866, d = Math.hypot(x, y);
      if (d === 0 || d >= LH) continue; const q = 1 - d / LH; r += q * q;
    }
    return r;
  })();
  function addLava(x, y, vx, vy, g = 0) {
    if (L.n >= MAXP) return false;
    const i = L.n++;
    L.x[i] = x; L.y[i] = y; L.vx[i] = vx; L.vy[i] = vy; L.c[i] = hash(L.made * 0.731, 3.3) > 0.7 ? 1 : 0; L.g[i] = g; L.fz[i] = 0; L.made++;
    return true;
  }
  function fillLava(x0, y0, x1, y1, g = 0) {
    let row = 0;
    for (let y = y1 - LQ.SP / 2; y > y0; y -= LQ.SP * 0.866, row++)
      for (let x = x0 + LQ.SP / 2 + (row % 2) * LQ.SP / 2; x < x1; x += LQ.SP)
        if (!solidAt(Math.floor(x), Math.floor(y))) addLava(x, y, 0, 0, g);
  }
  function lavaCollide(i) {
    const r = LQ.RP;
    let x = L.x[i], y = L.y[i];
    for (let it = 0; it < 3; it++) {
      let hit = false;
      const x0 = Math.floor(x - r), x1 = Math.floor(x + r), y0 = Math.floor(y - r), y1 = Math.floor(y + r);
      for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) {
        if (!solidAt(cx, cy)) continue;
        const qx = clamp(x, cx, cx + 1), qy = clamp(y, cy, cy + 1), dx = x - qx, dy = y - qy, d2 = dx * dx + dy * dy;
        if (d2 >= r * r) continue;
        let nx, ny, pen;
        if (d2 > 1e-10) { const d = Math.sqrt(d2); nx = dx / d; ny = dy / d; pen = r - d; }
        else {
          const opts = [[-1, 0, x - cx], [1, 0, cx + 1 - x], [0, -1, y - cy], [0, 1, cy + 1 - y]].sort((a, b) => a[2] - b[2]);
          const o = opts.find(q => !solidAt(cx + q[0], cy + q[1])) || opts[0];
          nx = o[0]; ny = o[1]; pen = o[2] + r;
        }
        x += nx * pen; y += ny * pen; hit = true;
        const mx = x - LOX[i], my = y - LOY[i], mn = mx * nx + my * ny;   // wall friction: viscous lava drags along walls
        x -= (mx - mn * nx) * LQ.FRIC; y -= (my - mn * ny) * LQ.FRIC;
      }
      if (!hit) break;
    }
    if (x < 0.2) x = 0.2; if (y < 0.2) y = 0.2; if (x > MW - 0.2) x = MW - 0.2; if (y > MH - 0.2) y = MH - 0.2;
    L.x[i] = x; L.y[i] = y;
  }
  function lavaStep(dt, gx, gy) {
    const n = L.n, X = L.x, Y = L.y, VX = L.vx, VY = L.vy, FZ = L.fz, H = LH, H2 = H * H;
    const damp = 1 / (1 + LQ.DAMP * dt), c2 = P.lavaDrag * (LV.dragMul || 1);
    for (let i = 0; i < n; i++) {
      if (FZ[i]) { VX[i] = 0; VY[i] = 0; continue; }
      let vx = (VX[i] + gx * dt) * damp, vy = (VY[i] + gy * dt) * damp;
      const f = 1 / (1 + c2 * Math.hypot(vx, vy) * dt);
      VX[i] = vx * f; VY[i] = vy * f;
    }
    lavaGrid();
    for (let i = 0; i < n; i++) {       // viscosity: approaching neighbours share momentum
      const xi = X[i], yi = Y[i], cx = cof[i] % GW, cy = (cof[i] / GW) | 0;
      for (let oy = -1; oy <= 1; oy++) for (let ox = -1; ox <= 1; ox++) {
        const c = (cy + oy) * GW + cx + ox; if (c < 0 || c >= GN) continue;
        for (let k = cst[c], ke = cst[c + 1]; k < ke; k++) {
          const j = srt[k]; if (j <= i || (FZ[i] && FZ[j])) continue;
          const dx = X[j] - xi, dy = Y[j] - yi, r2 = dx * dx + dy * dy; if (r2 >= H2 || r2 < 1e-12) continue;
          const r = Math.sqrt(r2), q = r / H, ux = dx / r, uy = dy / r, u = (VX[i] - VX[j]) * ux + (VY[i] - VY[j]) * uy;
          if (u > 0) {
            const I = dt * (1 - q) * (LQ.SIG * u + LQ.BETA * u * u) * 0.5;
            if (!FZ[i]) { VX[i] -= I * ux; VY[i] -= I * uy; }
            if (!FZ[j]) { VX[j] += I * ux; VY[j] += I * uy; }
          }
        }
      }
    }
    for (let i = 0; i < n; i++) { LOX[i] = X[i]; LOY[i] = Y[i]; if (!FZ[i]) { X[i] += VX[i] * dt; Y[i] += VY[i] * dt; } }
    lavaGrid();
    const dt2 = dt * dt;
    for (let i = 0; i < n; i++) {       // double density relaxation
      if (FZ[i]) { LRHO[i] = RHO0; continue; }
      const cx = cof[i] % GW, cy = (cof[i] / GW) | 0;
      let rho = 0, rhon = 0;
      for (let oy = -1; oy <= 1; oy++) for (let ox = -1; ox <= 1; ox++) {
        const c = (cy + oy) * GW + cx + ox; if (c < 0 || c >= GN) continue;
        for (let k = cst[c], ke = cst[c + 1]; k < ke; k++) {
          const j = srt[k]; if (j === i) continue;
          const dx = X[j] - X[i], dy = Y[j] - Y[i], r2 = dx * dx + dy * dy; if (r2 >= H2) continue;
          const q = 1 - Math.sqrt(r2) / H; rho += q * q; rhon += q * q * q;
        }
      }
      LRHO[i] = rho;
      let Pp = LQ.K * (rho - RHO0); if (Pp < 0) Pp *= LQ.COH;
      const Pn = LQ.KN * rhon;
      let ddx = 0, ddy = 0;
      for (let oy = -1; oy <= 1; oy++) for (let ox = -1; ox <= 1; ox++) {
        const c = (cy + oy) * GW + cx + ox; if (c < 0 || c >= GN) continue;
        for (let k = cst[c], ke = cst[c + 1]; k < ke; k++) {
          const j = srt[k]; if (j === i) continue;
          const dx = X[j] - X[i], dy = Y[j] - Y[i], r2 = dx * dx + dy * dy; if (r2 >= H2 || r2 < 1e-12) continue;
          const r = Math.sqrt(r2), q = 1 - r / H, D = dt2 * (Pp * q + Pn * q * q) * 0.5, ux = dx / r, uy = dy / r;
          if (FZ[j]) { ddx -= 2 * D * ux; ddy -= 2 * D * uy; }
          else { X[j] += D * ux; Y[j] += D * uy; ddx -= D * ux; ddy -= D * uy; }
        }
      }
      X[i] += ddx; Y[i] += ddy;
    }
    for (let i = 0; i < n; i++) {
      if (FZ[i]) continue;
      lavaCollide(i);
      let vx = (X[i] - LOX[i]) / dt, vy = (Y[i] - LOY[i]) / dt;
      const sp = Math.hypot(vx, vy); if (sp > LQ.VCAP) { vx *= LQ.VCAP / sp; vy *= LQ.VCAP / sp; }
      VX[i] = vx; VY[i] = vy;
    }
    // lone droplets cool into nothing (keeps stray splashes from lingering on ledges)
    for (let i = L.n - 1; i >= 0; i--) {
      LISO[i] = FZ[i] ? 0 : inCool(X[i], Y[i]) ? LISO[i] + dt * 4 : LRHO[i] < 0.3 ? LISO[i] + dt : 0;
      if (LISO[i] > 0.6) {
        if (L.onCool) L.onCool(X[i], Y[i]);
        const j = --L.n;
        X[i] = X[j]; Y[i] = Y[j]; VX[i] = VX[j]; VY[i] = VY[j]; L.c[i] = L.c[j]; L.g[i] = L.g[j]; FZ[i] = FZ[j]; LISO[i] = LISO[j]; LRHO[i] = LRHO[j]; LOX[i] = LOX[j]; LOY[i] = LOY[j];
      }
    }
    L.ver = (L.ver || 0) + 1;
  }
  // how many lava particles are within r of a point (uses the grid from the last step)
  function lavaCount(x, y, r, live) {
    const r2 = r * r, x0 = gcx(x - r), x1 = gcx(x + r), y0 = gcy(y - r), y1 = gcy(y + r);
    let n = 0;
    for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) {
      const c = cy * GW + cx;
      for (let k = cst[c], ke = cst[c + 1]; k < ke; k++) { const j = srt[k]; if (live && L.fz[j]) continue; const dx = L.x[j] - x, dy = L.y[j] - y; if (dx * dx + dy * dy < r2) n++; }
    }
    return n;
  }
  function lavaMinDist(x, y, r) {
    const x0 = gcx(x - r), x1 = gcx(x + r), y0 = gcy(y - r), y1 = gcy(y + r);
    let best = r * r;
    for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) {
      const c = cy * GW + cx;
      for (let k = cst[c], ke = cst[c + 1]; k < ke; k++) { const j = srt[k]; if (L.fz[j]) continue; const dx = L.x[j] - x, dy = L.y[j] - y, d2 = dx * dx + dy * dy; if (d2 < best) best = d2; }
    }
    return Math.sqrt(best);
  }
  function lavaIn(x0, y0, x1, y1) { let n = 0; for (let i = 0; i < L.n; i++) if (L.x[i] >= x0 && L.x[i] < x1 && L.y[i] >= y0 && L.y[i] < y1) n++; return n; }
  function stepVent(dt) {
    if (!VENT || VENT.rate <= 0) return;
    L.ventAcc += VENT.rate * dt;
    while (L.ventAcc >= 1) {
      L.ventAcc -= 1;
      const k = L.vk++ % 3, x = VENT.x + (k - 1) * 0.24, y = VENT.y + 0.18;
      if (L.vented < VENT.max && lavaCount(x, y, 0.2) === 0 && addLava(x, y, VENT.dx * 2.6, VENT.dy * 2.6)) L.vented++;
    }
  }
  function lavaSave() { return { n: L.n, made: L.made, vk: L.vk, ventAcc: L.ventAcc, vented: L.vented, x: L.x.slice(0, L.n), y: L.y.slice(0, L.n), vx: L.vx.slice(0, L.n), vy: L.vy.slice(0, L.n), c: L.c.slice(0, L.n), g: L.g.slice(0, L.n), fz: L.fz.slice(0, L.n), iso: LISO.slice(0, L.n) }; }
  function lavaLoad(s) {
    L.n = s.n; L.made = s.made; L.vk = s.vk; L.ventAcc = s.ventAcc; L.vented = s.vented;
    L.x.set(s.x); L.y.set(s.y); L.vx.set(s.vx); L.vy.set(s.vy); L.c.set(s.c); L.g.set(s.g); L.fz.set(s.fz); LISO.set(s.iso);
    lavaGrid(); L.ver = (L.ver || 0) + 1;
  }
  let LAVA0 = null;
  function wakeGroup(g) {
    let k = 0;
    for (let i = 0; i < L.n; i++) if (L.g[i] === g && L.fz[i]) { L.fz[i] = 0; k++; }
    return k;
  }
  function lavaInit() {
    L.n = 0; L.made = 0; L.vk = 0; L.ventAcc = 0; L.vented = 0;
    for (const p of POOLS) fillLava(p[0], p[1], p[2], p[3], p[4]);
    // asleep pools are cooled crust from the start: they keep their shape until something wakes them
    for (let i = 0; i < L.n; i++) L.fz[i] = POOLS.some(p => p[5] && p[4] === L.g[i]) ? 1 : 0;
    for (let k = 0; k < 90; k++) lavaStep(DT, 0, G);     // let the live pools settle before the run starts
    for (let i = 0; i < L.n; i++) { L.vx[i] = 0; L.vy[i] = 0; LISO[i] = 0; }
    LAVA0 = lavaSave();
  }

  function setupLevel(i) {
    LI = i; LV = LEVELS[i]; MW = LV.w; MH = LV.h; PLAN = LV.plan;
    rock = new Uint8Array(MW * MH).fill(1);
    for (const [x0, y0, x1, y1] of LV.carve) for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) rock[y * MW + x] = 0;
    for (const [x0, y0, x1, y1] of LV.fill || []) for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) rock[y * MW + x] = 1;
    GW = Math.ceil(MW / LH) + 2; GH = Math.ceil(MH / LH) + 2; GN = GW * GH;
    cst = new Int32Array(GN + 1); cfl = new Int32Array(GN + 1);
    const rot = -LV.door.dir * DEG;
    DOOR = { bx: LV.door.bx, by: LV.door.by, rot, ux: Math.sin(rot), uy: -Math.cos(rot), rx: Math.cos(rot), ry: Math.sin(rot) };
    VENT = LV.vent ? { ...LV.vent } : null;
    COOL = LV.cool || [];
    POOLS = LV.pools || [];
    WAKES = LV.wakes || [];
    lavaInit();
  }

  // ---------------- state ----------------
  const S = {
    mode: 'idle', theta: 0, tx: 0, ty: 0, px: 0, py: 0, vx: 0, vy: 0, grounded: true, airTime: 0, stick: 0, u: 0, uVis: 0, hist: [],
    state: 'play', timer: 0, time: 0, runStart: 0, runTime: 0, step: 0, stepAt: 0, deaths: 0, woke: 0, tiltT: 0,
    anim: 'idle', animT: 0, flash: 0, flashColor: '', shake: 0, slow: 0, near: {}, sizzle: 0,
    fx: [], pops: [], needleA: 0, needleV: 0, unlocked: false, doorF: 0, hint: 0, cam: [0, 0], done: false, dbg: {},
  };
  const prev = { theta: 0, tx: 0, ty: 0, px: 0, py: 0, cam: [0, 0] };
  function snapshot() { prev.theta = S.theta; prev.tx = S.tx; prev.ty = S.ty; prev.px = S.px; prev.py = S.py; prev.cam = [S.cam[0], S.cam[1]]; }

  // ---------------- transforms ----------------
  function toLevel(wx, wy) { const c = Math.cos(S.theta), s = Math.sin(S.theta), dx = wx - S.tx, dy = wy - S.ty; return [dx * c + dy * s, -dx * s + dy * c]; }
  function toWorld(lx, ly) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [S.tx + lx * c - ly * s, S.ty + lx * s + ly * c]; }
  function vW(x, y) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [x * c - y * s, x * s + y * c]; }
  const lp = () => toLevel(S.px, S.py - PC[0]);
  function doorTilt() { const u = vW(DOOR.ux, DOOR.uy); return Math.acos(clamp(-u[1], -1, 1)) / DEG; }

  // ---------------- collision (level space, tiles) ----------------
  function resolveCircleL(lx, ly, r, normals) {
    for (let it = 0; it < 4; it++) {
      let hit = false;
      const x0 = Math.floor(lx - r), x1 = Math.floor(lx + r), y0 = Math.floor(ly - r), y1 = Math.floor(ly + r);
      for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) {
        if (!solidAt(cx, cy)) continue;
        const qx = clamp(lx, cx, cx + 1), qy = clamp(ly, cy, cy + 1);
        const dx = lx - qx, dy = ly - qy, d2 = dx * dx + dy * dy;
        if (d2 >= r * r) continue;
        let nx, ny, pen;
        if (d2 > 1e-9) { const d = Math.sqrt(d2); nx = dx / d; ny = dy / d; pen = r - d; }
        else {
          const opts = [[-1, 0, lx - cx], [1, 0, cx + 1 - lx], [0, -1, ly - cy], [0, 1, cy + 1 - ly]].sort((a, b) => a[2] - b[2]);
          const o = opts.find(q => !solidAt(cx + q[0], cy + q[1])) || opts[0];
          nx = o[0]; ny = o[1]; pen = o[2] + r;
        }
        lx += nx * pen; ly += ny * pen; hit = true;
        if (normals) normals.push([nx, ny]);
      }
      if (!hit) break;
    }
    return [lx, ly];
  }
  function touchingL(lx, ly, r, normals) {
    const x0 = Math.floor(lx - r), x1 = Math.floor(lx + r), y0 = Math.floor(ly - r), y1 = Math.floor(ly + r);
    for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) {
      if (!solidAt(cx, cy)) continue;
      const qx = clamp(lx, cx, cx + 1), qy = clamp(ly, cy, cy + 1);
      const dx = lx - qx, dy = ly - qy, d2 = dx * dx + dy * dy;
      if (d2 >= r * r || d2 < 1e-9) continue;
      const d = Math.sqrt(d2); normals.push([dx / d, dy / d]);
    }
  }
  function resolveW(wx, wy, r, normals) {
    const [lx, ly] = toLevel(wx, wy), ns = [];
    const [nx, ny] = resolveCircleL(lx, ly, r, ns);
    for (const n of ns) normals.push(vW(n[0], n[1]));
    return vW(nx - lx, ny - ly);
  }
  function contactsNow() {
    const ns = [];
    for (const o of PC) { const [lx, ly] = toLevel(S.px, S.py - o); touchingL(lx, ly, PR + SKIN, ns); }
    return ns.map(n => vW(n[0], n[1]));
  }
  function project(ns) {
    for (let pass = 0; pass < 3; pass++) for (const n of ns) {
      const vn = S.vx * n[0] + S.vy * n[1];
      if (vn < 0) { S.vx -= vn * n[0]; S.vy -= vn * n[1]; }
    }
  }

  // ---------------- reset ----------------
  let lvlCv = null;
  function resetLevel(intro) {
    S.theta = 0; S.tx = 0; S.ty = 0; S.vx = 0; S.vy = 0; S.grounded = true;
    S.stick = 0; S.u = 0; S.hist = []; S.airTime = 0; S.state = intro ? 'intro' : 'play'; S.timer = intro ? P.intro : 0;
    S.anim = 'idle'; S.animT = 0; S.flash = 0; S.shake = 0; S.slow = 0; S.near = {}; S.fx = []; S.pops = []; S.sizzle = 0; S.toast = null;
    S.needleA = 0; S.needleV = 0; S.doorF = 0; S.hint = 0; S.tiltT = 0;
    S.step = 0; S.stepAt = S.time; S.woke = 0; S.done = false;
    const w = toWorld(LV.start.feet[0], LV.start.feet[1]); S.px = w[0]; S.py = w[1];
    lavaLoad(LAVA0);
    S.unlocked = doorTilt() <= 20;
    S.cam = camTarget();
    S.runStart = S.time + (intro ? P.intro : 0); S.runTime = 0;
    snapshot();
  }
  function loadLevel(i) {
    setupLevel(i);
    if (IMG.door) buildArt();
    S.mode = 'idle'; S.time = 0; S.deaths = 0;
    resetLevel(false);
  }
  function startRun(mode) {
    S.dbg = {}; S.time = 0;
    S.mode = mode; S.deaths = 0;
    resetLevel(P.intro > 0);
  }
  // camera: mostly the level's centre, pulled part of the way toward the player, so the whole small level stays in view
  function camTarget() {
    const c = toWorld(MW / 2, MH / 2), f = 0.4;
    return [c[0] + (S.px - c[0]) * f, c[1] + (S.py - 0.8 - c[1]) * f];
  }

  // ---------------- simulation ----------------
  const input = { keys: 0, buttons: 0, pad: 0, auto: null };
  function step(dt) {
    S.time += dt;
    snapshot();
    const live = S.state === 'play' && S.mode !== 'idle';
    let u = 0;
    if (live && S.mode === 'demo') {
      const react = PLAN[S.step].react ?? LV.react ?? P.react;
      const tgt = S.time - S.stepAt < react && S.step > 0 ? PLAN[S.step - 1].t : PLAN[S.step].t;
      u = clamp((tgt * DEG - S.theta) * 4, -1, 1);
    }
    else if (live && input.auto) u = clamp(input.auto(S), -1, 1);
    else if (live) {
      S.stick += (input.pad - S.stick) * Math.min(1, dt * 12);
      const st = Math.abs(S.stick) > 0.05 ? S.stick : 0;
      u = st !== 0 ? st : clamp(input.keys + input.buttons, -1, 1);
    }
    S.u = u;
    const dth = u * P.rotSpeed * DEG * dt;
    const lc = [S.px, S.py - PC[0]];
    S.hist.push(lc); if (S.hist.length > PIVOT_LAG) S.hist.shift();
    if (dth !== 0) {
      const pv = S.grounded ? lc : S.hist[0];
      const c = Math.cos(dth), s = Math.sin(dth), rx = S.tx - pv[0], ry = S.ty - pv[1];
      S.tx = pv[0] + rx * c - ry * s; S.ty = pv[1] + rx * s + ry * c; S.theta += dth;
    }
    if (S.mode !== 'idle' && S.state !== 'intro') {
      lavaStep(dt, G * Math.sin(S.theta), G * Math.cos(S.theta));
      if (S.state === 'play') stepVent(dt);
    }
    if (live) {
      stepPlayer(dt);
      checkHazards();
      checkWakes();
      const b = PLAN[S.step];
      if (b.hold) { const a = S.theta / DEG; S.tiltT = Math.abs(a - b.t) < 15 ? S.tiltT + dt : S.tiltT; } else S.tiltT = 0;
      advancePlan();
      if (!S.done) S.runTime = S.time - S.runStart;
    }
    updateDoor(dt);
    stepFx(dt);
    const tgt = camTarget(), kc = 1 - Math.exp(-7 * dt);
    S.cam[0] += (tgt[0] - S.cam[0]) * kc; S.cam[1] += (tgt[1] - S.cam[1]) * kc;
    if (S.state !== 'play') {
      S.timer -= dt;
      if (S.timer <= 0) {
        if (S.state === 'intro') { S.state = 'play'; S.stepAt = S.time; S.runStart = S.time; }
        else if (S.state === 'dead') resetLevel(false);
        else if (S.state === 'clear') { S.state = 'over'; onRunOver(); }
      }
    }
  }

  function stepPlayer(dt) {
    S.vx = 0;
    S.vy = Math.min(S.vy + G * dt, VMAX);
    project(contactsNow());
    for (let k = 0; k < SUB; k++) {
      S.px += S.vx * dt / SUB; S.py += S.vy * dt / SUB;
      const ns = [];
      for (const o of [PC[0], PC[1], PC[0]]) { const cr = resolveW(S.px, S.py - o, PR, ns); S.px += cr[0]; S.py += cr[1]; }
      project(ns);
    }
    const after = contactsNow();
    project(after);
    const grounded = after.some(n => n[1] < -0.5);
    if (grounded && !S.grounded && S.airTime > 0.18) { S.anim = 'land'; S.animT = 0; if (S.airTime > 0.5) dust(lp(), 6); }
    if (!grounded && S.grounded) { S.anim = 'fallstart'; S.animT = 0; }
    S.airTime = grounded ? 0 : S.airTime + dt;
    S.grounded = grounded;
    const l = lp();
    if (l[0] < -2 || l[1] < -2 || l[0] > MW + 2 || l[1] > MH + 2) die('掉出地图了');
  }

  function die(msg) {
    if (S.state !== 'play') return;
    S.state = 'dead'; S.timer = 0.9; S.flash = 0.5; S.flashColor = 'rgba(255,96,40,'; S.deaths++;
    S.shake = Math.max(S.shake, 0.4);
    const l = lp();
    for (let i = 0; i < 18; i++) S.fx.push({ x: l[0] + (Math.random() - 0.5) * 0.8, y: l[1] + (Math.random() - 0.5) * 0.8, vx: (Math.random() - 0.5) * 3, vy: (Math.random() - 0.5) * 3, life: 1.1, max: 1.1, k: 'smoke', s: 0.3 + Math.random() * 0.3, up: 1.4 });
    toast(msg, 'dead');
  }
  function clear() {
    if (S.state !== 'play') return;
    S.state = 'clear'; S.timer = 2.0; S.flash = 0.45; S.flashColor = 'rgba(63,214,224,'; S.doorF = 0; S.done = true;
    S.runTime = S.time - S.runStart;
    toast(LV.id + ' 通关！' + S.runTime.toFixed(1) + ' 秒', 'clear');
  }
  function nearMiss(kind) {
    if (S.time - (S.near[kind] ?? -9) < 1.6) return;
    S.near[kind] = S.time;
    pop('险！', true);
    if (P.slowmo && (!window.__lavaHeadless || window.__lavaVideo)) S.slow = 0.42;
  }

  const dbgMin = (k, v) => { S.dbg[k] = Math.min(S.dbg[k] ?? 99, v); };
  function checkHazards() {
    if (S.state !== 'play') return;
    const cs = PC.map(o => toLevel(S.px, S.py - o));
    let touch = 0, near = 0;
    for (const c of cs) {
      const md = lavaMinDist(c[0], c[1], 3);
      dbgMin('lava' + S.step, md - PR);
      touch = Math.max(touch, lavaCount(c[0], c[1], PR + 0.08, true));
      if (md < PR + 0.75) near = Math.max(near, lavaCount(c[0], c[1], PR + 0.75, true));
    }
    if (touch >= 4) return die('被岩浆吞没了');
    if (touch > 0 && S.time - S.sizzle > 0.15) {          // a lone droplet only sizzles
      S.sizzle = S.time; const l = lp();
      for (let i = 0; i < 4; i++) S.fx.push({ x: l[0] + (Math.random() - 0.5) * 0.6, y: l[1] + (Math.random() - 0.5) * 0.6, vx: 0, vy: 0, life: 0.6, max: 0.6, k: 'smoke', s: 0.18, up: 1.6 });
    }
    if (near >= 2) nearMiss('lava');
    // door
    const c = cs[0], dx = c[0] - DOOR.bx, dy = c[1] - DOOR.by;
    const u = dx * DOOR.rx + dy * DOOR.ry, v = dx * DOOR.ux + dy * DOOR.uy;
    if (Math.abs(u) < 0.95 && v > -0.2 && v < 2.0) { if (S.unlocked) clear(); else S.hint = 1; }
  }

  // asleep pools wake up when you make your move
  function checkWakes() {
    const l = lp();
    for (const w of WAKES) {
      if (S.woke & (1 << w.g) || !w.test(l)) continue;
      S.woke |= 1 << w.g;
      if (wakeGroup(w.g) > 0) { pop('岩浆活了！'); S.shake = Math.max(S.shake, 0.25); }
    }
  }
  function advancePlan() {
    while (S.step < PLAN.length - 1 && PLAN[S.step].next()) { S.step++; S.stepAt = S.time; }
  }

  function updateDoor(dt) {
    const tilt = doorTilt();
    if (S.unlocked && tilt > 23) S.unlocked = false;
    else if (!S.unlocked && tilt <= 20) S.unlocked = true;
    if (S.hint > 0) S.hint = Math.max(0, S.hint - dt);
    S.needleV += (-40 * Math.sin(S.needleA) - 5 * S.needleV) * dt;
    S.needleA += S.needleV * dt;
    if (S.state === 'clear' || S.state === 'over') S.doorF = Math.min(3, S.doorF + dt * 10);
  }

  // ---------------- effects ----------------
  function dust(at, n) {
    for (let i = 0; i < n; i++) S.fx.push({ x: at[0] + (Math.random() - 0.5) * 0.8, y: at[1] + (Math.random() - 0.5) * 0.8, vx: (Math.random() - 0.5) * 1.6, vy: (Math.random() - 0.5) * 1.6, life: 0.9, max: 0.9, k: 'dust', s: 0.18 + Math.random() * 0.22 });
  }
  function pop(text, big) { S.pops.push({ text, t: 0, big: !!big }); if (S.pops.length > 3) S.pops.shift(); }
  function stepFx(dt) {
    const gx = Math.sin(S.theta), gy = Math.cos(S.theta);
    // embers popping off the lava
    if (L.n > 0 && S.mode !== 'idle') {
      const k = Math.floor(L.n / 70 + Math.random());
      for (let e = 0; e < k; e++) {
        const i = (Math.random() * L.n) | 0;
        if (L.fz[i]) continue;
        S.fx.push({ x: L.x[i], y: L.y[i], vx: -gx * (1 + Math.random() * 2) + (Math.random() - 0.5), vy: -gy * (1 + Math.random() * 2) + (Math.random() - 0.5), life: 0.5 + Math.random() * 0.5, max: 1, k: 'ember', s: 0.07 + Math.random() * 0.05, grav: 0.25 });
      }
    }
    for (const p of S.fx) {
      p.life -= dt;
      const g = p.grav ? G * p.grav : p.k === 'dust' ? 0.6 : 0;
      p.vx += gx * g * dt; p.vy += gy * g * dt;
      if (p.up) { p.vx -= gx * p.up * dt; p.vy -= gy * p.up * dt; }
      if (p.k === 'dust' || p.k === 'smoke' || p.k === 'steam') { p.vx *= 1 - 1.5 * dt; p.vy *= 1 - 1.5 * dt; }
      p.x += p.vx * dt; p.y += p.vy * dt;
    }
    S.fx = S.fx.filter(p => p.life > 0);
    if (S.fx.length > 320) S.fx.splice(0, S.fx.length - 320);
    for (const p of S.pops) p.t += dt;
    S.pops = S.pops.filter(p => p.t < (p.big ? 0.9 : 0.8));
    S.shake *= Math.exp(-6 * dt);
    if (S.flash > 0) S.flash = Math.max(0, S.flash - dt);
  }

  // ---------------- art ----------------
  const COL = { airDark: [20, 11, 10], air: [38, 20, 17], rimHi: [206, 104, 58], rim: [132, 58, 38], mid: [70, 33, 26], deep: [38, 18, 15] };
  const put = (d, i, c) => { d[i] = c[0]; d[i + 1] = c[1]; d[i + 2] = c[2]; d[i + 3] = 255; };
  const shade = (c, k) => [clamp(c[0] + k, 0, 255), clamp(c[1] + k * 0.8, 0, 255), clamp(c[2] + k * 0.7, 0, 255)];
  const mixc = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
  function buildLevel() {
    const W = MW * TS, H = MH * TS, cv = mk(W, H), g = cv.getContext('2d');
    const img = g.createImageData(W, H), d = img.data;
    const open = (cx, cy) => !(cx < 0 || cy < 0 || cx >= MW || cy >= MH || rock[cy * MW + cx] === 1);
    const nearCool = (x, y) => COOL.some(r => x >= (r[0] - 1) * TS && x < (r[2] + 1) * TS && y >= r[1] * TS && y < r[3] * TS);
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const cx = (x / TS) | 0, cy = (y / TS) | 0, i = (y * W + x) * 4, n = hash(x >> 1, y >> 1);
      const isOpen = open(cx, cy);
      let dmin = 99;
      for (let oy = -1; oy <= 1; oy++) for (let ox = -1; ox <= 1; ox++) {
        if (open(cx + ox, cy + oy) === isOpen) continue;
        const rx0 = (cx + ox) * TS, ry0 = (cy + oy) * TS;
        const ddx = Math.max(rx0 - x, 0, x - (rx0 + TS - 1)), ddy = Math.max(ry0 - y, 0, y - (ry0 + TS - 1));
        dmin = Math.min(dmin, Math.hypot(ddx, ddy));
      }
      let col;
      if (isOpen) {
        col = mixc(COL.airDark, COL.air, clamp((dmin - 1) / 9, 0, 1));
        col = shade(col, (n - 0.5) * 6 + (hash(cx, cy) - 0.5) * 4);
      } else {
        col = dmin < 1.5 ? COL.rimHi : dmin < 4 ? COL.rim : dmin < 10 ? COL.mid : COL.deep;
        if (n > 0.94) col = shade(col, 16); else if (n < 0.05) col = shade(col, -12);
        // basalt columns: faint vertical seams in the deep rock
        if (dmin > 6 && ((x + ((y / 24) | 0) * 7) % 12 === 0)) col = shade(col, -10);
        if (dmin < 5 && nearCool(x, y)) {
          const t = dmin < 1.5 ? [176, 196, 214] : dmin < 3 ? [92, 112, 132] : [56, 66, 80];
          col = ((y >> 2) % 4 === 0 && dmin > 2) ? shade(t, -14) : t;
        }
      }
      put(d, i, col);
    }
    g.putImageData(img, 0, 0);
    g.save(); g.scale(TS, TS);
    g.lineCap = 'round';
    // glowing cracks in the rock: short zigzags on solid cells a little away from the tunnels
    const isRock = (cx, cy) => cx < 0 || cy < 0 || cx >= MW || cy >= MH || rock[cy * MW + cx] === 1;
    let made = 0;
    for (let k = 0; k < 600 && made < 5; k++) {
      const cx = Math.floor(hash(k * 1.7, LI * 3.1 + 0.5) * MW), cy = Math.floor(hash(k * 2.3 + 9, LI * 1.3 + 0.2) * MH);
      if (cx < 2 || cy < 2 || cx > MW - 3 || cy > MH - 3) continue;
      let near2 = false, near4 = false;
      for (let oy = -2; oy <= 2; oy++) for (let ox = -2; ox <= 2; ox++) { if (!isRock(cx + ox, cy + oy)) near2 = true; }
      for (let oy = -4; oy <= 4; oy++) for (let ox = -4; ox <= 4; ox++) { if (!isRock(cx + ox, cy + oy)) near4 = true; }
      if (near2 || !near4) continue;
      const pts = [], vert = hash(k, 7) > 0.5;
      for (let s = 0; s < 3; s++) pts.push(vert ? [cx + 0.3 + hash(k + s, 3) * 0.4, cy - 0.3 + s * 0.6] : [cx - 0.3 + s * 0.6, cy + 0.3 + hash(k + s, 5) * 0.4]);
      g.strokeStyle = 'rgba(255,110,40,0.18)'; g.lineWidth = 0.28; g.beginPath(); pts.forEach((p, i) => i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1])); g.stroke();
      g.strokeStyle = 'rgba(255,170,80,0.7)'; g.lineWidth = 0.07; g.beginPath(); pts.forEach((p, i) => i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1])); g.stroke();
      made++;
    }
    // rivets on cooling pipes
    for (const r of COOL) for (let y = r[1] + 0.6; y < r[3]; y += 1.4) for (const x of [r[0] - 0.45, r[2] + 0.45]) { g.fillStyle = '#c8d6e2'; g.fillRect(x - 0.08, y - 0.08, 0.16, 0.16); }
    // the vent: an iron nozzle in the ceiling
    if (VENT) {
      g.fillStyle = '#2e2a2a'; g.fillRect(VENT.x - 0.75, VENT.y - 0.55, 1.5, 0.55);
      g.fillStyle = '#4a4442'; g.fillRect(VENT.x - 0.75, VENT.y - 0.55, 1.5, 0.12);
      g.fillStyle = '#1c1717'; g.fillRect(VENT.x - 0.5, VENT.y - 0.05, 1.0, 0.3);
      g.fillStyle = '#ff9a3a'; g.fillRect(VENT.x - 0.38, VENT.y + 0.18, 0.76, 0.07);
      for (const bx of [-0.62, 0.5]) { g.fillStyle = '#5a5250'; g.fillRect(VENT.x + bx, VENT.y - 0.42, 0.12, 0.12); }
    }
    g.restore();
    return cv;
  }

  // lava image: splat particles into a density field (8 texels per tile), threshold, colour
  const LS = 8, LT = 0.42;
  let LTW = 8, LTH = 8, lf = null, lcr = null, lfr = null;
  let lavaCv = null, lavaCtx = null, lavaImg = null, glowA = null, glowB = null, lavaDrawn = -1;
  const LCOL = { edge: [255, 228, 150], hot: [255, 156, 48], core: [238, 98, 30], crust: [150, 46, 22], crustDeep: [92, 26, 14],
    coolEdge: [150, 64, 34], cool: [74, 28, 18], coolDeep: [52, 20, 14], coolSeam: [196, 76, 26] };
  function buildArt() {
    lvlCv = buildLevel();
    LTW = MW * LS; LTH = MH * LS;
    lf = new Float32Array(LTW * LTH); lcr = new Float32Array(LTW * LTH); lfr = new Float32Array(LTW * LTH);
    lavaCv = mk(LTW, LTH); lavaCtx = lavaCv.getContext('2d'); lavaImg = lavaCtx.createImageData(LTW, LTH);
    glowA = mk(LTW >> 2, LTH >> 2); glowB = mk(LTW >> 4, LTH >> 4);
    lavaDrawn = -1;
  }
  function buildLava() {
    lf.fill(0); lcr.fill(0); lfr.fill(0);
    const R = 0.4, R2 = R * R, rc = R * LS;
    for (let i = 0; i < L.n; i++) {
      const px = L.x[i] * LS, py = L.y[i] * LS, cr = L.c[i], fr = L.fz[i];
      const x0 = Math.max(0, Math.floor(px - rc)), x1 = Math.min(LTW - 1, Math.floor(px + rc)), y0 = Math.max(0, Math.floor(py - rc)), y1 = Math.min(LTH - 1, Math.floor(py + rc));
      for (let y = y0; y <= y1; y++) {
        const dy = (y + 0.5 - py) / LS, row = y * LTW;
        for (let x = x0; x <= x1; x++) {
          const dx = (x + 0.5 - px) / LS, d2 = dx * dx + dy * dy;
          if (d2 >= R2) continue;
          const q = 1 - d2 / R2, w = q * q;
          lf[row + x] += w; if (cr) lcr[row + x] += w; if (fr) lfr[row + x] += w;
        }
      }
    }
    const d = lavaImg.data, flick = Math.floor(S.time * 6);
    for (let k = 0, o = 0; k < LTW * LTH; k++, o += 4) {
      const f = lf[k];
      if (f < LT) { d[o + 3] = 0; continue; }
      const e = f - LT, cr = lcr[k] / f;
      let c;
      if (lfr[k] > f * 0.5) {        // asleep: dark crust with faint glowing seams
        const hx = (k % LTW) >> 1, hy = ((k / LTW) | 0) >> 1, sm = hash(hx * 0.37 + hy * 1.31, hy * 0.53 - hx * 0.21);
        c = e < 0.14 ? LCOL.coolEdge : sm > 0.9 ? LCOL.coolSeam : cr > 0.42 ? LCOL.coolDeep : LCOL.cool;
        d[o] = c[0]; d[o + 1] = c[1]; d[o + 2] = c[2]; d[o + 3] = 255; continue;
      }
      if (e < 0.14) c = LCOL.edge;
      else if (cr > 0.42) c = e < 0.6 ? LCOL.crust : LCOL.crustDeep;
      else c = e < 0.45 ? LCOL.hot : LCOL.core;
      let b = 0;
      if (c === LCOL.core && hash((k % LTW) >> 1, ((k / LTW) | 0) + flick * 3) > 0.93) b = 30;   // sparkle
      d[o] = Math.min(255, c[0] + b); d[o + 1] = Math.min(255, c[1] + b); d[o + 2] = Math.min(255, c[2] + b); d[o + 3] = 255;
    }
    lavaCtx.putImageData(lavaImg, 0, 0);
    const ga = glowA.getContext('2d'), gb = glowB.getContext('2d');
    ga.clearRect(0, 0, glowA.width, glowA.height); ga.imageSmoothingEnabled = true; ga.drawImage(lavaCv, 0, 0, glowA.width, glowA.height);
    gb.clearRect(0, 0, glowB.width, glowB.height); gb.imageSmoothingEnabled = true; gb.drawImage(glowA, 0, 0, glowB.width, glowB.height);
    lavaDrawn = L.ver;
  }

  // ---------------- drawing ----------------
  const view = document.getElementById('view'), ctx = view.getContext('2d');
  const REDUCED = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const IMG = {};
  function tinted(src, rgba) { const c = mk(src.width, src.height), g = c.getContext('2d'); g.drawImage(src, 0, 0); g.globalCompositeOperation = 'source-atop'; g.fillStyle = rgba; g.fillRect(0, 0, c.width, c.height); return c; }
  const ANIMS = { idle: { img: 'idle', n: 4, f: 0.2, loop: true }, fallstart: { img: 'fallstart', n: 2, f: 0.06, next: 'float' },
    float: { img: 'float', n: 6, f: 0.09, loop: true }, land: { img: 'land', n: 3, f: 0.07, next: 'idle' } };
  function animFrame(dt) {
    let a = ANIMS[S.anim];
    if (!S.grounded && (S.anim === 'idle' || S.anim === 'land')) { S.anim = 'fallstart'; S.animT = 0; a = ANIMS.fallstart; }
    if (S.grounded && (S.anim === 'float' || S.anim === 'fallstart')) { S.anim = 'idle'; S.animT = 0; a = ANIMS.idle; }
    S.animT += dt;
    let idx = Math.floor(S.animT / a.f);
    if (idx >= a.n) { if (a.loop) idx %= a.n; else { S.anim = a.next; S.animT = 0; a = ANIMS[S.anim]; idx = 0; } }
    return { img: a.img, idx };
  }

  let K = 40, CX = 0, CY = 0, alpha = 1, W = 0, H = 0;
  function resize() {
    const r = view.getBoundingClientRect(), dpr = Math.min(window.devicePixelRatio || 1, 2);
    view.width = Math.max(320, Math.round(r.width * dpr)); view.height = Math.max(200, Math.round(r.height * dpr));
  }
  function render(dt) {
    W = view.width; H = view.height;
    K = Math.min(H / (Math.max(MW, MH) + 3), W / (Math.max(MW, MH) + 8)); CX = W / 2; CY = H * 0.53;
    if (lavaDrawn !== L.ver) buildLava();
    const lp_ = (a, b) => a + (b - a) * alpha;
    const th = lp_(prev.theta, S.theta), Tx = lp_(prev.tx, S.tx), Ty = lp_(prev.ty, S.ty);
    const Px = lp_(prev.px, S.px), Py = lp_(prev.py, S.py);
    let camx = lp_(prev.cam[0], S.cam[0]), camy = lp_(prev.cam[1], S.cam[1]);
    if (S.shake > 0.01 && !REDUCED) { camx += (Math.random() - 0.5) * S.shake * 0.9; camy += (Math.random() - 0.5) * S.shake * 0.9; }
    const c = Math.cos(th), s = Math.sin(th);
    const ox = CX + K * (Tx - camx), oy = CY + K * (Ty - camy);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = '#0c0605'; ctx.fillRect(0, 0, W, H);
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(K * c, K * s, -K * s, K * c, ox, oy);
    ctx.fillStyle = 'rgb(38,18,15)'; ctx.fillRect(-40, -40, MW + 80, MH + 80);
    ctx.drawImage(lvlCv, 0, 0, MW, MH);
    // door
    ctx.save(); ctx.translate(DOOR.bx, DOOR.by); ctx.rotate(DOOR.rot);
    if (S.state === 'clear' || S.state === 'over') ctx.drawImage(IMG.doorOpen, Math.floor(S.doorF) * 32, 0, 32, 32, -1, -2, 2, 2);
    else ctx.drawImage(IMG.door, (Math.floor(S.time * 7) % 6) * 32, 0, 32, 32, -1, -2, 2, 2);
    const lock = (S.unlocked || S.state === 'clear' || S.state === 'over') ? IMG.lockOpen : (S.hint > 0 && Math.floor(S.hint * 8) % 2 === 0) ? IMG.lockHint : IMG.lockLocked;
    ctx.drawImage(lock, -1, -2, 2, 2);
    ctx.translate((15.5 - 16) / 16, -2 + 10.5 / 16); ctx.rotate(-(th + DOOR.rot) + S.needleA);
    ctx.drawImage(IMG.needle, -1 / 16, 0, 2 / 16, 8 / 16);
    ctx.restore();
    // lava: soft glow first, then the crisp pixel body
    ctx.globalCompositeOperation = 'lighter';
    ctx.imageSmoothingEnabled = true;
    ctx.globalAlpha = 0.5; ctx.drawImage(glowB, 0, 0, MW, MH);
    ctx.globalAlpha = 0.35; ctx.drawImage(glowA, 0, 0, MW, MH);
    ctx.globalAlpha = 1; ctx.globalCompositeOperation = 'source-over';
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(lavaCv, 0, 0, MW, MH);
    // particles
    for (const p of S.fx) {
      const a = clamp(p.life / p.max, 0, 1);
      if (p.k === 'ember') { ctx.globalAlpha = a; ctx.fillStyle = a > 0.5 ? '#ffd27a' : '#ff8a3a'; }
      else if (p.k === 'smoke') { ctx.globalAlpha = a * 0.5; ctx.fillStyle = '#3a3030'; }
      else if (p.k === 'steam') { ctx.globalAlpha = a * 0.6; ctx.fillStyle = '#c9d4dc'; }
      else { ctx.globalAlpha = a * 0.55; ctx.fillStyle = '#7a5a4a'; }
      const sz = p.k === 'dust' || p.k === 'smoke' || p.k === 'steam' ? p.s * (1.8 - a * 0.8) : p.s;
      ctx.fillRect(p.x - sz / 2, p.y - sz / 2, sz, sz);
    }
    ctx.globalAlpha = 1;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    const toS = (wx, wy) => [CX + K * (wx - camx), CY + K * (wy - camy)];
    // player
    const fr = animFrame(dt), sc = K * 1.54 / 92, pS = toS(Px, Py);
    let src = IMG[fr.img];
    if (S.state === 'dead') src = IMG[fr.img + 'Red'];
    ctx.globalAlpha = S.state === 'clear' ? Math.max(0, 1 - (2.0 - S.timer) * 1.6) : S.state === 'over' ? 0 : 1;
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(src, fr.idx * 96, 0, 96, 112, Math.round(pS[0] - 49.5 * sc), Math.round(pS[1] - 112 * sc), Math.round(96 * sc), Math.round(112 * sc));
    ctx.globalAlpha = 1;
    // speed lines
    const spd = Math.abs(S.vy);
    if (spd > 7.5) {
      const dx = 0, dy = S.vy < 0 ? 1 : -1;
      ctx.strokeStyle = 'rgba(255,236,210,0.28)'; ctx.lineWidth = Math.max(1, K * 0.035);
      const n = Math.min(14, Math.floor((spd - 7) * 2.2));
      for (let k = 0; k < n; k++) {
        const h1 = hash(k * 3.1, Math.floor(S.time * 14) + k), h2 = hash(k * 7.7, Math.floor(S.time * 14) - k);
        const off = (h1 - 0.5) * K * 7, along = (h2 - 0.2) * K * 6, len = K * (1.2 + spd * 0.12);
        const x0 = pS[0] - dy * off + dx * along, y0 = pS[1] - K * 0.8 + dx * off + dy * along;
        ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x0 + dx * len, y0 + dy * len); ctx.stroke();
      }
    }
    // heat haze vignette
    const vg = ctx.createRadialGradient(CX, CY, H * 0.3, CX, CY, H * 0.95);
    vg.addColorStop(0, 'rgba(0,0,0,0)'); vg.addColorStop(1, 'rgba(18,3,0,0.55)');
    ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H);
    if (S.flash > 0) { ctx.fillStyle = S.flashColor + Math.min(0.35, S.flash).toFixed(3) + ')'; ctx.fillRect(0, 0, W, H); }
    drawHud(pS);
  }

  function drawHud(pS) {
    const u = Math.max(1, H / 640);
    ctx.textBaseline = 'alphabetic';
    for (const p of S.pops) {
      const a = 1 - p.t / (p.big ? 0.9 : 0.8), rise = p.t * K * 1.6;
      ctx.globalAlpha = clamp(a * 1.5, 0, 1);
      ctx.font = `600 ${Math.round((p.big ? 34 : 20) * u)}px "Noto Serif SC", serif`;
      ctx.textAlign = 'center';
      ctx.lineWidth = 4 * u; ctx.strokeStyle = 'rgba(0,0,0,0.75)';
      ctx.fillStyle = p.big ? '#ffd27a' : '#ffe2bd';
      const x = pS[0] + (p.big ? K * 1.6 : 0), y = pS[1] - K * 2.4 - rise;
      ctx.strokeText(p.text, x, y); ctx.fillText(p.text, x, y);
    }
    ctx.globalAlpha = 1;
    if (S.toast && S.time - S.toast.t < (S.toast.kind === 'clear' ? 2.0 : 0.9)) {
      const a = clamp((S.time - S.toast.t) / 0.15, 0, 1);
      ctx.globalAlpha = a; ctx.textAlign = 'center';
      ctx.font = `600 ${Math.round(34 * u)}px "Noto Serif SC", serif`;
      ctx.lineWidth = 6 * u; ctx.strokeStyle = 'rgba(0,0,0,0.8)';
      ctx.fillStyle = S.toast.kind === 'clear' ? '#3fd6e0' : '#ff8a5a';
      ctx.strokeText(S.toast.text, CX, CY - H * 0.18); ctx.fillText(S.toast.text, CX, CY - H * 0.18);
      ctx.globalAlpha = 1;
    }
    // level badge, top-left
    ctx.textAlign = 'left'; ctx.fillStyle = '#ff9a4a'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText(LV.id, 14 * u, 26 * u);
    ctx.fillStyle = '#eadfd2'; ctx.font = `600 ${Math.round(16 * u)}px "Noto Serif SC", serif`;
    ctx.fillText(LV.name, 14 * u + ctx.measureText(LV.id).width + 40 * u, 26 * u);
    if (S.state === 'intro') { drawIntro(u); return; }
    if (S.mode === 'idle') return;
    const st = PLAN[S.step], age = S.time - S.stepAt, ap = clamp(age / 0.22, 0, 1);
    if (S.state === 'play' || S.state === 'dead') {
      const bw = Math.min(W * 0.62, 560 * u), bh = (st.sub ? 64 : 46) * u, bx = CX - bw / 2, by = 12 * u - (1 - ap) * 10 * u;
      ctx.globalAlpha = 0.25 + 0.75 * ap;
      ctx.fillStyle = 'rgba(16,7,5,0.8)'; rr(bx, by, bw, bh, 10 * u); ctx.fill();
      ctx.strokeStyle = 'rgba(255,140,60,0.55)'; ctx.lineWidth = 1.5 * u; rr(bx, by, bw, bh, 10 * u); ctx.stroke();
      ctx.textAlign = 'left'; ctx.fillStyle = '#ff9a4a'; ctx.font = `600 ${Math.round(13 * u)}px ui-monospace, Menlo, monospace`;
      ctx.fillText(`第 ${S.step + 1} 拍`, bx + 14 * u, by + 28 * u);
      ctx.textAlign = 'center'; ctx.fillStyle = '#fff1df'; ctx.font = `600 ${Math.round(20 * u)}px "Noto Serif SC", serif`;
      ctx.fillText(st.cap, CX + 22 * u, by + 30 * u);
      if (st.sub) { ctx.fillStyle = '#d6b9a4'; ctx.font = `${Math.round(14 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`; ctx.fillText(st.sub, CX + 22 * u, by + 52 * u); }
      ctx.globalAlpha = 1;
    }
    ctx.textAlign = 'left'; ctx.fillStyle = '#eadfd2'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText(S.runTime.toFixed(1) + ' s', 14 * u, H - 18 * u);
    ctx.fillStyle = S.mode === 'demo' ? '#ff9a4a' : '#a8968a'; ctx.font = `${Math.round(12 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.mode === 'demo' ? '演示中' : '自己玩', 14 * u, H - 38 * u);
    const n = PLAN.length, gap = 26 * u, tw = (n - 1) * gap, tx0 = CX - tw / 2, ty = H - 26 * u;
    ctx.fillStyle = 'rgba(16,7,5,0.6)'; rr(tx0 - 16 * u, ty - 14 * u, tw + 32 * u, 28 * u, 14 * u); ctx.fill();
    ctx.strokeStyle = 'rgba(234,223,210,0.25)'; ctx.lineWidth = 2 * u; ctx.beginPath(); ctx.moveTo(tx0, ty); ctx.lineTo(tx0 + tw, ty); ctx.stroke();
    for (let i = 0; i < n; i++) {
      const x = tx0 + i * gap, done = i < S.step || S.state === 'clear' || S.state === 'over', cur = i === S.step && S.state === 'play';
      ctx.beginPath(); ctx.arc(x, ty, (cur ? 7 + Math.sin(S.time * 10) * 1.5 : 5) * u, 0, Math.PI * 2);
      ctx.fillStyle = done ? '#ff9a4a' : cur ? '#fff1df' : 'rgba(20,10,8,0.9)'; ctx.fill();
      ctx.strokeStyle = done || cur ? '#ff9a4a' : 'rgba(234,223,210,0.45)'; ctx.lineWidth = 1.5 * u; ctx.stroke();
    }
    const wx = W - 52 * u, wy = H - 52 * u, wr = 30 * u;
    S.uVis += (S.u - S.uVis) * 0.25;
    ctx.save(); ctx.translate(wx, wy);
    ctx.fillStyle = 'rgba(16,7,5,0.7)'; ctx.beginPath(); ctx.arc(0, 0, wr + 10 * u, 0, Math.PI * 2); ctx.fill();
    const full = Math.abs(S.u) > 0.95;
    if (Math.abs(S.uVis) > 0.05) {
      ctx.strokeStyle = full ? '#ff9a4a' : 'rgba(255,154,74,0.6)'; ctx.lineWidth = 4 * u; ctx.beginPath();
      ctx.arc(0, 0, wr + 5 * u, -Math.PI / 2, -Math.PI / 2 + S.uVis * 1.4, S.uVis < 0); ctx.stroke();
    }
    ctx.rotate(S.uVis * 75 * DEG);
    ctx.strokeStyle = '#eadfd2'; ctx.lineWidth = 4 * u; ctx.beginPath(); ctx.arc(0, 0, wr, 0, Math.PI * 2); ctx.stroke();
    ctx.lineWidth = 3 * u; ctx.beginPath(); ctx.moveTo(-wr, 0); ctx.lineTo(wr, 0); ctx.moveTo(0, 0); ctx.lineTo(0, wr); ctx.stroke();
    ctx.fillStyle = '#ff9a4a'; ctx.beginPath(); ctx.arc(0, -wr, 4 * u, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#eadfd2'; ctx.beginPath(); ctx.arc(0, 0, 6 * u, 0, Math.PI * 2); ctx.fill();
    ctx.restore();
    // world angle readout left of the wheel (the slosh beat asks for 45°)
    ctx.fillStyle = '#a8968a'; ctx.textAlign = 'center'; ctx.font = `${Math.round(11 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.u > 0.05 ? '顺时针' : S.u < -0.05 ? '逆时针' : '手柄', wx, wy - wr - 16 * u);
    ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`; ctx.fillStyle = '#eadfd2'; ctx.textAlign = 'right';
    ctx.fillText(Math.round(S.theta / DEG) + '°', wx - wr - 18 * u, wy + 5 * u);
  }
  // title card shown for a moment when a level starts
  function drawIntro(u) {
    const t = P.intro - S.timer, a = clamp(Math.min(t / 0.18, S.timer / 0.25), 0, 1);
    ctx.globalAlpha = a * 0.72; ctx.fillStyle = '#0c0605'; ctx.fillRect(0, 0, W, H);
    ctx.globalAlpha = a; ctx.textAlign = 'center';
    ctx.fillStyle = '#ff9a4a'; ctx.font = `600 ${Math.round(22 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText('岩浆 ' + LV.id, CX, CY - 40 * u);
    ctx.fillStyle = '#fff1df'; ctx.font = `600 ${Math.round(52 * u)}px "Noto Serif SC", serif`;
    ctx.fillText(LV.name, CX, CY + 22 * u);
    ctx.fillStyle = '#d6b9a4'; ctx.font = `${Math.round(16 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(LV.tip, CX, CY + 62 * u);
    ctx.globalAlpha = 1;
  }
  function rr(x, y, w, h, r) { ctx.beginPath(); ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r); ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath(); }

  // ---------------- page UI ----------------
  function toast(text, kind) { S.toast = { text, kind, t: S.time }; }
  const $ = (id) => document.getElementById(id);
  const pTime = $('pTime'), pDeath = $('pDeath'), overlay = $('startOverlay'), ovText = $('ovText');
  const ovDemo = $('ovDemo'), ovPlay = $('ovPlay'), ovNext = $('ovNext');
  let lastHud = -1;
  function updateHud() { pTime.textContent = S.runTime.toFixed(1) + ' 秒'; pDeath.textContent = String(S.deaths); }
  function showOverlay(kind) {
    overlay.hidden = false;
    const nx = LEVELS[LI + 1];
    if (kind === 'start') {
      ovDemo.textContent = '▶ 看演示通关'; ovPlay.textContent = '自己玩';
      ovNext.hidden = true;
      ovText.textContent = LV.id + ' ' + LV.name + '：' + LV.tip;
    } else {
      ovDemo.textContent = S.mode === 'demo' ? '▶ 再看一遍' : '▶ 看演示';
      ovPlay.textContent = S.mode === 'demo' ? '自己玩这关' : '再玩一次';
      ovNext.hidden = !nx;
      if (nx) ovNext.textContent = '下一关 ' + nx.id + ' ' + nx.name + ' ▶';
      ovText.textContent = LV.id + ' ' + (S.mode === 'demo' ? '演示通关' : '通关') + '，用时 ' + S.runTime.toFixed(1) + ' 秒' + (S.deaths ? '，死亡 ' + S.deaths + ' 次' : '') + (nx ? '。' : '。岩浆四关都过完了。');
    }
  }
  function onRunOver() {
    if (window.__lavaHeadless) return;
    showOverlay('over');
    if (S.mode === 'demo' && LEVELS[LI + 1] && $('chkChain') && $('chkChain').checked) {
      overlay.hidden = true; selectLevel(LI + 1, false); begin('demo');
    }
  }
  const lvButtons = [...document.querySelectorAll('[data-level]')];
  function selectLevel(i, show) {
    loadLevel(i);
    lvButtons.forEach(b => { const on = +b.dataset.level === i; b.classList.toggle('active', on); b.setAttribute('aria-pressed', on ? 'true' : 'false'); });
    if (show) showOverlay('start');
  }
  lvButtons.forEach(b => b.addEventListener('click', () => { selectLevel(+b.dataset.level, true); }));
  function begin(mode) { overlay.hidden = true; startRun(mode); view.focus({ preventScroll: true }); }
  ovDemo.addEventListener('click', () => begin('demo'));
  ovPlay.addEventListener('click', () => begin('play'));
  ovNext.addEventListener('click', () => { const m = S.mode === 'demo' ? 'demo' : 'play'; selectLevel(LI + 1, false); begin(m); });
  $('btnDemo').addEventListener('click', () => begin('demo'));
  $('btnPlay').addEventListener('click', () => begin('play'));
  $('btnReset').addEventListener('click', () => { if (S.mode === 'idle') begin('play'); else { overlay.hidden = true; resetLevel(false); } view.focus({ preventScroll: true }); });
  $('chkSlow').addEventListener('change', (e) => { P.slowmo = e.target.checked; });

  const held = new Set();
  const KEYMAP = { ArrowLeft: -1, KeyQ: -1, KeyA: -1, ArrowRight: 1, KeyE: 1, KeyD: 1 };
  function syncKeys() { let v = 0; for (const k of held) v += KEYMAP[k]; input.keys = clamp(v, -1, 1); }
  function takeOver() { if (S.mode === 'idle' || S.state === 'over') begin('play'); else if (S.mode === 'demo') S.mode = 'play'; }
  window.addEventListener('keydown', (e) => {
    if (e.target && /INPUT|SELECT|TEXTAREA/.test(e.target.tagName)) return;
    if (KEYMAP[e.code] !== undefined) { e.preventDefault(); held.add(e.code); syncKeys(); takeOver(); }
    else if (e.code === 'KeyR') { e.preventDefault(); if (S.mode !== 'idle') { overlay.hidden = true; resetLevel(false); } }
  });
  window.addEventListener('keyup', (e) => { if (held.delete(e.code)) syncKeys(); });
  window.addEventListener('blur', () => { held.clear(); syncKeys(); input.buttons = 0; });
  const holdBtn = (id, dir) => {
    const b = $(id);
    const on = (e) => { e.preventDefault(); input.buttons = dir; b.classList.add('active'); takeOver(); };
    const off = () => { if (input.buttons === dir) input.buttons = 0; b.classList.remove('active'); };
    b.addEventListener('pointerdown', on); b.addEventListener('pointerup', off); b.addEventListener('pointerleave', off); b.addEventListener('pointercancel', off);
  };
  holdBtn('btnCcw', -1); holdBtn('btnCw', 1);
  let padReset = false;
  // some hosts (e.g. an embedded page) block the gamepad API and the call throws: then keyboard / buttons only
  let padOK = true;
  function pollPad() {
    if (!padOK) return;
    let v = 0, rs = false, pads = [];
    try { pads = (navigator.getGamepads && navigator.getGamepads()) || []; }
    catch (e) {
      padOK = false; input.pad = 0;
      const note = $('padNote');
      if (note) note.textContent = '手柄：这个网页里浏览器不让读手柄，用本地的 html 文件打开就能用（摇杆横向或 L2 / R2，Options 重来）';
      return;
    }
    for (const p of pads) {
      if (!p) continue;
      const ax = [p.axes[2] || 0, p.axes[0] || 0].find(a => Math.abs(a) > 0.18) || 0;
      const trig = ((p.buttons[7] && p.buttons[7].value) || 0) - ((p.buttons[6] && p.buttons[6].value) || 0);
      v = Math.abs(trig) > 0.05 ? trig : ax;
      rs = !!(p.buttons[9] && p.buttons[9].pressed);
      if (v) break;
    }
    input.pad = v;
    if (v && S.mode === 'demo') S.mode = 'play';
    if (rs && !padReset && S.mode !== 'idle') { overlay.hidden = true; resetLevel(false); }
    padReset = rs;
  }
  const sliders = [['tRot', 'oRot', 'rotSpeed', v => v + '°/s'], ['tDrag', 'oDrag', 'lavaDrag', v => v.toFixed(2)]];
  for (const [id, out, key, fmt] of sliders) {
    const el = $(id), o = $(out);
    if (!el) continue;
    el.addEventListener('input', () => { P[key] = parseFloat(el.value); o.textContent = fmt(P[key]); if (key === 'lavaDrag') lavaInit(); if (S.mode !== 'idle') { overlay.hidden = true; startRun(S.mode); } else resetLevel(false); });
  }

  // ---------------- boot ----------------
  let last = 0, acc = 0;
  let loopErr = false;
  function frame(ts) {
    requestAnimationFrame(frame);
    if (window.__lavaStop) return;
    try {
      const now = ts / 1000; let dt = last ? now - last : 0; last = now; dt = Math.min(dt, 0.1);
      pollPad();
      if (!window.__lavaHeadless) {
        let scale = 1;
        if (S.slow > 0) { S.slow -= dt; scale = 0.32; }
        acc += dt * scale;
        let guard = 0;
        while (acc >= DT && guard++ < 6) { step(DT); acc -= DT; }
        if (acc > DT) acc = DT;
        alpha = acc / DT;
      } else alpha = 1;
      render(dt);
      if (S.time - lastHud > 0.1 || S.time < lastHud) { lastHud = S.time; updateHud(); }
    } catch (e) { if (!loopErr) { loopErr = true; console.error(e); } }
  }
  const load = (src) => new Promise((res, rej) => { const im = new Image(); im.onload = () => res(im); im.onerror = rej; im.src = src; });
  Promise.all(Object.entries(A).map(([k, v]) => load(v).then(im => { IMG[k] = im; }))).then(() => {
    for (const k of ['idle', 'float', 'fallstart', 'land']) IMG[k + 'Red'] = tinted(IMG[k], 'rgba(255,90,30,0.62)');
    L.onCool = (x, y) => { if (S.fx.length < 300) S.fx.push({ x, y, vx: 0, vy: 0, life: 0.7, max: 0.7, k: inCool(x, y) ? 'steam' : 'smoke', s: 0.26, up: 1.6 }); };
    resize(); window.addEventListener('resize', resize);
    selectLevel(0, true);
    requestAnimationFrame(frame);
    window.__lavaReady = true;
  });
  window.__lava = {
    S, P, L, LQ, LEVELS, step, startRun, loadLevel, resetLevel, input, DT, lavaIn, lavaInit, solidAt, lp,
    get LV() { return LV; }, get LI() { return LI; }, get PLAN() { return PLAN; },
    render: () => render(0), renderAt: (a, dt) => { alpha = a; render(dt); },
  };
})();
