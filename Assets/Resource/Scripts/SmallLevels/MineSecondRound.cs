using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace Resource.Scripts.SmallLevels
{
    public partial class SmallLevelData
    {
        public RailBranchSeed branch;
        public TntSeed[] tnts = Array.Empty<TntSeed>();
        public Vector4[] soft = Array.Empty<Vector4>(), gas = Array.Empty<Vector4>();
        public Vector2[] drills = Array.Empty<Vector2>();
        public GasVentSeed[] gasVents = Array.Empty<GasVentSeed>();
        public float tntG = 1f, tntFuse, tntHit = 7f, blastKill = 2f;
        public bool tntGravitySpecified;
        public float ventDelay = float.MaxValue, gasTick = .06f, gasTol = .25f;
    }
    [Serializable] public class RailBranchSeed { public int at; public Vector2[] track = Array.Empty<Vector2>(); }
    [Serializable] public class TntSeed
    {
        public float x, y, delay = .2f;
        public string direction;
        public bool hasArm;
        public Vector4 arm;
    }
    [Serializable] public class GasVentSeed { public int x, y; public float rate; public int max = int.MaxValue; }

    public sealed partial class MineMechanisms
    {
        public TileBase[] weakRockTiles = Array.Empty<TileBase>(), softDirtTiles = Array.Empty<TileBase>();
        public TileBase gasTile, gasRimTile;
        [SerializeField] private Tilemap weakRockMap, softDirtMap, gasMap;
        [SerializeField] private Tilemap[] gasRimMaps = Array.Empty<Tilemap>();
        [SerializeField] private SpriteRenderer[] drillVisuals = Array.Empty<SpriteRenderer>(), ventVisuals = Array.Empty<SpriteRenderer>(), gasPuffs = Array.Empty<SpriteRenderer>();
        [SerializeField] private LineRenderer[] switchLines = Array.Empty<LineRenderer>();
        private bool[] weakAt, softAt, gasAt;
        private Vector2[] activeTrack, alternateTrack;
        private int branchChoice, gasTickCount;
        private float gasAccumulator, gasBreath, coughAt = -1f, blastFlash;
        private bool ventsOpen;
        private Drill[] drillStates;
        private GasVent[] gasVentStates;
        private Image blastOverlay;
        private readonly List<int> gasCells = new List<int>();
        private readonly Queue<int> gasQueue = new Queue<int>();
        private bool[] gasSeen;
        private static readonly Vector2Int[] GasDirections = { new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1), new Vector2Int(-1, 0), new Vector2Int(-1, -1) };
        private static readonly Vector2Int[] GasSearchDirections = { new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1) };
        private readonly List<BlastRing> blastRings = new List<BlastRing>();

        private sealed class Drill
        {
            public Vector2 position, previous, velocity;
            public float spin;
            public bool digging, dead;
        }
        private sealed class GasVent { public float accumulator; public int emitted; }
        private sealed class BlastRing { public Vector2 position; public float age; public LineRenderer line; }

        private void BuildSecondRoundVisuals(Transform root)
        {
            weakRockMap = MakeMineMap("WeakRock", data.weak.Length > 0, true, 5);
            softDirtMap = MakeMineMap("SoftDirt", data.soft.Length > 0, true, 5);
            PaintMineMap(weakRockMap, data.weak, weakRockTiles);
            PaintMineMap(softDirtMap, data.soft, softDirtTiles);
            bool hasGas = data.gas.Length > 0 || data.gasVents.Length > 0;
            gasMap = MakeMineMap("Gas", hasGas, false, 14);
            if (gasMap != null) gasMap.color = new Color(1f, 1f, 1f, .45f);
            gasRimMaps = new Tilemap[hasGas ? 4 : 0];
            for (int i = 0; i < 4; i++)
            {
                Tilemap map = MakeMineMap("GasRim_" + i, hasGas, false, 15);
                if (hasGas) { gasRimMaps[i] = map; map.color = new Color(1f, 1f, 1f, .5f); }
            }
            if (gasMap != null) PaintMineMap(gasMap, data.gas, new[] { gasTile });
            drillVisuals = new SpriteRenderer[data.drills.Length];
            for (int i = 0; i < drillVisuals.Length; i++)
            {
                drillVisuals[i] = MakeSprite("Drill_" + i, SpriteNamed("drill_bit"), data.drills[i], 13);
                drillVisuals[i].transform.SetParent(root, false);
            }
            ventVisuals = new SpriteRenderer[data.gasVents.Length];
            gasPuffs = new SpriteRenderer[data.gasVents.Length * 4];
            for (int i = 0; i < data.gasVents.Length; i++)
            {
                GasVentSeed seed = data.gasVents[i];
                Vector2 center = new Vector2(seed.x + .5f, seed.y + .5f);
                ventVisuals[i] = MakeSprite("GasVent_" + i, SpriteNamed("gas_vent"), center, 13);
                ventVisuals[i].transform.SetParent(root, false);
                for (int k = 0; k < 4; k++)
                {
                    var puff = MakeSprite("GasPuff_" + i + "_" + k, SpriteNamed("gas_puff"), center, 16);
                    puff.transform.SetParent(root, false); puff.enabled = false; gasPuffs[i * 4 + k] = puff;
                }
            }
            switchLines = new LineRenderer[HasBranch ? 7 : 0];
            for (int i = 0; i < switchLines.Length; i++)
            {
                bool active = i < 3;
                switchLines[i] = MakeLine("RailSwitch_" + i, active ? .14f : .08f,
                    active ? new Color32(255, 214, 90, 230) : new Color32(120, 110, 100, 204), 24);
                switchLines[i].transform.SetParent(root, false);
            }
            DrawRailSwitch();
        }

        private Tilemap MakeMineMap(string name, bool needed, bool collision, int order)
        {
            Transform grid = level.Find("Grid");
            Transform old = grid.Find(name);
            if (old != null) DestroyImmediate(old.gameObject);
            if (!needed) return null;
            var go = new GameObject(name); go.transform.SetParent(grid, false);
            go.layer = LayerMask.NameToLayer("Wall");
            var map = go.AddComponent<Tilemap>(); map.tileAnchor = new Vector3(0f, 1f, 0f);
            var renderer = go.AddComponent<TilemapRenderer>(); renderer.sortingOrder = order;
            renderer.sharedMaterial = rubbleMap.GetComponent<TilemapRenderer>().sharedMaterial;
            if (collision)
            {
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f; body.useFullKinematicContacts = true;
                body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                go.AddComponent<TilemapCollider2D>();
                var bodies = new List<Rigidbody2D>();
                foreach (Rigidbody2D existing in world.attachedGeometryBodies) if (existing != null) bodies.Add(existing);
                bodies.Add(body); world.attachedGeometryBodies = bodies.ToArray();
            }
            return map;
        }

        private void PaintMineMap(Tilemap map, Vector4[] rectangles, TileBase[] tiles)
        {
            if (map == null || tiles.Length == 0) return;
            foreach (Vector4 rect in rectangles)
            for (int y = Mathf.FloorToInt(rect.y); y < rect.w; y++)
            for (int x = Mathf.FloorToInt(rect.x); x < rect.z; x++)
                if (Inside(x, y)) map.SetTile(new Vector3Int(x, -y - 1, 0), tiles[(x + y) % tiles.Length]);
            var collider = map.GetComponent<TilemapCollider2D>();
            if (collider != null) collider.ProcessTilemapChanges();
        }

        private void InitializeSecondRound()
        {
            weakAt = new bool[solid.Length]; softAt = new bool[solid.Length]; gasAt = new bool[solid.Length]; gasSeen = new bool[solid.Length];
            MarkMineCells(data.weak, weakAt, true); MarkMineCells(data.soft, softAt, true); MarkMineCells(data.gas, gasAt, false);
            drillStates = new Drill[data.drills.Length];
            for (int i = 0; i < drillStates.Length; i++) drillStates[i] = new Drill { position = data.drills[i], previous = data.drills[i] };
            gasVentStates = new GasVent[data.gasVents.Length];
            for (int i = 0; i < gasVentStates.Length; i++) gasVentStates[i] = new GasVent();
            if (HasBranch)
            {
                alternateTrack = new Vector2[data.branch.at + 1 + data.branch.track.Length];
                Array.Copy(data.track, alternateTrack, data.branch.at + 1);
                Array.Copy(data.branch.track, 0, alternateTrack, data.branch.at + 1, data.branch.track.Length);
            }
            DrawGas();
        }

        private void MarkMineCells(Vector4[] rectangles, bool[] mask, bool replaceHardRock)
        {
            foreach (Vector4 rect in rectangles)
            for (int y = Mathf.FloorToInt(rect.y); y < rect.w; y++)
            for (int x = Mathf.FloorToInt(rect.x); x < rect.z; x++)
            {
                if (!Inside(x, y)) continue;
                int cell = y * data.w + x; mask[cell] = true;
                if (replaceHardRock) solid[cell] = false;
            }
        }

        private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < data.w && y < data.h;
        protected override bool SolidAt(int x, int y)
        {
            if (base.SolidAt(x, y)) return true;
            if (!Inside(x, y) || weakAt == null) return false;
            int cell = y * data.w + x;
            return weakAt[cell] || softAt[cell];
        }

        private bool HasBranch => data.branch != null && data.branch.at >= 0 && data.branch.at < data.track.Length - 1 && data.branch.track.Length > 0;
        private void SetTrack(Vector2[] track)
        {
            activeTrack = track;
            trackDistances = new float[track.Length];
            for (int i = 1; i < track.Length; i++) trackDistances[i] = trackDistances[i - 1] + Vector2.Distance(track[i - 1], track[i]);
        }

        private bool PickBranch()
        {
            int at = data.branch.at;
            Vector2 main = (data.track[at + 1] - data.track[at]).normalized;
            Vector2 alternate = (data.branch.track[0] - data.track[at]).normalized;
            return Vector2.Dot(Gravity, alternate) > Vector2.Dot(Gravity, main);
        }

        private void StepRailSwitch(float previous, float current)
        {
            if (!HasBranch) return;
            float junction = trackDistances[data.branch.at];
            if (previous < junction && current >= junction)
            {
                bool alternate = PickBranch();
                SetTrack(alternate ? alternateTrack : data.track); branchChoice = alternate ? 2 : 1;
                feedback.Pop(alternate ? "pop.rail_turn" : "pop.rail_straight");
            }
            else if (previous >= junction && current < junction)
            {
                SetTrack(data.track); branchChoice = 0;
            }
        }

        private void DrawRailSwitch()
        {
            if (!HasBranch || switchLines.Length != 7) return;
            int at = data.branch.at;
            Vector2 junction = data.track[at];
            bool alternate = branchChoice == 2 || branchChoice == 0 && PickBranch();
            Vector2 chosen = ((alternate ? data.branch.track[0] : data.track[at + 1]) - junction).normalized;
            Vector2 other = ((alternate ? data.track[at + 1] : data.branch.track[0]) - junction).normalized;
            Vector2 side = new Vector2(-chosen.y, chosen.x);
            SetSwitchLine(0, junction, junction + chosen * 1.3f);
            SetSwitchLine(1, junction + chosen * 1.9f, junction + chosen * 1.55f + side * .3f);
            SetSwitchLine(2, junction + chosen * 1.9f, junction + chosen * 1.55f - side * .3f);
            for (int i = 0; i < 4; i++) SetSwitchLine(i + 3, junction + other * (i * .3f), junction + other * Mathf.Min(1.1f, i * .3f + .15f));
        }

        private void SetSwitchLine(int i, Vector2 a, Vector2 b)
        {
            switchLines[i].SetPosition(0, ToWorld(a)); switchLines[i].SetPosition(1, ToWorld(b));
        }

        private void StepSecondRound(float dt)
        {
            StepDrills(dt);
            if (data.gas.Length > 0 || data.gasVents.Length > 0) StepGas(dt);
        }

        private void Explode(Rock rock)
        {
            if (rock.state == 3) return;
            rock.state = 3; rock.fuse = -1f;
            for (int y = Mathf.Max(0, Mathf.FloorToInt(rock.position.y - 2.4f)); y <= Mathf.Min(data.h - 1, Mathf.FloorToInt(rock.position.y + 2.4f)); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(rock.position.x - 2.4f)); x <= Mathf.Min(data.w - 1, Mathf.FloorToInt(rock.position.x + 2.4f)); x++)
            {
                int cell = y * data.w + x;
                if (!weakAt[cell] || RectDistance(rock.position, new Vector4(x, y, x + 1, y + 1)) >= 2.4f) continue;
                weakAt[cell] = false; weakRockMap.SetTile(new Vector3Int(x, -y - 1, 0), null);
                Burst(new Vector2(x + .5f, y + .5f), 7, new Color32(138, 138, 146, 255), 6f, 1f, .2f);
            }
            if (weakRockMap != null)
            {
                var collider = weakRockMap.GetComponent<TilemapCollider2D>();
                if (collider != null) collider.ProcessTilemapChanges();
            }
            foreach (Rock other in rocks)
            {
                if (other == rock || other.state == 3 || Vector2.Distance(other.position, rock.position) >= 2.4f + other.radius) continue;
                if (other.tnt) { if (other.fuse < 0f) other.fuse = .14f; }
                else
                {
                    other.state = 3;
                    Burst(other.position, 14, new Color32(138, 138, 146, 255), 4f, .7f, .16f); Dust(other.position, 6);
                }
            }
            feedback.Pop("pop.boom"); feedback.Shake(.95f); blastFlash = .3f;
            Burst(rock.position, 34, new Color32(255, 170, 70, 255), 7f, .55f, .2f);
            Burst(rock.position, 12, new Color32(255, 211, 122, 255), 5f, .55f, .08f); Dust(rock.position, 12);
            var line = MakeLine("BlastShockwave", .12f, new Color32(255, 226, 168, 230), 26);
            line.positionCount = 33;
            blastRings.Add(new BlastRing { position = rock.position, line = line });
            if (!Alive) return;
            float distance = Mathf.Min(Vector2.Distance(circles[0], rock.position), Vector2.Distance(circles[1], rock.position)) - radius;
            if (distance < data.blastKill) player.Die();
            else if (distance < data.blastKill + 1.6f) feedback.NearMiss("boom");
        }

        private void StepDrills(float dt)
        {
            Vector2 gravity = Gravity, side = new Vector2(-gravity.y, gravity.x);
            foreach (Drill drill in drillStates)
            {
                drill.previous = drill.position;
                if (drill.dead) continue;
                bool digging = false;
                for (int k = 0; k < 2; k++)
                for (int offset = -1; offset <= 1; offset++)
                {
                    Vector2 probe = drill.position + gravity * (k == 0 ? 1.05f : 1.45f) + side * (offset * .3f);
                    int x = Mathf.FloorToInt(probe.x), y = Mathf.FloorToInt(probe.y);
                    if (Inside(x, y) && softAt[y * data.w + x]) digging = true;
                }
                drill.velocity = digging ? gravity * 3.2f : Vector2.ClampMagnitude(drill.velocity + gravity * (9.81f * dt), 10f);
                drill.position += drill.velocity * dt;
                ResolveDrill(drill);
                if (drill.dead) continue;
                for (int y = Mathf.FloorToInt(drill.position.y - .5f); y <= Mathf.FloorToInt(drill.position.y + .5f); y++)
                for (int x = Mathf.FloorToInt(drill.position.x - .5f); x <= Mathf.FloorToInt(drill.position.x + .5f); x++)
                {
                    if (!Inside(x, y) || !softAt[y * data.w + x] || RectDistance(drill.position, new Vector4(x, y, x + 1, y + 1)) >= .5f) continue;
                    softAt[y * data.w + x] = false; softDirtMap.SetTile(new Vector3Int(x, -y - 1, 0), null);
                    Burst(new Vector2(x + .5f, y + .5f), 4, new Color32(148, 107, 64, 255), 1.5f, .6f, .1f);
                }
                drill.digging = digging; drill.spin += dt * (digging ? 30f : 12f);
                if (digging) feedback.Shake(.06f);
                if (!Alive) continue;
                foreach (Vector2 circle in circles)
                {
                    float distance = Vector2.Distance(circle, drill.position);
                    if (distance < radius + .45f - .05f) { player.Die(); break; }
                    if (distance < radius + .45f + 1f) feedback.NearMiss("drill");
                }
            }
            if (softDirtMap != null)
            {
                var collider = softDirtMap.GetComponent<TilemapCollider2D>();
                if (collider != null) collider.ProcessTilemapChanges();
            }
        }

        private void ResolveDrill(Drill drill)
        {
            for (int iteration = 0; iteration < 3; iteration++)
            {
                bool hit = false;
                for (int y = Mathf.FloorToInt(drill.position.y - .45f); y <= Mathf.FloorToInt(drill.position.y + .45f); y++)
                for (int x = Mathf.FloorToInt(drill.position.x - .45f); x <= Mathf.FloorToInt(drill.position.x + .45f); x++)
                {
                    if (!SolidAt(x, y) || Inside(x, y) && softAt[y * data.w + x]) continue;
                    Vector2 nearest = new Vector2(Mathf.Clamp(drill.position.x, x, x + 1), Mathf.Clamp(drill.position.y, y, y + 1));
                    Vector2 delta = drill.position - nearest;
                    float distance = delta.magnitude;
                    if (distance >= .45f || distance < .000001f) continue;
                    Vector2 normal = delta / distance;
                    drill.position += normal * (.45f - distance); hit = true;
                    float vn = Vector2.Dot(drill.velocity, normal);
                    if (vn < -6.5f)
                    {
                        drill.dead = true; feedback.Pop("pop.drill_snap"); feedback.Shake(.4f);
                        Burst(drill.position, 14, new Color32(255, 211, 122, 255), 3.5f, .8f, .1f); return;
                    }
                    if (vn < 0f) drill.velocity -= normal * vn;
                }
                if (!hit) break;
            }
        }

        private void StepGas(float dt)
        {
            if (!ventsOpen && data.gasVents.Length > 0 && (Mathf.Abs(world.ClockwiseAngleReadout * Mathf.Deg2Rad) > .14f || clock > data.ventDelay))
            {
                ventsOpen = true; feedback.Pop("pop.gas_vent"); feedback.Shake(.15f);
            }
            bool changed = false;
            if (ventsOpen)
            for (int i = 0; i < gasVentStates.Length; i++)
            {
                GasVent state = gasVentStates[i]; GasVentSeed seed = data.gasVents[i];
                state.accumulator += seed.rate * dt;
                while (state.accumulator >= 1f && state.emitted < seed.max)
                {
                    state.accumulator -= 1f;
                    if (!EmitGas(seed)) break;
                    state.emitted++; changed = true;
                }
            }
            gasAccumulator += dt;
            while (gasAccumulator >= data.gasTick)
            {
                gasAccumulator -= data.gasTick; MoveGas(); changed = true;
            }
            if (changed) DrawGas();
            if (!Alive) return;
            bool inside = false, near = false;
            foreach (Vector2 circle in circles)
            {
                inside |= HasGas(circle);
                near |= HasGas(circle + Vector2.right * .6f) || HasGas(circle - Vector2.right * .6f) || HasGas(circle + Vector2.up * .6f) || HasGas(circle - Vector2.up * .6f);
            }
            gasBreath = inside ? gasBreath + dt : 0f;
            if (gasBreath > data.gasTol) { player.Die(); return; }
            if (inside && clock - coughAt >= .5f) { coughAt = clock; feedback.Pop("pop.cough"); }
            else if (!inside && near) feedback.NearMiss("gas");
        }

        private bool HasGas(Vector2 point)
        {
            int x = Mathf.FloorToInt(point.x), y = Mathf.FloorToInt(point.y);
            return Inside(x, y) && gasAt[y * data.w + x];
        }

        private bool EmitGas(GasVentSeed vent)
        {
            Array.Clear(gasSeen, 0, gasSeen.Length); gasQueue.Clear();
            int first = vent.y * data.w + vent.x; gasSeen[first] = true; gasQueue.Enqueue(first);
            while (gasQueue.Count > 0)
            {
                int cell = gasQueue.Dequeue(), x = cell % data.w, y = cell / data.w;
                if (!SolidAt(x, y) && !gasAt[cell]) { gasAt[cell] = true; return true; }
                if (cell != first && SolidAt(x, y)) continue;
                foreach (Vector2Int direction in GasSearchDirections)
                {
                    int nx = x + direction.x, ny = y + direction.y;
                    if (!Inside(nx, ny) || SolidAt(nx, ny)) continue;
                    int next = ny * data.w + nx;
                    if (gasSeen[next]) continue;
                    gasSeen[next] = true; gasQueue.Enqueue(next);
                }
            }
            return false;
        }

        private bool GasFree(int x, int y) => Inside(x, y) && !SolidAt(x, y) && !gasAt[y * data.w + x];
        private bool GasCanMove(int x, int y, Vector2Int direction)
            => GasFree(x + direction.x, y + direction.y) && (direction.x == 0 || direction.y == 0 || GasFree(x + direction.x, y) || GasFree(x, y + direction.y));

        private void MoveGas()
        {
            Vector2 up = -Gravity;
            gasCells.Clear();
            for (int i = 0; i < gasAt.Length; i++) if (gasAt[i]) gasCells.Add(i);
            gasCells.Sort((a, b) => (b % data.w * up.x + b / data.w * up.y).CompareTo(a % data.w * up.x + a / data.w * up.y));
            int flip = (gasTickCount++ & 1) == 0 ? -1 : 1;
            foreach (int cell in gasCells)
            {
                int x = cell % data.w, y = cell / data.w;
                Vector2Int best = Vector2Int.zero; float score = .35f;
                foreach (Vector2Int direction in GasDirections)
                {
                    float dot = Vector2.Dot(((Vector2)direction).normalized, up);
                    if (dot <= score || !GasCanMove(x, y, direction)) continue;
                    best = direction; score = dot;
                }
                if (best == Vector2Int.zero)
                {
                    Vector2Int side = new Vector2Int(Mathf.RoundToInt(-up.y * flip), Mathf.RoundToInt(up.x * flip));
                    if (side != Vector2Int.zero && GasCanMove(x, y, side)) best = side;
                    else if (side != Vector2Int.zero && GasCanMove(x, y, -side)) best = -side;
                }
                if (best == Vector2Int.zero) continue;
                gasAt[cell] = false; gasAt[(y + best.y) * data.w + x + best.x] = true;
            }
        }

        private void DrawGas()
        {
            if (gasMap == null || gasAt == null) return;
            gasMap.ClearAllTiles(); foreach (Tilemap map in gasRimMaps) map.ClearAllTiles();
            for (int y = 0; y < data.h; y++)
            for (int x = 0; x < data.w; x++)
            {
                if (!gasAt[y * data.w + x]) continue;
                var cell = new Vector3Int(x, -y - 1, 0); gasMap.SetTile(cell, gasTile);
                for (int side = 0; side < 4; side++)
                {
                    Vector2Int direction = GasDirections[side * 2];
                    if (!GasFree(x + direction.x, y + direction.y)) continue;
                    Tilemap map = gasRimMaps[side]; map.SetTile(cell, gasRimTile); map.SetTileFlags(cell, TileFlags.None);
                    Vector3 offset = side == 1 ? Vector3.right : side == 2 ? new Vector3(1f, -1f) : side == 3 ? Vector3.down : Vector3.zero;
                    map.SetTransformMatrix(cell, Matrix4x4.TRS(offset, Quaternion.Euler(0f, 0f, -side * 90f), Vector3.one));
                }
            }
        }

        private void UpdateSecondRound()
        {
            DrawRailSwitch();
            for (int i = 0; i < drillStates.Length; i++)
            {
                Drill drill = drillStates[i]; SpriteRenderer visual = drillVisuals[i]; visual.enabled = !drill.dead;
                visual.color = drill.dead ? Color.white : DangerVisual.Red();
                Vector2 shake = drill.digging && Time.timeScale > 0f ? UnityEngine.Random.insideUnitCircle * .03f : Vector2.zero;
                PlaceVisual(visual, drill.previous + shake, drill.position + shake);
                visual.transform.rotation = Quaternion.identity;
                visual.transform.localScale = new Vector3(.7f + .3f * Mathf.Abs(Mathf.Cos(drill.spin)), 1f, 1f);
                visual.flipX = Mathf.Sin(drill.spin) < 0f;
            }
            for (int i = 0; i < gasVentStates.Length; i++)
            {
                GasVentSeed seed = data.gasVents[i];
                ventVisuals[i].sprite = SpriteNamed(ventsOpen ? "gas_vent_open" : "gas_vent");
                for (int k = 0; k < 4; k++)
                {
                    SpriteRenderer puff = gasPuffs[i * 4 + k]; puff.enabled = ventsOpen && gasVentStates[i].emitted < seed.max;
                    float phase = Mathf.Repeat((clock + Alpha * Time.fixedDeltaTime) / .55f + k * .25f, 1f);
                    float angle = i * 2.3f + k * 2.4f;
                    Vector2 point = new Vector2(seed.x + .5f, seed.y + .5f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (phase * .9f);
                    PlaceVisual(puff, point, point); puff.transform.localScale = Vector3.one * (.32f + phase * .67f);
                    puff.color = DangerVisual.Red(.6f * (1f - phase));
                }
            }
            for (int i = blastRings.Count - 1; i >= 0; i--)
            {
                BlastRing ring = blastRings[i]; ring.age += Time.deltaTime;
                if (ring.age >= .6f) { Destroy(ring.line.gameObject); blastRings.RemoveAt(i); continue; }
                float fraction = ring.age / .6f, size = Mathf.Lerp(.96f, 2.75f, fraction);
                Color color = DangerVisual.Red(.9f * (1f - fraction)); ring.line.startColor = ring.line.endColor = color;
                for (int k = 0; k < 33; k++)
                {
                    float angle = k * Mathf.PI / 16f;
                    ring.line.SetPosition(k, ToWorld(ring.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size));
                }
            }
            if (blastFlash > 0f && blastOverlay == null)
            {
                var go = new GameObject("BlastFlash", typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(transform, false);
                Canvas canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 9;
                var image = new GameObject("OrangeFlash", typeof(RectTransform), typeof(Image)); image.transform.SetParent(go.transform, false);
                blastOverlay = image.GetComponent<Image>(); blastOverlay.raycastTarget = false;
                var rect = image.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            blastFlash = Mathf.Max(0f, blastFlash - Time.deltaTime);
            if (blastOverlay != null) blastOverlay.color = DangerVisual.Red(blastFlash);
        }
    }
}
