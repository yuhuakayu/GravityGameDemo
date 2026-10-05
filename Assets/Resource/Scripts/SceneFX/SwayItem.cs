using UnityEngine;

namespace Resource.Scripts.SceneFX
{
    public enum SwayKind { Vine, Bush, Grass, Mushroom, Corner, FgVine, FgRoots, FgMoss, FgClump, FgCorner }

    /// <summary>
    /// 装饰摆动：按摆动量切换事先做好的像素帧（frames 中间一帧是原样，往两边每帧多偏 1 像素）。
    /// 三种力叠加：微风、世界旋转后倒向屏幕的「下」、旋转时的甩动；关卡内装饰还会被玩家拨动、落地震开。
    /// 物体放在所在地砖的上边中点，Sprite 枢轴也是上边中点。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public class SwayItem : MonoBehaviour
    {
        public SwayKind kind;
        [Tooltip("横排切好的帧，数量 = 2 × maxOffset + 1")]
        public Sprite[] frames = new Sprite[0];
        [Tooltip("SpriteRenderer.flipX 打开时勾选，帧方向会反过来")]
        public bool mirrored;
        [Tooltip("微风相位，每株不同")]
        public float phase;

        [Header("摆动参数（像素，已按 200% 幅度）")]
        public float maxOffset = 16f;     // 最大偏移
        public float windAmp = 3.2f;      // 微风
        public float gravityLean = 14f;   // 世界转 90° 时倒向重力的偏移
        public float drag = 1.8f;         // 甩动：每 1 单位/秒 的相对风
        public float frequency = 1.2f;    // 弹性（每秒摆几下）
        public float damping = 0.15f;     // 阻尼，越小回弹越多

        [Header("玩家拨动（外圈前景不用）")]
        public bool pushable = true;
        public float pushForce = 560f;
        public float pushRadius = 1.1f;

        [Header("相对物体位置的点（本地单位）")]
        public Vector2 anchorOffset;      // 固定点（用于甩动）
        public Vector2 bodyOffset;        // 身体中心（用于拨动）

        private SpriteRenderer _renderer;
        private float _x, _v;
        private int _shown;
        private const float SubStep = 1f / 120f;

        /// <summary>编辑器生成场景时调用：按种类填好参数和固定点。</summary>
        public void ApplyPreset(SwayKind k)
        {
            kind = k;
            float spriteHeight = frames != null && frames.Length > 0 && frames[0] != null ? frames[0].bounds.size.y : 1f;
            switch (k)
            {
                case SwayKind.Vine:     Set(16, 3.2f, 14, 1.8f, 1.2f, 0.15f, true, 560, 1.1f, 0f, -1f); break;
                case SwayKind.Bush:     Set(6, 1.8f, 6, 0.7f, 2.2f, 0.30f, true, 1040, 0.75f, -1f, -0.55f); break;
                case SwayKind.Grass:    Set(4, 2.0f, 4, 0.6f, 2.8f, 0.35f, true, 1300, 0.7f, -1f, -0.55f); break;
                case SwayKind.Mushroom: Set(2, 0f, 2, 0.24f, 4.0f, 0.40f, true, 300, 0.6f, -1f, -0.55f); break;
                case SwayKind.Corner:   Set(2, 1.4f, 2, 0.3f, 2.4f, 0.35f, true, 600, 0.7f, -0.5f, -0.5f); break;
                case SwayKind.FgVine:   Set(10, 4.0f, 10, 1.0f, 0.8f, 0.12f, false, 0, 0, 0f, -spriteHeight * 0.5f); break;
                case SwayKind.FgRoots:  Set(6, 2.0f, 6, 0.6f, 1.0f, 0.20f, false, 0, 0, 0f, -spriteHeight * 0.5f); break;
                case SwayKind.FgMoss:   Set(4, 2.4f, 4, 0.6f, 1.2f, 0.20f, false, 0, 0, 0f, -spriteHeight * 0.5f); break;
                case SwayKind.FgClump:  Set(6, 2.6f, 4, 0.6f, 1.2f, 0.25f, false, 0, 0, -spriteHeight, -spriteHeight * 0.5f); break;
                case SwayKind.FgCorner: Set(4, 2.0f, 4, 0.5f, 1.0f, 0.25f, false, 0, 0, 0f, -spriteHeight * 0.5f); break;
            }
        }

        private void Set(float t, float a, float g, float d, float f, float z, bool push, float k, float r, float anchorY, float bodyY)
        {
            maxOffset = t; windAmp = a; gravityLean = g; drag = d; frequency = f; damping = z;
            pushable = push; pushForce = k; pushRadius = r;
            anchorOffset = new Vector2(0f, anchorY);
            bodyOffset = new Vector2(0f, bodyY);
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0) return;
            SwayWorld.Tick();
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
            if (dt <= 0f) return;

            Transform parent = transform.parent;
            Vector2 anchor = transform.TransformPoint(anchorOffset);

            // 重力：屏幕向下在本地坐标里的 x 分量（世界转 90° 时 = ±1）
            float gravityX = ToLocal(parent, Vector2.down).x;
            // 甩动：固定点随世界旋转的速度 = ω × r，空气相对它反向吹
            Vector2 r = anchor - SwayWorld.Pivot;
            Vector2 velocity = SwayWorld.Omega * new Vector2(-r.y, r.x);
            float windX = ToLocal(parent, -velocity).x;
            // 微风：一阵一阵，从左往右传
            Vector2 local = parent != null ? (Vector2)parent.InverseTransformPoint(anchor) : anchor;
            float t = SwayWorld.WindTime;
            float gust = 0.62f + 0.38f * Mathf.Sin(2f * Mathf.PI * t / 11f + 1.3f);
            float breeze = windAmp * gust * Mathf.Sin(2f * Mathf.PI * t / 3.6f - 0.45f * (0.9f * local.x - 0.35f * local.y) + phase);
            float target = Mathf.Clamp(gravityLean * gravityX + drag * windX + breeze, -maxOffset, maxOffset);

            // 玩家拨动
            float force = 0f;
            if (pushable && SwayWorld.PlayerActive)
            {
                Vector2 body = transform.TransformPoint(bodyOffset);
                float speed = SwayWorld.PlayerVel.magnitude;
                float radius = pushRadius + 0.4f;
                for (int i = 0; i < 2; i++)
                {
                    Vector2 point = SwayWorld.PlayerFeet + Vector2.up * (i == 0 ? 0.4f : 1.13f);
                    Vector2 d = body - point;
                    float dist = d.magnitude;
                    if (dist >= radius) continue;
                    float side = ToLocal(parent, d).x >= 0f ? 1f : -1f;
                    force += pushForce * side * (1f - dist / radius) * Mathf.Min(1f, 0.25f + speed / 4f);
                }
                if (SwayWorld.LandingImpact > 0f)
                {
                    Vector2 d = body - (SwayWorld.PlayerFeet + Vector2.up * 0.2f);
                    float dist = d.magnitude;
                    if (dist < 2.6f)
                    {
                        float side = ToLocal(parent, d).x >= 0f ? 1f : -1f;
                        _v += side * Mathf.Min(SwayWorld.LandingImpact, 10f) * 3.2f * (1f - dist / 2.6f) * Mathf.Min(maxOffset * 0.5f, 3f);
                    }
                }
            }

            // 弹簧（固定步长，稳定）
            float w0 = 2f * Mathf.PI * frequency;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / SubStep));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float a = w0 * w0 * (target - _x) - 2f * damping * w0 * _v + force;
                _v += a * h;
                _x += _v * h;
            }
            float limit = maxOffset * 1.6f;
            if (_x > limit) { _x = limit; if (_v > 0f) _v = 0f; }
            if (_x < -limit) { _x = -limit; if (_v < 0f) _v = 0f; }

            // 显示：软限制在 maxOffset 以内，取整；差超过 0.62 像素才换帧，避免来回闪
            int mid = frames.Length / 2;
            float shown = SoftLimit(_x, maxOffset);
            int k = Mathf.Clamp(Mathf.RoundToInt(shown), -mid, mid);
            if (k != _shown && Mathf.Abs(shown - _shown) > 0.62f) _shown = k;
            Sprite sprite = frames[mid + (mirrored ? -_shown : _shown)];
            if (_renderer.sprite != sprite) _renderer.sprite = sprite;
        }

        private static Vector2 ToLocal(Transform parent, Vector2 worldDir)
        {
            return parent != null ? (Vector2)parent.InverseTransformDirection(worldDir) : worldDir;
        }

        private static float SoftLimit(float x, float max)
        {
            float knee = 0.7f * max, a = Mathf.Abs(x);
            if (a <= knee || max <= 0f) return x;
            float rest = max - knee;
            return Mathf.Sign(x) * (knee + rest * (float)System.Math.Tanh((a - knee) / rest));
        }
    }
}
