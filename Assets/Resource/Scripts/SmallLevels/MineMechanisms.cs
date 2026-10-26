using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(-200)]
    public sealed partial class MineMechanisms : SmallLevelRuntime
    {
        [SerializeField] private SpriteRenderer[] rockVisuals = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] plankVisuals = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer trolley, bucket;
        [SerializeField] private LineRenderer cable;
        [SerializeField] private Transform railPivot;

        private sealed class Rock
        {
            public Vector2 position, previous, velocity;
            public float radius, timer, rotation, previousRotation, delay, fuse = -1f;
            public int state;
            public string direction;
            public bool hasArm, armed, tnt;
            public Vector4 arm;
        }
        private sealed class Plank
        {
            public float y, previous, velocity, timer;
            public int state;
            public bool wire;
        }
        private struct RouteCell
        {
            public float distance;
            public int x, y;
        }
        private struct TrackPoint
        {
            public Vector2 position, tangent;
            public int segment;
        }

        private readonly List<Vector2> normals = new List<Vector2>();
        private readonly List<RouteCell> caveCells = new List<RouteCell>();
        private readonly List<Vector2> history = new List<Vector2>();
        private Rock[] rocks;
        private Plank[] planks;
        private float[] routeDistances, trackDistances;
        private float caveStart = -1f, caveFront = -1f, cartDistance, previousCartDistance, cartVelocity;
        private int caveIndex;
        private bool riding, rideInitialized, crashed;
        private bool HasCave => data.cave != null && data.cave.route != null && data.cave.route.Length > 0;
        private bool HasCart => data.track != null && data.track.Length > 1;

        public override void BuildVisuals()
        {
            Transform old = level.Find("MineVisuals");
            if (old != null) DestroyImmediate(old.gameObject);
            var root = new GameObject("MineVisuals").transform;
            root.SetParent(level, false);
            rockVisuals = new SpriteRenderer[data.rocks.Length + data.tnts.Length];
            for (int i = 0; i < rockVisuals.Length; i++)
            {
                bool tnt = i >= data.rocks.Length;
                Vector2 position = tnt ? new Vector2(data.tnts[i - data.rocks.Length].x, data.tnts[i - data.rocks.Length].y) : new Vector2(data.rocks[i].x, data.rocks[i].y);
                string sprite = tnt ? "tnt_barrel" : data.rocks[i].r > .43f ? "rock_r45" : "rock_r42";
                rockVisuals[i] = MakeSprite((tnt ? "TNT_" : "Rock_") + i, SpriteNamed(sprite), position, 12);
                rockVisuals[i].transform.SetParent(root, false);
            }
            plankVisuals = new SpriteRenderer[data.planks.Length];
            for (int i = 0; i < data.planks.Length; i++)
            {
                var seed = data.planks[i];
                plankVisuals[i] = MakeSprite("Plank_" + i, SpriteNamed("plank_held"), new Vector2((seed.x0 + seed.x1) * .5f, seed.top), 12);
                plankVisuals[i].transform.SetParent(root, false);
            }
            BuildSecondRoundVisuals(root);
            if (!HasCart) return;
            Vector2 initial = data.track[0];
            trolley = MakeSprite("CartTrolley", SpriteNamed("cart_trolley"), initial, 20);
            trolley.transform.SetParent(root, false);
            Vector2 tangent = (data.track[1] - initial).normalized;
            trolley.transform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);
            bucket = MakeSprite("CartBucketFront", SpriteNamed("cart_bucket_front"), initial, 31);
            bucket.transform.SetParent(root, false);
            bucket.transform.position = (Vector3)ToWorld(initial) + Vector3.down * .55f;
            bucket.transform.rotation = Quaternion.identity;
            cable = MakeLine("CartCable", .06f, new Color32(111, 111, 120, 255), 22);
            cable.transform.SetParent(root, false);
            cable.SetPosition(0, ToWorld(initial));
            cable.SetPosition(1, bucket.transform.position + Vector3.up * .3f);
            railPivot = new GameObject("CartRotationPivot").transform;
            railPivot.SetParent(root, false);
            railPivot.localPosition = new Vector3(initial.x, -initial.y);
        }

        protected override void Awake()
        {
            base.Awake();
            if (rockVisuals.Length != data.rocks.Length + data.tnts.Length || plankVisuals.Length != data.planks.Length || (HasCart && railPivot == null)) BuildVisuals();
            rocks = new Rock[data.rocks.Length + data.tnts.Length];
            for (int i = 0; i < rocks.Length; i++)
            {
                bool tnt = i >= data.rocks.Length;
                RockSeed seed = tnt ? null : data.rocks[i];
                TntSeed barrel = tnt ? data.tnts[i - data.rocks.Length] : null;
                Vector2 position = tnt ? new Vector2(barrel.x, barrel.y) : new Vector2(seed.x, seed.y);
                rocks[i] = new Rock { position = position, previous = position, radius = tnt ? .45f : seed.r, direction = tnt ? barrel.direction : seed.direction,
                    delay = tnt ? barrel.delay : seed.delay, hasArm = tnt ? barrel.hasArm : seed.hasArm, arm = tnt ? barrel.arm : seed.arm, tnt = tnt };
            }
            planks = new Plank[data.planks.Length];
            for (int i = 0; i < planks.Length; i++) planks[i] = new Plank { y = data.planks[i].top, previous = data.planks[i].top };
            routeDistances = new float[data.w * data.h];
            for (int i = 0; i < routeDistances.Length; i++) routeDistances[i] = -1f;
            if (HasCave) BuildRoute();
            if (HasCart)
            {
                SetTrack(data.track);
                if (data.riding) world.pivot = railPivot;
            }
            InitializeSecondRound();
            if (rubbleMap != null) rubbleMap.color = DangerVisual.Red();
            if (gasMap != null) gasMap.color = DangerVisual.Red(.45f);
            foreach (var rim in gasRimMaps) if (rim != null) rim.color = DangerVisual.Red(.65f);
        }

        private void FixedUpdate()
        {
            if (!BeginStep()) return;
            float dt = Time.fixedDeltaTime;
            foreach (Rock rock in rocks) { rock.previous = rock.position; rock.previousRotation = rock.rotation; }
            foreach (Plank plank in planks) plank.previous = plank.y;
            previousCartDistance = cartDistance;
            if (Alive)
            {
                if (HasCart && data.riding && !rideInitialized)
                {
                    rideInitialized = riding = true;
                    player.SetRiding(true);
                    world.pivot = railPivot;
                }
                if (HasCart) StepCart(dt);
            }
            StepRocks(dt);
            StepPlanks(dt);
            StepSecondRound(dt);
            if (!Alive) return;
            if (HasCave) StepCave(dt);
            CheckHazards();
        }

        private void BuildRoute()
        {
            float length = 0f;
            foreach (RouteSeed route in data.cave.route)
            {
                Vector4 r = route.rect;
                for (int y = (int)r.y; y < r.w; y++)
                for (int x = (int)r.x; x < r.z; x++)
                {
                    float distance = length + (route.direction == "L" ? r.z - x - .5f : route.direction == "R" ? x + .5f - r.x : y + .5f - r.y);
                    caveCells.Add(new RouteCell { distance = distance, x = x, y = y });
                    routeDistances[y * data.w + x] = distance;
                }
                length += route.direction == "D" ? r.w - r.y : r.z - r.x;
            }
            caveCells.Sort((a, b) => a.distance.CompareTo(b.distance));
        }

        private void StepRocks(float dt)
        {
            Vector2 gravity = Gravity;
            float release = Mathf.Sin(25f * Mathf.Deg2Rad);
            foreach (Rock rock in rocks)
            {
                if (rock.state == 3) continue;
                if (rock.fuse >= 0f)
                {
                    rock.fuse -= dt;
                    if (rock.fuse <= 0f) { Explode(rock); continue; }
                }
                if (rock.state == 0)
                {
                    if (rock.hasArm && !rock.armed)
                    {
                        Vector2 lower = circles[0];
                        if (Alive && lower.x >= rock.arm.x && lower.x <= rock.arm.z && lower.y >= rock.arm.y && lower.y <= rock.arm.w) rock.armed = true;
                        else continue;
                    }
                    bool loosen = rock.direction == "left" ? gravity.x < -release : rock.direction == "right" ? gravity.x > release : rock.direction == "down" ? gravity.y > Mathf.Cos(25f * Mathf.Deg2Rad) : gravity.y < Mathf.Cos(25f * Mathf.Deg2Rad);
                    if (loosen) { rock.state = 1; rock.timer = rock.delay; }
                    continue;
                }
                if (rock.state == 1)
                {
                    rock.timer -= dt;
                    if (rock.timer <= 0f) rock.state = 2;
                    continue;
                }
                if (rock.state == 4)
                {
                    if (UnityEngine.Random.value < .6f) Burst(rock.position - gravity * (rock.radius + .2f), 1, new Color32(255, 211, 122, 255), 2f, .3f, .08f);
                    continue;
                }
                rock.velocity = Vector2.ClampMagnitude(rock.velocity + gravity * (9.81f * (rock.tnt ? data.tntG : 1f) * dt), rock.tnt && data.tntGravitySpecified ? 20f : 15f);
                for (int step = 0; step < 2; step++)
                {
                    rock.position += rock.velocity * (dt * .5f);
                    normals.Clear();
                    ResolveCircle(ref rock.position, rock.radius, normals);
                    foreach (Vector2 normal in normals)
                    {
                        float vn = Vector2.Dot(rock.velocity, normal);
                        if (vn >= 0f) continue;
                        if (rock.tnt && vn < -data.tntHit)
                        {
                            if (data.tntFuse > 0f)
                            {
                                rock.state = 4; rock.fuse = data.tntFuse; rock.velocity = Vector2.zero;
                                feedback.Shake(.3f); feedback.Pop("pop.tnt_fuse");
                            }
                            else Explode(rock);
                            break;
                        }
                        if (!rock.tnt && vn < -3.5f)
                        {
                            rock.state = 3;
                            feedback.Shake(.35f);
                            Burst(rock.position - normal * rock.radius, 14, new Color32(138, 138, 146, 255), 4f, .7f, .16f);
                            Dust(rock.position, 6);
                            break;
                        }
                        rock.velocity = (rock.velocity - normal * (1.2f * vn)) * .995f;
                    }
                    if (rock.state == 3 || rock.state == 4) break;
                }
                rock.rotation += (rock.velocity.x * gravity.y - rock.velocity.y * gravity.x) * dt / rock.radius;
            }
            for (int i = 0; i < rocks.Length; i++)
            for (int j = i + 1; j < rocks.Length; j++)
            {
                Rock a = rocks[i], b = rocks[j];
                if (a.state == 3 || b.state == 3 || (a.state != 2 && b.state != 2)) continue;
                Vector2 delta = b.position - a.position;
                float distance = delta.magnitude, sum = a.radius + b.radius;
                if (distance >= sum || distance < 1e-6f) continue;
                Vector2 normal = delta / distance;
                float penetration = (sum - distance) * .5f;
                if (a.state == 2) a.position -= normal * penetration;
                if (b.state == 2) b.position += normal * penetration;
                float relative = Vector2.Dot(b.velocity - a.velocity, normal);
                if (relative < 0f)
                {
                    float impulse = -relative * .6f;
                    a.velocity -= normal * impulse;
                    b.velocity += normal * impulse;
                }
            }
            foreach (Rock rock in rocks)
            {
                int x = Mathf.FloorToInt(rock.position.x), y = Mathf.FloorToInt(rock.position.y);
                if (x >= 0 && y >= 0 && x < data.w && y < data.h && rubble[y * data.w + x]) rock.state = 3;
            }
        }

        private void StepPlanks(float dt)
        {
            Vector2 lower = circles[0];
            for (int i = 0; i < planks.Length; i++)
            {
                Plank plank = planks[i];
                PlankSeed seed = data.planks[i];
                bool inShaft = Alive && lower.x > seed.x0 && lower.x < seed.x1 && lower.y > seed.top;
                if (plank.state == 0)
                {
                    if (inShaft && Gravity.y > Mathf.Cos(45f * Mathf.Deg2Rad))
                    {
                        plank.state = 1; plank.timer = 2.5f;
                        feedback.Shake(.12f); feedback.Pop("pop.creak");
                    }
                    continue;
                }
                if (plank.state == 1)
                {
                    if (!plank.wire && inShaft && lower.y > seed.wire) { plank.wire = true; plank.timer = Mathf.Min(plank.timer, seed.delay); }
                    plank.timer -= dt;
                    if (plank.timer <= 0f) { plank.state = 2; feedback.Pop("pop.snap"); }
                    continue;
                }
                if (plank.state != 2) continue;
                plank.velocity += (9.81f * Gravity.y - .1f * plank.velocity) * dt;
                plank.y += plank.velocity * dt;
                if (plank.y < seed.top) { plank.y = seed.top; plank.velocity = 0f; }
                if (plank.y >= seed.bottom)
                {
                    plank.y = seed.bottom; plank.state = 3; plank.velocity = 0f;
                    feedback.Shake(.55f);
                    for (int k = 0; k < 22; k++) Burst(new Vector2(UnityEngine.Random.Range(seed.x0, seed.x1), seed.bottom - .2f), 1, new Color32(168, 116, 60, 255), 4f, .8f, .16f);
                }
            }
        }

        private void StepCave(float dt)
        {
            if (caveStart < 0f)
            {
                if (Gravity.y < Mathf.Cos(20f * Mathf.Deg2Rad)) { caveStart = clock; feedback.Shake(.3f); feedback.Pop("pop.rumble"); }
                return;
            }
            if (clock - caveStart < data.cave.delay) { feedback.Shake(.06f); return; }
            if (caveFront < 0f) { caveFront = 0f; history.Add(new Vector2(clock, 0f)); }
            float distance = -1f;
            foreach (Vector2 circle in circles)
            {
                int x = Mathf.FloorToInt(circle.x), y = Mathf.FloorToInt(circle.y);
                if (x >= 0 && y >= 0 && x < data.w && y < data.h) distance = Mathf.Max(distance, routeDistances[y * data.w + x]);
            }
            float gap = distance < 0f ? 0f : distance - caveFront;
            history.Add(new Vector2(clock, Mathf.Max(history[history.Count - 1].y, distance)));
            float lag = crashed && data.cave.lagEnd > 0f ? data.cave.lagEnd : data.cave.lag;
            if (lag > 0f)
            {
                float query = clock - lag;
                while (history.Count > 1 && history[1].x <= query) history.RemoveAt(0);
                float target = history[0].x <= query ? history[0].y : 0f;
                caveFront = Mathf.Min(caveFront + 16f * dt, Mathf.Max(caveFront, target));
            }
            else
            {
                while (history.Count > 2 && history[1].x < clock - 3f) history.RemoveAt(0);
                caveFront += data.cave.speed * (gap > 6f ? 1.4f : 1f) * dt;
            }
            while (caveIndex < caveCells.Count && caveCells[caveIndex].distance <= caveFront)
            {
                RouteCell cell = caveCells[caveIndex++];
                rubble[cell.y * data.w + cell.x] = true;
                rubbleMap.SetTile(new Vector3Int(cell.x, -cell.y - 1, 0), rubbleTiles[(cell.x * 7 + cell.y * 13) % 8]);
                if (UnityEngine.Random.value < .5f) Dust(new Vector2(cell.x + .5f, cell.y + .5f), 2);
            }
            feedback.Shake(.1f);
        }

        private TrackPoint TrackAt(float distance)
        {
            distance = Mathf.Clamp(distance, 0f, trackDistances[trackDistances.Length - 1]);
            int segment = 0;
            while (segment < activeTrack.Length - 2 && distance > trackDistances[segment + 1]) segment++;
            Vector2 a = activeTrack[segment], b = activeTrack[segment + 1];
            float length = trackDistances[segment + 1] - trackDistances[segment];
            return new TrackPoint { position = Vector2.Lerp(a, b, (distance - trackDistances[segment]) / length), tangent = (b - a) / length, segment = segment };
        }

        private Vector2 NextPoint(Vector2 point)
        {
            Vector2 local = world.transform.InverseTransformPoint(ToWorld(point));
            return world.NextPosition + (Vector2)(Quaternion.Euler(0f, 0f, world.NextAngle) * (Vector3)local);
        }

        private Vector2 CartWorldVelocity(TrackPoint track)
        {
            Vector3 local = world.transform.InverseTransformVector(level.TransformVector(new Vector3(track.tangent.x, -track.tangent.y)));
            return (Vector2)(Quaternion.Euler(0f, 0f, world.NextAngle) * local) * cartVelocity;
        }

        private void StepCart(float dt)
        {
            if (!riding || crashed) return;
            TrackPoint before = TrackAt(cartDistance);
            float acceleration = 9.81f * Vector2.Dot(Gravity, before.tangent);
            if (Mathf.Abs(cartVelocity) < .05f && Mathf.Abs(acceleration) < .981f) cartVelocity = 0f;
            else
            {
                cartVelocity = Mathf.Clamp(cartVelocity + (acceleration - .12f * cartVelocity) * dt, -11f, 11f);
                cartDistance += cartVelocity * dt;
                StepRailSwitch(previousCartDistance, cartDistance);
                TrackPoint after = TrackAt(cartDistance);
                if (after.segment != before.segment && Vector2.Dot(after.tangent, before.tangent) < .95f)
                {
                    cartVelocity *= data.cornerKeep;
                    feedback.Shake(.25f);
                    Burst(before.position, 8, new Color32(255, 211, 122, 255), 3f, .4f, .09f);
                }
                if (cartDistance <= 0f) { cartDistance = 0f; cartVelocity = Mathf.Max(0f, cartVelocity); }
            }
            TrackPoint current = TrackAt(cartDistance);
            railPivot.localPosition = new Vector3(current.position.x, -current.position.y);
            Vector2 nextFeet = NextPoint(current.position) + Vector2.down * 1.05f;
            Vector2 feetOffset = Feet - playerBody.position;
            Vector2 bodyPosition = nextFeet - feetOffset;
            playerBody.linearVelocity = Vector2.zero;
            playerBody.MovePosition(bodyPosition);
            Vector2 displacement = VectorToLevel(bodyPosition - playerBody.position);
            circles[0] += displacement; circles[1] += displacement;
            feedback.SpeedLineVelocityOverride = CartWorldVelocity(current);
            if (cartDistance >= trackDistances[trackDistances.Length - 1])
            {
                cartDistance = trackDistances[trackDistances.Length - 1];
                // Body-type changes can discard a pending kinematic MovePosition.
                playerBody.position = bodyPosition;
                Crash(current);
                return;
            }
            if (Mathf.Abs(cartVelocity) > 7f && UnityEngine.Random.value < .6f)
                Burst(current.position, 1, new Color32(255, 211, 122, 255), 2f, .35f, .08f);
        }

        private void Crash(TrackPoint track)
        {
            Vector2 velocity = CartWorldVelocity(track);
            crashed = true; riding = false;
            player.SetRiding(false);
            player.SetIntendedVelocity(new Vector2(0f, Mathf.Clamp(velocity.y, -16f, 16f)));
            cartVelocity = 0f;
            world.pivot = null;
            feedback.SpeedLineVelocityOverride = Vector2.zero;
            feedback.Shake(.7f); feedback.Pop("pop.crash");
            for (int i = 0; i < 26; i++) Burst(track.position + new Vector2(.4f, UnityEngine.Random.Range(-.5f, .5f)), 1, new Color32(138, 138, 146, 255), 5f, .9f, .18f);
        }

        private void CheckHazards()
        {
            if (HasCave)
            {
                bool near = false;
                float range = riding ? 3.2f : 1.4f;
                foreach (Vector2 circle in circles)
                for (int y = Mathf.FloorToInt(circle.y - range); y <= Mathf.FloorToInt(circle.y + range); y++)
                for (int x = Mathf.FloorToInt(circle.x - range); x <= Mathf.FloorToInt(circle.x + range); x++)
                {
                    if (x < 0 || y < 0 || x >= data.w || y >= data.h || !rubble[y * data.w + x]) continue;
                    float distance = RectDistance(circle, new Vector4(x, y, x + 1, y + 1));
                    if (distance < radius - .15f) { player.Die(); return; }
                    if (distance < radius + (riding ? data.cave.nearRide : .9f)) near = true;
                }
                if (near) feedback.NearMiss("cave");
            }
            Vector2 velocity = PlayerVelocity;
            foreach (Rock rock in rocks)
            {
                if (rock.state != 2) continue;
                float relative = (rock.velocity - velocity).magnitude;
                foreach (Vector2 circle in circles)
                {
                    float distance = Vector2.Distance(circle, rock.position);
                    if (distance < rock.radius + radius - .1f && relative > 2.5f) { player.Die(); return; }
                    if (distance < rock.radius + radius + 2f && relative > 3f) feedback.NearMiss("rock");
                }
            }
            for (int i = 0; i < planks.Length; i++)
            {
                Plank plank = planks[i]; PlankSeed seed = data.planks[i];
                if (plank.state != 2 || plank.velocity <= 2f) continue;
                foreach (Vector2 circle in circles)
                {
                    if (circle.x <= seed.x0 - radius || circle.x >= seed.x1 + radius) continue;
                    float distance = Mathf.Abs(circle.y - plank.y);
                    if (distance < seed.h * .5f + radius - .08f) { player.Die(); return; }
                    if (distance < seed.h * .5f + radius + 1f) feedback.NearMiss("plank");
                }
            }
        }

        private void Dust(Vector2 point, int count) => Burst(point, count, new Color32(138, 116, 98, 125), 1f, .6f, .2f);

        private void Update()
        {
            if (rocks == null) return;
            for (int i = 0; i < rocks.Length; i++)
            {
                Rock rock = rocks[i];
                rockVisuals[i].enabled = rock.state != 3;
                bool lethal = rock.state == 2 && (rock.velocity - PlayerVelocity).magnitude > 2.5f;
                rockVisuals[i].color = lethal || (rock.tnt && rock.fuse >= 0f) ? DangerVisual.Red() : Color.white;
                if (rock.tnt) rockVisuals[i].sprite = SpriteNamed((rock.velocity.magnitude > 5.6f || rock.fuse >= 0f) && Mathf.FloorToInt(clock / .07f) % 2 == 0 ? "tnt_barrel_hot" : "tnt_barrel");
                Vector2 shake = rock.state == 1 && Time.timeScale > 0f ? new Vector2(UnityEngine.Random.Range(-.04f, .04f), UnityEngine.Random.Range(-.04f, .04f)) : Vector2.zero;
                PlaceVisual(rockVisuals[i], rock.previous + shake, rock.position + shake, Mathf.Lerp(rock.previousRotation, rock.rotation, Alpha));
            }
            for (int i = 0; i < planks.Length; i++)
            {
                Plank plank = planks[i]; PlankSeed seed = data.planks[i];
                plankVisuals[i].sprite = SpriteNamed(plank.state < 2 ? "plank_held" : "plank_free");
                plankVisuals[i].color = plank.state == 2 && plank.velocity > 2f ? DangerVisual.Red() : Color.white;
                Vector2 shake = plank.state == 1 && Time.timeScale > 0f ? new Vector2(UnityEngine.Random.Range(-.04f, .04f), UnityEngine.Random.Range(-.04f, .04f)) : Vector2.zero;
                float x = (seed.x0 + seed.x1) * .5f;
                PlaceVisual(plankVisuals[i], new Vector2(x, plank.previous) + shake, new Vector2(x, plank.y) + shake);
            }
            UpdateSecondRound();
            if (!HasCart) return;
            TrackPoint track = TrackAt(Mathf.Lerp(previousCartDistance, cartDistance, Alpha));
            float angle = Mathf.Atan2(track.tangent.y, track.tangent.x) + (crashed ? .35f : 0f);
            PlaceVisual(trolley, track.position, track.position, angle);
            Vector2 rail = ToWorld(track.position);
            bucket.transform.position = rail + Vector2.down * .55f;
            bucket.transform.rotation = Quaternion.identity;
            cable.SetPosition(0, rail);
            cable.SetPosition(1, (Vector2)bucket.transform.position + Vector2.up * .3f);
        }

        private void OnDisable()
        {
            if (feedback != null) feedback.SpeedLineVelocityOverride = Vector2.zero;
            if (world != null && world.pivot == railPivot) world.pivot = null;
        }
    }
}
