using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Resource.Scripts.SceneFX
{
    /// <summary>
    /// 漂浮孢子：关卡空气里飘着的 1 像素小光点。跟着世界一起转（放在 MazeGrid 下），
    /// 同时慢慢往屏幕真正的「下」飘；世界转得快时会被甩动。画在墙后面。
    /// </summary>
    public class SporeField : MonoBehaviour
    {
        [Tooltip("墙体 Tilemap：cellBounds 里没有砖的格子就是空气")]
        public Tilemap walls;
        [Tooltip("每个空气格子的孢子数量")]
        public float density = 0.18f;
        public int sortingOrder = -1;
        public float fallSpeed = 0.28f;       // 往重力方向飘的速度（单位/秒）
        public float wander = 0.12f;          // 左右漂移
        public float swirl = 0.12f;           // 被旋转甩动的程度
        public Color pink = new Color32(170, 130, 190, 255);
        public Color blue = new Color32(130, 138, 200, 255);

        private struct Spore
        {
            public Vector2 p, v;
            public float life, age, ph;
            public Color color;
            public SpriteRenderer sr;
        }

        private readonly List<Vector2> _air = new List<Vector2>();
        private Spore[] _spores = new Spore[0];
        private static Sprite _dot;

        private void Start()
        {
            if (walls == null) return;
            BoundsInt b = walls.cellBounds;
            foreach (var cell in b.allPositionsWithin)
                if (!walls.HasTile(cell)) _air.Add(walls.GetCellCenterLocal(cell));
            if (_air.Count == 0) return;

            if (_dot == null)
            {
                var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _dot = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 16f);
            }
            int count = Mathf.RoundToInt(_air.Count * density);
            _spores = new Spore[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Spore");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _dot;
                sr.sortingOrder = sortingOrder;
                _spores[i].sr = sr;
                Respawn(ref _spores[i], true);
            }
        }

        private void Respawn(ref Spore s, bool randomAge)
        {
            Vector2 c = _air[Random.Range(0, _air.Count)];
            s.p = c + new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
            s.v = Vector2.zero;
            s.life = Random.Range(5f, 10f);
            s.age = randomAge ? Random.Range(0f, s.life) : 0f;
            s.ph = Random.Range(0f, 6.28f);
            s.color = Random.value < 0.45f ? pink : blue;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _spores.Length == 0) return;
            SwayWorld.Tick();
            Transform grid = transform.parent != null ? transform.parent : transform;
            Vector2 gravity = grid.InverseTransformDirection(Vector3.down);
            float t = SwayWorld.WindTime, k = 1f - Mathf.Exp(-1.5f * dt);
            for (int i = 0; i < _spores.Length; i++)
            {
                ref Spore s = ref _spores[i];
                s.age += dt;
                if (s.age >= s.life) Respawn(ref s, false);
                Vector2 drift = new Vector2(Mathf.Sin(t * 0.7f + s.ph), Mathf.Cos(t * 0.5f + s.ph * 1.3f)) * wander;
                Vector2 world = transform.TransformPoint(s.p);
                Vector2 r = world - SwayWorld.Pivot;
                Vector2 air = -SwayWorld.Omega * new Vector2(-r.y, r.x);
                Vector2 target = gravity * fallSpeed + drift + (Vector2)grid.InverseTransformDirection(air) * swirl;
                s.v += (target - s.v) * k;
                s.p += s.v * dt;
                s.sr.transform.localPosition = new Vector3(Mathf.Round(s.p.x * 16f) / 16f, Mathf.Round(s.p.y * 16f) / 16f, 0f);
                float alpha = Mathf.Min(1f, s.age / 1.2f, (s.life - s.age) / 1.2f) * 0.9f;
                Color c = s.color; c.a = Mathf.Max(0f, alpha);
                s.sr.color = c;
            }
        }
    }
}
