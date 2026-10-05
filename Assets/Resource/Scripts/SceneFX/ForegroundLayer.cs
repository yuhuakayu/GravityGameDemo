using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts.SceneFX
{
    /// <summary>
    /// 外圈前景植物：前后两排整体做视差（离摄像机更近，镜头移动时比世界多移一点），
    /// 玩家走到某一株后面时，那一株变半透明。本物体放在 MazeGrid 下（跟世界一起转）。
    /// </summary>
    public class ForegroundLayer : MonoBehaviour
    {
        public Transform front;               // 前排（更黑、视差大）
        public Transform back;                // 后排（颜色浅、视差小）
        [Tooltip("地图中心（MazeGrid 的原点就是地图中心）")]
        public Transform mapCenter;
        public float frontParallax = 0.12f;
        public float backParallax = 0.06f;
        [Header("玩家靠近变淡")]
        public float fadeAlpha = 0.3f;
        public float fadeSpeed = 10f;
        public float fadePadding = 0.3f;

        private Camera _cam;
        private PlayerController _player;
        private Vector3 _frontBase, _backBase;
        private readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();

        private void Start()
        {
            _cam = Camera.main;
            _player = FindFirstObjectByType<PlayerController>();
            if (front != null) { _frontBase = front.localPosition; _renderers.AddRange(front.GetComponentsInChildren<SpriteRenderer>()); }
            if (back != null) { _backBase = back.localPosition; _renderers.AddRange(back.GetComponentsInChildren<SpriteRenderer>()); }
        }

        private void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam != null && mapCenter != null)
            {
                Vector2 camOffset = (Vector2)_cam.transform.position - (Vector2)mapCenter.position;
                if (front != null) front.localPosition = _frontBase + transform.InverseTransformVector(-camOffset * frontParallax);
                if (back != null) back.localPosition = _backBase + transform.InverseTransformVector(-camOffset * backParallax);
            }

            if (_player == null) return;
            Vector2 feet = _player.transform.position;
            Vector2 p0 = feet + Vector2.up * 0.4f, p1 = feet + Vector2.up * 1.2f;
            float k = 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime);
            foreach (var sr in _renderers)
            {
                if (sr == null || sr.sprite == null) continue;
                float target = Inside(sr, p0) || Inside(sr, p1) ? fadeAlpha : 1f;
                Color c = sr.color;
                c.a += (target - c.a) * k;
                sr.color = c;
            }
        }

        private bool Inside(SpriteRenderer sr, Vector2 worldPoint)
        {
            Vector3 lp = sr.transform.InverseTransformPoint(worldPoint);
            if (sr.flipX) lp.x = -lp.x;
            Bounds b = sr.sprite.bounds;
            b.Expand(fadePadding * 2f);
            lp.z = b.center.z;
            return b.Contains(lp);
        }
    }
}
