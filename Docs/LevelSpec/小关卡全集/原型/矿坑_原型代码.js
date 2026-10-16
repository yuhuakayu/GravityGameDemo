(() => {
  'use strict';
  const A = window.__ASSETS;
  const DEG = Math.PI / 180;
  // Player rules ported from the game's PlayerController / WorldRotator (fixed step 0.02, no own horizontal speed)
  const G = 9.81, PR = 0.4, PC = [0.4, 1.13];
  const DT = 0.02, SUB = 4, SKIN = 0.02, PIVOT_LAG = 8, VMAX = 16;
  const HANG = 0.55, FEET = 1.05;          // rail -> bucket centre / rail -> player's feet, along screen-down
  const CART_MAX = 11, CART_DRAG = 0.12;
  const P = { rotSpeed: 180, caveMul: 1, delayMul: 1, slowmo: true, react: 0.12, intro: 1.2 };
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const hash = (x, y) => { const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return s - Math.floor(s); };
  const mk = (w, h) => { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; };

  // ---------------- the four small levels (tiles, y down) ----------------
  // One mechanic each. The door opens only while it stands upright on screen (within 20°).
  const LEVELS = [
    {
      id: '1-1', name: '落石', w: 20, h: 18,
      tip: '你一转，松石也跟着滚。落地别停，石头就在身后。',
      carve: [[1, 1, 6, 5], [6, 3, 17, 5], [15, 5, 17, 10], [5, 10, 19, 12], [5, 12, 7, 16]],
      start: { feet: [4.6, 5.0] },
      door: { bx: 6, by: 16, dir: 0 },
      rocks: [[1.5, 4.55, 0.45, 'right'], [2.45, 4.55, 0.45, 'right'], [1.95, 3.72, 0.42, 'right'], [17.6, 11.55, 0.42, 'left'], [18.5, 11.55, 0.42, 'left']],
      rockDelay: 1.5,
      plan: [
        { t: 90, cap: '顺时针转 90°', sub: '掉进右边的隧道，松石跟着滚过来', next: () => lp()[0] > 16.1 && S.grounded },
        { t: 0, cap: '落地马上逆时针转回来', sub: '往下掉，慢一步就被石头砸到', react: 0.55, next: () => lp()[1] > 11 && S.grounded },
        { t: -90, cap: '继续逆时针：往左冲', sub: '右边那堆石头也松了', next: () => lp()[0] < 6 && S.grounded },
        { t: 0, cap: '顺时针转正，掉进门', sub: '石头砸在你刚站的地方', react: 0.55, next: () => false },
      ],
    },
    {
      id: '1-2', name: '塌方', w: 18, h: 21,
      tip: '一转，身后的隧道就开始塌。六拍一口气转完，别停。',
      carve: [[1, 1, 5, 4], [5, 2, 16, 4], [14, 4, 16, 8], [4, 8, 16, 10], [4, 10, 6, 14], [4, 14, 16, 16], [14, 16, 16, 19]],
      start: { feet: [3.6, 4.0] },
      door: { bx: 15, by: 19, dir: 0 },
      cave: { route: [[1, 1, 5, 4, 'R'], [5, 2, 16, 4, 'R'], [14, 4, 16, 8, 'D'], [4, 8, 16, 10, 'L'], [4, 10, 6, 14, 'D'], [4, 14, 16, 16, 'R'], [14, 16, 16, 19, 'D']], delay: 0.3, lag: 1.35 },
      react: 0.4,
      plan: [
        { t: 90, cap: '顺时针转 90°', sub: '一转，身后就开始塌', next: () => lp()[0] > 15 && S.grounded },
        { t: 0, cap: '逆时针：往下', sub: '', next: () => lp()[1] > 9 && S.grounded },
        { t: -90, cap: '继续逆时针：往左', sub: '塌方追上来了', next: () => lp()[0] < 5 && S.grounded },
        { t: 0, cap: '顺时针：往下', sub: '', next: () => lp()[1] > 15 && S.grounded },
        { t: 90, cap: '继续顺时针：往右', sub: '别停', next: () => lp()[0] > 15 && S.grounded },
        { t: 0, cap: '逆时针转正，掉进门', sub: '', next: () => false },
      ],
    },
    {
      id: '1-3', name: '矿车', w: 20, h: 18,
      tip: '坐在吊斗里，转世界让矿车冲起来；弯道前大翻 180°。',
      carve: [[2, 2, 18, 5], [15, 5, 18, 6], [2, 6, 18, 9], [2, 9, 5, 10], [2, 10, 19, 13], [17, 13, 19, 17]],
      start: { riding: true },
      track: [[3.5, 3.5], [16.5, 3.5], [16.5, 7.5], [3.5, 7.5], [3.5, 11.5], [15.5, 11.5]], cornerKeep: 0.7,
      door: { bx: 18, by: 17, dir: 0 },
      cave: { route: [[2, 2, 18, 5, 'R'], [15, 5, 18, 6, 'D'], [2, 6, 18, 9, 'L'], [2, 9, 5, 10, 'D'], [2, 10, 19, 13, 'R'], [17, 13, 19, 17, 'D']], delay: 0.5, speed: 8.3, hurry: [[6, 1.4]], lagEnd: 1.3, nearRide: 2.6 },
      plan: [
        { t: 90, cap: '顺时针转 90°：矿车冲起来', sub: '一动，身后就开始塌', next: () => S.cart.s > CUM[1] - 2.6 },
        { t: -90, cap: '逆时针大翻 180°', sub: '弯道来了，掉头往左', next: () => S.cart.s > CUM[3] - 2.6 },
        { t: 90, cap: '顺时针大翻 180°', sub: '前面的轨道断了！', next: () => S.cart.crashed },
        { t: 0, cap: '撞车！马上逆时针转正', sub: '掉进门', react: 0.4, next: () => false },
      ],
    },
    {
      id: '1-4', name: '木板', w: 22, h: 20,
      tip: '竖井顶上的木板，你一往下掉它就松。落地马上转走。',
      carve: [[1, 1, 5, 4], [5, 2, 9, 4], [9, 1, 11, 12], [11, 10, 15, 12], [15, 6, 17, 19], [17, 17, 21, 19]],
      start: { feet: [3.6, 4.0] },
      door: { bx: 21, by: 18, dir: 90 },
      planks: [{ x0: 9, x1: 11, top: 1.6, bottom: 11.82, wire: 9.5, delay: 0 }, { x0: 15, x1: 17, top: 6.6, bottom: 18.82, wire: 15.3, delay: 0 }],
      plan: [
        { t: 90, cap: '顺时针转 90°', sub: '冲进竖井，头顶有块松木板', next: () => lp()[0] > 10 && S.grounded },
        { t: 0, cap: '逆时针：往下掉', sub: '木板跟着砸下来', next: () => lp()[1] > 11 && S.grounded },
        { t: 90, cap: '落地马上顺时针', sub: '躲开木板', react: 0.45, next: () => lp()[0] > 16 && S.grounded },
        { t: 0, cap: '逆时针：往下掉', sub: '又一块', next: () => lp()[1] > 18 && S.grounded },
        { t: 90, cap: '落地马上顺时针', sub: '门在右边，转正就进', react: 0.45, next: () => false },
      ],
    },
  ];

  // ---------------- current level (rebuilt by loadLevel) ----------------
  let LI = 0, L = LEVELS[0], MW = 1, MH = 1, PLAN = L.plan;
  let rock = new Uint8Array(1), rubble = new Uint8Array(1), routeD = new Float32Array(1);
  let TRACK = null, CUM = [0], TLEN = 0, caveCells = [], DOOR = null, PLANKS = [];
  const TS = 16;
  const solidAt = (cx, cy) => cx < 0 || cy < 0 || cx >= MW || cy >= MH || rock[cy * MW + cx] === 1 || rubble[cy * MW + cx] === 1;
  function trackAt(s) {
    if (!TRACK) return { x: -99, y: -99, tx: 1, ty: 0, seg: 0 };
    s = clamp(s, 0, TLEN);
    let i = 0;
    while (i < TRACK.length - 2 && s > CUM[i + 1]) i++;
    const a = TRACK[i], b = TRACK[i + 1], Ls = CUM[i + 1] - CUM[i], f = (s - CUM[i]) / Ls;
    return { x: a[0] + (b[0] - a[0]) * f, y: a[1] + (b[1] - a[1]) * f, tx: (b[0] - a[0]) / Ls, ty: (b[1] - a[1]) / Ls, seg: i };
  }
  function playerRouteD() {
    let best = -1;
    for (const o of PC) { const l = toLevel(S.px, S.py - o), cx = Math.floor(l[0]), cy = Math.floor(l[1]); if (cx >= 0 && cy >= 0 && cx < MW && cy < MH) best = Math.max(best, routeD[cy * MW + cx]); }
    return best;
  }
  function setupLevel(i) {
    LI = i; L = LEVELS[i]; MW = L.w; MH = L.h; PLAN = L.plan;
    rock = new Uint8Array(MW * MH).fill(1); rubble = new Uint8Array(MW * MH); routeD = new Float32Array(MW * MH).fill(-1);
    for (const [x0, y0, x1, y1] of L.carve) for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) rock[y * MW + x] = 0;
    TRACK = L.track || null; CUM = [0];
    if (TRACK) for (let k = 1; k < TRACK.length; k++) CUM.push(CUM[k - 1] + Math.hypot(TRACK[k][0] - TRACK[k - 1][0], TRACK[k][1] - TRACK[k - 1][1]));
    TLEN = CUM[CUM.length - 1];
    caveCells = [];
    if (L.cave) {
      let base = 0;
      for (const [x0, y0, x1, y1, dir] of L.cave.route) {
        for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
          const d = base + (dir === 'L' ? x1 - (x + 0.5) : dir === 'R' ? x + 0.5 - x0 : y + 0.5 - y0);
          caveCells.push([d, x, y]);
        }
        base += dir === 'D' ? y1 - y0 : x1 - x0;
      }
      caveCells.sort((a, b) => a[0] - b[0]);
      for (const [d, x, y] of caveCells) routeD[y * MW + x] = d;
    }
    const rot = -L.door.dir * DEG;
    DOOR = { bx: L.door.bx, by: L.door.by, rot, ux: Math.sin(rot), uy: -Math.cos(rot), rx: Math.cos(rot), ry: Math.sin(rot) };
    PLANKS = (L.planks || []).map(p => ({ h: 0.36, ...p }));
  }

  // ---------------- state ----------------
  const S = {
    mode: 'idle', theta: 0, tx: 0, ty: 0, px: 0, py: 0, vx: 0, vy: 0, grounded: true, airTime: 0, stick: 0, u: 0, uVis: 0, hist: [],
    state: 'play', timer: 0, time: 0, runStart: 0, runTime: 0, step: 0, stepAt: 0, deaths: 0, riding: false,
    cart: { s: 0, ps: 0, v: 0, active: false, crashed: false }, planks: [], rocks: [],
    caveT0: -1, caveFront: -1, caveIdx: 0, anim: 'idle', animT: 0, flash: 0, flashColor: '', shake: 0, slow: 0, near: {},
    fx: [], pops: [], needleA: 0, needleV: 0, unlocked: false, doorF: 0, hint: 0, cam: [0, 0], done: false, dbg: {},
  };
  const prev = { theta: 0, tx: 0, ty: 0, px: 0, py: 0, cs: 0, pky: [], cam: [0, 0] };
  function snapshot() {
    prev.theta = S.theta; prev.tx = S.tx; prev.ty = S.ty; prev.px = S.px; prev.py = S.py; prev.cs = S.cart.s;
    prev.pky = S.planks.map(p => p.y);
    prev.cam = [S.cam[0], S.cam[1]];
    for (const r of S.rocks) { r.px = r.x; r.py = r.y; }
  }

  // ---------------- transforms ----------------
  function toLevel(wx, wy) { const c = Math.cos(S.theta), s = Math.sin(S.theta), dx = wx - S.tx, dy = wy - S.ty; return [dx * c + dy * s, -dx * s + dy * c]; }
  function toWorld(lx, ly) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [S.tx + lx * c - ly * s, S.ty + lx * s + ly * c]; }
  function vW(x, y) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [x * c - y * s, x * s + y * c]; }
  function vL(x, y) { const c = Math.cos(S.theta), s = Math.sin(S.theta); return [x * c + y * s, -x * s + y * c]; }
  const lp = () => toLevel(S.px, S.py - PC[0]);
  function railWorld() { const t = trackAt(S.cart.s); return toWorld(t.x, t.y); }
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
  let lvlBase = null, lvlCv = null, lvlCtx = null;
  function resetLevel(intro) {
    S.theta = 0; S.tx = 0; S.ty = 0; S.stick = 0; S.u = 0; S.hist = [];
    S.vx = 0; S.vy = 0; S.grounded = true; S.airTime = 0; S.state = intro ? 'intro' : 'play'; S.timer = intro ? P.intro : 0;
    S.anim = 'idle'; S.animT = 0; S.flash = 0; S.shake = 0; S.slow = 0; S.near = {}; S.fx = []; S.pops = [];
    S.needleA = 0; S.needleV = 0; S.doorF = 0; S.hint = 0;
    S.toast = null;
    rubble.fill(0);
    if (lvlCtx) { lvlCtx.globalCompositeOperation = 'copy'; lvlCtx.drawImage(lvlBase, 0, 0); lvlCtx.globalCompositeOperation = 'source-over'; }
    S.caveT0 = -1; S.caveFront = -1; S.caveIdx = 0; S.caveGap = 0;
    S.rocks = (L.rocks || []).map(([x, y, r, rel, dl]) => ({ x, y, r, rel, dl: dl ?? L.rockDelay, vx: 0, vy: 0, st: 'held', t: 0, px: x, py: y, rot: 0 }));
    S.cart = { s: 0, ps: 0, v: 0, active: false, crashed: false };
    S.planks = PLANKS.map(p => ({ y: p.top, v: 0, st: 'held', t: 0 }));
    S.riding = !!L.start.riding;
    if (S.riding) S.cart.active = true;
    S.step = 0; S.stepAt = S.time;
    if (S.riding) { const rw = railWorld(); S.px = rw[0]; S.py = rw[1] + FEET; }
    else { const w = toWorld(L.start.feet[0], L.start.feet[1]); S.px = w[0]; S.py = w[1]; }
    S.unlocked = doorTilt() <= 20;
    S.cam = camTarget(); S.done = false;
    S.runStart = S.time + (intro ? P.intro : 0); S.runTime = 0;
    snapshot();
  }
  function loadLevel(i) {
    setupLevel(i);
    lvlBase = buildLevel(); lvlCv = mk(lvlBase.width, lvlBase.height); lvlCtx = lvlCv.getContext('2d'); lvlCtx.drawImage(lvlBase, 0, 0);
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
  function step(dt) {
    S.time += dt;
    snapshot();
    const live = S.state === 'play' && S.mode !== 'idle';
    let u = 0;
    if (live && S.mode === 'demo') {
      const react = PLAN[S.step].react ?? L.react ?? P.react;
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
      const pv = S.riding ? railWorld() : S.grounded ? lc : S.hist[0];
      const c = Math.cos(dth), s = Math.sin(dth), rx = S.tx - pv[0], ry = S.ty - pv[1];
      S.tx = pv[0] + rx * c - ry * s; S.ty = pv[1] + rx * s + ry * c; S.theta += dth;
    }
    if (live) {
      stepCart(dt);
      stepPlayer(dt);
      stepRocks(dt);
      stepPlanks(dt);
      stepCave(dt);
      checkHazards();
      advancePlan();
      if (!S.done) S.runTime = S.time - S.runStart;
    } else if (S.mode !== 'idle' && S.state !== 'intro') { stepRocks(dt); stepPlanks(dt); }
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
  // camera: mostly the level's centre, pulled part of the way toward the player, so the whole small level stays in view
  function camTarget() {
    const c = toWorld(MW / 2, MH / 2), f = 0.4;
    return [c[0] + (S.px - c[0]) * f, c[1] + (S.py - 0.8 - c[1]) * f];
  }

  function stepPlayer(dt) {
    if (S.riding) {
      const rw = railWorld();
      S.px = rw[0]; S.py = rw[1] + FEET; S.vx = 0; S.vy = 0; S.grounded = true; S.airTime = 0;
      return;
    }
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

  function stepCart(dt) {
    const c = S.cart; c.ps = c.s;
    if (!TRACK || !c.active || c.crashed) return;
    const tp = trackAt(c.s);
    const a = G * (Math.sin(S.theta) * tp.tx + Math.cos(S.theta) * tp.ty);
    if (Math.abs(c.v) < 0.05 && Math.abs(a) < G * 0.1) { c.v = 0; return; }
    c.v = clamp(c.v + (a - CART_DRAG * c.v) * dt, -CART_MAX, CART_MAX);
    c.s += c.v * dt;
    const seg = trackAt(c.s).seg;
    if (seg !== tp.seg && L.cornerKeep) { c.v *= L.cornerKeep; S.shake = Math.max(S.shake, 0.25); for (let k = 0; k < 8; k++) S.fx.push({ x: tp.x, y: tp.y, vx: (Math.random() - 0.5) * 6, vy: (Math.random() - 0.5) * 6, life: 0.4, max: 0.4, k: 'spark', s: 0.09 }); }   // a sharp corner costs speed
    if (c.s <= 0) { c.s = 0; c.v = Math.max(0, c.v); }
    if (c.s >= TLEN) { c.s = TLEN; crash(); return; }
    if (Math.abs(c.v) > 7 && Math.random() < 0.6) {   // sparks off the wheels
      const t = trackAt(c.s);
      S.fx.push({ x: t.x, y: t.y, vx: -t.tx * c.v * 0.35 + (Math.random() - 0.5) * 2, vy: -t.ty * c.v * 0.35 + (Math.random() - 0.5) * 2, life: 0.35, max: 0.35, k: 'spark', s: 0.08 });
    }
  }
  function crash() {
    const c = S.cart; c.crashed = true;
    const tp = trackAt(TLEN), vw = vW(tp.tx * c.v, tp.ty * c.v);
    S.riding = false; S.vx = 0; S.vy = clamp(vw[1], -VMAX, VMAX); c.v = 0;
    S.shake = Math.max(S.shake, 0.7);
    for (let i = 0; i < 26; i++) S.fx.push({ x: tp.x + 0.4, y: tp.y + (Math.random() - 0.5), vx: 3 + Math.random() * 6, vy: (Math.random() - 0.5) * 8, life: 0.9, max: 0.9, k: 'debris', s: 0.12 + Math.random() * 0.12, grav: 1 });
    pop('哐！');
  }

  function stepRocks(dt) {
    const gx = Math.sin(S.theta), gy = Math.cos(S.theta), R = S.rocks, s25 = Math.sin(25 * DEG);
    for (const r of R) {
      if (r.st === 'held') {
        const go = r.rel === 'left' ? gx < -s25 : r.rel === 'right' ? gx > s25 : gy < Math.cos(25 * DEG);
        if (go) { r.st = 'shake'; r.t = r.dl * P.delayMul; }
        continue;
      }
      if (r.st === 'shake') { r.t -= dt; if (r.t <= 0) r.st = 'free'; continue; }
      r.vx += G * gx * dt; r.vy += G * gy * dt;
      const sp = Math.hypot(r.vx, r.vy); if (sp > 15) { r.vx *= 15 / sp; r.vy *= 15 / sp; }
      for (let k = 0; k < 2; k++) {
        r.x += r.vx * dt / 2; r.y += r.vy * dt / 2;
        const ns = [];
        const q = resolveCircleL(r.x, r.y, r.r, ns);
        r.x = q[0]; r.y = q[1];
        for (const n of ns) {
          const vn = r.vx * n[0] + r.vy * n[1];
          if (vn < 0) {
            if (vn < -3.5) { r.dead = true; shatter(r, n); break; }   // a hard hit smashes the rock where you just were
            r.vx -= 1.2 * vn * n[0]; r.vy -= 1.2 * vn * n[1];
            r.vx *= 0.995; r.vy *= 0.995;   // rolls freely along the floor
          }
        }
        if (r.dead) break;
      }
      r.rot += (r.vx * gy - r.vy * gx) * dt / r.r;
    }
    for (let i = 0; i < R.length; i++) for (let j = i + 1; j < R.length; j++) {
      const a = R[i], b = R[j];
      if (a.st !== 'free' && b.st !== 'free') continue;
      const dx = b.x - a.x, dy = b.y - a.y, d = Math.hypot(dx, dy), m = a.r + b.r;
      if (d >= m || d < 1e-6) continue;
      const nx = dx / d, ny = dy / d, pen = (m - d) / 2;
      if (a.st === 'free') { a.x -= nx * pen; a.y -= ny * pen; }
      if (b.st === 'free') { b.x += nx * pen; b.y += ny * pen; }
      const rv = (b.vx - a.vx) * nx + (b.vy - a.vy) * ny;
      if (rv < 0) { const im = -rv * 0.6; a.vx -= nx * im; a.vy -= ny * im; b.vx += nx * im; b.vy += ny * im; }
    }
    S.rocks = R.filter(r => !r.dead && !rubble[Math.floor(r.y) * MW + Math.floor(r.x)]);
  }

  function stepPlanks(dt) {
    const gy = Math.cos(S.theta), l = lp();
    PLANKS.forEach((d, i) => {
      const pk = S.planks[i];
      const inShaft = S.state === 'play' && l[0] > d.x0 && l[0] < d.x1 && l[1] > d.top;
      if (pk.st === 'held') {
        // you drop into its shaft: it starts to creak
        if (inShaft && gy > Math.cos(45 * DEG)) { pk.st = 'creak'; pk.t = 2.5; pk.wire = false; S.shake = Math.max(S.shake, 0.12); pop('嘎吱…'); }
        return;
      }
      if (pk.st === 'creak') {
        // you pass the tripwire near the bottom: it snaps a moment later
        if (!pk.wire && inShaft && l[1] > d.wire) { pk.wire = true; pk.t = Math.min(pk.t, d.delay * P.delayMul); }
        pk.t -= dt; if (pk.t <= 0) { pk.st = 'free'; pop('咔嚓'); }
        return;
      }
      if (pk.st !== 'free') return;
      pk.v += (G * gy - 0.1 * pk.v) * dt;
      pk.y += pk.v * dt;
      if (pk.y < d.top) { pk.y = d.top; pk.v = 0; }
      if (pk.y >= d.bottom) {
        pk.y = d.bottom; pk.st = 'down'; pk.v = 0; S.shake = Math.max(S.shake, 0.55);
        for (let k = 0; k < 22; k++) S.fx.push({ x: d.x0 + Math.random() * (d.x1 - d.x0), y: d.bottom - 0.2, vx: (Math.random() - 0.5) * 7, vy: -Math.random() * 6, life: 0.8, max: 0.8, k: 'wood', s: 0.1 + Math.random() * 0.12, grav: 1 });
      }
    });
  }

  function shatter(r, n) {
    S.shake = Math.max(S.shake, 0.35);
    for (let i = 0; i < 14; i++) S.fx.push({ x: r.x - n[0] * r.r, y: r.y - n[1] * r.r, vx: n[0] * (2 + Math.random() * 4) + (Math.random() - 0.5) * 5, vy: n[1] * (2 + Math.random() * 4) + (Math.random() - 0.5) * 5, life: 0.7, max: 0.7, k: 'debris', s: 0.1 + Math.random() * 0.14, grav: 1 });
    dust([r.x, r.y], 6);
  }
  function fillCave() {
    while (S.caveIdx < caveCells.length && caveCells[S.caveIdx][0] <= S.caveFront) {
      const [, cx, cy] = caveCells[S.caveIdx++];
      rubble[cy * MW + cx] = 1;
      paintRubble(cx, cy);
      if (Math.random() < 0.5) dust([cx + 0.5, cy + 0.5], 2);
    }
  }
  function stepCave(dt) {
    if (!L.cave) return;
    if (S.caveT0 < 0) {
      if (Math.cos(S.theta) < Math.cos(20 * DEG)) { S.caveT0 = S.time; S.shake = Math.max(S.shake, 0.3); pop('轰隆隆…'); }
      return;
    }
    if (S.time - S.caveT0 < L.cave.delay) { S.shake = Math.max(S.shake, 0.06); return; }
    if (S.caveFront < 0) { S.caveFront = 0; S.caveHist = [[S.time, 0]]; }
    const dp = playerRouteD(), gap = dp < 0 ? 0 : dp - S.caveFront;
    const H = S.caveHist;
    H.push([S.time, Math.max(H[H.length - 1][1], dp)]);
    const lag = S.cart.crashed && L.cave.lagEnd ? L.cave.lagEnd : L.cave.lag;
    if (lag) {
      // the collapse retraces your path exactly `lag` seconds behind you: stop longer than that and it buries you
      const tq = S.time - lag / P.caveMul;
      while (H.length > 1 && H[1][0] <= tq) H.shift();
      const target = H[0][0] <= tq ? H[0][1] : 0;
      S.caveFront = Math.min(S.caveFront + 16 * dt, Math.max(S.caveFront, target));
    } else {
      // it never lets you get far ahead: when the gap opens up it hurries to stay right behind you
      while (H.length > 2 && H[1][0] < S.time - 3) H.shift();
      const hz = L.cave.hurry || [[4, 1.9], [2.5, 1.35]], h = hz.find(q => gap > q[0]), hurry = h ? h[1] : 1;
      S.caveFront += L.cave.speed * P.caveMul * hurry * dt;
    }
    S.caveGap = gap;
    fillCave();
    S.shake = Math.max(S.shake, 0.1);
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
    toast(L.id + ' 通关！' + S.runTime.toFixed(1) + ' 秒', 'clear');
  }
  function nearMiss(kind) {
    if (S.time - (S.near[kind] ?? -9) < 1.6) return;
    S.near[kind] = S.time;
    pop('险！', true);
    if (P.slowmo && (!window.__mineHeadless || window.__mineVideo)) S.slow = 0.42;
  }

  const dbgMin = (k, v) => { S.dbg[k] = Math.min(S.dbg[k] ?? 99, v); };
  function checkHazards() {
    if (S.state !== 'play') return;
    const cs = PC.map(o => toLevel(S.px, S.py - o));
    // cave-in
    if (L.cave) {
      let caveNear = false;
      for (const c of cs) {
        const R0 = S.riding ? 3.2 : 1.4;
        for (let cy = Math.floor(c[1] - R0); cy <= Math.floor(c[1] + R0); cy++) for (let cx = Math.floor(c[0] - R0); cx <= Math.floor(c[0] + R0); cx++) {
          if (cx < 0 || cy < 0 || cx >= MW || cy >= MH || !rubble[cy * MW + cx]) continue;
          const qx = clamp(c[0], cx, cx + 1), qy = clamp(c[1], cy, cy + 1), d = Math.hypot(c[0] - qx, c[1] - qy);
          dbgMin('cave', d - PR);
          if (d < PR - 0.15) return die('被塌方埋住了');
          if (d < PR + (S.riding && L.cave.nearRide ? L.cave.nearRide : 0.9)) caveNear = true;
        }
      }
      if (caveNear) nearMiss('cave');
    }
    // rocks
    const pv = vL(S.vx, S.vy);
    for (const r of S.rocks) {
      if (r.st !== 'free') continue;
      const rel = Math.hypot(r.vx - pv[0], r.vy - pv[1]);
      for (const c of cs) {
        const d = Math.hypot(c[0] - r.x, c[1] - r.y);
        if (rel > 2.5) dbgMin('rock', d - r.r - PR);
        if (d < r.r + PR - 0.1 && rel > 2.5) return die('被落石砸中了');
        if (d < r.r + PR + 2.0 && rel > 3) nearMiss('rock');
      }
    }
    // planks
    for (let i = 0; i < PLANKS.length; i++) {
      const d = PLANKS[i], pk = S.planks[i];
      if (pk.st !== 'free' || pk.v <= 2) continue;
      for (const c of cs) {
        const inX = c[0] > d.x0 - PR && c[0] < d.x1 + PR, dy = Math.abs(c[1] - pk.y);
        if (inX) dbgMin('plank' + i, dy - d.h / 2 - PR);
        if (inX && dy < d.h / 2 + PR - 0.08) return die('被木板砸中了');
        if (inX && dy < d.h / 2 + PR + 1.0) nearMiss('plank');
      }
    }
    // door
    const c = cs[0], dx = c[0] - DOOR.bx, dy = c[1] - DOOR.by;
    const u = dx * DOOR.rx + dy * DOOR.ry, v = dx * DOOR.ux + dy * DOOR.uy;
    if (Math.abs(u) < 0.95 && v > -0.2 && v < 2.0) { if (S.unlocked) clear(); else S.hint = 1; }
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
    for (const p of S.fx) {
      p.life -= dt;
      const g = p.grav ? G : p.k === 'dust' ? 0.6 : 0;
      p.vx += gx * g * dt; p.vy += gy * g * dt;
      if (p.k === 'dust') { p.vx *= 1 - 1.5 * dt; p.vy *= 1 - 1.5 * dt; }
      p.x += p.vx * dt; p.y += p.vy * dt;
    }
    S.fx = S.fx.filter(p => p.life > 0);
    if (S.fx.length > 260) S.fx.splice(0, S.fx.length - 260);
    for (const p of S.pops) p.t += dt;
    S.pops = S.pops.filter(p => p.t < (p.big ? 0.9 : 0.8));
    S.shake *= Math.exp(-6 * dt);
    if (S.flash > 0) S.flash = Math.max(0, S.flash - dt);
  }

  // ---------------- art ----------------
  const COL = {
    airDark: [16, 11, 9], air: [30, 22, 17], rimHi: [196, 140, 86], rim: [138, 98, 62], mid: [78, 57, 41], deep: [43, 32, 25],
    beam: [94, 60, 34], beamHi: [128, 84, 48], rail: [150, 150, 160], sleeper: [88, 58, 34],
  };
  const put = (d, i, c) => { d[i] = c[0]; d[i + 1] = c[1]; d[i + 2] = c[2]; d[i + 3] = 255; };
  const shade = (c, k) => [clamp(c[0] + k, 0, 255), clamp(c[1] + k, 0, 255), clamp(c[2] + k * 0.8, 0, 255)];
  const mixc = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
  const rgb = c => `rgb(${c[0] | 0},${c[1] | 0},${c[2] | 0})`;
  function buildLevel() {
    const W = MW * TS, H = MH * TS, cv = mk(W, H), g = cv.getContext('2d');
    const img = g.createImageData(W, H), d = img.data;
    const open = (cx, cy) => !(cx < 0 || cy < 0 || cx >= MW || cy >= MH || rock[cy * MW + cx] === 1);
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
        if (dmin > 10 && hash(x >> 3, y >> 3) > 0.8) col = shade(col, 5);
      }
      put(d, i, col);
    }
    g.putImageData(img, 0, 0);
    g.save(); g.scale(TS, TS);
    // wooden support frames across corridors (only where both ends sit on rock)
    const isRock = (cx, cy) => cx < 0 || cy < 0 || cx >= MW || cy >= MH || rock[cy * MW + cx] === 1;
    const beams = [], lights = [];
    for (const [x0, y0, x1, y1] of L.carve) {
      const w = x1 - x0, h = y1 - y0;
      if (w > h && h <= 3) {
        for (let x = x0 + 2.4; x < x1 - 1.2; x += 3.3) if (isRock(Math.floor(x), y0 - 1) && isRock(Math.floor(x), y1) && isRock(Math.floor(x + 0.3), y0 - 1) && isRock(Math.floor(x + 0.3), y1)) beams.push([x, y0, y1, 'v']);
        const mx = Math.floor((x0 + x1) / 2) + 0.5; if (isRock(Math.floor(mx), y0 - 1)) lights.push([mx, y0 + 0.4]);
      } else if (h > w && w <= 3) {
        for (let y = y0 + 2.4; y < y1 - 1.2; y += 3.3) if (isRock(x0 - 1, Math.floor(y)) && isRock(x1, Math.floor(y)) && isRock(x0 - 1, Math.floor(y + 0.3)) && isRock(x1, Math.floor(y + 0.3))) beams.push([y, x0, x1, 'h']);
      } else lights.push([(x0 + x1) / 2, y0 + 0.4]);
    }
    for (const [a, b0, b1, o] of beams) {
      g.fillStyle = rgb(COL.beam);
      if (o === 'v') { g.fillRect(a, b0, 0.28, b1 - b0); g.fillStyle = rgb(COL.beamHi); g.fillRect(a, b0, 0.08, b1 - b0); }
      else { g.fillRect(b0, a, b1 - b0, 0.28); g.fillStyle = rgb(COL.beamHi); g.fillRect(b0, a, b1 - b0, 0.08); }
    }
    // the rail: sleepers + two rails
    if (TRACK) {
      g.lineCap = 'round';
      for (let s = 0; s <= TLEN; s += 0.7) {
        const t = trackAt(s), nx = -t.ty, ny = t.tx;
        g.strokeStyle = rgb(COL.sleeper); g.lineWidth = 0.16;
        g.beginPath(); g.moveTo(t.x - nx * 0.42, t.y - ny * 0.42); g.lineTo(t.x + nx * 0.42, t.y + ny * 0.42); g.stroke();
      }
      for (const off of [-0.22, 0.22]) {
        g.strokeStyle = rgb(COL.rail); g.lineWidth = 0.07; g.beginPath();
        TRACK.forEach((p, i) => {
          const a = TRACK[Math.max(0, i - 1)], b = TRACK[Math.min(TRACK.length - 1, i + 1)];
          let tx = b[0] - a[0], ty = b[1] - a[1]; const Ln = Math.hypot(tx, ty); tx /= Ln; ty /= Ln;
          const k = i > 0 && i < TRACK.length - 1 ? 1.41 : 1;
          const x = p[0] - ty * off * k, y = p[1] + tx * off * k;
          i ? g.lineTo(x, y) : g.moveTo(x, y);
        });
        g.stroke();
      }
      // broken rail end + warning stripes
      const e = trackAt(TLEN);
      g.save(); g.translate(e.x, e.y); g.rotate(Math.atan2(e.ty, e.tx));
      g.fillStyle = '#d9a23a'; g.fillRect(0.2, -1.15, 0.3, 2.3);
      g.fillStyle = '#2a1d14'; for (let k = 0; k < 4; k++) g.fillRect(0.2, -1.0 + k * 0.6, 0.3, 0.25);
      g.restore();
    }
    // lanterns
    for (const [x, y] of lights) {
      const gr = g.createRadialGradient(x, y, 0, x, y, 2.2);
      gr.addColorStop(0, 'rgba(255,190,110,0.32)'); gr.addColorStop(1, 'rgba(255,190,110,0)');
      g.fillStyle = gr; g.fillRect(x - 2.2, y - 2.2, 4.4, 4.4);
      g.fillStyle = '#ffd08a'; g.fillRect(x - 0.12, y - 0.1, 0.24, 0.3);
      g.fillStyle = '#5a3a1e'; g.fillRect(x - 0.16, y - 0.2, 0.32, 0.1);
    }
    g.restore();
    return cv;
  }
  function paintRubble(cx, cy) {
    if (!lvlCtx) return;
    const g = lvlCtx, x = cx * TS, y = cy * TS;
    g.fillStyle = '#3a2c22'; g.fillRect(x, y, TS, TS);
    for (let k = 0; k < 6; k++) {
      const h1 = hash(cx * 7 + k, cy * 13 - k), h2 = hash(cx * 3 - k, cy * 5 + k * 2), h3 = hash(cx + k * 11, cy + k * 17);
      const px = x + h1 * TS * 0.8, py = y + h2 * TS * 0.8, s = 3 + h3 * 7;
      g.fillStyle = h3 > 0.6 ? '#6b5240' : h3 > 0.3 ? '#56412f' : '#7d7266';
      g.beginPath(); g.moveTo(px, py + s * 0.5); g.lineTo(px + s * 0.4, py); g.lineTo(px + s, py + s * 0.3); g.lineTo(px + s * 0.8, py + s); g.lineTo(px + s * 0.1, py + s * 0.9); g.closePath(); g.fill();
    }
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
    K = Math.min(H / (Math.max(MW, MH) + 3), W / (Math.max(MW, MH) + 8)); CX = W / 2; CY = H * 0.53;
    const lp_ = (a, b) => a + (b - a) * alpha;
    const th = lp_(prev.theta, S.theta), Tx = lp_(prev.tx, S.tx), Ty = lp_(prev.ty, S.ty);
    const Px = lp_(prev.px, S.px), Py = lp_(prev.py, S.py);
    let camx = lp_(prev.cam[0], S.cam[0]), camy = lp_(prev.cam[1], S.cam[1]);
    if (S.shake > 0.01) { camx += (Math.random() - 0.5) * S.shake * 0.9; camy += (Math.random() - 0.5) * S.shake * 0.9; }
    const c = Math.cos(th), s = Math.sin(th);
    const ox = CX + K * (Tx - camx), oy = CY + K * (Ty - camy);
    const level = () => ctx.setTransform(K * c, K * s, -K * s, K * c, ox, oy);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = '#0a0706'; ctx.fillRect(0, 0, W, H);
    ctx.imageSmoothingEnabled = false;
    level();
    ctx.fillStyle = 'rgb(43,32,25)'; ctx.fillRect(-40, -40, MW + 80, MH + 80);
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
    // planks
    PLANKS.forEach((d, i) => {
      const pk = S.planks[i], pky = lp_(prev.pky[i] ?? pk.y, pk.y), w = d.x1 - d.x0;
      ctx.save();
      if (pk.st === 'creak') ctx.translate((Math.random() - 0.5) * 0.08, (Math.random() - 0.5) * 0.08);
      ctx.fillStyle = '#8b5a2b'; ctx.fillRect(d.x0 + 0.02, pky - d.h / 2, w - 0.04, d.h);
      ctx.fillStyle = '#a8743c'; ctx.fillRect(d.x0 + 0.02, pky - d.h / 2, w - 0.04, 0.07);
      ctx.fillStyle = '#5e3a1a'; for (let k = 0; k < 3; k++) ctx.fillRect(d.x0 + 0.4 + k * 0.95, pky - 0.05, 0.5, 0.04);
      if (pk.st === 'held' || pk.st === 'creak') { ctx.fillStyle = '#4a3020'; ctx.fillRect(d.x0 - 0.05, pky - 0.3, 0.2, 0.25); ctx.fillRect(d.x1 - 0.15, pky - 0.3, 0.2, 0.25); }
      ctx.restore();
    });
    // rocks
    for (const r of S.rocks) {
      const rx = lp_(r.px, r.x), ry = lp_(r.py, r.y);
      ctx.save(); ctx.translate(rx, ry);
      if (r.st === 'shake') ctx.translate((Math.random() - 0.5) * 0.08, (Math.random() - 0.5) * 0.08);
      ctx.rotate(r.rot);
      ctx.fillStyle = '#5f5a55'; ctx.beginPath();
      for (let k = 0; k < 9; k++) { const a = k / 9 * Math.PI * 2, rr = r.r * (0.86 + 0.14 * hash(k, r.r * 10)); k ? ctx.lineTo(Math.cos(a) * rr, Math.sin(a) * rr) : ctx.moveTo(Math.cos(a) * rr, Math.sin(a) * rr); }
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#8e8780'; ctx.beginPath(); ctx.arc(-r.r * 0.25, -r.r * 0.25, r.r * 0.35, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#3a3633'; ctx.lineWidth = 0.05; ctx.beginPath(); ctx.moveTo(r.r * 0.1, -r.r * 0.4); ctx.lineTo(r.r * 0.3, 0.1); ctx.lineTo(r.r * 0.05, r.r * 0.45); ctx.stroke();
      ctx.restore();
    }
    // particles
    for (const p of S.fx) {
      const a = clamp(p.life / p.max, 0, 1);
      ctx.globalAlpha = p.k === 'dust' ? a * 0.55 : a;
      ctx.fillStyle = p.k === 'spark' ? '#ffd37a' : p.k === 'wood' ? '#a8743c' : p.k === 'debris' ? '#8a8a92' : '#8a7462';
      const sz = p.k === 'dust' ? p.s * (1.6 - a * 0.6) : p.s;
      ctx.fillRect(p.x - sz / 2, p.y - sz / 2, sz, sz);
    }
    ctx.globalAlpha = 1;
    const toS = (wx, wy) => [CX + K * (wx - camx), CY + K * (wy - camy)];
    let bS = null;
    if (TRACK) {
      // cart: trolley on the rail (level space) + bucket hanging along screen-down
      const ct = trackAt(lp_(prev.cs, S.cart.s));
      ctx.save(); ctx.translate(ct.x, ct.y); ctx.rotate(Math.atan2(ct.ty, ct.tx));
      if (S.cart.crashed) ctx.rotate(0.35);
      ctx.fillStyle = '#2b2b31'; ctx.fillRect(-0.55, -0.16, 1.1, 0.32);
      ctx.fillStyle = '#1a1a1e'; for (const wx of [-0.38, 0.38]) { ctx.beginPath(); ctx.arc(wx, 0, 0.2, 0, Math.PI * 2); ctx.fill(); }
      ctx.restore();
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      const rwx = Tx + ct.x * c - ct.y * s, rwy = Ty + ct.x * s + ct.y * c;
      const rS = toS(rwx, rwy); bS = toS(rwx, rwy + HANG);
      ctx.strokeStyle = '#6f6f78'; ctx.lineWidth = Math.max(1, K * 0.06);
      ctx.beginPath(); ctx.moveTo(rS[0], rS[1]); ctx.lineTo(bS[0], bS[1] - K * 0.3); ctx.stroke();
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    // player
    const fr = animFrame(dt), sc = K * 1.54 / 92, pS = toS(Px, Py);
    let src = IMG[fr.img];
    if (S.state === 'dead') src = IMG[fr.img + 'Red'];
    ctx.globalAlpha = S.state === 'clear' ? Math.max(0, 1 - (2.0 - S.timer) * 1.6) : S.state === 'over' ? 0 : 1;
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(src, fr.idx * 96, 0, 96, 112, Math.round(pS[0] - 49.5 * sc), Math.round(pS[1] - 112 * sc), Math.round(96 * sc), Math.round(112 * sc));
    ctx.globalAlpha = 1;
    if (bS) {
      // bucket front (covers the legs while riding)
      ctx.save(); ctx.translate(bS[0], bS[1]);
      const bw = K * 1.5, bh = K * 0.85;
      ctx.fillStyle = '#4c4f59'; ctx.beginPath(); ctx.moveTo(-bw / 2, -bh * 0.35); ctx.lineTo(bw / 2, -bh * 0.35); ctx.lineTo(bw * 0.4, bh * 0.65); ctx.lineTo(-bw * 0.4, bh * 0.65); ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#8d919c'; ctx.fillRect(-bw / 2 - K * 0.04, -bh * 0.35 - K * 0.08, bw + K * 0.08, K * 0.12);
      ctx.fillStyle = '#353841'; ctx.fillRect(-bw * 0.32, -bh * 0.05, bw * 0.64, K * 0.06); ctx.fillRect(-bw * 0.28, bh * 0.25, bw * 0.56, K * 0.06);
      ctx.restore();
    }
    // speed lines
    const spd = S.riding ? Math.abs(S.cart.v) : Math.abs(S.vy);
    if (spd > 7.5) {
      let dx = 0, dy = -1;
      if (S.riding) { const t = trackAt(S.cart.s); const w = vW(t.tx * Math.sign(S.cart.v), t.ty * Math.sign(S.cart.v)); dx = -w[0]; dy = -w[1]; }
      else if (S.vy < 0) dy = 1;
      ctx.strokeStyle = 'rgba(255,236,210,0.28)'; ctx.lineWidth = Math.max(1, K * 0.035);
      const n = Math.min(14, Math.floor((spd - 7) * 2.2));
      for (let k = 0; k < n; k++) {
        const h1 = hash(k * 3.1, Math.floor(S.time * 14) + k), h2 = hash(k * 7.7, Math.floor(S.time * 14) - k);
        const off = (h1 - 0.5) * K * 7, along = (h2 - 0.2) * K * 6, len = K * (1.2 + spd * 0.12);
        const x0 = pS[0] - dy * off + dx * along, y0 = pS[1] - K * 0.8 + dx * off + dy * along;
        ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x0 + dx * len, y0 + dy * len); ctx.stroke();
      }
    }
    // cave mood: soft vignette
    const vg = ctx.createRadialGradient(CX, CY, H * 0.3, CX, CY, H * 0.95);
    vg.addColorStop(0, 'rgba(0,0,0,0)'); vg.addColorStop(1, 'rgba(0,0,0,0.55)');
    ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H);
    if (S.flash > 0) { ctx.fillStyle = S.flashColor + Math.min(0.35, S.flash).toFixed(3) + ')'; ctx.fillRect(0, 0, W, H); }
    drawHud(pS);
  }

  function drawHud(pS) {
    const u = Math.max(1, H / 640);
    ctx.textBaseline = 'alphabetic';
    // pops near the player
    for (const p of S.pops) {
      const a = 1 - p.t / (p.big ? 0.9 : 0.8), rise = p.t * K * 1.6;
      ctx.globalAlpha = clamp(a * 1.5, 0, 1);
      ctx.font = `600 ${Math.round((p.big ? 34 : 20) * u)}px "Noto Serif SC", serif`;
      ctx.textAlign = 'center';
      ctx.lineWidth = 4 * u; ctx.strokeStyle = 'rgba(0,0,0,0.75)';
      ctx.fillStyle = p.big ? '#ff8a6a' : '#ffe2bd';
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
      ctx.strokeText(S.toast.text, CX, CY - H * 0.18); ctx.fillText(S.toast.text, CX, CY - H * 0.18);
      ctx.globalAlpha = 1;
    }
    // level badge, top-left
    ctx.textAlign = 'left'; ctx.fillStyle = '#f0a64a'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText(L.id, 14 * u, 26 * u);
    ctx.fillStyle = '#eadfd2'; ctx.font = `600 ${Math.round(16 * u)}px "Noto Serif SC", serif`;
    ctx.fillText(L.name, 14 * u + ctx.measureText(L.id).width + 40 * u, 26 * u);
    if (S.state === 'intro') { drawIntro(u); return; }
    if (S.mode === 'idle') return;
    // caption of the current beat
    const st = PLAN[S.step], age = S.time - S.stepAt, ap = clamp(age / 0.22, 0, 1);
    if (S.state === 'play' || S.state === 'dead') {
      const bw = Math.min(W * 0.62, 520 * u), bh = (st.sub ? 64 : 46) * u, bx = CX - bw / 2, by = 12 * u - (1 - ap) * 10 * u;
      ctx.globalAlpha = 0.25 + 0.75 * ap;
      ctx.fillStyle = 'rgba(14,9,7,0.78)'; rr(bx, by, bw, bh, 10 * u); ctx.fill();
      ctx.strokeStyle = 'rgba(240,166,74,0.55)'; ctx.lineWidth = 1.5 * u; rr(bx, by, bw, bh, 10 * u); ctx.stroke();
      ctx.textAlign = 'left'; ctx.fillStyle = '#f0a64a'; ctx.font = `600 ${Math.round(13 * u)}px ui-monospace, Menlo, monospace`;
      ctx.fillText(`第 ${S.step + 1} 拍`, bx + 14 * u, by + 28 * u);
      ctx.textAlign = 'center'; ctx.fillStyle = '#fff1df'; ctx.font = `600 ${Math.round(21 * u)}px "Noto Serif SC", serif`;
      ctx.fillText(st.cap, CX + 20 * u, by + 30 * u);
      if (st.sub) { ctx.fillStyle = '#cdb8a4'; ctx.font = `${Math.round(14 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`; ctx.fillText(st.sub, CX + 20 * u, by + 52 * u); }
      ctx.globalAlpha = 1;
    }
    // timer + mode
    ctx.textAlign = 'left'; ctx.fillStyle = '#eadfd2'; ctx.font = `600 ${Math.round(15 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText(S.runTime.toFixed(1) + ' s', 14 * u, H - 18 * u);
    ctx.fillStyle = S.mode === 'demo' ? '#f0a64a' : '#a8968a'; ctx.font = `${Math.round(12 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.mode === 'demo' ? '演示中' : '自己玩', 14 * u, H - 38 * u);
    // beat timeline
    const n = PLAN.length, gap = 26 * u, tw = (n - 1) * gap, tx0 = CX - tw / 2, ty = H - 26 * u;
    ctx.fillStyle = 'rgba(14,9,7,0.6)'; rr(tx0 - 16 * u, ty - 14 * u, tw + 32 * u, 28 * u, 14 * u); ctx.fill();
    ctx.strokeStyle = 'rgba(234,223,210,0.25)'; ctx.lineWidth = 2 * u; ctx.beginPath(); ctx.moveTo(tx0, ty); ctx.lineTo(tx0 + tw, ty); ctx.stroke();
    for (let i = 0; i < n; i++) {
      const x = tx0 + i * gap, done = i < S.step || S.state === 'clear' || S.state === 'over', cur = i === S.step && S.state === 'play';
      ctx.beginPath(); ctx.arc(x, ty, (cur ? 7 + Math.sin(S.time * 10) * 1.5 : 5) * u, 0, Math.PI * 2);
      ctx.fillStyle = done ? '#f0a64a' : cur ? '#fff1df' : 'rgba(20,14,11,0.9)'; ctx.fill();
      ctx.strokeStyle = done || cur ? '#f0a64a' : 'rgba(234,223,210,0.45)'; ctx.lineWidth = 1.5 * u; ctx.stroke();
    }
    // steering wheel = the controller input
    const wx = W - 52 * u, wy = H - 52 * u, wr = 30 * u;
    S.uVis += (S.u - S.uVis) * 0.25;
    ctx.save(); ctx.translate(wx, wy);
    ctx.fillStyle = 'rgba(14,9,7,0.7)'; ctx.beginPath(); ctx.arc(0, 0, wr + 10 * u, 0, Math.PI * 2); ctx.fill();
    const full = Math.abs(S.u) > 0.95;
    if (Math.abs(S.uVis) > 0.05) {
      ctx.strokeStyle = full ? '#f0a64a' : 'rgba(240,166,74,0.6)'; ctx.lineWidth = 4 * u; ctx.beginPath();
      ctx.arc(0, 0, wr + 5 * u, -Math.PI / 2, -Math.PI / 2 + S.uVis * 1.4, S.uVis < 0); ctx.stroke();
    }
    ctx.rotate(S.uVis * 75 * DEG);
    ctx.strokeStyle = '#eadfd2'; ctx.lineWidth = 4 * u; ctx.beginPath(); ctx.arc(0, 0, wr, 0, Math.PI * 2); ctx.stroke();
    ctx.lineWidth = 3 * u; ctx.beginPath(); ctx.moveTo(-wr, 0); ctx.lineTo(wr, 0); ctx.moveTo(0, 0); ctx.lineTo(0, wr); ctx.stroke();
    ctx.fillStyle = '#f0a64a'; ctx.beginPath(); ctx.arc(0, -wr, 4 * u, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#eadfd2'; ctx.beginPath(); ctx.arc(0, 0, 6 * u, 0, Math.PI * 2); ctx.fill();
    ctx.restore();
    ctx.fillStyle = '#a8968a'; ctx.textAlign = 'center'; ctx.font = `${Math.round(11 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
    ctx.fillText(S.u > 0.05 ? '顺时针' : S.u < -0.05 ? '逆时针' : '手柄', wx, wy - wr - 16 * u);
  }
  // title card shown for a moment when a level starts
  function drawIntro(u) {
    const t = P.intro - S.timer, a = clamp(Math.min(t / 0.18, S.timer / 0.25), 0, 1);
    ctx.globalAlpha = a * 0.72; ctx.fillStyle = '#0a0706'; ctx.fillRect(0, 0, W, H);
    ctx.globalAlpha = a; ctx.textAlign = 'center';
    ctx.fillStyle = '#f0a64a'; ctx.font = `600 ${Math.round(22 * u)}px ui-monospace, Menlo, monospace`;
    ctx.fillText('矿坑 ' + L.id, CX, CY - 40 * u);
    ctx.fillStyle = '#fff1df'; ctx.font = `600 ${Math.round(52 * u)}px "Noto Serif SC", serif`;
    ctx.fillText(L.name, CX, CY + 22 * u);
    ctx.fillStyle = '#cdb8a4'; ctx.font = `${Math.round(16 * u)}px "PingFang SC", "Microsoft YaHei", sans-serif`;
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
      ovText.textContent = L.id + ' ' + L.name + '：' + L.tip;
    } else {
      ovDemo.textContent = S.mode === 'demo' ? '▶ 再看一遍' : '▶ 看演示';
      ovPlay.textContent = S.mode === 'demo' ? '自己玩这关' : '再玩一次';
      ovNext.hidden = !nx;
      if (nx) ovNext.textContent = '下一关 ' + nx.id + ' ' + nx.name + ' ▶';
      ovText.textContent = L.id + ' ' + (S.mode === 'demo' ? '演示通关' : '通关') + '，用时 ' + S.runTime.toFixed(1) + ' 秒' + (S.deaths ? '，死亡 ' + S.deaths + ' 次' : '') + (nx ? '。' : '。矿坑四关都过完了。');
    }
  }
  function onRunOver() {
    if (window.__mineHeadless) return;
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
  const sliders = [['tRot', 'oRot', 'rotSpeed', v => v + '°/s'], ['tCave', 'oCave', 'caveMul', v => '×' + v.toFixed(2)], ['tDelay', 'oDelay', 'delayMul', v => '×' + v.toFixed(2)]];
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
    if (window.__mineStop) return;
    try {
      const now = ts / 1000; let dt = last ? now - last : 0; last = now; dt = Math.min(dt, 0.1);
      pollPad();
      if (!window.__mineHeadless) {
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
  const load = (src) => new Promise((res, rej) => { const im = new Image(); im.onload = () => res(im); im.onerror = rej; im.src = src; });
  Promise.all(Object.entries(A).map(([k, v]) => load(v).then(im => { IMG[k] = im; }))).then(() => {
    for (const k of ['idle', 'float', 'fallstart', 'land']) IMG[k + 'Red'] = tinted(IMG[k], 'rgba(226,70,96,0.6)');
    resize(); window.addEventListener('resize', resize);
    selectLevel(0, true);
    window.__mineReady = true;
    requestAnimationFrame(frame);
  });
  window.__mine = {
    S, P, LEVELS, step, startRun, loadLevel, resetLevel, input, DT, lp, trackAt,
    get L() { return L; }, get LI() { return LI; }, get PLAN() { return PLAN; }, get CUM() { return CUM; }, get TLEN() { return TLEN; },
    render: () => render(0), renderAt: (a, dt) => { alpha = a; render(dt); },
  };
})();
