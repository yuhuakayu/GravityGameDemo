using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(200)]
    public sealed class NearMissFeedback : MonoBehaviour
    {
        public string worldKind = "Maze";
        public float cooldown = 1.4f;
        public Vector2 SpeedLineVelocityOverride;
        public float VoidDistance = float.MaxValue;

        private const float SlowScale = 0.32f;
        private static NearMissFeedback _slowOwner;
        private readonly Dictionary<string, float> _lastNear = new Dictionary<string, float>();
        private readonly List<Popup> _pops = new List<Popup>(3);
        private PlayerController _player;
        private CapsuleCollider2D _capsule;
        private GoalDoor _door;
        private Camera _camera;
        private SmallLevelCamera _smallCamera;
        private RectTransform _canvas;
        private NearMissScreenGraphic _screen;
        private LocalizationManager _localization;
        private float _slowRemaining;
        private float _mazeShake;
        private Vector2 _mazeShakeOffset;

        private sealed class Popup
        {
            public RectTransform Root;
            public TextMeshProUGUI[] Labels;
            public Color Fill;
            public Color Outline;
            public float Age, Lifetime;
            public bool Big;
        }

        private void Awake()
        {
            _player = FindFirstObjectByType<PlayerController>();
            if (_player != null) _capsule = _player.GetComponent<CapsuleCollider2D>();
            _door = FindFirstObjectByType<GoalDoor>();
            _camera = Camera.main;
            if (_camera != null) _smallCamera = _camera.GetComponent<SmallLevelCamera>();
            _localization = LocalizationManager.Instance;
            var canvasObject = new GameObject("NearMissCanvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8;
            PixelUI.ConfigureCanvas(canvas);
            canvasObject.GetComponent<CanvasScaler>().matchWidthOrHeight = 1f;
            _canvas = canvasObject.GetComponent<RectTransform>();
            var effects = new GameObject("SpeedLinesAndVignette", typeof(RectTransform));
            effects.transform.SetParent(_canvas, false);
            var rect = effects.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _screen = effects.AddComponent<NearMissScreenGraphic>();
            _screen.raycastTarget = false;
            _screen.Initialize(this, _camera);
        }

        public void NearMiss(string kind)
        {
            if (_door != null && _door.IsOpening) return;
            if (Time.timeScale == 0f || _player == null || !_player.IsGameplayActive) return;
            if (_lastNear.TryGetValue(kind, out float last) && Time.time - last < cooldown) return;
            _lastNear[kind] = Time.time;
            AddPopup("hud.near_miss", true);
            _slowOwner = this;
            _slowRemaining = 0.42f;
            Time.timeScale = SlowScale;
        }

        public void Pop(string localizationKey)
        {
            if (_player != null && _player.IsGameplayActive) AddPopup(localizationKey, false);
        }

        public static void RestoreTimeScale()
        {
            if (_slowOwner != null) _slowOwner._slowRemaining = 0f;
            _slowOwner = null;
            if (Mathf.Approximately(Time.timeScale, SlowScale)) Time.timeScale = 1f;
        }

        public void Shake(float amplitude)
        {
            if (_smallCamera != null) _smallCamera.Shake(amplitude);
            else _mazeShake = Mathf.Max(_mazeShake, amplitude);
        }

        private void Update()
        {
            RemoveMazeShake();
            if (_slowOwner == this)
            {
                _slowRemaining = Mathf.Max(0f, _slowRemaining - Time.unscaledDeltaTime);
                // Pausing may save 0.32 and restore it later; retain ownership until it can safely be cleared.
                if ((_slowRemaining <= 0f && Time.timeScale > 0f) || (_player != null && _player.IsDead) || (_door != null && _door.IsOpening))
                    RestoreTimeScale();
            }
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            if (_smallCamera == null && Time.deltaTime > 0f && _mazeShake > 0.001f)
            {
                _mazeShakeOffset = new Vector2(Random.value - 0.5f, Random.value - 0.5f) * (_mazeShake * 0.9f);
                _camera.transform.position += (Vector3)_mazeShakeOffset;
                _mazeShake *= Mathf.Exp(-6f * Time.deltaTime);
            }
            Vector2 feet = _player != null ? FeetPosition(_player, _capsule) : Vector2.zero;
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                Popup pop = _pops[i];
                pop.Age += Time.deltaTime;
                if (pop.Age >= pop.Lifetime)
                {
                    Destroy(pop.Root.gameObject);
                    _pops.RemoveAt(i);
                    continue;
                }
                Vector2 point = feet + new Vector2(pop.Big ? 1.6f : 0f, 2.4f + pop.Age * 1.6f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvas,
                    _camera.WorldToScreenPoint(point), null, out Vector2 screenPoint);
                pop.Root.anchoredPosition = screenPoint;
                float alpha = Mathf.Clamp01((1f - pop.Age / pop.Lifetime) * 1.5f);
                for (int j = 0; j < pop.Labels.Length; j++)
                {
                    Color color = j == 8 ? pop.Fill : pop.Outline;
                    color.a *= alpha;
                    pop.Labels[j].color = color;
                }
            }
            _screen.Refresh(_player, feet);
        }

        private void AddPopup(string key, bool big)
        {
            if (_pops.Count == 3)
            {
                Destroy(_pops[0].Root.gameObject);
                _pops.RemoveAt(0);
            }
            var root = new GameObject("Popup_" + key, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(_canvas, false);
            root.sizeDelta = new Vector2(800f, 80f);
            Color fill;
            if (worldKind == "Sky") fill = big ? Hex(0xFFD166) : Color.white;
            else if (worldKind == "Lava") fill = big ? Hex(0xFFD27A) : Hex(0xFFE2BD);
            else if (worldKind == "Mine") fill = big ? Hex(0xFF8A6A) : Hex(0xFFE2BD);
            else fill = big ? Hex(0xFF8AA6) : Hex(0xE6E2FF);
            Color outline = worldKind == "Sky" ? new Color32(20, 40, 70, 191) : new Color(0f, 0f, 0f, 0.75f);
            var labels = new TextMeshProUGUI[9];
            for (int i = 0; i < labels.Length; i++)
            {
                var go = new GameObject(i == 8 ? "Text" : "Outline", typeof(RectTransform));
                go.transform.SetParent(root, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                // Bitmap TMP fonts have no distance-field outline: duplicate glyphs at a fixed screen-space radius.
                rect.anchoredPosition = i == 8 ? Vector2.zero : new Vector2(Mathf.Cos(i * Mathf.PI / 4f), Mathf.Sin(i * Mathf.PI / 4f)) * 4.32f;
                labels[i] = PixelUI.EnsureText(go, big ? 36 : 22, true, TextAnchor.MiddleCenter, i == 8 ? fill : outline);
                labels[i].text = _localization.Get(key);
            }
            _pops.Add(new Popup { Root = root, Labels = labels, Fill = fill, Outline = outline,
                Lifetime = big ? 0.9f : 0.8f, Big = big });
        }

        private void OnDisable()
        {
            RemoveMazeShake();
            if (_slowOwner == this) RestoreTimeScale();
        }

        private void OnDestroy()
        {
            if (_slowOwner == this) RestoreTimeScale();
        }

        private void RemoveMazeShake()
        {
            if (_camera != null) _camera.transform.position -= (Vector3)_mazeShakeOffset;
            _mazeShakeOffset = Vector2.zero;
        }

        public static Vector2 FeetPosition(PlayerController player)
            => FeetPosition(player, player.GetComponent<CapsuleCollider2D>());

        private static Vector2 FeetPosition(PlayerController player, CapsuleCollider2D capsule)
            => capsule != null ? new Vector2(capsule.bounds.center.x, capsule.bounds.min.y) : (Vector2)player.transform.position;

        public static void GetPlayerCircles(PlayerController player, out Vector2 a, out Vector2 b, out float radius)
        {
            var capsule = player.GetComponent<CapsuleCollider2D>();
            if (capsule == null)
            {
                radius = 0.4f;
                a = (Vector2)player.transform.position + Vector2.up * radius;
                b = (Vector2)player.transform.position + Vector2.up * 1.13f;
                return;
            }
            bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
            Vector2 axis = vertical ? Vector2.up : Vector2.right;
            float localRadius = (vertical ? capsule.size.x : capsule.size.y) * 0.5f;
            float halfLine = Mathf.Max(0f, (vertical ? capsule.size.y : capsule.size.x) * 0.5f - localRadius);
            a = capsule.transform.TransformPoint(capsule.offset - axis * halfLine);
            b = capsule.transform.TransformPoint(capsule.offset + axis * halfLine);
            radius = capsule.transform.TransformVector((vertical ? Vector2.right : Vector2.up) * localRadius).magnitude;
        }

        private static Color Hex(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    internal sealed class NearMissScreenGraphic : MaskableGraphic
    {
        private struct SpeedLine { public float X, Y; }
        private readonly SpeedLine[] _lines = new SpeedLine[14];
        private NearMissFeedback _feedback;
        private Camera _camera;
        private Vector2 _feet, _direction;
        private float _speed, _rearrange;
        private int _lineCount;
        private bool _alive;

        public void Initialize(NearMissFeedback feedback, Camera camera)
        {
            _feedback = feedback;
            _camera = camera;
        }

        public void Refresh(PlayerController player, Vector2 feet)
        {
            _feet = feet;
            _alive = player != null && !player.IsDead;
            Vector2 velocity = player != null ? player.IntendedVelocity : Vector2.zero;
            bool overridden = _feedback.SpeedLineVelocityOverride.sqrMagnitude > 0.0001f;
            if (overridden) velocity = _feedback.SpeedLineVelocityOverride;
            _speed = overridden ? velocity.magnitude : Mathf.Abs(velocity.y);
            bool sky = _feedback.worldKind == "Sky";
            bool maze = _feedback.worldKind == "Maze";
            _lineCount = player != null && player.IsGameplayActive && _speed > (sky ? 7f : 7.5f)
                ? Mathf.Min(maze ? 12 : 14, Mathf.FloorToInt((_speed - (sky ? 6.5f : 7f)) * (maze ? 2f : sky ? 2.4f : 2.2f))) : 0;
            _direction = overridden ? -velocity.normalized : !sky && !maze && velocity.y > 0f ? Vector2.down : Vector2.up;
            _rearrange -= Time.deltaTime;
            if (_rearrange <= 0f)
            {
                _rearrange += 1f / 14f;
                for (int i = 0; i < _lines.Length; i++)
                    _lines[i] = new SpeedLine { X = Random.Range(maze ? -2.5f : -3.5f, maze ? 2.5f : 3.5f),
                        Y = Random.Range(maze ? -1f : -1.2f, maze ? 4f : 4.8f) };
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_feedback == null || _camera == null) return;
            bool sky = _feedback.worldKind == "Sky";
            bool maze = _feedback.worldKind == "Maze";
            Color shade = sky ? new Color32(230, 60, 80, 255) : _feedback.worldKind == "Lava" ? new Color32(18, 3, 0, 255) : Color.black;
            shade.a = sky ? (_alive ? Mathf.Clamp01((3f - _feedback.VoidDistance) / 3f) * 0.45f : 0f) : maze ? 0.45f : 0.55f;
            if (shade.a > 0f) AddVignette(mesh, shade, maze ? 0.35f : 0.3f, sky ? 0.9f : 0.95f);
            Color lineColor = sky ? new Color32(255, 255, 255, 140) : maze ? new Color32(214, 220, 255, 71) : new Color32(255, 236, 210, 71);
            Vector2 side = new Vector2(_direction.y, -_direction.x);
            float length = maze ? 1f + _speed * 0.1f : 1.2f + _speed * 0.12f;
            for (int i = 0; i < _lineCount; i++)
            {
                Vector2 start = _feet + side * _lines[i].X + _direction * (0.8f + _lines[i].Y);
                Vector2 end = start + _direction * length;
                Vector2 halfWidth = side * 0.0175f;
                AddQuad(mesh, Project(start - halfWidth), Project(start + halfWidth),
                    Project(end + halfWidth), Project(end - halfWidth), lineColor);
            }
        }

        private Vector2 Project(Vector2 point)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,
                _camera.WorldToScreenPoint(point), null, out Vector2 local);
            return local;
        }

        private void AddVignette(VertexHelper mesh, Color shade, float inner, float outer)
        {
            Rect rect = rectTransform.rect;
            Vector2 center = new Vector2(rect.center.x, rect.yMin + rect.height * 0.47f);
            float[] radii = { inner * rect.height, outer * rect.height, Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height) };
            const int segments = 64;
            int first = mesh.currentVertCount;
            for (int ring = 0; ring < radii.Length; ring++)
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * (2f * Mathf.PI / segments);
                Color color = shade;
                if (ring == 0) color.a = 0f;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radii[ring], color, Vector2.zero);
            }
            for (int ring = 0; ring < radii.Length - 1; ring++)
            for (int i = 0; i < segments; i++)
            {
                int a = first + ring * (segments + 1) + i;
                int b = a + segments + 1;
                mesh.AddTriangle(a, b, b + 1);
                mesh.AddTriangle(a, b + 1, a + 1);
            }
        }

        private static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
