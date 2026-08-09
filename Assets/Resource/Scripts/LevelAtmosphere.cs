using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Resource.Scripts
{
    /// <summary>
    /// 第一关氛围层（运行时自动搭建，场景里不需要手动放置）：
    ///   背景（远景，视差慢）——用项目自带的 Super_Retro_Collection/Backgrounds/DungeonA
    ///   前景剪影（近景，视差快）——没有对应美术资源，用程序化生成的石柱形状代替
    ///   萤火虫氛围粒子——同样没有素材，用粒子系统生成
    /// 由 PlayerController.Start() 自动创建。背景图资源放在名为 Resources 的目录下，
    /// 所以可以用 Resources.Load 在运行时直接加载，不需要在 Inspector 里手动拖引用。
    /// </summary>
    public class LevelAtmosphere : MonoBehaviour
    {
        [Header("背景（远景）")]
        public string backgroundResourcePath = "Backgrounds/DungeonA";
        [Tooltip("视差系数：以前用 0.2 制造远景移动慢的错觉，但背景图是有限大小的单张图，" +
                 "玩家只要跑出不到一个屏幕宽的距离，背景就跟不上摄像机、露出后面纯色的空白。" +
                 "改成 1 = 完全跟摄像机锁死，背景不会再被跑出范围（前景石柱还是用更大的视差系数保留纵深感）")]
        public float backgroundParallax = 1f;
        [Tooltip("背景相对摄像机视野的放大倍数，避免摄像机移动/旋转抖动时露出背景图边缘")]
        public float backgroundCoverageMultiplier = 2f;
        public Color backgroundTint = new Color(0.55f, 0.5f, 0.75f, 1f);

        [Header("前景剪影（近景）")]
        public int foregroundPillarCount = 3;
        [Tooltip("视差系数：大于 1 比摄像机移动更快，制造贴近镜头的错觉")]
        public float foregroundParallax = 1.4f;
        public Color foregroundColor = new Color(0.05f, 0.03f, 0.08f, 0.85f);

        [Header("萤火虫氛围粒子")]
        public int fireflyCount = 20;
        public Color fireflyColorWarm = new Color(1f, 0.85f, 0.4f, 0.9f);
        public Color fireflyColorCool = new Color(1f, 0.55f, 0.75f, 0.8f);

        [Header("环境光（调暗场景里已有的 Global Light 2D，营造黑暗地牢氛围）")]
        public bool tuneGlobalLight = true;
        public float globalLightIntensity = 0.35f;
        public Color globalLightColor = new Color(0.55f, 0.55f, 0.75f);

        void Start()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[LevelAtmosphere] 找不到 Main Camera，氛围层跳过初始化。");
                return;
            }

            EnsureCameraFollow(cam);
            BuildBackground(cam);
            BuildForegroundSilhouette(cam);
            BuildFireflies(cam);
            TuneAmbientLight();
        }

        private void TuneAmbientLight()
        {
            if (!tuneGlobalLight) return;

            var lights = FindObjectsOfType<Light2D>();
            foreach (var l in lights)
            {
                if (l.lightType == Light2D.LightType.Global)
                {
                    l.intensity = globalLightIntensity;
                    l.color = globalLightColor;
                }
            }
        }

        void EnsureCameraFollow(Camera cam)
        {
            if (cam.GetComponent<FollowTarget2D>() == null)
                cam.gameObject.AddComponent<FollowTarget2D>();
        }

        /// <summary>场景里已经摆好同名背景就直接复用（位置/缩放/贴图不重算），没有才照参数新建。</summary>
        void BuildBackground(Camera cam)
        {
            if (transform.Find("Background (Auto)") != null) return;

            var sprite = Resources.Load<Sprite>(backgroundResourcePath);
            if (sprite == null)
            {
                Debug.LogWarning($"[LevelAtmosphere] 找不到背景资源：{backgroundResourcePath}");
                return;
            }

            var go = new GameObject("Background (Auto)");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = backgroundTint;
            sr.sortingOrder = -100;

            // 按摄像机视野等比放大，避免摄像机移动/视差偏移时露出背景图边缘
            float viewHeight = cam.orthographicSize * 2f;
            float viewWidth  = viewHeight * cam.aspect;
            float spriteWidth  = Mathf.Max(0.01f, sprite.bounds.size.x);
            float spriteHeight = Mathf.Max(0.01f, sprite.bounds.size.y);
            float scaleX = viewWidth  / spriteWidth  * backgroundCoverageMultiplier;
            float scaleY = viewHeight / spriteHeight * backgroundCoverageMultiplier;
            float scale  = Mathf.Max(scaleX, scaleY);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var layer = go.AddComponent<ParallaxLayer>();
            layer.parallaxFactor  = backgroundParallax;
            layer.cameraTransform = cam.transform;
        }

        /// <summary>石柱剪影同名的就跳过（位置/大小是随机生成的，场景里已经摆好就不重新随机），
        /// 没有才照参数新建。</summary>
        void BuildForegroundSilhouette(Camera cam)
        {
            Sprite pillarSprite = CreatePillarSprite(64);

            float viewHeight = cam.orthographicSize * 2f;
            float viewWidth  = viewHeight * cam.aspect;

            for (int i = 0; i < foregroundPillarCount; i++)
            {
                string goName = $"ForegroundSilhouette (Auto) {i}";
                if (transform.Find(goName) != null) continue;

                var go = new GameObject(goName);
                go.transform.SetParent(transform, false);

                float xNorm = foregroundPillarCount > 1
                    ? (i / (float)(foregroundPillarCount - 1)) - 0.5f
                    : 0f;
                float x = cam.transform.position.x + xNorm * viewWidth * 1.3f;
                float y = cam.transform.position.y - viewHeight * 0.5f;
                go.transform.position = new Vector3(x, y, 0f);

                float w = viewWidth  * Random.Range(0.32f, 0.5f);
                float h = viewHeight * Random.Range(0.35f, 0.55f);
                go.transform.localScale = new Vector3(w, h, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = pillarSprite;
                sr.color  = foregroundColor;
                sr.sortingOrder = 50 + i;

                var layer = go.AddComponent<ParallaxLayer>();
                layer.parallaxFactor  = foregroundParallax;
                layer.cameraTransform = cam.transform;
            }
        }

        void BuildFireflies(Camera cam)
        {
            var existing = transform.Find("Fireflies (Auto)");
            if (existing != null)
            {
                // 粒子系统已经在场景里了，只需要保证跟随摄像机的引用是对的（场景切换后摄像机是新实例）
                var existingFollower = existing.GetComponent<FollowTarget2D>();
                if (existingFollower != null) existingFollower.target = cam.transform;
                return;
            }

            var go = new GameObject("Fireflies (Auto)");
            go.transform.SetParent(transform, false);
            go.transform.position = cam.transform.position;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startColor = Color.white;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.maxParticles = Mathf.Max(fireflyCount * 2, 50);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = fireflyCount / 4f;

            float viewHeight = cam.orthographicSize * 2f;
            float viewWidth  = viewHeight * cam.aspect;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(viewWidth * 1.1f, viewHeight * 1.1f, 0.1f);

            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            // x/y/z 必须用同一种曲线模式，否则报 "Particle Velocity curves must all be in the same mode"
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(fireflyColorWarm, 0f),
                    new GradientColorKey(fireflyColorCool, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = grad;

            var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
            psRenderer.sortingOrder = -10;

            // 跟随摄像机，保证萤火虫始终覆盖可视范围（不做视差，直接锁定摄像机位置）
            var follower = go.AddComponent<FollowTarget2D>();
            follower.target = cam.transform;
            follower.smoothTime = 0f;
        }

        /// <summary>生成一个矩形+顶部半圆的石柱剪影（正方形贴图，PPU=贴图宽度，缩放时 1 单位=1 世界单位，方便直接用 localScale 控制世界尺寸）</summary>
        private static Sprite CreatePillarSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            float radius = size * 0.5f;
            float domeStart = size - radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha;
                    if (y < domeStart)
                    {
                        alpha = 1f; // 矩形主体
                    }
                    else
                    {
                        float dx = x - radius;
                        float dy = y - domeStart;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        alpha = Mathf.Clamp01(radius - dist);
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0f), size);
        }
    }
}
