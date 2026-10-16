(() => {
  'use strict';
  const A = window.__ASSETS;
  const DEG = Math.PI / 180;
  // Player rules ported from the game's PlayerController / WorldRotator (fixed step 0.02, no own horizontal speed)
  const G = 9.81, PR = 0.4, PC = [0.4, 1.13];
  const DT = 0.02, SUB = 4, SKIN = 0.02, PIVOT_LAG = 8;
  const P = { rotSpeed: 180, vmax: 11, mineLift: 16, cloudTime: 1.0, riseSpeed: 3.2, slowmo: true, react: 0.12, intro: 1.2 };
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
  const mk = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };
  const air = () => !S.grounded && S.airTime > 0.06;
  // balloon mines: give where each balloon should float while you hold angle th; the anchor goes on the "down" side
  function minesAt(list, th, len = 0.9) { const t = th * DEG; return list.map(([x, y]) => [x + len * Math.sin(t), y + len * Math.cos(t), len]); }

  // ---------------- the four small levels (tiles, y down). Open sky; past `bound` is the cloud sea ----------------
  const LEVELS = [
    {
      id: '3-1', name: '跷跷板', w: 28, h: 18,
      tip: '一站上去它就翻，把你倒进半空。在空中转世界拐弯，落到下一块上，它又翻。',
      bound: { x0: 0, y0: -3, x1: 28, y1: 13.5 },
      isles: [[24, 5, 27, 12]],
      sees: [{ px: 4.5, py: 5, hl: 2.4, ht: 0.16, a0: 0 }, { px: 13, py: 9.4, hl: 2.6, ht: 0.16, a0: 90 }],
      start: [5.8, 4.84],
      door: { bx: 24, by: 9, dir: 90 },
      cue: { a: 5.6, b: 15.0 },
      plan: [
        { t: 0, cap: '跷跷板一翻，就把你倒出去了', sub: '下面是云海，掉下去就没了', next: () => air() && lp()[1] > LV.cue.a },
        { t: 90, cap: '顺时针转 90°：横着往右掉', sub: '落到竖着的那块跷跷板上', react: 0.55, next: () => S.on.has('see1') },
        { t: 90, cap: '它也翻了，把你倒出去', sub: '', next: () => air() && lp()[0] > LV.cue.b },
        { t: () => 90 - clamp((DOOR.by - lp()[1]) * 12, -30, 30), cap: '边飞边对准门', sub: '门在 ±20° 内才开', next: () => lp()[0] > DOOR.bx - 2.2 },
        { t: 90, cap: '摆正，冲进门', sub: '', next: () => false },
      ],
    },
    {
      id: '3-2', name: '气球雷', w: 27, h: 15,
      tip: '气球雷永远往屏幕上方飘，你一拧它们也跟着摆。从缝里钻过去。',
      bound: { x0: 0, y0: -3, x1: 27, y1: 14.5 },
      isles: [[1, 5, 5, 7], [23, 3, 26, 11]],
      mines: [...minesAt([[11.2, 3.4], [11.4, 4.8], [11.6, 6.2], [11.8, 7.6]], 90), ...minesAt([[19.4, -1.6], [19.6, -0.2], [19.8, 1.2], [20.0, 2.6]], 90)],
      cue: { climb: 5.2, level: 3.2, dive: 11.6 },
      start: [3.5, 5.0],
      door: { bx: 23, by: 6.2, dir: 90 },
      plan: [
        { t: 90, cap: '顺时针转 90°：横着往右掉', sub: '在半空中转世界，就是在拐弯', next: () => lp()[0] > LV.cue.climb },
        { t: 135, cap: '再顺时针拧：从气球雷上面飞过', sub: '气球也跟着摆', next: () => lp()[1] < LV.cue.level },
        { t: 90, cap: '摆平', sub: '', next: () => lp()[0] > LV.cue.dive },
        { t: 45, cap: '逆时针拧：钻到下面去', sub: '', next: () => lp()[1] > 4.2 },
        { t: () => 90 - clamp((DOOR.by - lp()[1]) * 12, -30, 30), cap: '摆平，边飞边对准门', sub: '门在 ±20° 内才开', next: () => lp()[0] > DOOR.bx - 2.2 },
        { t: 90, cap: '摆正，冲进门', sub: '', next: () => false },
      ],
    },
    {
      id: '3-3', name: '散云', w: 20, h: 22,
      tip: '云只能托你 1 秒。散之前转走，从两排刺中间的缝里穿过去。',
      bound: { x0: 0, y0: -3, x1: 20, y1: 21.5 },
      isles: [[1, 4, 5, 6], [14, 13, 18, 19]],
      clouds: [{ cx: 9, cy: 4, hx: 0.8, hy: 3 }, { cx: 7.8, cy: 16, hx: 2.2, hy: 0.6 }],
      spikes: [[2, 11.6, 6.6, 12.4], [9, 11.6, 16, 12.4]],
      start: [3, 4.0],
      door: { bx: 14, by: 15, dir: 90 },
      plan: [
        { t: 90, cap: '顺时针转 90°：落到云上', sub: '云只能托你 1 秒', next: () => S.on.has('cloud0') },
        { t: 0, cap: '云要散了！逆时针转回来往下掉', sub: '从两排刺中间的缝里穿过去', react: 0.5, next: () => S.on.has('cloud1') },
        { t: 90, cap: '又一朵会散的云：马上顺时针，冲进门', sub: '', react: 0.5, next: () => false },
      ],
    },
    {
      id: '3-4', name: '气球平台', w: 22, h: 24,
      tip: '平台被气球带着往上飘，撞上刺就爆。爆之前转身飞向门。',
      bound: { x0: 0, y0: -3, x1: 22, y1: 23.5 },
      isles: [[15, 4, 19, 6], [1, 11, 4, 19]],
      plats: [{ cx: 10.8, cy: 18, hx: 1.9, hy: 0.16 }],
      spikes: [[4.6, 10.6, 8.7, 11.4], [12.8, 10.6, 20, 11.4]],
      start: [16, 4.0],
      door: { bx: 4, by: 14.2, dir: -90 },
      plan: [
        { t: -90, cap: '逆时针转 90°：从岛边掉下去', sub: '', next: () => air() && lp()[0] < 14.6 },
        { t: 0, cap: '顺时针转回来：往下掉', sub: '从刺中间的缝里穿过去，落到气球平台上', react: 0.18, next: () => S.on.has('plat0') },
        { t: -90, cap: '气球扎破前，逆时针转 90°：飞向门', sub: '慢了就跟着平台掉进云海', react: 0.9, next: () => air() && lp()[0] < 10.5 },
        { t: () => -90 + clamp((DOOR.by - lp()[1]) * 10, -15, 15), cap: '边飞边微调：对准门', sub: '门在 ±20° 内才开', next: () => lp()[0] < DOOR.bx + 3.5 },
        { t: -90, cap: '摆正，冲进门', sub: '', next: () => false },
      ],
    },
  ];

  // ---------------- current level (rebuilt by setupLevel) ----------------
  let LI = 0, LV = LEVELS[0], MW = 1, MH = 1, PLAN = LV.plan, BOUND = null, DOOR = null;
  let rock = new Uint8Array(1), SEES = [], MINES0 = [], CLOUDS = [], PLATS = [], SPKS = [];
  const TS = 16;
  const solidAt = (cx, cy) => cx >= 0 && cy >= 0 && cx < MW && cy < MH && rock[cy * MW + cx] === 1;
  function setupLevel(i) {
    LI = i; LV = LEVELS[i]; MW = LV.w; MH = LV.h; PLAN = LV.plan; BOUND = LV.bound;
    rock = new Uint8Array(MW * MH);
    for (const [x0, y0, x1, y1] of LV.isles || []) for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) rock[y * MW + x] = 1;
    SEES = LV.sees || []; MINES0 = LV.mines || []; CLOUDS = LV.clouds || []; PLATS = LV.plats || []; SPKS = LV.spikes || [];
    const rot = -LV.door.dir * DEG;
    DOOR = { bx: LV.door.bx, by: LV.door.by, rot, ux: Math.sin(rot), uy: -Math.cos(rot), rx: Math.cos(rot), ry: Math.sin(rot) };
  }

  // ---------------- state ----------------
  const S = {
    mode: 'idle', theta: 0, tx: 0, ty: 0, px: 0, py: 0, vx: 0, vy: 0, grounded: true, airTime: 0, stick: 0, u: 0, uVis: 0, hist: [],
    state: 'play', timer: 0, time: 0, runStart: 0, runTime: 0, step: 0, stepAt: 0, deaths: 0, on: new Set(), leftAt: {},
    anim: 'idle', animT: 0, flash: 0, flashColor: '', shake: 0, slow: 0, near: {},
    fx: [], pops: [], needleA: 0, needleV: 0, unlocked: false, doorF: 0, hint: 0, cam: [0, 0], done: false, dbg: {},
  };
  let O = null;    // moving things: seesaws, mines, clouds, balloon platforms
  function freshWorld() {
    return {
      sees: SEES.map(() => ({ ang: 0, av: 0, st: 'idle', t: 0, side: 1 })),
      mines: MINES0.map(([ax, ay, len]) => ({ bx: ax, by: ay - len, vx: 0, vy: 0, boom: 0 })),
      clouds: CLOUDS.map(() => ({ st: 'solid', t: 0 })),
      plats: PLATS.map(p => ({ cx: p.cx, cy: p.cy, st: 'wait', t: 0, vx: 0, vy: 0, bob: 0 })),
    };
  }
  const prev = { theta: 0, tx: 0, ty: 0, px: 0, py: 0, cam: [0, 0], see: [], plat: [], mines: [] };
  function snapshot() {
    prev.theta = S.theta; prev.tx = S.tx; prev.ty = S.ty; prev.px = S.px; prev.py = S.py; prev.cam = [S.cam[0], S.cam[1]];
    prev.see = O.sees.map(s => s.ang); prev.plat = O.plats.map(p => [p.cx, p.cy]); prev.mines = O.mines.map(m => [m.bx, m.by]);
  }

  // ---------------- transforms ----------------
  function toLevel(wx, wy) { const c = Math.cos(S.theta), s = Math.sin(S.theta), dx = wx - S.tx, dy = wy - S.ty; return [dx * c + dy * s, -dx * s + dy * c]; }
  function toWorld(lx, ly) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [S.tx + lx * c - ly * s, S.ty + lx * s + ly * c]; }
  function vW(x, y) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [x * c - y * s, x * s + y * c]; }
  const lp = () => toLevel(S.px, S.py - PC[0]);
  function doorTilt() { const u = vW(DOOR.ux, DOOR.uy); return Math.acos(clamp(-u[1], -1, 1)) / DEG; }

  // ---------------- collision: tiles + moving boxes (level space) ----------------
  let COLS = [];
  function buildCols() {
    COLS = [];
    SEES.forEach((d, i) => COLS.push({ kind: 'see' + i, cx: d.px, cy: d.py, hx: d.hl, hy: d.ht, ang: d.a0 * DEG + O.sees[i].ang }));
    CLOUDS.forEach((d, i) => { if (O.clouds[i].st !== 'gone') COLS.push({ kind: 'cloud' + i, cx: d.cx, cy: d.cy, hx: d.hx, hy: d.hy, ang: 0 }); });
    PLATS.forEach((d, i) => { const p = O.plats[i]; if (p.st !== 'gone') COLS.push({ kind: 'plat' + i, cx: p.cx, cy: p.cy, hx: d.hx, hy: d.hy, ang: 0 }); });
  }
  // circle vs oriented box: [nx, ny, penetration] in level space, or null
  function boxPush(lx, ly, r, b) {
    const c = Math.cos(b.ang), s = Math.sin(b.ang), dx = lx - b.cx, dy = ly - b.cy;
    const ux = dx * c + dy * s, uy = -dx * s + dy * c;
    const qx = clamp(ux, -b.hx, b.hx), qy = clamp(uy, -b.hy, b.hy), ex = ux - qx, ey = uy - qy, d2 = ex * ex + ey * ey;
    if (d2 >= r * r) return null;
    let nx, ny, pen;
    if (d2 > 1e-9) { const d = Math.sqrt(d2); nx = ex / d; ny = ey / d; pen = r - d; }
    else { const px = b.hx - Math.abs(ux), py = b.hy - Math.abs(uy); if (px < py) { nx = ux < 0 ? -1 : 1; ny = 0; pen = px + r; } else { nx = 0; ny = uy < 0 ? -1 : 1; pen = py + r; } }
    return [nx * c - ny * s, nx * s + ny * c, pen];
  }
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
      for (const b of COLS) {
        const o = boxPush(lx, ly, r, b);
        if (!o) continue;
        lx += o[0] * o[2]; ly += o[1] * o[2]; hit = true;
        if (normals) normals.push([o[0], o[1]]);
      }
      if (!hit) break;
    }
    return [lx, ly];
  }
  // contacts within r: normals (level) plus which moving box they belong to
  function touchingL(lx, ly, r, out) {
    const x0 = Math.floor(lx - r), x1 = Math.floor(lx + r), y0 = Math.floor(ly - r), y1 = Math.floor(ly + r);
    for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) {
      if (!solidAt(cx, cy)) continue;
      const qx = clamp(lx, cx, cx + 1), qy = clamp(ly, cy, cy + 1);
      const dx = lx - qx, dy = ly - qy, d2 = dx * dx + dy * dy;
      if (d2 >= r * r || d2 < 1e-9) continue;
      const d = Math.sqrt(d2); out.push({ n: [dx / d, dy / d], kind: 'tile' });
    }
    for (const b of COLS) { const o = boxPush(lx, ly, r, b); if (o) out.push({ n: [o[0], o[1]], kind: b.kind }); }
  }
  function resolveW(wx, wy, r, normals) {
    const [lx, ly] = toLevel(wx, wy), ns = [];
    const [nx, ny] = resolveCircleL(lx, ly, r, ns);
    for (const n of ns) normals.push(vW(n[0], n[1]));
    return vW(nx - lx, ny - ly);
  }
  function contactsNow() {
    const cs = [];
    for (const o of PC) { const [lx, ly] = toLevel(S.px, S.py - o); touchingL(lx, ly, PR + SKIN, cs); }
    return cs.map(c => ({ n: vW(c.n[0], c.n[1]), kind: c.kind }));
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
    S.stick = 0; S.u = 0; S.hist = []; S.airTime = 0; S.state = intro ? 'intro' : 'play'; S.timer = intro ? P.intro : 0; S.on = new Set(); S.leftAt = {};
    S.anim = 'idle'; S.animT = 0; S.flash = 0; S.shake = 0; S.slow = 0; S.near = {}; S.fx = []; S.pops = []; S.toast = null;
    S.needleA = 0; S.needleV = 0; S.doorF = 0; S.hint = 0;
    S.step = 0; S.stepAt = S.time; S.done = false;
    const w = toWorld(LV.start[0], LV.start[1]); S.px = w[0]; S.py = w[1];
    O = freshWorld();
    buildCols();
    S.unlocked = doorTilt() <= 20;
    S.cam = camTarget();
    S.runStart = S.time + (intro ? P.intro : 0); S.runTime = 0;
    snapshot();
  }
  function loadLevel(i) {
    setupLevel(i);
    if (IMG.door) lvlCv = buildLevel();
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
    const c = toWorld((BOUND.x0 + BOUND.x1) / 2, (BOUND.y0 + BOUND.y1) / 2), f = 0.4;
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
      const b = S.time - S.stepAt < react && S.step > 0 ? PLAN[S.step - 1] : PLAN[S.step];
      const tgt = typeof b.t === 'function' ? b.t() : b.t;
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
      stepSees(dt); stepMines(dt); stepClouds(dt); stepPlats(dt);
      buildCols();
    }
    if (live) {
      stepPlayer(dt);
      checkHazards();
      advancePlan();
      if (!S.done) S.runTime = S.time - S.runStart;
    }
    updateDoor(dt);
    stepFx(dt);
    const tgt = camTarget(), kc = 1 - Math.exp(-6 * dt);
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
    S.vy = Math.min(S.vy + G * dt, P.vmax);
    project(contactsNow().map(c => c.n));
    for (let k = 0; k < SUB; k++) {
      S.px += S.vx * dt / SUB; S.py += S.vy * dt / SUB;
      const ns = [];
      for (const o of [PC[0], PC[1], PC[0]]) { const cr = resolveW(S.px, S.py - o, PR, ns); S.px += cr[0]; S.py += cr[1]; }
      project(ns);
    }
    const after = contactsNow();
    project(after.map(c => c.n));
    const feet = after.filter(c => c.n[1] < -0.5);
    const grounded = feet.length > 0;
    const on = new Set(feet.map(c => c.kind));
    for (const k of S.on) if (!on.has(k)) S.leftAt[k] = S.time;
    S.on = on;
    if (grounded && !S.grounded && S.airTime > 0.18) { S.anim = 'land'; S.animT = 0; if (S.airTime > 0.5) puff(lp(), 6, 'dust'); }
    if (!grounded && S.grounded) { S.anim = 'fallstart'; S.animT = 0; }
    S.airTime = grounded ? 0 : S.airTime + dt;
    S.grounded = grounded;
  }

  // seesaw: whichever side you land on drops (toward the current down), steeply enough to pour you off
  function stepSees(dt) {
    const gx = Math.sin(S.theta), gy = Math.cos(S.theta);
    O.sees.forEach((s, i) => {
      const d = SEES[i];
      if (s.st === 'idle') {
        if (S.on.has('see' + i)) {
          const l = lp(), rx = l[0] - d.px, ry = l[1] - d.py;
          s.side = rx * gy - ry * gx >= 0 ? 1 : -1;
          s.st = 'load'; s.t = 0.15;
        }
      } else if (s.st === 'load') { s.t -= dt; S.shake = Math.max(S.shake, 0.05); if (s.t <= 0) { s.st = 'tip'; s.av = 0; pop('咔！'); } }
      else if (s.st === 'tip') {
        s.av += s.side * 16 * dt; s.ang += s.av * dt;
        if (Math.abs(s.ang) >= 82 * DEG) { s.ang = s.side * 82 * DEG; s.av = 0; s.st = 'tipped'; s.t = 1.6; S.shake = Math.max(S.shake, 0.2); }
      } else if (s.st === 'tipped') { s.t -= dt; if (s.t <= 0) s.st = 'back'; }
      else if (s.st === 'back') {
        const dd = -s.ang, st = Math.sign(dd) * Math.min(Math.abs(dd), 1.4 * dt);
        s.ang += st; if (Math.abs(s.ang) < 1e-4) { s.ang = 0; s.st = 'idle'; }
      }
    });
  }
  // tethered balloon mines float to the "up" side of their anchor, so every turn swings the whole field
  function stepMines(dt) {
    const gx = Math.sin(S.theta), gy = Math.cos(S.theta);
    O.mines.forEach((m, i) => {
      if (m.boom) { m.boom += dt; return; }
      const [ax, ay, len] = MINES0[i];
      m.vx += -gx * P.mineLift * dt; m.vy += -gy * P.mineLift * dt;
      m.vx *= 1 - 3.5 * dt; m.vy *= 1 - 3.5 * dt;
      m.bx += m.vx * dt; m.by += m.vy * dt;
      const dx = m.bx - ax, dy = m.by - ay, d = Math.hypot(dx, dy);
      if (d > len) {
        const nx = dx / d, ny = dy / d;
        m.bx = ax + nx * len; m.by = ay + ny * len;
        const vr = m.vx * nx + m.vy * ny; if (vr > 0) { m.vx -= vr * nx; m.vy -= vr * ny; }
      }
    });
  }
  // a cloud holds you for one second, then it is gone for a while
  function stepClouds(dt) {
    O.clouds.forEach((c, i) => {
      const d = CLOUDS[i], k = 'cloud' + i;
      if (c.st === 'solid') { if (S.on.has(k)) { c.st = 'fade'; c.t = P.cloudTime; pop('云要散了！'); } }
      else if (c.st === 'fade') {
        c.t -= dt;
        if (c.t <= 0) {
          c.st = 'gone'; c.t = 3; if (S.time - (S.leftAt[k] ?? -9) < 0.45) nearMiss('cloud');
          for (let j = 0; j < 26; j++) S.fx.push({ x: d.cx + (Math.random() - 0.5) * d.hx * 2.4, y: d.cy + (Math.random() - 0.5) * d.hy * 2, vx: (Math.random() - 0.5) * 2, vy: (Math.random() - 0.5) * 2, life: 1, max: 1, k: 'cloud', s: 0.5 + Math.random() * 0.6 });
        }
      } else if (c.st === 'gone') { c.t -= dt; if (c.t <= 0) c.st = 'solid'; }
    });
  }
  // balloon platform: carries you up toward the spikes; the balloons pop when they reach them
  const BALLOONS = [[-0.9, 2.4], [0, 2.75], [0.9, 2.35]];
  function balloonSpots(cx, cy, gx, gy) { const ux = -gx, uy = -gy, sx = -uy, sy = ux; return BALLOONS.map(([o, h]) => [cx + sx * o + ux * h, cy + sy * o + uy * h]); }
  function spikeDist(x, y) { let m = 99; for (const k of SPKS) { const qx = clamp(x, k[0], k[2]), qy = clamp(y, k[1], k[3]); m = Math.min(m, Math.hypot(x - qx, y - qy)); } return m; }
  function stepPlats(dt) {
    const gx = Math.sin(S.theta), gy = Math.cos(S.theta);
    O.plats.forEach((p, i) => {
      const d = PLATS[i], k = 'plat' + i;
      if (p.st === 'wait') { p.bob += dt; p.cy = d.cy + Math.sin(p.bob * 2.2) * 0.06; if (S.on.has(k)) { p.st = 'rise'; p.t = 0; pop('被气球带上去了'); } }
      else if (p.st === 'rise') {
        p.t += dt;
        p.cx += -gx * P.riseSpeed * dt; p.cy += -gy * P.riseSpeed * dt;
        const bs = balloonSpots(p.cx, p.cy, gx, gy), bx = bs[1][0], by = bs[1][1];
        const gap = Math.min(...bs.map(b => spikeDist(b[0], b[1]))) - 0.55;
        dbgMin('balloon', gap);
        if (gap < 0.45 && gap >= 0 && (S.on.has(k) || S.time - (S.leftAt[k] ?? -9) < 0.5)) nearMiss('pop');
        if (gap < 0 || p.t > 4) {
          p.st = 'popped'; p.vx = 0; p.vy = 0; pop('啪！'); S.shake = Math.max(S.shake, 0.3);
          if (S.time - (S.leftAt[k] ?? -9) < 0.45) nearMiss('pop');
          for (let j = 0; j < 18; j++) S.fx.push({ x: bx + (Math.random() - 0.5) * 1.4, y: by + (Math.random() - 0.5) * 1.0, vx: (Math.random() - 0.5) * 6, vy: (Math.random() - 0.5) * 6, life: 0.7, max: 0.7, k: 'shred', s: 0.14, grav: 0.5 });
        }
      } else if (p.st === 'popped') {
        p.vx += gx * G * dt; p.vy += gy * G * dt; p.cx += p.vx * dt; p.cy += p.vy * dt;
        if (p.cx < BOUND.x0 - 6 || p.cx > BOUND.x1 + 6 || p.cy < BOUND.y0 - 6 || p.cy > BOUND.y1 + 6) p.st = 'gone';
      }
    });
  }

  function die(msg) {
    if (S.state !== 'play') return;
    S.state = 'dead'; S.timer = 0.9; S.flash = 0.5; S.flashColor = 'rgba(230,80,90,'; S.deaths++;
    S.shake = Math.max(S.shake, 0.4);
    toast(msg, 'dead');
  }
  function clear() {
    if (S.state !== 'play') return;
    S.state = 'clear'; S.timer = 2.0; S.flash = 0.45; S.flashColor = 'rgba(255,255,255,'; S.doorF = 0; S.done = true;
    S.runTime = S.time - S.runStart;
    toast(LV.id + ' 通关！' + S.runTime.toFixed(1) + ' 秒', 'clear');
  }
  function nearMiss(kind) {
    if (S.time - (S.near[kind] ?? -9) < 1.4) return;
    S.near[kind] = S.time;
    pop('险！', true);
    if (P.slowmo && (!window.__skyHeadless || window.__skyVideo)) S.slow = 0.42;
  }

  const dbgMin = (k, v) => { S.dbg[k] = Math.min(S.dbg[k] ?? 99, v); };
  function checkHazards() {
    if (S.state !== 'play') return;
    const cs = PC.map(o => toLevel(S.px, S.py - o));
    // the cloud sea around the map
    for (const c of cs) {
      const m = Math.min(c[0] - BOUND.x0, BOUND.x1 - c[0], c[1] - BOUND.y0, BOUND.y1 - c[1]);
      dbgMin('void' + S.step, m);
      if (m < 0) return die('掉进云海了');
      if (m < 2.2 && S.vy > 3) nearMiss('void');
    }
    // balloon mines
    for (let i = 0; i < O.mines.length; i++) {
      const m = O.mines[i];
      if (m.boom) continue;
      for (const c of cs) {
        const d = Math.hypot(c[0] - m.bx, c[1] - m.by) - PR - 0.42;
        dbgMin('mine' + S.step, d);
        if (d < -0.1) { m.boom = 0.001; boom(m); return die('撞上气球雷了'); }
        if (d < 1.15) nearMiss('mine');
      }
    }
    // spiked logs
    for (const c of cs) for (const k of SPKS) {
      const qx = clamp(c[0], k[0], k[2]), qy = clamp(c[1], k[1], k[3]), d = Math.hypot(c[0] - qx, c[1] - qy) - PR;
      dbgMin('spike' + S.step, d);
      if (d < -0.05) return die('被刺扎到了');
      if (d < 0.75) nearMiss('spike');
    }
    // door
    const c = cs[0], dx = c[0] - DOOR.bx, dy = c[1] - DOOR.by;
    const u = dx * DOOR.rx + dy * DOOR.ry, v = dx * DOOR.ux + dy * DOOR.uy;
    if (Math.abs(u) < 0.95 && v > -0.2 && v < 2.0) { if (S.unlocked) clear(); else S.hint = 1; }
  }
  function boom(m) {
    S.shake = Math.max(S.shake, 0.5);
    for (let i = 0; i < 22; i++) S.fx.push({ x: m.bx, y: m.by, vx: (Math.random() - 0.5) * 9, vy: (Math.random() - 0.5) * 9, life: 0.6, max: 0.6, k: 'spark', s: 0.12 });
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
  function puff(at, n, k) {
    for (let i = 0; i < n; i++) S.fx.push({ x: at[0] + (Math.random() - 0.5) * 0.8, y: at[1] + (Math.random() - 0.5) * 0.8, vx: (Math.random() - 0.5) * 1.6, vy: (Math.random() - 0.5) * 1.6, life: 0.9, max: 0.9, k, s: 0.18 + Math.random() * 0.22 });
  }
  function pop(text, big) { S.pops.push({ text, t: 0, big: !!big }); if (S.pops.length > 3) S.pops.shift(); }
  function stepFx(dt) {
    const gx = Math.sin(S.theta), gy = Math.cos(S.theta);
    // wind streaks while you fall fast
    if (S.mode !== 'idle' && S.vy > 6 && Math.random() < 0.6) {
      const l = lp();
      S.fx.push({ x: l[0] + (Math.random() - 0.5) * 8, y: l[1] + (Math.random() - 0.5) * 8, vx: -gx * 4, vy: -gy * 4, life: 0.4, max: 0.4, k: 'wind', s: 0.06 });
    }
    for (const p of S.fx) {
      p.life -= dt;
      const g = p.grav ? G * p.grav : 0;
      p.vx += gx * g * dt; p.vy += gy * g * dt;
      if (p.k === 'dust' || p.k === 'cloud') { p.vx *= 1 - 1.5 * dt; p.vy *= 1 - 1.5 * dt; }
      p.x += p.vx * dt; p.y += p.vy * dt;
    }
    S.fx = S.fx.filter(p => p.life > 0);
    if (S.fx.length > 300) S.fx.splice(0, S.fx.length - 300);
    for (const p of S.pops) p.t += dt;
    S.pops = S.pops.filter(p => p.t < (p.big ? 0.9 : 0.8));
    S.shake *= Math.exp(-6 * dt);
    if (S.flash > 0) S.flash = Math.max(0, S.flash - dt);
  }

  // ---------------- art ----------------
  const COL = { grass: [118, 196, 92], grassHi: [168, 226, 120], grassLo: [78, 150, 70], dirt: [150, 104, 70], dirtLo: [112, 76, 54] };
  const put = (d, i, c) => { d[i] = c[0]; d[i + 1] = c[1]; d[i + 2] = c[2]; d[i + 3] = 255; };
  const shade = (c, k) => [clamp(c[0] + k, 0, 255), clamp(c[1] + k, 0, 255), clamp(c[2] + k, 0, 255)];
  function buildLevel() {
    const W_ = MW * TS, H_ = MH * TS, cv = mk(W_, H_), g = cv.getContext('2d');
    const img = g.createImageData(W_, H_), d = img.data;
    for (let y = 0; y < H_; y++) for (let x = 0; x < W_; x++) {
      const cx = (x / TS) | 0, cy = (y / TS) | 0, i = (y * W_ + x) * 4;
      if (!solidAt(cx, cy)) { d[i + 3] = 0; continue; }
      const n = hash(x >> 1, y >> 1);
      const fy = y - cy * TS;
      let col;
      if (!solidAt(cx, cy - 1) && fy < 5) col = fy < 2 ? COL.grassHi : COL.grass;
      else if (!solidAt(cx, cy - 1) && fy < 7 && hash(x, cy) > 0.45) col = COL.grassLo;
      else col = (cy % 2 === 0 ? n > 0.5 : n > 0.4) ? COL.dirt : COL.dirtLo;
      if (n > 0.95) col = shade(col, 14);
      if ((!solidAt(cx - 1, cy) && x - cx * TS < 2) || (!solidAt(cx + 1, cy) && (cx + 1) * TS - 1 - x < 2)) col = shade(col, -18);
      put(d, i, col);
    }
    g.putImageData(img, 0, 0);
    // hanging roots under each island
    g.save(); g.scale(TS, TS);
    for (let cx = 0; cx < MW; cx++) for (let cy = 0; cy < MH; cy++) {
      if (!solidAt(cx, cy) || solidAt(cx, cy + 1)) continue;
      const k = (hash(cx, cy) * 3) | 0;
      for (let j = 0; j < k; j++) {
        const rx = cx + 0.15 + hash(cx * 3 + j, cy) * 0.7, rl = 0.3 + hash(cx, cy * 5 + j) * 0.6, bend = (hash(cx + j, cy * 2) - 0.5) * 0.3;
        g.strokeStyle = '#8a5a34'; g.lineWidth = 0.09; g.lineCap = 'round';
        g.beginPath(); g.moveTo(rx, cy + 1); g.quadraticCurveTo(rx + bend, cy + 1 + rl * 0.5, rx - bend * 0.5, cy + 1 + rl); g.stroke();
        g.fillStyle = '#76c45c'; g.fillRect(rx - bend * 0.5 - 0.05, cy + 1 + rl - 0.05, 0.1, 0.1);
      }
    }
    // spiked logs
    for (const [x0, y0, x1, y1] of SPKS) {
      const my = (y0 + y1) / 2;
      for (let x = x0 + 0.05; x < x1 - 0.2; x += 0.42) for (const dir of [-1, 1]) {
        g.fillStyle = dir < 0 ? '#d7dce6' : '#9aa1ae';
        g.beginPath(); g.moveTo(x, my); g.lineTo(x + 0.36, my); g.lineTo(x + 0.18, dir < 0 ? y0 - 0.05 : y1 + 0.05); g.closePath(); g.fill();
      }
      g.fillStyle = '#7a5532'; g.fillRect(x0, my - 0.2, x1 - x0, 0.4);
      g.fillStyle = '#9c7046'; g.fillRect(x0, my - 0.2, x1 - x0, 0.1);
      g.fillStyle = '#5e3f24'; g.beginPath(); g.ellipse(x0, my, 0.12, 0.2, 0, 0, Math.PI * 2); g.ellipse(x1, my, 0.12, 0.2, 0, 0, Math.PI * 2); g.fill();
    }
    g.restore();
    return cv;
  }
  // soft cloud blobs drawn in level space
  const DECO = [];
  for (let i = 0; i < 22; i++) DECO.push([hash(i, 1) * 44 - 10, hash(i, 2) * 36 - 10, 1.2 + hash(i, 3) * 2.4, 0.18 + hash(i, 4) * 0.22]);
  function cloudBlob(g, x, y, w, h, a) {
    g.globalAlpha = a;
    g.beginPath();
    g.ellipse(x, y, w, h, 0, 0, Math.PI * 2);
    g.ellipse(x - w * 0.55, y + h * 0.25, w * 0.6, h * 0.75, 0, 0, Math.PI * 2);
    g.ellipse(x + w * 0.55, y + h * 0.2, w * 0.65, h * 0.8, 0, 0, Math.PI * 2);
    g.fill();
    g.globalAlpha = 1;
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
    const span = Math.max(BOUND.x1 - BOUND.x0, BOUND.y1 - BOUND.y0);
    K = Math.min(H / (span + 2), W / (span + 7)); CX = W / 2; CY = H * 0.53;
    const lp_ = (a, b) => a + (b - a) * alpha;
    const th = lp_(prev.theta, S.theta), Tx = lp_(prev.tx, S.tx), Ty = lp_(prev.ty, S.ty);
    const Px = lp_(prev.px, S.px), Py = lp_(prev.py, S.py);
    let camx = lp_(prev.cam[0], S.cam[0]), camy = lp_(prev.cam[1], S.cam[1]);
    if (S.shake > 0.01 && !REDUCED) { camx += (Math.random() - 0.5) * S.shake * 0.9; camy += (Math.random() - 0.5) * S.shake * 0.9; }
    const c = Math.cos(th), s = Math.sin(th);
    const ox = CX + K * (Tx - camx), oy = CY + K * (Ty - camy);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = '#9fd3f5'; ctx.fillRect(0, 0, W, H);
    ctx.imageSmoothingEnabled = false;
    ctx.setTransform(K * c, K * s, -K * s, K * c, ox, oy);
    // sky: high up is deep blue, the cloud sea below is warm and bright
    const sky = ctx.createLinearGradient(0, BOUND.y0 - 10, 0, BOUND.y1 + 6);
    sky.addColorStop(0, '#4f9fe0'); sky.addColorStop(0.55, '#a9daf6'); sky.addColorStop(0.85, '#ffe9cf'); sky.addColorStop(1, '#fff4e4');
    ctx.fillStyle = sky; ctx.fillRect(-60, -60, MW + 120, MH + 120);
    ctx.fillStyle = '#ffffff';
    for (const [x, y, w, a] of DECO) cloudBlob(ctx, x, y, w, w * 0.45, a);
    // the cloud sea: a thick cloud wall all around the playable box, with a soft shadow line where it begins
    const B = BOUND;
    ctx.fillStyle = 'rgba(110,140,185,0.22)';
    ctx.fillRect(-60, B.y1 + 0.2, MW + 120, 1.0); ctx.fillRect(B.x0 - 1.2, -60, 1.0, MH + 120); ctx.fillRect(B.x1 + 0.2, -60, 1.0, MH + 120); ctx.fillRect(-60, B.y0 - 1.2, MW + 120, 1.0);
    const edge = (x, y, w, h) => { ctx.globalAlpha = 0.94; ctx.fillRect(x, y, w, h); ctx.globalAlpha = 1; };
    ctx.fillStyle = '#eef2f9';
    edge(-60, B.y1 + 1.2, MW + 120, 60); edge(-60, -60, 60 + B.x0 - 1.2, MH + 120); edge(B.x1 + 1.2, -60, 60, MH + 120); edge(-60, -60, MW + 120, 60 + B.y0 - 1.2);
    ctx.fillStyle = '#f7f9fd';
    for (let x = B.x0 - 2; x < B.x1 + 2; x += 1.6) { cloudBlob(ctx, x, B.y1 + 1.4 + hash(x, 7) * 0.4, 1.3, 0.8, 0.95); cloudBlob(ctx, x, B.y0 - 1.4 - hash(x, 6) * 0.4, 1.3, 0.8, 0.95); }
    for (let y = B.y0 - 2; y < B.y1 + 2; y += 1.6) { cloudBlob(ctx, B.x0 - 1.4 - hash(y, 8) * 0.4, y, 0.9, 1.1, 0.95); cloudBlob(ctx, B.x1 + 1.4 + hash(y, 9) * 0.4, y, 0.9, 1.1, 0.95); }
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
    // seesaws: plank on a stone hub
    SEES.forEach((d, i) => {
      const sa = d.a0 * DEG + lp_(prev.see[i] ?? O.sees[i].ang, O.sees[i].ang);
      ctx.save(); ctx.translate(d.px, d.py);
      ctx.fillStyle = '#ffffff'; cloudBlob(ctx, 0, 0.9, 1.5, 0.7, 0.85);
      ctx.rotate(sa);
      if (O.sees[i].st === 'load') ctx.translate((Math.random() - 0.5) * 0.06, 0);
      ctx.fillStyle = '#9a6a3c'; ctx.fillRect(-d.hl, -d.ht, d.hl * 2, d.ht * 2);
      ctx.fillStyle = '#c38c52'; ctx.fillRect(-d.hl, -d.ht, d.hl * 2, 0.08);
      ctx.fillStyle = '#6e4826'; for (let k = -2; k <= 2; k++) ctx.fillRect(k * 1.05 - 0.03, -d.ht, 0.06, d.ht * 2);
      ctx.rotate(-sa);
      ctx.fillStyle = '#7d7684'; ctx.beginPath(); ctx.arc(0, 0, 0.32, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#b4adbb'; ctx.beginPath(); ctx.arc(-0.08, -0.08, 0.12, 0, Math.PI * 2); ctx.fill();
      ctx.restore();
    });
    // cloud platforms
    CLOUDS.forEach((d, i) => {
      const cl = O.clouds[i];
      if (cl.st === 'gone') return;
      const fade = cl.st === 'fade' ? clamp(cl.t / P.cloudTime, 0, 1) : 1;
      ctx.save(); ctx.translate(d.cx, d.cy);
      if (cl.st === 'fade') ctx.translate((Math.random() - 0.5) * 0.08 * (1 - fade), (Math.random() - 0.5) * 0.08 * (1 - fade));
      ctx.fillStyle = '#ffffff';
      if (d.hy > d.hx) { for (let k = 0; k < 5; k++) { const yy = -d.hy + 0.6 + k * (d.hy * 2 - 1.2) / 4; cloudBlob(ctx, 0, yy, d.hx * 1.2, 0.95, 0.35 + 0.6 * fade); } }
      else { for (let k = 0; k < 4; k++) { const xx = -d.hx + 0.5 + k * (d.hx * 2 - 1.0) / 3; cloudBlob(ctx, xx, 0, 0.95, d.hy * 1.3, 0.35 + 0.6 * fade); } }
      ctx.globalAlpha = 0.5 + 0.5 * fade; ctx.fillStyle = '#ffffff'; ctx.fillRect(-d.hx, -d.hy + 0.15, d.hx * 2, d.hy * 2 - 0.3); ctx.globalAlpha = 1;
      ctx.restore();
    });
    // balloon platforms: board hanging under three balloons that always pull toward screen-up
    PLATS.forEach((d, i) => {
      const pl = O.plats[i];
      if (pl.st === 'gone') return;
      const pp = prev.plat[i] || [pl.cx, pl.cy], pcx = lp_(pp[0], pl.cx), pcy = lp_(pp[1], pl.cy);
      const up = [-Math.sin(th), -Math.cos(th)];
      ctx.save(); ctx.translate(pcx, pcy);
      if (pl.st !== 'popped') {
        const side = [-up[1], up[0]];
        BALLOONS.forEach(([o, h], k) => {
          const bx = side[0] * o + up[0] * h, by = side[1] * o + up[1] * h;
          ctx.strokeStyle = 'rgba(80,70,70,0.8)'; ctx.lineWidth = 0.04; ctx.beginPath(); ctx.moveTo(o * 0.8, -d.hy); ctx.lineTo(bx, by); ctx.stroke();
          ctx.fillStyle = ['#ff7a8a', '#ffd166', '#6fd3e8'][k]; ctx.beginPath(); ctx.arc(bx, by, 0.55, 0, Math.PI * 2); ctx.fill();
          ctx.fillStyle = 'rgba(255,255,255,0.6)'; ctx.beginPath(); ctx.arc(bx - 0.18, by - 0.18, 0.15, 0, Math.PI * 2); ctx.fill();
        });
      }
      ctx.fillStyle = '#9a6a3c'; ctx.fillRect(-d.hx, -d.hy, d.hx * 2, d.hy * 2);
      ctx.fillStyle = '#c38c52'; ctx.fillRect(-d.hx, -d.hy, d.hx * 2, 0.08);
      ctx.restore();
    });
    // balloon mines on their strings
    O.mines.forEach((m, i) => {
      const [ax, ay] = MINES0[i], pm = prev.mines[i] || [m.bx, m.by];
      const bx = lp_(pm[0], m.bx), by = lp_(pm[1], m.by);
      ctx.fillStyle = '#6b6577'; ctx.fillRect(ax - 0.18, ay - 0.12, 0.36, 0.24);
      if (m.boom) return;
      ctx.strokeStyle = 'rgba(70,60,70,0.75)'; ctx.lineWidth = 0.04; ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke();
      ctx.save(); ctx.translate(bx, by);
      ctx.fillStyle = '#3d3a46';
      for (let k = 0; k < 8; k++) { const a = k / 8 * Math.PI * 2 + S.time * 0.6; ctx.beginPath(); ctx.moveTo(Math.cos(a) * 0.3, Math.sin(a) * 0.3); ctx.lineTo(Math.cos(a + 0.2) * 0.62, Math.sin(a + 0.2) * 0.62); ctx.lineTo(Math.cos(a + 0.4) * 0.3, Math.sin(a + 0.4) * 0.3); ctx.fill(); }
      ctx.fillStyle = '#e04f5f'; ctx.beginPath(); ctx.arc(0, 0, 0.42, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = 'rgba(255,255,255,0.55)'; ctx.beginPath(); ctx.arc(-0.13, -0.14, 0.12, 0, Math.PI * 2); ctx.fill();
      ctx.restore();
    });
    // particles
    for (const p of S.fx) {
      const a = clamp(p.life / p.max, 0, 1);
      if (p.k === 'wind') { ctx.globalAlpha = a * 0.5; ctx.fillStyle = '#ffffff'; ctx.fillRect(p.x - 0.03, p.y - 0.3, 0.06, 0.6); continue; }
      ctx.globalAlpha = p.k === 'cloud' ? a * 0.8 : a;
      ctx.fillStyle = p.k === 'spark' ? '#ffb347' : p.k === 'shred' ? '#ff7a8a' : p.k === 'cloud' ? '#ffffff' : '#c2a78a';
      const sz = p.k === 'dust' || p.k === 'cloud' ? p.s * (1.8 - a * 0.8) : p.s;
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
    if (spd > 7) {
      ctx.strokeStyle = 'rgba(255,255,255,0.55)'; ctx.lineWidth = Math.max(1, K * 0.035);
      const n = Math.min(14, Math.floor((spd - 6.5) * 2.4));
      for (let k = 0; k < n; k++) {
        const h1 = hash(k * 3.1, Math.floor(S.time * 14) + k), h2 = hash(k * 7.7, Math.floor(S.time * 14) - k);
        const off = (h1 - 0.5) * K * 7, along = (h2 - 0.2) * K * 6, len = K * (1.2 + spd * 0.12);
        const x0 = pS[0] + off, y0 = pS[1] - K * 0.8 - along;
        ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x0, y0 - len); ctx.stroke();
      }
    }
    // the cloud sea glows red when you get close to it
    const l = lp(), m = Math.min(l[0] - BOUND.x0, BOUND.x1 - l[0], l[1] - BOUND.y0, BOUND.y1 - l[1]);
    if (m < 3 && S.state === 'play' && S.mode !== 'idle') {
      const a = clamp((3 - m) / 3, 0, 1) * 0.45;
      const vg = ctx.createRadialGradient(CX, CY, H * 0.3, CX, CY, H * 0.9);
      vg.addColorStop(0, 'rgba(230,60,80,0)'); vg.addColorStop(1, `rgba(230,60,80,${a.toFixed(3)})`);
      ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H);
    }
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
      ctx.lineWidth = 4 * u; ctx.strokeStyle = 'rgba(20,40,70,0.75)';
      ctx.fillStyle = p.big ? '#ffd166' : '#ffffff';
      const x = pS[0] + (p.big ? K * 1.6 : 0), y = pS[1] - K * 2.4 - rise;
      ctx.strokeText(p.text, x, y); ctx.fillText(p.text, x, y);
    }
    ctx.globalAlpha = 1;
    if (S.toast && S.time - S.toast.t < (S.toast.kind === 'clear' ? 2.0 : 0.9)) {
      const a = clamp((S.time - S.toast.t) / 0.15, 0, 1);
      ctx.globalAlpha = a; ctx.textAlign = 'center';
      ctx.font = `600 ${Math.round(34 * u)}px "Noto Serif SC", serif`;
      ctx.lineWidth = 6 * u; ctx.strokeStyle = 'rgba(20,40,70,0.8)';
      ctx.fillStyle = S.toast.kind === 'clear' ? '#ffffff' : '#ff8a96';
      ctx.strokeText(S.toast.text, CX, CY - H * 0.18); ctx.fillText(S.toast.text, CX, CY - H * 0.18);
      ctx.globalAlpha = 1;
    }
    // level badge, top-left, on a pill so it reads on the bright sky
    ctx.font = `600 ${Math.round(16 * u)}px "Noto Serif SC", serif`;
    const bw = ctx.measureText(LV.id + '  ' + LV.name).width + 24 * u;
    ctx.fillStyle = 'rgba(22,48,84,0.72)'; rr(8 * u, 8 * u, bw, 28 * u, 14 * u); ctx.fill();
    ctx.textAlign = 'left'; ctx.fillStyle = '#ffd166'; ctx.fillText(LV.id, 20 * u, 28 * u);
    ctx.fillStyle = '#ffffff'; ctx.fillText(LV.name, 20 * u + ctx.measureText(LV.id + '  ').width, 28 * u);
    if (S.state === 'intro') { drawIntro(u); return; }
    if (S.mode === 'idle') return;
    const st = PLAN[S.step], age = S.time - S.stepAt, ap = clamp(age / 0.22, 0, 1);
    if (S.state === 'play' || S.state === 'dead') {
      const bw2 = Math.min(W * 0.6, 560 * u), bh = (st.sub ? 64 : 46) * u, bx = CX - bw2 / 2, by = 12 * u - (1 - ap) * 10 * u;
      ctx.globalAlpha = 0.25 + 0.75 * ap;
      ctx.fillStyle = 'rgba(22,48,84,0.78)'; rr(bx, by, bw2, bh, 10 * u); ctx.fill();
      ctx.strokeStyle = 'rgba(255,255,255,0.5)'; ctx.lineWidth = 1.5 * u; rr(bx, by, bw2, bh, 10 * u); ctx.stroke();
      ctx.textAlign = 'left'; ctx.fillStyle = '#ffd166'; ctx.font = `600 ${Math.round(13 * u)}px ui-monospace, Menlo, monospace`;
      ctx.fillText(`第 ${S.step + 1} 拍`, bx + 14 * u, by + 28 * u);
      ctx.textAlign = 'center'; ctx.fillStyle = '#ffffff'; ctx.font = `600 ${Math.round(20 * u)}px "Noto Serif SC", serif`;
      ctx.fillText(st.cap, CX + 22 * u, by + 30 * u);
      if (st.sub) { ctx.fillStyle = '#d4e6f7'; ctx.font = `${Math.round(14 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`; ctx.fillText(st.sub, CX + 22 * u, by + 52 * u); }
      ctx.globalAlpha = 1;
    }
    ctx.textAlign = 'left'; ctx.fillStyle = '#163054'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText(S.runTime.toFixed(1) + ' s', 14 * u, H - 18 * u);
    ctx.fillStyle = S.mode === 'demo' ? '#c2410c' : '#41587a'; ctx.font = `${Math.round(12 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.mode === 'demo' ? '演示中' : '自己玩', 14 * u, H - 38 * u);
    const n = PLAN.length, gap = 26 * u, tw = (n - 1) * gap, tx0 = CX - tw / 2, ty = H - 26 * u;
    ctx.fillStyle = 'rgba(22,48,84,0.55)'; rr(tx0 - 16 * u, ty - 14 * u, tw + 32 * u, 28 * u, 14 * u); ctx.fill();
    ctx.strokeStyle = 'rgba(255,255,255,0.4)'; ctx.lineWidth = 2 * u; ctx.beginPath(); ctx.moveTo(tx0, ty); ctx.lineTo(tx0 + tw, ty); ctx.stroke();
    for (let i = 0; i < n; i++) {
      const x = tx0 + i * gap, done = i < S.step || S.state === 'clear' || S.state === 'over', cur = i === S.step && S.state === 'play';
      ctx.beginPath(); ctx.arc(x, ty, (cur ? 7 + Math.sin(S.time * 10) * 1.5 : 5) * u, 0, Math.PI * 2);
      ctx.fillStyle = done ? '#f59e0b' : cur ? '#ffffff' : 'rgba(22,48,84,0.9)'; ctx.fill();
      ctx.strokeStyle = done || cur ? '#ffd166' : 'rgba(255,255,255,0.6)'; ctx.lineWidth = 1.5 * u; ctx.stroke();
    }
    const wx = W - 52 * u, wy = H - 52 * u, wr = 30 * u;
    S.uVis += (S.u - S.uVis) * 0.25;
    ctx.save(); ctx.translate(wx, wy);
    ctx.fillStyle = 'rgba(22,48,84,0.7)'; ctx.beginPath(); ctx.arc(0, 0, wr + 10 * u, 0, Math.PI * 2); ctx.fill();
    const full = Math.abs(S.u) > 0.95;
    if (Math.abs(S.uVis) > 0.05) {
      ctx.strokeStyle = full ? '#ffd166' : 'rgba(255,209,102,0.6)'; ctx.lineWidth = 4 * u; ctx.beginPath();
      ctx.arc(0, 0, wr + 5 * u, -Math.PI / 2, -Math.PI / 2 + S.uVis * 1.4, S.uVis < 0); ctx.stroke();
    }
    ctx.rotate(S.uVis * 75 * DEG);
    ctx.strokeStyle = '#ffffff'; ctx.lineWidth = 4 * u; ctx.beginPath(); ctx.arc(0, 0, wr, 0, Math.PI * 2); ctx.stroke();
    ctx.lineWidth = 3 * u; ctx.beginPath(); ctx.moveTo(-wr, 0); ctx.lineTo(wr, 0); ctx.moveTo(0, 0); ctx.lineTo(0, wr); ctx.stroke();
    ctx.fillStyle = '#ffd166'; ctx.beginPath(); ctx.arc(0, -wr, 4 * u, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#ffffff'; ctx.beginPath(); ctx.arc(0, 0, 6 * u, 0, Math.PI * 2); ctx.fill();
    ctx.restore();
    ctx.fillStyle = '#163054'; ctx.textAlign = 'center'; ctx.font = `${Math.round(11 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.u > 0.05 ? '顺时针' : S.u < -0.05 ? '逆时针' : '手柄', wx, wy - wr - 16 * u);
    ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`; ctx.fillStyle = '#163054'; ctx.textAlign = 'right';
    ctx.fillText(Math.round(S.theta / DEG) + '°', wx - wr - 18 * u, wy + 5 * u);
  }
  // title card shown for a moment when a level starts
  function drawIntro(u) {
    const t = P.intro - S.timer, a = clamp(Math.min(t / 0.18, S.timer / 0.25), 0, 1);
    ctx.globalAlpha = a * 0.62; ctx.fillStyle = '#163054'; ctx.fillRect(0, 0, W, H);
    ctx.globalAlpha = a; ctx.textAlign = 'center';
    ctx.fillStyle = '#ffd166'; ctx.font = `600 ${Math.round(22 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText('天空 ' + LV.id, CX, CY - 40 * u);
    ctx.fillStyle = '#ffffff'; ctx.font = `600 ${Math.round(52 * u)}px "Noto Serif SC", serif`;
    ctx.fillText(LV.name, CX, CY + 22 * u);
    ctx.fillStyle = '#d4e6f7'; ctx.font = `${Math.round(16 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
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
      ovText.textContent = LV.id + ' ' + (S.mode === 'demo' ? '演示通关' : '通关') + '，用时 ' + S.runTime.toFixed(1) + ' 秒' + (S.deaths ? '，死亡 ' + S.deaths + ' 次' : '') + (nx ? '。' : '。天空四关都过完了。');
    }
  }
  function onRunOver() {
    if (window.__skyHeadless) return;
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
  const sliders = [['tRot', 'oRot', 'rotSpeed', v => v + '°/s'], ['tVmax', 'oVmax', 'vmax', v => v + ' 格/秒'], ['tCloud', 'oCloud', 'cloudTime', v => v.toFixed(1) + ' 秒']];
  for (const [id, out, key, fmt] of sliders) {
    const el = $(id), o = $(out);
    if (!el) continue;
    el.addEventListener('input', () => { P[key] = parseFloat(el.value); o.textContent = fmt(P[key]); if (S.mode !== 'idle') { overlay.hidden = true; startRun(S.mode); } });
  }

  // ---------------- boot ----------------
  let last = 0, acc = 0;
  let loopErr = false;
  function frame(ts) {
    requestAnimationFrame(frame);
    if (window.__skyStop) return;
    try {
      const now = ts / 1000; let dt = last ? now - last : 0; last = now; dt = Math.min(dt, 0.1);
      pollPad();
      if (!window.__skyHeadless) {
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
    for (const k of ['idle', 'float', 'fallstart', 'land']) IMG[k + 'Red'] = tinted(IMG[k], 'rgba(230,70,90,0.6)');
    resize(); window.addEventListener('resize', resize);
    selectLevel(0, true);
    requestAnimationFrame(frame);
    window.__skyReady = true;
  });
  window.__sky = {
    S, P, LEVELS, step, startRun, loadLevel, resetLevel, input, DT, lp,
    get O() { return O; }, get LV() { return LV; }, get LI() { return LI; }, get PLAN() { return PLAN; },
    render: () => render(0), renderAt: (a, dt) => { alpha = a; render(dt); },
  };
})();
