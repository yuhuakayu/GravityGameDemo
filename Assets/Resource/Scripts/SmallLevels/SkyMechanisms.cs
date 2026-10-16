using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(-150)]
    public sealed class SkyMechanisms : SmallLevelRuntime
    {
        [SerializeField] private Rigidbody2D[] seeBodies = Array.Empty<Rigidbody2D>(), cloudBodies = Array.Empty<Rigidbody2D>(), platBodies = Array.Empty<Rigidbody2D>();
        [SerializeField] private SpriteRenderer[] seeVisuals = Array.Empty<SpriteRenderer>(), seeHubs = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] mineVisuals = Array.Empty<SpriteRenderer>(), mineAnchors = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] cloudVisuals = Array.Empty<SpriteRenderer>(), platVisuals = Array.Empty<SpriteRenderer>(), balloonVisuals = Array.Empty<SpriteRenderer>();
        [SerializeField] private LineRenderer[] mineRopes = Array.Empty<LineRenderer>(), balloonRopes = Array.Empty<LineRenderer>();

        private sealed class See
        {
            public float angle, previousAngle, velocity, timer, side = 1f;
            public int state;
            public bool on;
        }
        private sealed class Mine
        {
            public Vector2 position, previous, velocity;
            public bool exploded;
        }
        private sealed class Cloud
        {
            public float timer, leftAt = float.NegativeInfinity;
            public int state;
            public bool on;
        }
        private sealed class Platform
        {
            public Vector2 position, previous, velocity;
            public float timer, bob, leftAt = float.NegativeInfinity;
            public int state;
            public bool on;
        }
        private sealed class Wind
        {
            public Vector2 position, previous;
            public float life = 0.4f;
            public LineRenderer line;
        }

        private static readonly Vector2[] BalloonOffsets = { new Vector2(-0.9f, 2.4f), new Vector2(0f, 2.75f), new Vector2(0.9f, 2.35f) };
        private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>(16);
        private readonly List<Wind> winds = new List<Wind>();
        private See[] sees;
        private Mine[] mines;
        private Cloud[] clouds;
        private Platform[] platforms;
        private BoxCollider2D[] seeColliders, cloudColliders, platColliders;
        private Material particleMaterial;

        public override void BuildVisuals()
        {
            Transform previous = level.Find("SkyVisuals");
            if (previous != null) DestroyImmediate(previous.gameObject);
            previous = level.Find("SkyBodies");
            if (previous != null) DestroyImmediate(previous.gameObject);
            var visualRoot = new GameObject("SkyVisuals").transform;
            visualRoot.SetParent(level, false);
            var bodyRoot = new GameObject("SkyBodies").transform;
            bodyRoot.SetParent(level, false);
            seeBodies = new Rigidbody2D[data.sees.Length];
            seeVisuals = new SpriteRenderer[data.sees.Length];
            seeHubs = new SpriteRenderer[data.sees.Length];
            for (int i = 0; i < data.sees.Length; i++)
            {
                var seed = data.sees[i];
                Vector2 position = new Vector2(seed.px, seed.py);
                seeBodies[i] = MakeBody(bodyRoot, "SeeBody_" + i, position, new Vector2(seed.hl, seed.ht), seed.a0);
                seeVisuals[i] = MakeVisual(visualRoot, "See_" + i, seed.hl < 2.5f ? "seesaw_board_48" : "seesaw_board_52", position, 20);
                seeVisuals[i].transform.localRotation = Quaternion.Euler(0f, 0f, -seed.a0);
                seeHubs[i] = MakeVisual(visualRoot, "SeeHub_" + i, "seesaw_hub", position, 21);
            }
            mineVisuals = new SpriteRenderer[data.mines.Length];
            mineAnchors = new SpriteRenderer[data.mines.Length];
            mineRopes = new LineRenderer[data.mines.Length];
            for (int i = 0; i < data.mines.Length; i++)
            {
                Vector3 seed = data.mines[i];
                Vector2 anchor = new Vector2(seed.x, seed.y), point = anchor + Vector2.down * seed.z;
                mineAnchors[i] = MakeVisual(visualRoot, "MineAnchor_" + i, "balloon_mine_anchor", anchor, 24);
                mineVisuals[i] = MakeVisual(visualRoot, "Mine_" + i, "balloon_mine", point, 24);
                mineRopes[i] = MakeLine("MineRope_" + i, 0.04f, new Color32(70, 60, 70, 191), 24);
                mineRopes[i].transform.SetParent(visualRoot, false);
                mineRopes[i].SetPosition(0, ToWorld(anchor));
                mineRopes[i].SetPosition(1, ToWorld(point));
            }
            cloudBodies = new Rigidbody2D[data.clouds.Length];
            cloudVisuals = new SpriteRenderer[data.clouds.Length];
            for (int i = 0; i < data.clouds.Length; i++)
            {
                var seed = data.clouds[i];
                Vector2 point = new Vector2(seed.cx, seed.cy);
                cloudBodies[i] = MakeBody(bodyRoot, "CloudBody_" + i, point, new Vector2(seed.hx, seed.hy));
                cloudVisuals[i] = MakeVisual(visualRoot, "Cloud_" + i, "cloud_platform_" + i, point, 22);
            }
            platBodies = new Rigidbody2D[data.plats.Length];
            platVisuals = new SpriteRenderer[data.plats.Length];
            balloonVisuals = new SpriteRenderer[data.plats.Length * 3];
            balloonRopes = new LineRenderer[balloonVisuals.Length];
            for (int i = 0; i < data.plats.Length; i++)
            {
                var seed = data.plats[i];
                Vector2 point = new Vector2(seed.cx, seed.cy);
                platBodies[i] = MakeBody(bodyRoot, "PlatformBody_" + i, point, new Vector2(seed.hx, seed.hy));
                platVisuals[i] = MakeVisual(visualRoot, "Platform_" + i, "balloon_platform_board", point, 23);
                for (int j = 0; j < 3; j++)
                {
                    int index = i * 3 + j;
                    Vector2 balloon = BalloonPoint(point, Vector2.up, j);
                    balloonVisuals[index] = MakeVisual(visualRoot, "Balloon_" + i + "_" + j, "balloon_" + j, balloon, 24);
                    balloonVisuals[index].transform.rotation = Quaternion.identity;
                    balloonRopes[index] = MakeLine("BalloonRope_" + i + "_" + j, 0.04f, new Color32(80, 70, 70, 204), 23);
                    balloonRopes[index].transform.SetParent(visualRoot, false);
                    balloonRopes[index].SetPosition(0, ToWorld(point + new Vector2(BalloonOffsets[j].x * 0.8f, -seed.hy)));
                    balloonRopes[index].SetPosition(1, ToWorld(balloon));
                }
            }
        }

        private SpriteRenderer MakeVisual(Transform parent, string name, string sprite, Vector2 point, int order)
        {
            var renderer = MakeSprite(name, SpriteNamed(sprite), point, order);
            renderer.transform.SetParent(parent, false);
            return renderer;
        }

        private static Rigidbody2D MakeBody(Transform parent, string name, Vector2 point, Vector2 half, float angle = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(point.x, -point.y);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, -angle);
            go.layer = LayerMask.NameToLayer("Wall");
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.useFullKinematicContacts = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<BoxCollider2D>().size = half * 2f;
            return body;
        }

        protected override void Awake()
        {
            base.Awake();
            if (seeBodies.Length != data.sees.Length || cloudBodies.Length != data.clouds.Length || platBodies.Length != data.plats.Length || mineVisuals.Length != data.mines.Length)
                BuildVisuals();
            sees = new See[data.sees.Length];
            seeColliders = new BoxCollider2D[sees.Length];
            for (int i = 0; i < sees.Length; i++) { sees[i] = new See(); seeColliders[i] = seeBodies[i].GetComponent<BoxCollider2D>(); }
            mines = new Mine[data.mines.Length];
            for (int i = 0; i < mines.Length; i++)
            {
                Vector3 seed = data.mines[i];
                Vector2 point = new Vector2(seed.x, seed.y - seed.z);
                mines[i] = new Mine { position = point, previous = point };
            }
            clouds = new Cloud[data.clouds.Length];
            cloudColliders = new BoxCollider2D[clouds.Length];
            for (int i = 0; i < clouds.Length; i++) { clouds[i] = new Cloud(); cloudColliders[i] = cloudBodies[i].GetComponent<BoxCollider2D>(); }
            platforms = new Platform[data.plats.Length];
            platColliders = new BoxCollider2D[platforms.Length];
            for (int i = 0; i < platforms.Length; i++)
            {
                Vector2 point = new Vector2(data.plats[i].cx, data.plats[i].cy);
                platforms[i] = new Platform { position = point, previous = point };
                platColliders[i] = platBodies[i].GetComponent<BoxCollider2D>();
            }
            particleMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        private void FixedUpdate()
        {
            if (!BeginStep()) return;
            float dt = Time.fixedDeltaTime;
            SampleContacts();
            StepSees(dt);
            StepMines(dt);
            StepClouds(dt);
            StepPlatforms(dt);
            StepWind(dt);
            if (Alive) CheckHazards();
        }

        private void SampleContacts()
        {
            contacts.Clear();
            if (Alive) playerBody.GetContacts(contacts);
            for (int i = 0; i < sees.Length; i++) sees[i].on = StandingOn(seeColliders[i]);
            for (int i = 0; i < clouds.Length; i++)
            {
                bool on = clouds[i].state != 2 && StandingOn(cloudColliders[i]);
                if (clouds[i].on && !on) clouds[i].leftAt = clock;
                clouds[i].on = on;
            }
            for (int i = 0; i < platforms.Length; i++)
            {
                bool on = platforms[i].state != 3 && StandingOn(platColliders[i]);
                if (platforms[i].on && !on) platforms[i].leftAt = clock;
                platforms[i].on = on;
            }
        }

        private bool StandingOn(Collider2D collider)
        {
            foreach (ContactPoint2D contact in contacts)
                if (contact.normal.y > 0.5f && (contact.collider == collider || contact.otherCollider == collider)) return true;
            return false;
        }

        private void StepSees(float dt)
        {
            Vector2 gravity = Gravity;
            for (int i = 0; i < sees.Length; i++)
            {
                See see = sees[i];
                SeeSeed seed = data.sees[i];
                see.previousAngle = see.angle;
                if (see.state == 0)
                {
                    if (see.on)
                    {
                        Vector2 offset = circles[0] - new Vector2(seed.px, seed.py);
                        see.side = offset.x * gravity.y - offset.y * gravity.x >= 0f ? 1f : -1f;
                        see.state = 1;
                        see.timer = 0.15f;
                        feedback.Shake(0.05f);
                    }
                }
                else if (see.state == 1)
                {
                    see.timer -= dt;
                    feedback.Shake(0.05f);
                    if (see.timer <= 0f) { see.state = 2; see.velocity = 0f; feedback.Pop("pop.tip"); }
                }
                else if (see.state == 2)
                {
                    see.velocity += see.side * 16f * dt;
                    see.angle += see.velocity * dt;
                    if (Mathf.Abs(see.angle) >= 82f * Mathf.Deg2Rad)
                    {
                        see.angle = see.side * 82f * Mathf.Deg2Rad;
                        see.velocity = 0f; see.state = 3; see.timer = 1.6f;
                        feedback.Shake(0.2f);
                    }
                }
                else if (see.state == 3)
                {
                    see.timer -= dt;
                    if (see.timer <= 0f) see.state = 4;
                }
                else
                {
                    see.angle = Mathf.MoveTowards(see.angle, 0f, 1.4f * dt);
                    if (Mathf.Abs(see.angle) < 0.0001f) { see.angle = 0f; see.state = 0; }
                }
                MoveBody(seeBodies[i], new Vector2(seed.px, seed.py), seed.a0 * Mathf.Deg2Rad + see.angle);
            }
        }

        private void StepMines(float dt)
        {
            for (int i = 0; i < mines.Length; i++)
            {
                Mine mine = mines[i];
                mine.previous = mine.position;
                if (mine.exploded) continue;
                Vector3 seed = data.mines[i];
                mine.velocity -= Gravity * (16f * dt);
                mine.velocity *= 1f - 3.5f * dt;
                mine.position += mine.velocity * dt;
                Vector2 offset = mine.position - new Vector2(seed.x, seed.y);
                if (offset.magnitude > seed.z)
                {
                    Vector2 normal = offset.normalized;
                    mine.position = new Vector2(seed.x, seed.y) + normal * seed.z;
                    float radial = Vector2.Dot(mine.velocity, normal);
                    if (radial > 0f) mine.velocity -= normal * radial;
                }
            }
        }

        private void StepClouds(float dt)
        {
            for (int i = 0; i < clouds.Length; i++)
            {
                Cloud cloud = clouds[i];
                PlatformSeed seed = data.clouds[i];
                if (cloud.state == 0)
                {
                    if (cloud.on) { cloud.state = 1; cloud.timer = 1f; feedback.Pop("pop.cloud_fade"); }
                }
                else if (cloud.state == 1)
                {
                    cloud.timer -= dt;
                    if (cloud.timer <= 0f)
                    {
                        cloud.state = 2; cloud.timer = 3f;
                        cloudColliders[i].enabled = false;
                        cloudVisuals[i].enabled = false;
                        if (clock - cloud.leftAt < 0.45f) feedback.NearMiss("cloud");
                        EmitBurst(new Vector2(seed.cx, seed.cy), 26, Color.white, 2f, 1f, 0.6f);
                    }
                }
                else
                {
                    cloud.timer -= dt;
                    if (cloud.timer <= 0f)
                    {
                        cloud.state = 0;
                        cloudColliders[i].enabled = true;
                        cloudVisuals[i].enabled = true;
                    }
                }
                MoveBody(cloudBodies[i], new Vector2(seed.cx, seed.cy), 0f);
            }
        }

        private void StepPlatforms(float dt)
        {
            Vector2 gravity = Gravity;
            for (int i = 0; i < platforms.Length; i++)
            {
                Platform platform = platforms[i];
                PlatformSeed seed = data.plats[i];
                platform.previous = platform.position;
                if (platform.state == 0)
                {
                    platform.bob += dt;
                    platform.position.y = seed.cy + Mathf.Sin(platform.bob * 2.2f) * 0.06f;
                    if (platform.on) { platform.state = 1; platform.timer = 0f; feedback.Pop("pop.balloon_up"); }
                }
                else if (platform.state == 1)
                {
                    platform.timer += dt;
                    Vector2 delta = -gravity * (3.2f * dt);
                    platform.position += delta;
                    if (platform.on)
                    {
                        Vector3 rootDelta = level.localRotation * new Vector3(delta.x, -delta.y);
                        player.ApplyPlatformDisplacement(Quaternion.Euler(0f, 0f, world.NextAngle) * rootDelta);
                        GetCircles();
                    }
                    float gap = float.MaxValue;
                    for (int j = 0; j < 3; j++)
                    {
                        Vector2 balloon = BalloonPoint(platform.position, gravity, j);
                        foreach (Vector4 spike in data.spikes) gap = Mathf.Min(gap, RectDistance(balloon, spike) - 0.55f);
                    }
                    if (gap >= 0f && gap < 0.45f && (platform.on || clock - platform.leftAt < 0.5f)) feedback.NearMiss("pop");
                    if (gap < 0f || platform.timer >= 4f)
                    {
                        platform.state = 2; platform.velocity = Vector2.zero;
                        feedback.Pop("pop.balloon_pop"); feedback.Shake(0.3f);
                        if (clock - platform.leftAt < 0.45f) feedback.NearMiss("pop");
                        EmitBurst(BalloonPoint(platform.position, gravity, 1), 18, new Color32(255, 122, 138, 255), 3f, 0.7f, 0.14f);
                    }
                }
                else if (platform.state == 2)
                {
                    platform.velocity += gravity * (9.81f * dt);
                    platform.position += platform.velocity * dt;
                    Vector4 bound = data.bound;
                    if (platform.position.x < bound.x - 6f || platform.position.x > bound.z + 6f || platform.position.y < bound.y - 6f || platform.position.y > bound.w + 6f)
                    {
                        platform.state = 3;
                        platBodies[i].gameObject.SetActive(false);
                        platVisuals[i].enabled = false;
                    }
                }
                if (platform.state != 3) MoveBody(platBodies[i], platform.position, 0f);
            }
        }

        private static Vector2 BalloonPoint(Vector2 center, Vector2 gravity, int index)
        {
            Vector2 offset = BalloonOffsets[index];
            return center + new Vector2(gravity.y, -gravity.x) * offset.x - gravity * offset.y;
        }

        private void CheckHazards()
        {
            float clearance = float.MaxValue;
            foreach (Vector2 circle in circles)
            {
                Vector4 bound = data.bound;
                float distance = Mathf.Min(circle.x - bound.x, bound.z - circle.x, circle.y - bound.y, bound.w - circle.y);
                clearance = Mathf.Min(clearance, distance);
            }
            feedback.VoidDistance = clearance;
            if (clearance < 0f) { player.Die(); return; }
            if (clearance < 2.2f && player.IntendedVelocity.y < -3f) feedback.NearMiss("void");
            for (int i = 0; i < mines.Length; i++)
            {
                Mine mine = mines[i];
                if (mine.exploded) continue;
                foreach (Vector2 circle in circles)
                {
                    float distance = Vector2.Distance(circle, mine.position) - radius - 0.42f;
                    if (distance < -0.1f)
                    {
                        mine.exploded = true;
                        mineVisuals[i].enabled = mineRopes[i].enabled = false;
                        EmitBurst(mine.position, 22, new Color32(255, 179, 71, 255), 4.5f, 0.6f, 0.12f);
                        feedback.Shake(0.5f);
                        player.Die();
                        return;
                    }
                    if (distance < 1.15f) feedback.NearMiss("mine");
                }
            }
            foreach (Vector2 circle in circles)
            foreach (Vector4 spike in data.spikes)
            {
                float distance = RectDistance(circle, spike) - radius;
                if (distance < -0.05f) { player.Die(); return; }
                if (distance < 0.75f) feedback.NearMiss("spike");
            }
        }

        private void StepWind(float dt)
        {
            for (int i = winds.Count - 1; i >= 0; i--)
            {
                Wind wind = winds[i];
                wind.previous = wind.position;
                wind.position -= Gravity * (4f * dt);
                wind.life -= dt;
                if (wind.life <= 0f) { Destroy(wind.line.gameObject); winds.RemoveAt(i); }
            }
            if (!Alive || player.IntendedVelocity.y >= -6f || UnityEngine.Random.value >= 0.6f) return;
            Vector2 point = circles[0] + new Vector2(UnityEngine.Random.Range(-4f, 4f), UnityEngine.Random.Range(-4f, 4f));
            var line = new GameObject("Wind").AddComponent<LineRenderer>();
            line.transform.SetParent(level, false);
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.06f;
            line.startColor = line.endColor = new Color(1f, 1f, 1f, 0.5f);
            line.sortingOrder = 25;
            line.sharedMaterial = particleMaterial;
            line.useWorldSpace = false;
            line.SetPosition(0, new Vector3(0f, -0.3f));
            line.SetPosition(1, new Vector3(0f, 0.3f));
            line.transform.localPosition = new Vector3(point.x, -point.y);
            winds.Add(new Wind { position = point, previous = point, line = line });
        }

        private void Update()
        {
            if (sees == null) return;
            for (int i = 0; i < sees.Length; i++)
            {
                See see = sees[i];
                SeeSeed seed = data.sees[i];
                float angle = seed.a0 * Mathf.Deg2Rad + Mathf.Lerp(see.previousAngle, see.angle, Alpha);
                Vector2 point = new Vector2(seed.px, seed.py);
                if (see.state == 1 && Time.deltaTime > 0f)
                    point += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(-0.03f, 0.03f);
                PlaceVisual(seeVisuals[i], point, point, angle);
            }
            for (int i = 0; i < mines.Length; i++)
            {
                if (mines[i].exploded) continue;
                Mine mine = mines[i];
                PlaceVisual(mineVisuals[i], mine.previous, mine.position, clock * 0.6f);
                mineRopes[i].SetPosition(0, ToWorld(new Vector2(data.mines[i].x, data.mines[i].y)));
                mineRopes[i].SetPosition(1, mineVisuals[i].transform.position);
            }
            for (int i = 0; i < clouds.Length; i++)
            {
                Cloud cloud = clouds[i];
                if (cloud.state == 2) continue;
                float fraction = cloud.state == 1 ? Mathf.Clamp01(cloud.timer) : 1f;
                cloudVisuals[i].color = new Color(1f, 1f, 1f, 0.4f + 0.6f * fraction);
                Vector2 point = new Vector2(data.clouds[i].cx, data.clouds[i].cy);
                if (cloud.state == 1 && Time.deltaTime > 0f)
                    point += new Vector2(UnityEngine.Random.Range(-0.04f, 0.04f), UnityEngine.Random.Range(-0.04f, 0.04f)) * (1f - fraction);
                PlaceVisual(cloudVisuals[i], point, point);
            }
            Vector3 localDown = level.InverseTransformVector(Vector3.down).normalized;
            Vector2 renderGravity = new Vector2(localDown.x, -localDown.y);
            for (int i = 0; i < platforms.Length; i++)
            {
                Platform platform = platforms[i];
                if (platform.state != 3) PlaceVisual(platVisuals[i], platform.previous, platform.position);
                Vector2 point = Vector2.Lerp(platform.previous, platform.position, Alpha);
                for (int j = 0; j < 3; j++)
                {
                    int index = i * 3 + j;
                    bool shown = platform.state < 2;
                    balloonVisuals[index].enabled = balloonRopes[index].enabled = shown;
                    if (!shown) continue;
                    Vector2 balloon = BalloonPoint(point, renderGravity, j);
                    PlaceVisual(balloonVisuals[index], balloon, balloon);
                    balloonVisuals[index].transform.rotation = Quaternion.identity;
                    balloonRopes[index].SetPosition(0, ToWorld(point + new Vector2(BalloonOffsets[j].x * 0.8f, -data.plats[i].hy)));
                    balloonRopes[index].SetPosition(1, balloonVisuals[index].transform.position);
                }
            }
            foreach (Wind wind in winds)
            {
                Vector2 point = Vector2.Lerp(wind.previous, wind.position, Alpha);
                wind.line.transform.localPosition = new Vector3(point.x, -point.y);
                wind.line.startColor = wind.line.endColor = new Color(1f, 1f, 1f, 0.5f * Mathf.Clamp01(wind.life / 0.4f));
            }
        }

        private void EmitBurst(Vector2 point, int count, Color color, float speed, float life, float size)
        {
            var go = new GameObject("Sky particles");
            go.transform.SetParent(level, false);
            go.transform.localPosition = new Vector3(point.x, -point.y);
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false; main.playOnAwake = false; main.duration = life; main.startLifetime = life;
            main.startSpeed = 0f; main.startSize = size; main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 25; renderer.sharedMaterial = particleMaterial;
            particles.Play();
            for (int i = 0; i < count; i++)
            {
                var emit = new ParticleSystem.EmitParams { velocity = UnityEngine.Random.insideUnitCircle * speed };
                particles.Emit(emit, 1);
            }
            Destroy(go, life + 0.1f);
        }

        private void OnDestroy()
        {
            if (particleMaterial != null) Destroy(particleMaterial);
        }
    }
}
