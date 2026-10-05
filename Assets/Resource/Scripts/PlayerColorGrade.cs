using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Resource.Scripts
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerColorGrade : MonoBehaviour
    {
        [SerializeField, Range(0.6f, 1f)] private float brightness = 0.88f;
        [SerializeField, Range(0f, 0.15f)] private float coolness = 0.08f;
        [SerializeField, Range(0f, 1.2f)] private float lampIntensity = 0.5f;
        [SerializeField] private Material unlitMaterial;
        [SerializeField] private PlayerController player;
        [SerializeField] private SpriteRenderer playerRenderer;
        [SerializeField] private Light2D playerLight;

        public float Brightness
        {
            get => brightness;
            set { brightness = Mathf.Clamp(value, 0.6f, 1f); Apply(); }
        }

        public float Coolness
        {
            get => coolness;
            set { coolness = Mathf.Clamp(value, 0f, 0.15f); Apply(); }
        }

        public float LampIntensity
        {
            get => lampIntensity;
            set { lampIntensity = Mathf.Clamp(value, 0f, 1.2f); Apply(); }
        }

        public void Configure(Material material)
        {
            unlitMaterial = material;
            CacheReferences();
            Apply();
        }

        private void OnEnable()
        {
            CacheReferences();
            Apply();
        }

        private void Start()
        {
            // PlayerController creates the light in Start if it is absent from the scene.
            CacheReferences();
            Apply();
        }

        private void OnValidate()
        {
            brightness = Mathf.Clamp(brightness, 0.6f, 1f);
            coolness = Mathf.Clamp(coolness, 0f, 0.15f);
            lampIntensity = Mathf.Clamp(lampIntensity, 0f, 1.2f);
            Apply();
        }

        private void CacheReferences()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (playerRenderer == null)
                playerRenderer = transform.Find("PlayerIM")?.GetComponent<SpriteRenderer>();
            if (playerLight == null)
                playerLight = transform.Find("PlayerLight2D (Auto)")?.GetComponent<Light2D>();
        }

        private void Apply()
        {
            if (player != null && player.IsDead) return;
            if (playerRenderer != null)
            {
                if (unlitMaterial != null) playerRenderer.sharedMaterial = unlitMaterial;
                playerRenderer.color = new Color(brightness, brightness, Mathf.Min(1f, brightness + coolness), 1f);
            }
            if (playerLight != null) playerLight.intensity = lampIntensity;
        }
    }
}
