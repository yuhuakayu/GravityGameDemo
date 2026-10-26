using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Resource.Scripts.SmallLevels
{
    [Serializable] public partial class SmallLevelData
    {
        public string scene;
        public int w, h;
        public bool sky, riding;
        public Vector2 start;
        public Vector4[] carve = Array.Empty<Vector4>(), fill = Array.Empty<Vector4>(), isles = Array.Empty<Vector4>(), spikes = Array.Empty<Vector4>();
        public Vector4[] weak = Array.Empty<Vector4>();
        public DoorSeed door = new DoorSeed();
        public RockSeed[] rocks = Array.Empty<RockSeed>();
        public float rockDelay = 1.5f, cornerKeep = .7f, dragMul = 1f;
        public CaveSeed cave;
        public Vector2[] track = Array.Empty<Vector2>();
        public PlankSeed[] planks = Array.Empty<PlankSeed>();
        public Vector4 bound;
        public SeeSeed[] sees = Array.Empty<SeeSeed>();
        public Vector3[] mines = Array.Empty<Vector3>();
        public PlatformSeed[] clouds = Array.Empty<PlatformSeed>(), plats = Array.Empty<PlatformSeed>();
        public LavaSeed lavaStart;
        public VentSeed vent;
        public WakeSeed[] wakes = Array.Empty<WakeSeed>();
    }
    [Serializable] public class DoorSeed { public float bx, by, dir; }
    [Serializable] public class RockSeed { public float x, y, r, delay; public string direction; public bool hasArm; public Vector4 arm; }
    [Serializable] public class CaveSeed { public RouteSeed[] route; public float delay, lag, speed, lagEnd, nearRide; }
    [Serializable] public class RouteSeed { public Vector4 rect; public string direction; }
    [Serializable] public class PlankSeed { public float x0, x1, top, bottom, wire, delay, h = .36f; }
    [Serializable] public class SeeSeed { public float px, py, hl, ht, a0; }
    [Serializable] public class PlatformSeed { public float cx, cy, hx, hy; }
    [Serializable] public class LavaSeed { public int n; public float[] x, y; public int[] g; public bool[] frozen, crust, water; }
    [Serializable] public class VentSeed { public float x, y, dx, dy, rate; public int max; }
    [Serializable] public class WakeSeed { public int group; public string axis, op; public float value; }

    public abstract class SmallLevelRuntime : MonoBehaviour
    {
        public SmallLevelData data;
        public WorldRotator world;
        public Transform level;
        public PlayerController player;
        public NearMissFeedback feedback;
        public Sprite[] sprites;
        public Tilemap rubbleMap;
        public TileBase[] rubbleTiles;
        protected readonly Vector2[] circles = new Vector2[2];
        protected float radius, clock;
        protected bool started;
        protected bool[] solid, rubble;
        protected Rigidbody2D playerBody;
        private CapsuleCollider2D capsule;
        protected float Alpha => Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
        protected bool Alive => player != null && player.IsGameplayActive;
        protected Vector2 Gravity { get { float a = world.ClockwiseAngleReadout * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); } }
        protected virtual void Awake()
        {
            if (level == null) level = transform;
            if (world == null) world = GetComponentInParent<WorldRotator>();
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (feedback == null) feedback = FindFirstObjectByType<NearMissFeedback>();
            playerBody = player.GetComponent<Rigidbody2D>();
            capsule = player.GetComponent<CapsuleCollider2D>();
            BuildGrid();
        }
        public abstract void BuildVisuals();
        protected bool BeginStep()
        {
            if (Alive) started = true;
            if (!started || Time.timeScale <= 0f || SceneTransition.Instance.IsTransitioning) return false;
            clock += Time.fixedDeltaTime;
            GetCircles();
            return true;
        }
        public Vector2 ToLevel(Vector2 point)
        {
            Vector3 p = level.InverseTransformPoint(point);
            return new Vector2(p.x, -p.y);
        }
        public Vector2 ToWorld(Vector2 point) => level.TransformPoint(new Vector3(point.x, -point.y));
        public Vector2 VectorToLevel(Vector2 velocity)
        {
            Vector3 p = level.InverseTransformVector(velocity);
            return new Vector2(p.x, -p.y);
        }
        protected Vector2 PlayerVelocity => VectorToLevel(player.IntendedVelocity);
        protected Vector2 Feet => (Vector2)capsule.bounds.center + Vector2.down * capsule.bounds.extents.y;
        protected void GetCircles()
        {
            Vector2 size = Vector2.Scale(capsule.size, new Vector2(Mathf.Abs(capsule.transform.lossyScale.x), Mathf.Abs(capsule.transform.lossyScale.y)));
            radius = Mathf.Min(size.x, size.y) * .5f;
            Vector2 center = capsule.transform.TransformPoint(capsule.offset);
            Vector2 axis = capsule.direction == CapsuleDirection2D.Vertical ? (Vector2)capsule.transform.up : (Vector2)capsule.transform.right;
            float half = Mathf.Max(size.x, size.y) * .5f - radius;
            Vector2 a = center + axis * half, b = center - axis * half;
            circles[0] = ToLevel(a.y < b.y ? a : b);
            circles[1] = ToLevel(a.y < b.y ? b : a);
        }
        protected Sprite SpriteNamed(string name) => Array.Find(sprites, s => s != null && s.name == name);
        protected SpriteRenderer MakeSprite(string name, Sprite sprite, Vector2 point, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(level, false);
            go.transform.localPosition = new Vector3(point.x, -point.y);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sortingOrder = order;
            sr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            return sr;
        }
        protected void PlaceVisual(SpriteRenderer sprite, Vector2 previous, Vector2 current, float radians = 0f)
        {
            if (sprite == null) return;
            Vector2 p = Vector2.Lerp(previous, current, Alpha);
            sprite.transform.localPosition = new Vector3(p.x, -p.y);
            sprite.transform.localRotation = Quaternion.Euler(0, 0, -radians * Mathf.Rad2Deg);
        }
        protected LineRenderer MakeLine(string name, float width, Color color, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(level, false);
            var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2;
            line.startWidth = line.endWidth = width; line.startColor = line.endColor = color;
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default")); line.sortingOrder = order;
            return line;
        }
        protected void MoveBody(Rigidbody2D body, Vector2 point, float clockwiseRadians)
        {
            Vector2 local = world.transform.InverseTransformPoint(ToWorld(point));
            Vector2 next = world.NextPosition + (Vector2)(Quaternion.Euler(0, 0, world.NextAngle) * (Vector3)local);
            body.MovePosition(next);
            body.MoveRotation(world.NextAngle - clockwiseRadians * Mathf.Rad2Deg);
        }
        protected void BuildGrid()
        {
            solid = new bool[data.w * data.h]; rubble = new bool[solid.Length];
            if (!data.sky) for (int i = 0; i < solid.Length; i++) solid[i] = true;
            foreach (Vector4 r in data.carve) Fill(r, false);
            foreach (Vector4 r in data.fill) Fill(r, true);
            foreach (Vector4 r in data.isles) Fill(r, true);
        }
        private void Fill(Vector4 rect, bool value)
        {
            for (int y = Mathf.FloorToInt(rect.y); y < rect.w; y++)
            for (int x = Mathf.FloorToInt(rect.x); x < rect.z; x++)
                if (x >= 0 && y >= 0 && x < data.w && y < data.h) solid[y * data.w + x] = value;
        }
        protected virtual bool SolidAt(int x, int y) => x < 0 || y < 0 || x >= data.w || y >= data.h ? !data.sky : solid[y * data.w + x] || rubble[y * data.w + x];
        public static float RectDistance(Vector2 p, Vector4 r) => Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x, r.x, r.z), Mathf.Clamp(p.y, r.y, r.w)));
        protected void ResolveCircle(ref Vector2 p, float r, List<Vector2> normals, int iterations = 4, Vector2? oldPosition = null, float friction = 0f)
        {
            for (int it = 0; it < iterations; it++)
            {
                bool hit = false;
                int x0 = Mathf.FloorToInt(p.x - r), x1 = Mathf.FloorToInt(p.x + r), y0 = Mathf.FloorToInt(p.y - r), y1 = Mathf.FloorToInt(p.y + r);
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    if (!SolidAt(x, y)) continue;
                    Vector2 q = new Vector2(Mathf.Clamp(p.x, x, x + 1), Mathf.Clamp(p.y, y, y + 1));
                    Vector2 d = p - q; float d2 = d.sqrMagnitude;
                    if (d2 >= r * r) continue;
                    Vector2 n; float penetration;
                    if (d2 > 1e-9f) { float length = Mathf.Sqrt(d2); n = d / length; penetration = r - length; }
                    else
                    {
                        n = Vector2.left; float best = float.MaxValue; bool foundOpen = false;
                        for (int side = 0; side < 4; side++)
                        {
                            Vector2 face = side == 0 ? Vector2.left : side == 1 ? Vector2.right : side == 2 ? Vector2.down : Vector2.up;
                            float distance = side == 0 ? p.x - x : side == 1 ? x + 1 - p.x : side == 2 ? p.y - y : y + 1 - p.y;
                            bool open = !SolidAt(x + (int)face.x, y + (int)face.y);
                            if ((open && !foundOpen) || (open == foundOpen && distance < best)) { best = distance; n = face; foundOpen = open; }
                        }
                        penetration = best + r;
                    }
                    p += n * penetration; hit = true; normals?.Add(n);
                    if (oldPosition.HasValue && friction > 0f)
                    {
                        Vector2 moved = p - oldPosition.Value;
                        p -= (moved - Vector2.Dot(moved, n) * n) * friction;
                    }
                }
                if (!hit) break;
            }
        }
        protected void Burst(Vector2 point, int count, Color color, float speed = 2f, float life = .6f, float size = .08f, Vector2? direction = null)
        {
            var go = new GameObject("Level particles"); go.transform.position = ToWorld(point);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.duration = life; main.startLifetime = life;
            main.startSpeed = speed; main.startSize = size; main.startColor = color; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.enabled = false; var shape = ps.shape; shape.enabled = false;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sortingOrder = 16;
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            ps.Play();
            for (int i = 0; i < count; i++)
            {
                var emit = new ParticleSystem.EmitParams();
                emit.velocity = direction.HasValue ? (Vector3)direction.Value + (Vector3)UnityEngine.Random.insideUnitCircle * .5f : (Vector3)UnityEngine.Random.insideUnitCircle * speed;
                ps.Emit(emit, 1);
            }
            Destroy(go, life + .1f);
        }
    }
}
