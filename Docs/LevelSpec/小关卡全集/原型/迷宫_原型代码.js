(() => {
  'use strict';
  const A = window.__ASSETS;
  const DEG = Math.PI / 180;
  // Player rules ported from the game's PlayerController / WorldRotator (fixed step 0.02, no own horizontal speed)
  const G = 9.81, PR = 0.4, PC = [0.4, 1.13];
  const DT = 0.02, SUB = 4, SKIN = 0.02, PIVOT_LAG = 8, VMAX = 16;
  const P = { rotSpeed: 135, slowmo: true, react: 0.12, intro: 1.2 };
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
  const mk = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };

  // ---------------- the five maze levels ----------------
  // One map character = one maze cell = 2 x 2 units, exactly like MazeSceneBuilder.
  // # wall   . air   P start   X spikes (whole cell kills)   > < ^ v laser (emitter on the wall behind the arrow)
  // D door on the floor   U door on the ceiling   [ door on the left wall   ] door on the right wall
  // The door opens only while it stands upright on screen (within 20 degrees).
  // lasers: { 'col,row': { on, off } }  off = 0 means always on; every cycle starts switched on.
  const LEVELS = window.__MAZE_LEVELS || [];

  // ---------------- current level ----------------
  let LI = 0, L = null, CW = 1, CH = 1, MW = 2, MH = 2, PLAN = [];
  let solid = new Uint8Array(1), SPIKES = [], LASERS = [], BOXES = [], DOOR = null, START = [1, 2];
  const TS = 16;
  const cellAt = (cx, cy) => (cx < 0 || cy < 0 || cx >= CW || cy >= CH ? '#' : L.map[cy][cx]);
  const solidAt = (x, y) => x < 0 || y < 0 || x >= MW || y >= MH || solid[y * MW + x] === 1;
  const DOORS = { D: { dir: 0, b: [1, 2] }, U: { dir: 180, b: [1, 0] }, '[': { dir: -90, b: [0, 1] }, ']': { dir: 90, b: [2, 1] } };
  const ARROWS = { '>': [1, 0], '<': [-1, 0], '^': [0, -1], 'v': [0, 1] };
  function setupLevel(i) {
    LI = i; L = LEVELS[i]; PLAN = L.plan;
    CW = L.map[0].length; CH = L.map.length; MW = CW * 2; MH = CH * 2;
    solid = new Uint8Array(MW * MH);
    for (let y = 0; y < MH; y++) for (let x = 0; x < MW; x++) solid[y * MW + x] = cellAt(x >> 1, y >> 1) === '#' ? 1 : 0;
    SPIKES = []; LASERS = []; BOXES = []; DOOR = null;
    const cfgs = L.lasers || {};
    for (let cy = 0; cy < CH; cy++) for (let cx = 0; cx < CW; cx++) {
      const c = L.map[cy][cx], x0 = cx * 2, y0 = cy * 2, mx = x0 + 1, my = y0 + 1;
      if (c === 'P') START = [mx, y0 + 2];
      else if (c === 'X') SPIKES.push({ x0, y0, x1: x0 + 2, y1: y0 + 2, cx, cy });
      else if (DOORS[c]) {
        const d = DOORS[c], rot = -d.dir * DEG;
        DOOR = { kind: c, cx, cy, bx: x0 + d.b[0], by: y0 + d.b[1], rot, ux: Math.sin(rot), uy: -Math.cos(rot), rx: Math.cos(rot), ry: Math.sin(rot) };
      } else if (ARROWS[c]) {
        const [dx, dy] = ARROWS[c], cfg = cfgs[cx + ',' + cy] || {};
        const ex = mx - dx * 0.875, ey = my - dy * 0.875;
        const hx = dx ? 0.125 : 0.325, hy = dx ? 0.325 : 0.125;
        const box = { x0: ex - hx, y0: ey - hy, x1: ex + hx, y1: ey + hy };
        BOXES.push(box);
        LASERS.push({ cx, cy, dx, dy, ex, ey, sx: ex + dx * 0.16, sy: ey + dy * 0.16, on: cfg.on ?? 2, off: cfg.off ?? 0, box, len: 0, lastIn: -9, wasOn: true });
      }
    }
    for (const l of LASERS) l.len = castBeam(l);
  }
  // beams are axis-aligned in the level: walk until a wall tile or another emitter's body
  function castBeam(l) {
    let best = 100;
    if (l.dx) {
      const ty = Math.floor(l.sy);
      for (let x = Math.floor(l.sx) + (l.dx > 0 ? 0 : 0); x >= -1 && x <= MW; x += l.dx) {
        if (solidAt(x, ty)) { best = l.dx > 0 ? x - l.sx : l.sx - (x + 1); break; }
      }
    } else {
      const tx = Math.floor(l.sx);
      for (let y = Math.floor(l.sy); y >= -1 && y <= MH; y += l.dy) {
        if (solidAt(tx, y)) { best = l.dy > 0 ? y - l.sy : l.sy - (y + 1); break; }
      }
    }
    for (const o of LASERS) {
      if (o === l) continue;
      const b = o.box;
      if (l.dx && l.sy > b.y0 && l.sy < b.y1) {
        const d = l.dx > 0 ? b.x0 - l.sx : l.sx - b.x1;
        if (d > 0) best = Math.min(best, d);
      } else if (l.dy && l.sx > b.x0 && l.sx < b.x1) {
        const d = l.dy > 0 ? b.y0 - l.sy : l.sy - b.y1;
        if (d > 0) best = Math.min(best, d);
      }
    }
    return Math.max(0, best);
  }
  function laserPhase(l, t = S.lt) { return l.off > 0 ? t % (l.on + l.off) : 0; }
  function laserOn(l, t = S.lt) { return l.off <= 0 || laserPhase(l, t) < l.on; }
  // seconds until this laser next switches (on -> off or off -> on)
  function laserLeft(l, t = S.lt) { if (l.off <= 0) return 99; const p = laserPhase(l, t); return p < l.on ? l.on - p : l.on + l.off - p; }

  // ---------------- state ----------------
  const S = {
    mode: 'idle', theta: 0, tx: 0, ty: 0, px: 0, py: 0, vx: 0, vy: 0, grounded: true, airTime: 0, stick: 0, u: 0, uVis: 0, hist: [],
    state: 'play', timer: 0, time: 0, lt: 0, runStart: 0, runTime: 0, step: 0, stepAt: 0, deaths: 0,
    anim: 'idle', animT: 0, flash: 0, flashColor: '', shake: 0, slow: 0, near: {},
    fx: [], pops: [], needleA: 0, needleV: 0, unlocked: false, doorF: 0, hint: 0, cam: [0, 0], done: false, dbg: {}, toast: null,
  };
  const prev = { theta: 0, tx: 0, ty: 0, px: 0, py: 0, cam: [0, 0] };
  function snapshot() { prev.theta = S.theta; prev.tx = S.tx; prev.ty = S.ty; prev.px = S.px; prev.py = S.py; prev.cam = [S.cam[0], S.cam[1]]; }

  // ---------------- transforms ----------------
  function toLevel(wx, wy) { const c = Math.cos(S.theta), s = Math.sin(S.theta), dx = wx - S.tx, dy = wy - S.ty; return [dx * c + dy * s, -dx * s + dy * c]; }
  function toWorld(lx, ly) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [S.tx + lx * c - ly * s, S.ty + lx * s + ly * c]; }
  function vW(x, y) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [x * c - y * s, x * s + y * c]; }
  function vL(x, y) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [x * c + y * s, -x * s + y * c]; }
  const lp = () => toLevel(S.px, S.py - PC[0]);
  function doorTilt() { const u = vW(DOOR.ux, DOOR.uy); return Math.acos(clamp(-u[1], -1, 1)) / DEG; }

  // ---------------- collision (level space, units) ----------------
  function pushBox(lx, ly, r, x0, y0, x1, y1, normals, freeSide) {
    const qx = clamp(lx, x0, x1), qy = clamp(ly, y0, y1);
    const dx = lx - qx, dy = ly - qy, d2 = dx * dx + dy * dy;
    if (d2 >= r * r) return null;
    let nx, ny, pen;
    if (d2 > 1e-9) { const d = Math.sqrt(d2); nx = dx / d; ny = dy / d; pen = r - d; }
    else {
      const opts = [[-1, 0, lx - x0], [1, 0, x1 - lx], [0, -1, ly - y0], [0, 1, y1 - ly]].sort((a, b) => a[2] - b[2]);
      const o = (freeSide && opts.find(q => freeSide(q[0], q[1]))) || opts[0];
      nx = o[0]; ny = o[1]; pen = o[2] + r;
    }
    if (normals) normals.push([nx, ny]);
    return [lx + nx * pen, ly + ny * pen];
  }
  function resolveCircleL(lx, ly, r, normals) {
    for (let it = 0; it < 4; it++) {
      let hit = false;
      const x0 = Math.floor(lx - r), x1 = Math.floor(lx + r), y0 = Math.floor(ly - r), y1 = Math.floor(ly + r);
      for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) {
        if (!solidAt(cx, cy)) continue;
        const q = pushBox(lx, ly, r, cx, cy, cx + 1, cy + 1, normals, (ox, oy) => !solidAt(cx + ox, cy + oy));
        if (q) { lx = q[0]; ly = q[1]; hit = true; }
      }
      for (const b of BOXES) {
        if (lx + r < b.x0 || lx - r > b.x1 || ly + r < b.y0 || ly - r > b.y1) continue;
        const q = pushBox(lx, ly, r, b.x0, b.y0, b.x1, b.y1, normals, null);
        if (q) { lx = q[0]; ly = q[1]; hit = true; }
      }
      if (!hit) break;
    }
    return [lx, ly];
  }
  function touchingL(lx, ly, r, normals) {
    const near = (x0, y0, x1, y1) => {
      const qx = clamp(lx, x0, x1), qy = clamp(ly, y0, y1);
      const dx = lx - qx, dy = ly - qy, d2 = dx * dx + dy * dy;
      if (d2 >= r * r || d2 < 1e-9) return;
      const d = Math.sqrt(d2); normals.push([dx / d, dy / d]);
    };
    const x0 = Math.floor(lx - r), x1 = Math.floor(lx + r), y0 = Math.floor(ly - r), y1 = Math.floor(ly + r);
    for (let cy = y0; cy <= y1; cy++) for (let cx = x0; cx <= x1; cx++) if (solidAt(cx, cy)) near(cx, cy, cx + 1, cy + 1);
    for (const b of BOXES) near(b.x0, b.y0, b.x1, b.y1);
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
  function resetLevel(intro) {
    S.theta = (L.theta0 || 0) * DEG; S.tx = 0; S.ty = 0; S.stick = 0; S.u = 0; S.hist = [];
    S.vx = 0; S.vy = 0; S.grounded = true; S.airTime = 0; S.state = intro ? 'intro' : 'play'; S.timer = intro ? P.intro : 0;
    S.anim = 'idle'; S.animT = 0; S.flash = 0; S.shake = 0; S.slow = 0; S.near = {}; S.fx = []; S.pops = [];
    S.needleA = 0; S.needleV = 0; S.doorF = 0; S.hint = 0; S.lt = 0; S.toast = null; S.cross = {};
    for (const l of LASERS) { l.lastIn = -9; l.wasOn = true; l.offAt = -9; }
    S.step = 0; S.stepAt = S.time; S.lastTgt = S.theta / DEG; S.uVis = 0;
    const w = toWorld(START[0], START[1]); S.px = w[0]; S.py = w[1];
    // settle onto the floor like the game's first physics steps
    S.unlocked = doorTilt() <= 20;
    S.cam = camTarget(); S.done = false;
    S.runStart = S.time + (intro ? P.intro : 0); S.runTime = 0;
    snapshot();
  }
  let lvlTex = null;
  function loadLevel(i) {
    setupLevel(i);
    lvlTex = buildLevel();
    S.mode = 'idle'; S.time = 0; S.deaths = 0; S.toast = null;
    resetLevel(false);
  }
  function startRun(mode) {
    S.dbg = {}; S.time = 0; S.toast = null;
    S.mode = mode; S.deaths = 0;
    resetLevel(P.intro > 0);
  }

  // ---------------- simulation ----------------
  const input = { keys: 0, buttons: 0, pad: 0, auto: null };
  function beatTarget(b) { return typeof b.t === 'function' ? b.t() : b.t; }
  function step(dt) {
    S.time += dt;
    snapshot();
    const live = S.state === 'play' && S.mode !== 'idle';
    let u = 0;
    if (live && S.mode === 'demo') {
      const b = PLAN[S.step], react = b.react ?? L.react ?? P.react;
      const tgt = S.time - S.stepAt < react && S.step > 0 ? S.lastTgt : beatTarget(b);
      if (!(S.time - S.stepAt < react && S.step > 0)) S.lastTgt = tgt;
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
    if (live) {
      S.lt += dt;
      stepPlayer(dt);
      checkHazards();
      advancePlan();
      if (!S.done) S.runTime = S.time - S.runStart;
    }
    updateDoor(dt);
    stepFx(dt);
    const tgt = camTarget(), kc = 1 - Math.exp(-dt / 0.06);
    S.cam[0] += (tgt[0] - S.cam[0]) * kc; S.cam[1] += (tgt[1] - S.cam[1]) * kc;
    if (S.state !== 'play') {
      S.timer -= dt;
      if (S.timer <= 0) {
        if (S.state === 'intro') { S.state = 'play'; S.stepAt = S.time; S.runStart = S.time; S.lastTgt = 0; }
        else if (S.state === 'dead') resetLevel(false);
        else if (S.state === 'clear') { S.state = 'over'; onRunOver(); }
      }
    }
  }
  // camera: the game's MazeCameraFit + FollowTarget2D -> always the maze centre, fixed size
  function camTarget() { return toWorld(MW / 2, MH / 2); }

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
    S.speed = Math.abs(S.vy);
    const l = lp();
    if (l[0] < -2 || l[1] < -2 || l[0] > MW + 2 || l[1] > MH + 2) die('掉出地图了');
  }

  function die(msg) {
    if (S.state !== 'play') return;
    S.state = 'dead'; S.timer = 0.9; S.flash = 0.5; S.flashColor = 'rgba(196,48,80,'; S.deaths++;
    S.shake = Math.max(S.shake, 0.4);
    toast(msg, 'dead');
  }
  function clear() {
    if (S.state !== 'play') return;
    S.state = 'clear'; S.timer = 2.0; S.flash = 0.45; S.flashColor = 'rgba(63,214,224,'; S.doorF = 0; S.done = true;
    S.runTime = S.time - S.runStart;
    toast('第 ' + L.id + ' 关 通关！' + S.runTime.toFixed(1) + ' 秒', 'clear');
  }
  function nearMiss(kind) {
    if (S.time - (S.near[kind] ?? -9) < 1.4) return;
    S.near[kind] = S.time;
    pop('险！', true);
    if (P.slowmo && (!window.__mazeHeadless || window.__mazeVideo)) S.slow = 0.42;
  }

  const dbgMin = (k, v) => { S.dbg[k] = Math.min(S.dbg[k] ?? 99, v); };
  const boxDist = (x, y, b) => Math.hypot(Math.max(b.x0 - x, 0, x - b.x1), Math.max(b.y0 - y, 0, y - b.y1));
  function beamDist(l, x, y) {
    if (l.dx) { const a = Math.min(l.sx, l.sx + l.dx * l.len), b = Math.max(l.sx, l.sx + l.dx * l.len); return Math.hypot(Math.max(a - x, 0, x - b), y - l.sy); }
    const a = Math.min(l.sy, l.sy + l.dy * l.len), b = Math.max(l.sy, l.sy + l.dy * l.len); return Math.hypot(Math.max(a - y, 0, y - b), x - l.sx);
  }
  function checkHazards() {
    if (S.state !== 'play') return;
    const cs = PC.map(o => toLevel(S.px, S.py - o));
    const fast = S.speed > 2.5 || Math.abs(S.u) > 0.3;
    // spikes: the whole cell is a kill box
    for (const b of SPIKES) for (const c of cs) {
      const d = boxDist(c[0], c[1], b);
      dbgMin('spike', d - PR);
      if (d < PR) return die('撞上尖刺了');
      if (d < PR + 0.55 && fast) nearMiss('spike');
    }
    // lasers: a thin kill line while switched on
    LASERS.forEach((l, i) => {
      const on = laserOn(l);
      let dmin = 99;
      for (const c of cs) dmin = Math.min(dmin, beamDist(l, c[0], c[1]));
      const inside = dmin < PR + 0.04;
      if (inside) (S.cross[i] = S.cross[i] || []).push(+S.lt.toFixed(2));
      if (on) {
        dbgMin('laser' + i, dmin - PR);
        if (inside && !P.noLaser) return die('被激光打中了');
        if (dmin < PR + 0.5 && fast) nearMiss('laser');
        // it just switched on right behind you
        if (!l.wasOn && S.lt - l.lastIn < 0.45) nearMiss('laser');
      } else {
        if (inside) l.lastIn = S.lt;
        if (l.wasOn) l.offAt = S.lt;
      }
      l.wasOn = on;
    });
    if (S.state !== 'play') return;
    // door: GoalDoor's trigger is a 2 x 2 box over the whole door cell
    const db = { x0: DOOR.cx * 2, y0: DOOR.cy * 2, x1: DOOR.cx * 2 + 2, y1: DOOR.cy * 2 + 2 };
    if (cs.some(c => boxDist(c[0], c[1], db) < PR)) { if (S.unlocked) clear(); else if (S.hint <= 0) S.hint = 1; }
  }

  function advancePlan() {
    while (S.step < PLAN.length - 1 && PLAN[S.step].next(PLAN[S.step])) { S.step++; S.stepAt = S.time; }
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
    for (const p of S.fx) {
      p.life -= dt;
      const g = p.grav ? G : p.k === 'dust' ? 0.6 : 0;
      p.vx += gx * g * dt; p.vy += gy * g * dt;
      if (p.k === 'dust') { p.vx *= 1 - 1.5 * dt; p.vy *= 1 - 1.5 * dt; }
      p.x += p.vx * dt; p.y += p.vy * dt;
    }
    S.fx = S.fx.filter(p => p.life > 0);
    if (S.fx.length > 200) S.fx.splice(0, S.fx.length - 200);
    for (const p of S.pops) p.t += dt;
    S.pops = S.pops.filter(p => p.t < (p.big ? 0.9 : 0.8));
    S.shake *= Math.exp(-6 * dt);
    if (S.flash > 0) S.flash = Math.max(0, S.flash - dt);
  }

  // ---------------- art ----------------
  const C = { air: [18, 18, 39], airDeep: [14, 14, 30], wall: [39, 39, 68], outer: [20, 19, 36],
    spike: [196, 92, 122], spikeLit: [232, 142, 166], spikeBase: [37, 37, 63], spikeCore: [26, 25, 46] };
  const rgb = c => `rgb(${c[0] | 0},${c[1] | 0},${c[2] | 0})`;
  const MARG = 16;   // tiles of rock drawn around the maze (covers the screen at any angle); beyond that a flat fill
  function autoIdx(x, y, s) {
    const n = s(x, y - 1), so = s(x, y + 1), w = s(x - 1, y), e = s(x + 1, y);
    const col = !w ? 2 : !e ? 4 : 3, row = !n ? 0 : !so ? 2 : 1;
    if (col !== 3 || row !== 1) return row * 5 + col;
    if (!s(x - 1, y - 1)) return 6;
    if (!s(x + 1, y - 1)) return 5;
    if (!s(x - 1, y + 1)) return 1;
    if (!s(x + 1, y + 1)) return 0;
    return 8;
  }
  function buildLevel() {
    const W = (MW + 2 * MARG) * TS, H = (MH + 2 * MARG) * TS, cv = mk(W, H), g = cv.getContext('2d');
    g.imageSmoothingEnabled = false;
    // air with a faint large-cell pattern
    g.fillStyle = rgb(C.air); g.fillRect(MARG * TS, MARG * TS, MW * TS, MH * TS);
    for (let cy = 0; cy < CH; cy++) for (let cx = 0; cx < CW; cx++) {
      if (cellAt(cx, cy) === '#') continue;
      const h = hash(cx * 3.1, cy * 7.3);
      if (h > 0.55) { g.fillStyle = h > 0.8 ? 'rgb(22,22,46)' : 'rgb(20,20,43)'; g.fillRect((MARG + cx * 2) * TS, (MARG + cy * 2) * TS, 2 * TS, 2 * TS); }
    }
    // rock: the game's rim tileset, auto-tiled exactly like MazeSceneBuilder
    const sol = (x, y) => solidAt(x, y);
    for (let y = -MARG; y < MH + MARG; y++) for (let x = -MARG; x < MW + MARG; x++) {
      if (!sol(x, y)) continue;
      const k = autoIdx(x, y, sol);
      g.drawImage(IMG.tiles, (k % 5) * TS, Math.floor(k / 5) * TS, TS, TS, (x + MARG) * TS, (y + MARG) * TS, TS, TS);
    }
    // exterior shade like the game's exterior_shade_*.png: rock further than ~2 units from air fades to dark navy
    {
      const img = g.getImageData(0, 0, W, H), d = img.data, INF = 1e9;
      const dist = new Float32Array(W * H);
      for (let py = 0; py < H; py++) for (let px = 0; px < W; px++)
        dist[py * W + px] = solidAt(Math.floor(px / TS) - MARG, Math.floor(py / TS) - MARG) ? INF : 0;
      for (let py = 0; py < H; py++) for (let px = 0; px < W; px++) {
        const i = py * W + px; let v = dist[i]; if (v === 0) continue;
        if (px > 0) v = Math.min(v, dist[i - 1] + 3);
        if (py > 0) { v = Math.min(v, dist[i - W] + 3); if (px > 0) v = Math.min(v, dist[i - W - 1] + 4); if (px < W - 1) v = Math.min(v, dist[i - W + 1] + 4); }
        dist[i] = v;
      }
      for (let py = H - 1; py >= 0; py--) for (let px = W - 1; px >= 0; px--) {
        const i = py * W + px; let v = dist[i]; if (v === 0) continue;
        if (px < W - 1) v = Math.min(v, dist[i + 1] + 3);
        if (py < H - 1) { v = Math.min(v, dist[i + W] + 3); if (px < W - 1) v = Math.min(v, dist[i + W + 1] + 4); if (px > 0) v = Math.min(v, dist[i + W - 1] + 4); }
        dist[i] = v;
      }
      const LV = [[39, 39, 68], [31, 31, 55], [25, 24, 45], [20, 19, 36]];
      for (let i = 0; i < W * H; i++) {
        const dd = dist[i]; if (dd === 0) continue;
        const v = clamp((dd / 3 - 30) / 80, 0, 1) * 3; if (v <= 0) continue;
        const lo = Math.min(3, Math.floor(v)), hi = Math.min(3, lo + 1), f = v - Math.floor(v);
        for (let k = 0; k < 3; k++) d[i * 4 + k] = Math.floor(LV[lo][k] + (LV[hi][k] - LV[lo][k]) * f + 0.5);
        d[i * 4 + 3] = 255;
      }
      g.putImageData(img, 0, 0);
    }
    // spikes: dark core with teeth pointing out on every side
    for (const b of SPIKES) {
      const X = (b.x0 + MARG) * TS, Y = (b.y0 + MARG) * TS, Sz = 2 * TS;
      g.fillStyle = rgb(C.spikeCore); g.fillRect(X + 3, Y + 3, Sz - 6, Sz - 6);
      g.fillStyle = rgb(C.spikeBase); g.fillRect(X + 5, Y + 5, Sz - 10, Sz - 10);
      const tooth = (ax, ay, bx, by, cx2, cy2, lit) => { g.fillStyle = rgb(lit ? C.spikeLit : C.spike); g.beginPath(); g.moveTo(ax, ay); g.lineTo(bx, by); g.lineTo(cx2, cy2); g.closePath(); g.fill(); };
      const n = 4, w = Sz / n;
      for (let k = 0; k < n; k++) {
        const a = X + k * w, m = a + w / 2, bnd = a + w;
        tooth(a, Y + 6, bnd, Y + 6, m, Y, k % 2 === 0);              // top
        tooth(a, Y + Sz - 6, bnd, Y + Sz - 6, m, Y + Sz, false);     // bottom
        const a2 = Y + k * w, m2 = a2 + w / 2, b2 = a2 + w;
        tooth(X + 6, a2, X + 6, b2, X, m2, k % 2 === 0);              // left
        tooth(X + Sz - 6, a2, X + Sz - 6, b2, X + Sz, m2, false);    // right
      }
    }
    // laser emitters: the game's 4 x 10 pixel sprite (blue base, orange arrow pointing along the beam)
    for (const l of LASERS) {
      g.save(); g.translate((l.ex + MARG) * TS, (l.ey + MARG) * TS); g.rotate(Math.atan2(l.dy, l.dx));
      g.fillStyle = 'rgb(39,53,91)'; g.fillRect(-2, -5, 4, 10);
      g.fillStyle = 'rgb(83,125,177)'; g.fillRect(-1, -4, 3, 8);
      g.fillStyle = 'rgb(239,168,69)';
      for (let y = 2; y <= 7; y++) for (let x = 1; x <= 3; x++) if (Math.abs(y - 4.5) <= 3.5 - x) g.fillRect(x - 2, y - 5, 1, 1);
      g.restore();
    }
    return cv;
  }

  // ---------------- drawing ----------------
  const view = document.getElementById('view'), ctx = view.getContext('2d');
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
    // MazeCameraFit default: half-height R = half diagonal + 0.5, so the maze fits at any angle
    const R = Math.hypot(MW, MH) / 2 + 0.5, aspect = W / H;
    K = H / (2 * Math.max(R, R / aspect)); CX = W / 2; CY = H / 2;
    const lp_ = (a, b) => a + (b - a) * alpha;
    const th = lp_(prev.theta, S.theta), Tx = lp_(prev.tx, S.tx), Ty = lp_(prev.ty, S.ty);
    const Px = lp_(prev.px, S.px), Py = lp_(prev.py, S.py);
    let camx = lp_(prev.cam[0], S.cam[0]), camy = lp_(prev.cam[1], S.cam[1]);
    if (S.shake > 0.01) { camx += (Math.random() - 0.5) * S.shake * 0.7; camy += (Math.random() - 0.5) * S.shake * 0.7; }
    const c = Math.cos(th), s = Math.sin(th);
    const ox = CX + K * (Tx - camx), oy = CY + K * (Ty - camy);
    const level = () => ctx.setTransform(K * c, K * s, -K * s, K * c, ox, oy);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = rgb(C.outer); ctx.fillRect(0, 0, W, H);
    ctx.imageSmoothingEnabled = false;
    level();
    ctx.drawImage(lvlTex, -MARG, -MARG, MW + 2 * MARG, MH + 2 * MARG);
    // lasers
    const blinkT = S.lt;
    for (const l of LASERS) {
      if (!laserOn(l)) {
        // switched off: a faint dotted guide so the line stays readable
        ctx.globalAlpha = 0.22; ctx.fillStyle = 'rgb(166,227,107)';
        for (let d = 0.2; d < l.len; d += 0.5) {
          const x = l.sx + l.dx * d, y = l.sy + l.dy * d;
          ctx.fillRect(x - 0.04, y - 0.04, 0.08, 0.08);
        }
        ctx.globalAlpha = 1;
        continue;
      }
      const ph = laserPhase(l), warn = l.off > 0 && ph >= Math.max(0, l.on - 0.5);
      const k = warn && Math.floor(ph * 16) % 2 !== 0 ? 0.3 : 1;
      const x1 = l.sx + l.dx * l.len, y1 = l.sy + l.dy * l.len;
      const line = (w, col, a) => {
        ctx.globalAlpha = a; ctx.fillStyle = col;
        if (l.dx) ctx.fillRect(Math.min(l.sx, x1), l.sy - w / 2, Math.abs(x1 - l.sx), w);
        else ctx.fillRect(l.sx - w / 2, Math.min(l.sy, y1), w, Math.abs(y1 - l.sy));
      };
      line(0.1875 * 1.6, 'rgb(166,227,107)', 0.25 * k * 0.8);
      line(0.1875, 'rgb(166,227,107)', 0.25 * k);
      line(0.0625 * 1.4, `rgb(${166 * k | 0},${227 * k | 0},${107 * k | 0})`, 1);
      ctx.globalAlpha = 1;
    }
    // door
    ctx.save(); ctx.translate(DOOR.bx, DOOR.by); ctx.rotate(DOOR.rot);
    if (S.state === 'clear' || S.state === 'over') ctx.drawImage(IMG.doorOpen, Math.floor(S.doorF) * 32, 0, 32, 32, -1, -2, 2, 2);
    else ctx.drawImage(IMG.door, (Math.floor(S.time * 7) % 6) * 32, 0, 32, 32, -1, -2, 2, 2);
    const lock = (S.unlocked || S.state === 'clear' || S.state === 'over') ? IMG.lockOpen : (S.hint > 0 && Math.floor(S.hint * 8) % 2 === 0) ? IMG.lockHint : IMG.lockLocked;
    ctx.drawImage(lock, -1, -2, 2, 2);
    ctx.translate((15.5 - 16) / 16, -2 + 10.5 / 16); ctx.rotate(-(th + DOOR.rot) + S.needleA);
    ctx.drawImage(IMG.needle, -1 / 16, 0, 2 / 16, 8 / 16);
    ctx.restore();
    // particles
    for (const p of S.fx) {
      const a = clamp(p.life / p.max, 0, 1);
      ctx.globalAlpha = p.k === 'dust' ? a * 0.55 : a;
      ctx.fillStyle = p.k === 'spark' ? '#c9f08e' : '#8c88b8';
      const sz = p.k === 'dust' ? p.s * (1.6 - a * 0.6) : p.s;
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
    // speed lines while falling fast
    const spd = Math.abs(S.vy);
    if (spd > 7.5 && S.state === 'play') {
      ctx.strokeStyle = 'rgba(214,220,255,0.28)'; ctx.lineWidth = Math.max(1, K * 0.035);
      const n = Math.min(12, Math.floor((spd - 7) * 2));
      for (let k = 0; k < n; k++) {
        const h1 = hash(k * 3.1, Math.floor(S.time * 14) + k), h2 = hash(k * 7.7, Math.floor(S.time * 14) - k);
        const off = (h1 - 0.5) * K * 5, along = (h2 - 0.2) * K * 5, len = K * (1.0 + spd * 0.1);
        const x0 = pS[0] + off, y0 = pS[1] - K * 0.8 - along;
        ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x0, y0 - len); ctx.stroke();
      }
    }
    const vg = ctx.createRadialGradient(CX, CY, H * 0.35, CX, CY, H * 0.95);
    vg.addColorStop(0, 'rgba(0,0,0,0)'); vg.addColorStop(1, 'rgba(0,0,0,0.45)');
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
      ctx.fillStyle = p.big ? '#ff8aa6' : '#e6e2ff';
      const x = pS[0] + (p.big ? K * 1.6 : 0), y = pS[1] - K * 2.4 - rise;
      ctx.strokeText(p.text, x, y); ctx.fillText(p.text, x, y);
    }
    ctx.globalAlpha = 1;
    if (S.toast && S.time - S.toast.t < (S.toast.kind === 'clear' ? 2.0 : 0.9)) {
      const a = clamp((S.time - S.toast.t) / 0.15, 0, 1);
      ctx.globalAlpha = a; ctx.textAlign = 'center';
      ctx.font = `600 ${Math.round(34 * u)}px "Noto Serif SC", serif`;
      ctx.lineWidth = 6 * u; ctx.strokeStyle = 'rgba(0,0,0,0.8)';
      ctx.fillStyle = S.toast.kind === 'clear' ? '#3fd6e0' : '#ff7a8f';
      ctx.strokeText(S.toast.text, CX, CY - H * 0.2); ctx.fillText(S.toast.text, CX, CY - H * 0.2);
      ctx.globalAlpha = 1;
    }
    // level badge, top-left (only when the caption box leaves room for it)
    const capW = Math.min(W * 0.62, 520 * u);
    ctx.textAlign = 'left'; ctx.fillStyle = '#9fa8ff'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
    const idW = ctx.measureText('Maze_0' + L.id).width;
    ctx.font = `600 ${Math.round(16 * u)}px "Noto Serif SC", serif`;
    const nameW = ctx.measureText(L.name).width;
    const room = CX - capW / 2 - 14 * u;
    if (S.mode === 'idle' || S.state === 'intro' || room > idW + nameW + 24 * u) {
      ctx.fillStyle = '#9fa8ff'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
      ctx.fillText('Maze_0' + L.id, 14 * u, 26 * u);
      ctx.fillStyle = '#e6e2ff'; ctx.font = `600 ${Math.round(16 * u)}px "Noto Serif SC", serif`;
      ctx.fillText(L.name, 14 * u + idW + 10 * u, 26 * u);
    } else if (room > idW + 8 * u) {
      ctx.fillStyle = '#9fa8ff'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
      ctx.fillText('Maze_0' + L.id, 14 * u, 26 * u);
    }
    if (S.state === 'intro') { drawIntro(u); return; }
    if (S.mode === 'idle') return;
    const st = PLAN[S.step], age = S.time - S.stepAt, ap = clamp(age / 0.22, 0, 1);
    if (S.state === 'play' || S.state === 'dead') {
      const bw = Math.min(W * 0.62, 520 * u), bh = (st.sub ? 64 : 46) * u, bx = CX - bw / 2, by = 12 * u - (1 - ap) * 10 * u;
      ctx.globalAlpha = 0.25 + 0.75 * ap;
      ctx.fillStyle = 'rgba(12,12,26,0.8)'; rr(bx, by, bw, bh, 10 * u); ctx.fill();
      ctx.strokeStyle = 'rgba(159,168,255,0.55)'; ctx.lineWidth = 1.5 * u; rr(bx, by, bw, bh, 10 * u); ctx.stroke();
      ctx.textAlign = 'left'; ctx.fillStyle = '#9fa8ff'; ctx.font = `600 ${Math.round(13 * u)}px ui-monospace, Menlo, monospace`;
      ctx.fillText(`第 ${S.step + 1} 拍`, bx + 14 * u, by + 28 * u);
      ctx.textAlign = 'center'; ctx.fillStyle = '#f2f0ff'; ctx.font = `600 ${Math.round(21 * u)}px "Noto Serif SC", serif`;
      ctx.fillText(st.cap, CX + 20 * u, by + 30 * u);
      if (st.sub) { ctx.fillStyle = '#b9b6da'; ctx.font = `${Math.round(14 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`; ctx.fillText(st.sub, CX + 20 * u, by + 52 * u); }
      ctx.globalAlpha = 1;
    }
    ctx.textAlign = 'left'; ctx.fillStyle = '#e6e2ff'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText(S.runTime.toFixed(1) + ' s', 14 * u, H - 18 * u);
    ctx.fillStyle = S.mode === 'demo' ? '#9fa8ff' : '#8f8cb0'; ctx.font = `${Math.round(12 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.mode === 'demo' ? '演示中' : '自己玩', 14 * u, H - 38 * u);
    const n = PLAN.length, gap = 26 * u, tw = (n - 1) * gap, tx0 = CX - tw / 2, ty = H - 26 * u;
    ctx.fillStyle = 'rgba(12,12,26,0.6)'; rr(tx0 - 16 * u, ty - 14 * u, tw + 32 * u, 28 * u, 14 * u); ctx.fill();
    ctx.strokeStyle = 'rgba(230,226,255,0.25)'; ctx.lineWidth = 2 * u; ctx.beginPath(); ctx.moveTo(tx0, ty); ctx.lineTo(tx0 + tw, ty); ctx.stroke();
    for (let i = 0; i < n; i++) {
      const x = tx0 + i * gap, done = i < S.step || S.state === 'clear' || S.state === 'over', cur = i === S.step && S.state === 'play';
      ctx.beginPath(); ctx.arc(x, ty, (cur ? 7 + Math.sin(S.time * 10) * 1.5 : 5) * u, 0, Math.PI * 2);
      ctx.fillStyle = done ? '#9fa8ff' : cur ? '#f2f0ff' : 'rgba(16,16,32,0.9)'; ctx.fill();
      ctx.strokeStyle = done || cur ? '#9fa8ff' : 'rgba(230,226,255,0.45)'; ctx.lineWidth = 1.5 * u; ctx.stroke();
    }
    const wx = W - 52 * u, wy = H - 52 * u, wr = 30 * u;
    S.uVis += (S.u - S.uVis) * 0.25;
    ctx.save(); ctx.translate(wx, wy);
    ctx.fillStyle = 'rgba(12,12,26,0.7)'; ctx.beginPath(); ctx.arc(0, 0, wr + 10 * u, 0, Math.PI * 2); ctx.fill();
    const full = Math.abs(S.u) > 0.95;
    if (Math.abs(S.uVis) > 0.05) {
      ctx.strokeStyle = full ? '#9fa8ff' : 'rgba(159,168,255,0.6)'; ctx.lineWidth = 4 * u; ctx.beginPath();
      ctx.arc(0, 0, wr + 5 * u, -Math.PI / 2, -Math.PI / 2 + S.uVis * 1.4, S.uVis < 0); ctx.stroke();
    }
    ctx.rotate(S.uVis * 75 * DEG);
    ctx.strokeStyle = '#e6e2ff'; ctx.lineWidth = 4 * u; ctx.beginPath(); ctx.arc(0, 0, wr, 0, Math.PI * 2); ctx.stroke();
    ctx.lineWidth = 3 * u; ctx.beginPath(); ctx.moveTo(-wr, 0); ctx.lineTo(wr, 0); ctx.moveTo(0, 0); ctx.lineTo(0, wr); ctx.stroke();
    ctx.fillStyle = '#9fa8ff'; ctx.beginPath(); ctx.arc(0, -wr, 4 * u, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#e6e2ff'; ctx.beginPath(); ctx.arc(0, 0, 6 * u, 0, Math.PI * 2); ctx.fill();
    ctx.restore();
    ctx.fillStyle = '#8f8cb0'; ctx.textAlign = 'center'; ctx.font = `${Math.round(11 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.u > 0.05 ? '顺时针' : S.u < -0.05 ? '逆时针' : '手柄', wx, wy - wr - 16 * u);
  }
  function drawIntro(u) {
    const t = P.intro - S.timer, a = clamp(Math.min(t / 0.18, S.timer / 0.25), 0, 1);
    ctx.globalAlpha = a * 0.72; ctx.fillStyle = '#0c0c1a'; ctx.fillRect(0, 0, W, H);
    ctx.globalAlpha = a; ctx.textAlign = 'center';
    ctx.fillStyle = '#9fa8ff'; ctx.font = `600 ${Math.round(22 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText('Maze_0' + L.id, CX, CY - 40 * u);
    ctx.fillStyle = '#f2f0ff'; ctx.font = `600 ${Math.round(52 * u)}px "Noto Serif SC", serif`;
    ctx.fillText(L.name, CX, CY + 22 * u);
    ctx.fillStyle = '#b9b6da'; ctx.font = `${Math.round(16 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(L.tip, CX, CY + 62 * u);
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
      ovText.textContent = '第 ' + L.id + ' 关 ' + L.name + '：' + L.tip;
    } else {
      ovDemo.textContent = S.mode === 'demo' ? '▶ 再看一遍' : '▶ 看演示';
      ovPlay.textContent = S.mode === 'demo' ? '自己玩这关' : '再玩一次';
      ovNext.hidden = !nx;
      if (nx) ovNext.textContent = '下一关 ' + nx.id + ' ' + nx.name + ' ▶';
      ovText.textContent = '第 ' + L.id + ' 关' + (S.mode === 'demo' ? '演示通关' : '通关') + '，用时 ' + S.runTime.toFixed(1) + ' 秒' + (S.deaths ? '，死亡 ' + S.deaths + ' 次' : '') + (nx ? '。' : '。五关都过完了。');
    }
  }
  function onRunOver() {
    if (window.__mazeHeadless) return;
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
  const sliders = [['tRot', 'oRot', 'rotSpeed', v => v + '°/s']];
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
    if (window.__mazeStop) return;
    try {
      const now = ts / 1000; let dt = last ? now - last : 0; last = now; dt = Math.min(dt, 0.1);
      pollPad();
      if (!window.__mazeHeadless) {
        let scale = 1;
        if (S.slow > 0) { S.slow -= dt; scale = 0.32; }
        acc += dt * scale;
        while (acc >= DT) { step(DT); acc -= DT; }
        alpha = acc / DT;
      } else alpha = 1;
      render(dt);
      if (S.time - lastHud > 0.1 || S.time < lastHud) { lastHud = S.time; updateHud(); }
    } catch (e) { if (!loopErr) { loopErr = true; console.error(e); } }
  }
  // the text maps under the stage, straight from the level data, so they always match what is played
  const mapList = $('mapList');
  if (mapList) {
    for (const lv of LEVELS) {
      const fig = document.createElement('figure');
      const cap = document.createElement('figcaption');
      const ls = Object.entries(lv.lasers || {}).map(([k, v]) => `激光(${k}) 亮${v.on}s 灭${v.off}s`);
      cap.textContent = 'Maze_0' + lv.id + ' ' + lv.name + (ls.length ? ' · ' + ls.join('，') : '');
      const pre = document.createElement('pre');
      pre.textContent = lv.map.join('\n');
      fig.append(cap, pre); mapList.append(fig);
    }
  }
  const load = (src) => new Promise((res, rej) => { const im = new Image(); im.onload = () => res(im); im.onerror = rej; im.src = src; });
  Promise.all(Object.entries(A).map(([k, v]) => load(v).then(im => { IMG[k] = im; }))).then(() => {
    for (const k of ['idle', 'float', 'fallstart', 'land']) IMG[k + 'Red'] = tinted(IMG[k], 'rgba(226,70,96,0.6)');
    resize(); window.addEventListener('resize', resize);
    selectLevel(0, true);
    window.__mazeReady = true;
    requestAnimationFrame(frame);
  });
  window.__maze = {
    S, P, LEVELS, step, startRun, loadLevel, resetLevel, input, DT, lp, toLevel, toWorld, laserOn, laserLeft, laserPhase,
    get L() { return L; }, get LI() { return LI; }, get PLAN() { return PLAN; }, get LASERS() { return LASERS; }, get DOOR() { return DOOR; },
    render: () => render(0), renderAt: (a, dt) => { alpha = a; render(dt); }, doorTilt: () => doorTilt(),
  };
})();
