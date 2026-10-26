using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Resource.Scripts.SmallLevels
{
    public partial class SmallLevelData
    {
        public Vector2[] crystals = Array.Empty<Vector2>(), kegs = Array.Empty<Vector2>();
        public float freezeT = 4.5f, kegKill = 2f;
    }

    public sealed partial class LavaMechanisms
    {
        [SerializeField] private Tilemap iceMap, weakMap, stoneMap;
        [SerializeField] private Tile iceTile, weakTile, stoneTile;
        [SerializeField] private SpriteRenderer[] crystalVisuals, kegVisuals;
        private readonly bool[] water = new bool[Capacity];
        private readonly float[] cooling = new float[Capacity];
        private float[] waterField;
        private Color32[] glowSourcePixels;
        private bool[] iceCells, weakCells, blackCells, crystalLeft;
        private byte[] kegStates;
        private float[] kegTimers;
        private float heat, freezeRemaining, ventSealTime;
        private bool ventSealed, stoneMessage;
        private readonly List<int> newStones = new List<int>(12);
        private readonly List<Blast> blasts = new List<Blast>();

        private sealed class Blast
        {
            public Vector2 Point;
            public LineRenderer Line;
            public float Started;
        }

        private void InitializeSecondRoundState()
        {
            heat = freezeRemaining = ventSealTime = 0f;
            ventSealed = stoneMessage = false;
            Array.Clear(cooling, 0, cooling.Length);
            woken.Clear();
            BuildGrid();
            iceCells = new bool[solid.Length]; weakCells = new bool[solid.Length]; blackCells = new bool[solid.Length];
            crystalLeft = new bool[data.crystals.Length];
            for (int i = 0; i < crystalLeft.Length; i++) crystalLeft[i] = true;
            kegStates = new byte[data.kegs.Length]; kegTimers = new float[data.kegs.Length];
            ResetMechanismTiles();
        }

        private void BuildSecondRoundVisuals(Transform root)
        {
            crystalVisuals = new SpriteRenderer[data.crystals.Length];
            for (int i = 0; i < crystalVisuals.Length; i++)
                crystalVisuals[i] = MakeSprite("IceCrystal_" + i, SpriteNamed("ice_crystal"), data.crystals[i], 15);
            kegVisuals = new SpriteRenderer[data.kegs.Length];
            for (int i = 0; i < kegVisuals.Length; i++)
                kegVisuals[i] = MakeSprite("PowderKeg_" + i, SpriteNamed("powder_keg"), data.kegs[i], 15);
            foreach (SpriteRenderer visual in crystalVisuals) visual.transform.SetParent(root, true);
            foreach (SpriteRenderer visual in kegVisuals) visual.transform.SetParent(root, true);
            iceMap = MakeMechanismMap("FrozenLava", data.crystals.Length > 0, false);
            weakMap = MakeMechanismMap("WeakLavaRock", data.weak.Length > 0, true);
            bool hasWater = false;
            for (int i = 0; i < count; i++) if (water[i]) { hasWater = true; break; }
            stoneMap = MakeMechanismMap("BlackStone", hasWater, true);
            if (iceMap != null) iceTile = MechanismTile("FrozenLava", null);
            if (weakMap != null) weakTile = MechanismTile("WeakLavaRock", SpriteNamed("weak_lava_rock"));
            if (stoneMap != null) stoneTile = MechanismTile("BlackStone", SpriteNamed("black_stone"));
            ResetMechanismTiles();
        }

        private Tilemap MakeMechanismMap(string name, bool needed, bool visible)
        {
            Transform grid = level.Find("Grid");
            Transform previous = grid.Find(name);
            if (previous != null) DestroyImmediate(previous.gameObject);
            if (!needed) return null;
            var go = new GameObject(name); go.transform.SetParent(grid, false);
            go.layer = LayerMask.NameToLayer("Wall");
            var map = go.AddComponent<Tilemap>(); map.tileAnchor = new Vector3(0f, 1f, 0f);
            var renderer = go.AddComponent<TilemapRenderer>(); renderer.sortingOrder = 14; renderer.enabled = visible;
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f; body.useFullKinematicContacts = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<TilemapCollider2D>();
            var bodies = new List<Rigidbody2D>();
            foreach (Rigidbody2D attached in world.attachedGeometryBodies) if (attached != null) bodies.Add(attached);
            bodies.Add(body); world.attachedGeometryBodies = bodies.ToArray();
            return map;
        }

        private Tile MechanismTile(string name, Sprite sprite)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                const string folder = "Assets/Resource/Art/SmallLevels/Lava/Tiles";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resource/Art/SmallLevels/Lava", "Tiles");
                string path = folder + "/" + name + ".asset";
                Tile asset = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (asset == null) { asset = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(asset, path); }
                asset.sprite = sprite; asset.colliderType = Tile.ColliderType.Grid; EditorUtility.SetDirty(asset);
                return asset;
            }
#endif
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite; tile.colliderType = Tile.ColliderType.Grid; owned.Add(tile);
            return tile;
        }

        private void ResetMechanismTiles()
        {
            if (iceMap != null) iceMap.ClearAllTiles();
            if (weakMap != null) weakMap.ClearAllTiles();
            if (stoneMap != null) stoneMap.ClearAllTiles();
            foreach (Vector4 rect in data.weak)
                for (int y = Mathf.FloorToInt(rect.y); y < rect.w; y++)
                for (int x = Mathf.FloorToInt(rect.x); x < rect.z; x++)
                {
                    if (x < 0 || y < 0 || x >= data.w || y >= data.h) continue;
                    int cell = y * data.w + x; solid[cell] = weakCells[cell] = true;
                    if (weakMap != null) weakMap.SetTile(MapCell(cell), weakTile);
                }
            RefreshCollider(iceMap); RefreshCollider(weakMap); RefreshCollider(stoneMap);
        }

        private Vector3Int MapCell(int cell) => new Vector3Int(cell % data.w, -cell / data.w - 1, 0);
        private static void RefreshCollider(Tilemap map)
        {
            if (map != null) map.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        }

        private void RemoveParticle(int i)
        {
            int last = --count;
            positions[i] = positions[last]; velocities[i] = velocities[last]; oldPositions[i] = oldPositions[last];
            groups[i] = groups[last]; crust[i] = crust[last]; frozen[i] = frozen[last]; water[i] = water[last];
            isolated[i] = isolated[last]; densities[i] = densities[last]; cooling[i] = cooling[last];
        }

        private void CheckCrystals()
        {
            for (int i = 0; i < crystalLeft.Length; i++)
            {
                if (!crystalLeft[i]) continue;
                bool touched = false;
                foreach (Vector2 circle in circles) if (Vector2.Distance(circle, data.crystals[i]) < radius + .45f) touched = true;
                if (!touched) continue;
                crystalLeft[i] = false;
                if (crystalVisuals != null && i < crystalVisuals.Length && crystalVisuals[i] != null) crystalVisuals[i].enabled = false;
                FreezeLava(); feedback.Pop("pop.freeze"); feedback.Shake(.3f);
                Burst(data.crystals[i], 16, new Color32(150, 206, 240, 255), 3f, .7f, .1f);
            }
        }

        private void FreezeLava()
        {
            ClearIce(); freezeRemaining = data.freezeT;
            var amount = new int[solid.Length];
            for (int i = 0; i < count; i++)
            {
                velocities[i] = Vector2.zero;
                if (frozen[i] || water[i]) continue;
                int x = Mathf.FloorToInt(positions[i].x), y = Mathf.FloorToInt(positions[i].y);
                if (x >= 0 && y >= 0 && x < data.w && y < data.h) amount[y * data.w + x]++;
            }
            for (int cell = 0; cell < amount.Length; cell++)
            {
                if (amount[cell] < 6 || solid[cell] || PlayerTouchesCell(cell, .43f)) continue;
                solid[cell] = iceCells[cell] = true;
                if (iceMap != null) iceMap.SetTile(MapCell(cell), iceTile);
            }
            RefreshCollider(iceMap);
        }

        private void StepFreeze(float dt)
        {
            freezeRemaining = Mathf.Max(0f, freezeRemaining - dt);
            if (freezeRemaining > 0f) return;
            ClearIce(); feedback.Pop("pop.thaw"); feedback.Shake(.15f);
        }

        private void ClearIce()
        {
            for (int cell = 0; cell < iceCells.Length; cell++) if (iceCells[cell]) { solid[cell] = iceCells[cell] = false; }
            if (iceMap != null) iceMap.ClearAllTiles();
            RefreshCollider(iceMap);
        }

        private bool PlayerTouchesCell(int cell, float distance)
        {
            int x = cell % data.w, y = cell / data.w;
            var rect = new Vector4(x, y, x + 1f, y + 1f);
            foreach (Vector2 circle in circles) if (RectDistance(circle, rect) < distance) return true;
            return false;
        }

        private void StepKegs(float dt)
        {
            for (int i = 0; i < kegStates.Length; i++)
            {
                if (kegStates[i] == 2) continue;
                Vector2 point = data.kegs[i];
                if (kegStates[i] == 0)
                {
                    if (freezeRemaining > 0f || CountNear(point, .75f, true) < 3) continue;
                    kegStates[i] = 1; kegTimers[i] = .6f; feedback.Pop("pop.keg_lit");
                    continue;
                }
                kegTimers[i] -= dt;
                if (embers != null && UnityEngine.Random.value < .8f)
                {
                    var spark = new ParticleSystem.EmitParams { position = ToWorld(point) + Vector2.up * .5f, velocity = Vector3.up * 2f + (Vector3)UnityEngine.Random.insideUnitCircle, startLifetime = .3f, startSize = .08f, startColor = Color.white };
                    embers.Emit(spark, 1);
                }
                if (kegTimers[i] > 0f) continue;
                kegStates[i] = 2;
                if (kegVisuals != null && i < kegVisuals.Length && kegVisuals[i] != null) kegVisuals[i].enabled = false;
                ExplodeKeg(point);
            }
        }

        private void ExplodeKeg(Vector2 point)
        {
            for (int cell = 0; cell < weakCells.Length; cell++)
            {
                if (!weakCells[cell]) continue;
                int x = cell % data.w, y = cell / data.w;
                if (RectDistance(point, new Vector4(x, y, x + 1f, y + 1f)) >= 2.4f) continue;
                solid[cell] = weakCells[cell] = false;
                if (weakMap != null) weakMap.SetTile(MapCell(cell), null);
            }
            RefreshCollider(weakMap);
            for (int i = count - 1; i >= 0; i--)
            {
                Vector2 delta = positions[i] - point; float distance = delta.magnitude;
                if (distance < 4.5f)
                {
                    if (UnityEngine.Random.value < .3f) Burst(positions[i], 1, new Color32(255, 180, 72, 255), 2f, .5f, .08f);
                    RemoveParticle(i);
                }
                else if (distance < 6f) { velocities[i] += delta / distance * (7f * (1f - distance / 6f)); frozen[i] = false; }
            }
            BuildNeighbourGrid();
            feedback.Pop("pop.boom"); feedback.Shake(.6f);
            Burst(point, 30, new Color32(255, 126, 44, 255), 5f, .7f, .16f);
            var ring = MakeLine("KegShockwave", .08f, new Color32(255, 210, 122, 255), 17);
            ring.positionCount = 49; ring.loop = true;
            blasts.Add(new Blast { Point = point, Line = ring, Started = clock });
            if (!Alive) return;
            float closest = float.MaxValue;
            foreach (Vector2 circle in circles) closest = Mathf.Min(closest, Vector2.Distance(circle, point));
            if (closest < data.kegKill) player.Die();
            else if (closest < data.kegKill + 1.5f) feedback.NearMiss("keg");
        }

        private void StepWater()
        {
            if (stoneMap == null) return;
            BuildNeighbourGrid(); newStones.Clear();
            for (int i = 0; i < count && newStones.Count < 12; i++)
            {
                if (!water[i] || frozen[i]) continue;
                int length = GatherNeighbours(i), hit = -1;
                for (int k = 0; k < length; k++)
                {
                    int j = neighbours[k];
                    if (!water[j] && !frozen[j] && (positions[j] - positions[i]).sqrMagnitude < .34f * .34f) { hit = j; break; }
                }
                if (hit < 0) continue;
                int x = Mathf.FloorToInt(positions[hit].x), y = Mathf.FloorToInt(positions[hit].y);
                if (x < 0 || y < 0 || x >= data.w || y >= data.h || SolidAt(x, y)) continue;
                int cell = y * data.w + x;
                if (PlayerTouchesCell(cell, .45f) || Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(data.door.bx, data.door.by)) < 1.2f) continue;
                SetStone(cell); newStones.Add(cell);
                Steam(new Vector2(x + .5f, y + .5f), 3);
            }
            if (newStones.Count == 0) return;
            RemoveStoneParticles(); RefreshCollider(stoneMap); BuildNeighbourGrid();
            if (data.vent != null && !ventSealed)
                foreach (int cell in newStones)
                    if (Vector2.Distance(new Vector2(cell % data.w + .5f, cell / data.w + .5f), new Vector2(data.vent.x, data.vent.y + .18f)) < 2f) { SealVent(); break; }
            if (!stoneMessage) { stoneMessage = true; feedback.Pop("pop.obsidian"); }
            feedback.Shake(.08f);
        }

        private void SetStone(int cell)
        {
            solid[cell] = blackCells[cell] = true;
            if (stoneMap != null) stoneMap.SetTile(MapCell(cell), stoneTile);
        }

        private void RemoveStoneParticles()
        {
            for (int i = count - 1; i >= 0; i--)
            {
                int x = Mathf.FloorToInt(positions[i].x), y = Mathf.FloorToInt(positions[i].y);
                if (x >= 0 && y >= 0 && x < data.w && y < data.h && blackCells[y * data.w + x]) RemoveParticle(i);
            }
        }

        private void SealVent()
        {
            if (ventSealed || data.vent == null) return;
            ventSealed = true; ventSealTime = clock;
            int x = Mathf.FloorToInt(data.vent.x), y = Mathf.FloorToInt(data.vent.y + .18f);
            if (x >= 0 && y >= 0 && x < data.w && y < data.h)
            {
                int cell = y * data.w + x;
                if (!solid[cell]) SetStone(cell);
                for (int i = count - 1; i >= 0; i--)
                    if (Mathf.FloorToInt(positions[i].x) == x && Mathf.FloorToInt(positions[i].y) == y) RemoveParticle(i);
                Steam(new Vector2(x + .5f, y + .5f), 6);
                RefreshCollider(stoneMap); BuildNeighbourGrid();
            }
            feedback.Pop("pop.vent_sealed"); feedback.Shake(.2f);
        }

        private bool TrySealBlockedVent()
        {
            Vector2 mouth = new Vector2(data.vent.x, data.vent.y + .18f);
            bool blocked = SolidAt(Mathf.FloorToInt(mouth.x), Mathf.FloorToInt(mouth.y));
            for (int y = Mathf.Max(0, Mathf.FloorToInt(mouth.y - 2f)); !blocked && y <= Mathf.Min(data.h - 1, Mathf.FloorToInt(mouth.y + 2f)); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(mouth.x - 2f)); x <= Mathf.Min(data.w - 1, Mathf.FloorToInt(mouth.x + 2f)); x++)
                if (blackCells[y * data.w + x] && (new Vector2(x + .5f, y + .5f) - mouth).sqrMagnitude < 4f)
                { blocked = true; break; }
            if (blocked) SealVent();
            return blocked;
        }

        private void CoolVentLava(float dt)
        {
            bool late = clock - ventSealTime > 2f;
            for (int i = 0; i < count; i++)
            {
                if (groups[i] != 8 || frozen[i] || water[i]) continue;
                cooling[i] = velocities[i].sqrMagnitude < 1f ? cooling[i] + dt : 0f;
                if (cooling[i] > .3f || late) { frozen[i] = true; velocities[i] = Vector2.zero; }
            }
        }

        private void Steam(Vector2 point, int amount) => Burst(point, amount, new Color32(220, 235, 241, 180), .8f, 1.1f, .35f, Vector2.up * 1.5f);

        private void UpdateSecondRoundVisuals()
        {
            if (crystalVisuals != null) for (int i = 0; i < crystalVisuals.Length; i++)
            {
                if (crystalVisuals[i] == null) continue;
                crystalVisuals[i].transform.position = ToWorld(data.crystals[i]) + Vector2.up * (Mathf.Sin(clock * 2.4f + i) * .08f);
                crystalVisuals[i].transform.rotation = Quaternion.identity;
            }
            if (kegVisuals != null) for (int i = 0; i < kegVisuals.Length; i++)
                if (kegVisuals[i] != null)
                {
                    kegVisuals[i].transform.rotation = Quaternion.identity;
                    kegVisuals[i].color = kegStates[i] == 1 ? DangerVisual.Red() : Color.white;
                }
            for (int i = blasts.Count - 1; i >= 0; i--)
            {
                Blast blast = blasts[i]; float t = (clock - blast.Started) / .45f;
                if (t >= 1f) { Destroy(blast.Line.gameObject); blasts.RemoveAt(i); continue; }
                float r = Mathf.Lerp(.2f, 4.5f, t);
                for (int k = 0; k < blast.Line.positionCount; k++)
                {
                    float a = k * Mathf.PI * 2f / (blast.Line.positionCount - 1);
                    blast.Line.SetPosition(k, ToWorld(blast.Point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r));
                }
                Color color = DangerVisual.Red(1f - t); blast.Line.startColor = blast.Line.endColor = color;
            }
        }
    }
}
