using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Resource.Scripts
{
    /// <summary>
    /// 挂在火把/篝火之类的装饰物上：自动加一个暖色闪烁的 Light2D + 火焰噼啪声循环。
    /// 这个脚本只负责"光 + 音"，火焰贴图/燃烧动画用现成的
    /// Super_Retro_Collection/Prefabs/Torches 或 Fires 预制体（自带 Animator）即可，两者叠加使用。
    /// </summary>
    public class TorchLight2D : MonoBehaviour
    {
        [Header("光照")]
        public Color lightColor = new Color(1f, 0.65f, 0.3f);
        public float baseIntensity = 1.2f;
        public float flickerAmount = 0.15f;
        public float flickerSpeed = 8f;
        public float outerRadius = 4f;
        public float innerRadius = 0.5f;

        private Light2D _light;
        private float _seed;

        void Start()
        {
            _seed = Random.Range(0f, 100f);

            _light = GetComponent<Light2D>();
            if (_light == null)
            {
                _light = gameObject.AddComponent<Light2D>();
                _light.lightType = Light2D.LightType.Point;
                _light.color = lightColor;
                _light.pointLightOuterRadius = outerRadius;
                _light.pointLightInnerRadius = innerRadius;
                _light.falloffIntensity = 0.5f;
            }

            SfxManager.Instance.AttachTorchLoop(transform);
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed);
            _light.intensity = baseIntensity + (n - 0.5f) * 2f * flickerAmount;
        }
    }
}
