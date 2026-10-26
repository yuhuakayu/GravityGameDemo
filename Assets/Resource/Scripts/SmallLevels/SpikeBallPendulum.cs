using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(-160)]
    public sealed class SpikeBallPendulum : MonoBehaviour
    {
        public string[] map;
        public WorldRotator world;
        public PlayerController player;
        public NearMissFeedback feedback;
        [SerializeField] private Rigidbody2D ballBody;
        [SerializeField] private Transform ballVisual;
        [SerializeField] private Transform[] chainLinks;
        [SerializeField] private Material lineMaterial;
        private const float Length = 3f, Radius = 0.85f;
        private float angle, previousAngle, angularVelocity;
        private float Width => map[0].Length * 2f;
        private float Height => map.Length * 2f;
        private Vector2 Mount => new Vector2(9f - Width * 0.5f, Height * 0.5f - 2f);

        public void BuildVisuals()
        {
            Transform old = transform.Find("SpikePendulumVisuals");
            if (old != null) DestroyImmediate(old.gameObject);
            old = transform.Find("SpikePendulumBody");
            if (old != null) DestroyImmediate(old.gameObject);
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            var visualRoot = new GameObject("SpikePendulumVisuals").transform;
            visualRoot.SetParent(transform, false);
            var mount = Line(visualRoot, "YellowMount", 0.24f, new Color32(235, 197, 91, 255), 23);
            mount.SetPosition(0, Mount + Vector2.left * 0.32f);
            mount.SetPosition(1, Mount + Vector2.right * 0.32f);
            var pin = Line(visualRoot, "MountPin", 0.055f, new Color32(83, 72, 56, 255), 24);
            Circle(pin, 0.09f, 12);
            pin.transform.localPosition = Mount;
            int count = Mathf.Max(3, Mathf.RoundToInt(Length / 0.35f));
            chainLinks = new Transform[count - 1];
            for (int i = 0; i < chainLinks.Length; i++)
            {
                var link = Line(visualRoot, "ChainLink_" + i, 0.035f,
                    i % 2 == 0 ? new Color32(119, 115, 154, 255) : new Color32(161, 157, 196, 255), 22);
                Circle(link, 0.07f, 8);
                chainLinks[i] = link.transform;
            }
            ballVisual = new GameObject("SpikedBall").transform;
            ballVisual.SetParent(visualRoot, false);
            for (int i = 0; i < 10; i++)
            {
                float radians = i * Mathf.PI * 0.2f;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                var spike = Line(ballVisual, "Spike_" + i, 0.34f, new Color32(153, 151, 170, 255), 23);
                spike.startWidth = 0.34f;
                spike.endWidth = 0f;
                spike.SetPosition(0, direction * (Radius * 0.66f));
                spike.SetPosition(1, direction * (Radius + 0.22f));
            }
            var ball = Line(ballVisual, "IronBall", Radius * 0.88f, new Color32(74, 72, 94, 255), 24);
            Circle(ball, Radius * 0.42f, 24);
            var rim = Line(ballVisual, "IronRim", 0.065f, new Color32(48, 46, 65, 255), 25);
            Circle(rim, Radius * 0.86f, 24);
            var highlight = Line(ballVisual, "IronHighlight", Radius * 0.34f, new Color32(119, 116, 143, 255), 25);
            Circle(highlight, Radius * 0.12f, 12);
            highlight.transform.localPosition = new Vector2(-Radius * 0.25f, Radius * 0.25f);
            var bodyObject = new GameObject("SpikePendulumBody");
            bodyObject.transform.SetParent(transform, false);
            bodyObject.transform.localPosition = BallLocal(0f);
            ballBody = bodyObject.AddComponent<Rigidbody2D>();
            ballBody.bodyType = RigidbodyType2D.Kinematic;
            ballBody.gravityScale = 0f;
            ballBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            ballBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            ballBody.useFullKinematicContacts = true;
            var collider = bodyObject.AddComponent<CircleCollider2D>();
            collider.radius = Radius;
            collider.isTrigger = true;
            bodyObject.AddComponent<HazardKill>();
            PlaceVisuals(0f);
        }

        private LineRenderer Line(Transform parent, string name, float width, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.sortingOrder = order;
            line.sharedMaterial = lineMaterial;
            return line;
        }

        private static void Circle(LineRenderer line, float radius, int segments)
        {
            line.loop = true;
            line.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float radians = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
            }
        }

        private void Awake()
        {
            if (ballBody == null) BuildVisuals();
            if (ballVisual.GetComponent<DangerVisual>() == null) ballVisual.gameObject.AddComponent<DangerVisual>();
            angle = previousAngle = angularVelocity = 0f;
        }

        private void FixedUpdate()
        {
            if (!player.IsGameplayActive || Time.timeScale <= 0f) return;
            previousAngle = angle;
            float dt = Time.fixedDeltaTime / 4f;
            float gravityAngle = -world.NextAngle * Mathf.Deg2Rad;
            for (int i = 0; i < 4; i++)
            {
                angularVelocity += (-(9.81f / Length) * Mathf.Sin(angle - gravityAngle) - 0.25f * angularVelocity) * dt;
                float next = angle + angularVelocity * dt;
                if (BallInWall(next))
                {
                    if (Mathf.Abs(angularVelocity) > 1.2f) feedback.Shake(0.18f);
                    angularVelocity *= -0.3f;
                }
                else angle = next;
            }
            Vector2 rootLocal = world.transform.InverseTransformPoint(transform.TransformPoint(BallLocal(angle)));
            Vector2 nextPosition = world.NextPosition + (Vector2)(Quaternion.Euler(0f, 0f, world.NextAngle) * (Vector3)rootLocal);
            ballBody.MovePosition(nextPosition);
            ballBody.MoveRotation(world.NextAngle - angle * 2f * Mathf.Rad2Deg);
            NearMissFeedback.GetPlayerCircles(player, out Vector2 a, out Vector2 b, out float radius);
            Vector2 currentBall = ballBody.position;
            float distance = Mathf.Min(Vector2.Distance(a, currentBall), Vector2.Distance(b, currentBall)) - Radius - radius;
            if (distance < 0f) player.Die();
            else if (distance < 0.6f && (Mathf.Abs(angularVelocity) * Length > 2f || Mathf.Abs(player.IntendedVelocity.y) > 2.5f || world.IsRotating))
                feedback.NearMiss("pend");
        }

        private bool BallInWall(float candidate)
        {
            Vector2 point = new Vector2(9f + Length * Mathf.Sin(candidate), 2f + Length * Mathf.Cos(candidate));
            for (int y = Mathf.FloorToInt((point.y - Radius) * 0.5f); y <= Mathf.FloorToInt((point.y + Radius) * 0.5f); y++)
            for (int x = Mathf.FloorToInt((point.x - Radius) * 0.5f); x <= Mathf.FloorToInt((point.x + Radius) * 0.5f); x++)
            {
                if (y >= 0 && y < map.Length && x >= 0 && x < map[y].Length && map[y][x] != '#') continue;
                Vector2 nearest = new Vector2(Mathf.Clamp(point.x, x * 2f, x * 2f + 2f), Mathf.Clamp(point.y, y * 2f, y * 2f + 2f));
                if (Vector2.Distance(point, nearest) < Radius - 0.02f) return true;
            }
            return false;
        }

        private Vector2 BallLocal(float radians) => Mount + new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians)) * Length;

        private void LateUpdate()
        {
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            PlaceVisuals(Mathf.Lerp(previousAngle, angle, alpha));
        }

        private void PlaceVisuals(float radians)
        {
            Vector2 ball = BallLocal(radians);
            ballVisual.localPosition = ball;
            ballVisual.localRotation = Quaternion.Euler(0f, 0f, -radians * 2f * Mathf.Rad2Deg);
            for (int i = 0; i < chainLinks.Length; i++)
                chainLinks[i].localPosition = Vector2.Lerp(Mount, ball, (i + 1f) / (chainLinks.Length + 1f));
        }

        private void OnDestroy()
        {
            if (Application.isPlaying && lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
