using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>光束使用发射器局部 +X；墙体与光束同属 WorldRoot，长度不会因世界旋转落后一帧。</summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
    public sealed class LaserEmitter : MonoBehaviour
    {
        [Header("光束")]
        [SerializeField] private LayerMask worldGeometryMask;
        [SerializeField, Min(0.01f)] private float maxDistance = 100f;
        [SerializeField] private Vector2 muzzleOffset = new Vector2(0.16f, 0f);
        [SerializeField] private Color beamColor = new Color32(166, 227, 107, 255);
        [SerializeField, Min(0.001f)] private float coreWidth = 0.0625f;
        [SerializeField, Min(0.001f)] private float glowWidth = 0.1875f;
        [SerializeField, Range(0f, 1f)] private float glowAlpha = 0.25f;
        [SerializeField] private Material beamMaterial;

        [Header("开关周期")]
        [SerializeField, Min(0.01f)] private float onDuration = 2f;
        [Tooltip("0 表示持续开启。周期只在正式游戏中计时。")]
        [SerializeField, Min(0f)] private float offDuration;
        [SerializeField, Min(0f)] private float warningDuration = 0.5f;
        [SerializeField, Min(0.1f)] private float blinkFrequency = 8f;

        [SerializeField, HideInInspector] private BoxCollider2D beamCollider;
        [SerializeField, HideInInspector] private LineRenderer core;
        [SerializeField, HideInInspector] private LineRenderer glow;

        private readonly List<RaycastHit2D> _hits = new List<RaycastHit2D>(8);
        private BoxCollider2D _bodyCollider;
        private Rigidbody2D _worldBody;
        private PlayerController _player;
        private float _phase;

        public float BeamLength { get; private set; }
        public bool IsBeamActive { get; private set; }

        private void Reset()
        {
            worldGeometryMask = LayerMask.GetMask("Wall", "Box");
            GetComponent<BoxCollider2D>().size = new Vector2(0.25f, 0.65f);
        }

        /// <summary>编辑器生成工具用此入口建立可序列化的预制体层级。</summary>
        public void Configure(Sprite emitterSprite, Material material, LayerMask geometryMask, float distance = 100f)
        {
            GetComponent<SpriteRenderer>().sprite = emitterSprite;
            GetComponent<BoxCollider2D>().size = new Vector2(0.25f, 0.65f);
            beamMaterial = material;
            worldGeometryMask = geometryMask;
            maxDistance = distance;
            EnsureBeam();
            RefreshBeam();
        }

        private void Awake()
        {
            EnsureBeam();
            _player = FindFirstObjectByType<PlayerController>();
            RefreshBeam();
        }

        private void OnEnable() => RefreshBeam();

        private void Start()
        {
            // 允许场景安装工具先创建发射器、再创建玩家。
            if (_player == null) _player = FindFirstObjectByType<PlayerController>();
        }

        private void FixedUpdate()
        {
            if (_player != null && _player.IsGameplayActive && offDuration > 0f)
                _phase = Mathf.Repeat(_phase + Time.fixedDeltaTime, onDuration + offDuration);
            RefreshBeam();
        }

        public void RefreshBeam()
        {
            if (beamCollider == null || core == null || glow == null) return;

            Vector2 origin = transform.TransformPoint(muzzleOffset);
            Vector2 direction = transform.right;
            if (_worldBody != null)
            {
                // Raycast 查询当前物理姿态。剔除渲染插值，不能用下一步姿态射向尚未移动的墙。
                Transform root = _worldBody.transform;
                Quaternion rotation = Quaternion.Euler(0f, 0f, _worldBody.rotation) * Quaternion.Inverse(root.rotation);
                origin = _worldBody.position + (Vector2)(rotation * (origin - (Vector2)root.position));
                direction = rotation * direction;
            }

            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(worldGeometryMask);
            filter.useTriggers = false;
            Physics2D.Raycast(origin, direction, filter, _hits, maxDistance);
            BeamLength = maxDistance;
            foreach (RaycastHit2D hit in _hits)
                if (hit.collider != _bodyCollider)
                    BeamLength = Mathf.Min(BeamLength, hit.distance);

            float length = BeamLength / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
            Vector3 start = muzzleOffset;
            Vector3 end = start + Vector3.right * length;
            core.SetPosition(0, start);
            core.SetPosition(1, end);
            glow.SetPosition(0, start);
            glow.SetPosition(1, end);
            beamCollider.offset = muzzleOffset + Vector2.right * (length * 0.5f);
            beamCollider.size = new Vector2(Mathf.Max(0.001f, length), coreWidth);

            IsBeamActive = isActiveAndEnabled && BeamLength > 0.001f && (offDuration <= 0f || _phase < onDuration);
            bool warning = offDuration > 0f && _phase >= Mathf.Max(0f, onDuration - warningDuration);
            float intensity = warning && Mathf.FloorToInt(_phase * blinkFrequency * 2f) % 2 != 0 ? 0.3f : 1f;
            ApplyLineStyle(core, coreWidth, new Color(beamColor.r * intensity, beamColor.g * intensity,
                beamColor.b * intensity, beamColor.a));
            ApplyLineStyle(glow, glowWidth, new Color(beamColor.r, beamColor.g, beamColor.b,
                beamColor.a * glowAlpha * intensity));
            core.enabled = glow.enabled = IsBeamActive;
            beamCollider.enabled = IsBeamActive && _player != null && _player.IsGameplayActive;
        }

        internal void TryKill(Collider2D other)
        {
            if (!IsBeamActive || !isActiveAndEnabled) return;
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null && player.IsGameplayActive) player.Die();
        }

        private void EnsureBeam()
        {
            _bodyCollider = GetComponent<BoxCollider2D>();
            _bodyCollider.isTrigger = false;
            _worldBody = GetComponentInParent<Rigidbody2D>();
            Transform beam = transform.Find("LaserBeam");
            if (beam == null)
            {
                beam = new GameObject("LaserBeam").transform;
                beam.SetParent(transform, false);
            }
            beam.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            beamCollider = beam.GetComponent<BoxCollider2D>();
            if (beamCollider == null) beamCollider = beam.gameObject.AddComponent<BoxCollider2D>();
            beamCollider.isTrigger = true;
            LaserBeamTrigger trigger = beam.GetComponent<LaserBeamTrigger>();
            if (trigger == null) trigger = beam.gameObject.AddComponent<LaserBeamTrigger>();
            trigger.Configure(this);
            core = EnsureLine(beam, "Core", 2);
            glow = EnsureLine(beam, "Glow", 1);
        }

        private LineRenderer EnsureLine(Transform parent, string childName, int orderOffset)
        {
            Transform child = parent.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(parent, false);
            }
            LineRenderer line = child.GetComponent<LineRenderer>();
            if (line == null) line = child.gameObject.AddComponent<LineRenderer>();
            SpriteRenderer emitter = GetComponent<SpriteRenderer>();
            line.sharedMaterial = beamMaterial;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.numCapVertices = line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.sortingLayerID = emitter.sortingLayerID;
            line.sortingOrder = emitter.sortingOrder + orderOffset;
            return line;
        }

        private static void ApplyLineStyle(LineRenderer line, float width, Color color)
        {
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
        }

        private void OnDisable()
        {
            IsBeamActive = false;
            if (beamCollider != null) beamCollider.enabled = false;
            if (core != null) core.enabled = false;
            if (glow != null) glow.enabled = false;
        }
    }
}
