using System;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 音效管理器：已确认音效使用素材，保留摆板、悬停、转场和火把的合成声音。
    ///
    /// 首次访问会自动创建常驻 GameObject。
    /// </summary>
    public class SfxManager : MonoBehaviour
    {
        private static SfxManager _instance;
        public static SfxManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var prefab = Resources.Load<GameObject>("SfxManager");
                    if (prefab != null) Instantiate(prefab);
                    else new GameObject("SfxManager (Auto)").AddComponent<SfxManager>();
                }
                return _instance;
            }
        }

        [Header("总开关")]
        [Tooltip("关闭后不再播放音效，并立即停止所有世界旋转音源")]
        public bool sfxEnabled = false;
        [Tooltip("主音量倍率（0~1），由 SettingsManager 的 Master/SFX 音量滑条驱动")]
        [Range(0f, 1f)] public float masterVolume = 1f;

        [Header("世界旋转音效")]
        [SerializeField] private AudioClip[] worldRotateClickClips = new AudioClip[0];
        [SerializeField] private AudioClip worldRotateLoopClip;
        [SerializeField] private AudioClip worldRotateStopClip;
        [SerializeField, Min(0.1f)] private float clickDegrees = 8f;
        [SerializeField, Min(0.035f)] private float minClickInterval = 0.035f;
        [SerializeField] private Vector2 clickPitchRange = new Vector2(0.97f, 1.03f);
        [SerializeField] private Vector2 clickVolumeRange = new Vector2(0.8f, 1f);
        [SerializeField, Min(0f)] private float loopStartSpeed = 90f;
        [SerializeField, Min(0f)] private float loopFullSpeed = 150f;
        [SerializeField, Range(0f, 1f)] private float loopMaxVolume = 0.8f;
        [SerializeField, Min(0f)] private float rotationFadeIn = 0.05f;
        [SerializeField, Min(0f)] private float rotationFadeOut = 0.12f;
        [SerializeField, Min(0f)] private float rotationStopDelay = 0.1f;

        [Header("玩法与界面音效")]
        [SerializeField] private AudioClip _landClip;
        [SerializeField] private AudioClip _heavyLandClip;
        [Tooltip("重落地的下落速度阈值，12.5 约为标准重力下下落 4 格（8 单位）的速度")]
        [SerializeField, Min(0f)] private float heavyLandSpeed = 12.5f;
        [SerializeField] private AudioClip _wallBumpClip;
        [SerializeField] private AudioClip _playerDeathClip;
        [SerializeField] private AudioClip _doorOpenClip;
        [SerializeField] private AudioClip _oxygenPickupClip;
        [SerializeField] private AudioClip _buttonClickClip;
        [SerializeField] private AudioClip _uiMoveClip;
        [SerializeField] private AudioClip _uiBackClip;
        [SerializeField] private AudioClip _uiErrorClip;
        [SerializeField] private AudioClip _stageCompleteClip;
        [Header("关卡循环音效")]
        [SerializeField] private AudioClip ambientCaveClip;
        [SerializeField] private AudioClip fallWindClip;
        [SerializeField] private AudioClip doorHumClip;
        [SerializeField] private AudioClip laserHumClip;

        private const int SampleRate = 44100;
        private const int OneShotPoolSize = 4;

        private AudioSource[] _oneShotPool;
        private int _poolCursor;
        private AudioSource _rotateClickSource;
        private AudioSource _rotateLoopSource;
        private AudioSource _rotateStopSource;
        private WorldRotator _rotationWorld;
        private float _rotationSpeed;
        private float _pendingRotationDegrees;
        private float _clickAngle;
        private float _rotationDegrees;
        private double _lastClickTime = double.NegativeInfinity;
        private float _rotationEnvelope;
        private float _quietSeconds;
        private bool _rotationActive;

        private AudioClip _pivotClackClip;
        private AudioClip _buttonHoverClip;
        private AudioClip _transitionWhooshClip;
        private AudioClip _torchLoopClip;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _oneShotPool = new AudioSource[OneShotPoolSize];
            for (int i = 0; i < OneShotPoolSize; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake  = false;
                src.spatialBlend = 0f;
                _oneShotPool[i] = src;
            }

            _rotateClickSource = gameObject.AddComponent<AudioSource>();
            _rotateClickSource.playOnAwake = false;
            _rotateClickSource.spatialBlend = 0f;

            _rotateLoopSource = gameObject.AddComponent<AudioSource>();
            _rotateLoopSource.playOnAwake = false;
            _rotateLoopSource.spatialBlend = 0f;
            _rotateLoopSource.loop = true;
            _rotateLoopSource.volume = 0f;
            _rotateLoopSource.clip = worldRotateLoopClip;

            _rotateStopSource = gameObject.AddComponent<AudioSource>();
            _rotateStopSource.playOnAwake = false;
            _rotateStopSource.spatialBlend = 0f;
            _rotateStopSource.clip = worldRotateStopClip;

            BuildClips();
            gameObject.AddComponent<GameplayLoopAudio>().Configure(ambientCaveClip, fallWindClip, doorHumClip, laserHumClip);
        }

        private void LateUpdate()
        {
            // 在所有玩法 Update 之后再次检查，暂停或死亡当帧就静音。
            if (_rotationWorld == null || !sfxEnabled || masterVolume <= 0f ||
                _rotationWorld.RotationInput.IsGameplayBlocked(_rotationWorld))
            {
                StopWorldRotation();
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float loopTarget = _rotationWorld.IsRotating
                ? Mathf.InverseLerp(loopStartSpeed, Mathf.Max(loopStartSpeed + 0.01f, loopFullSpeed), _rotationSpeed)
                : 0f;
            float fadeTime = loopTarget > _rotationEnvelope ? rotationFadeIn : rotationFadeOut;
            _rotationEnvelope = Mathf.MoveTowards(_rotationEnvelope, loopTarget, fadeTime > 0f ? dt / fadeTime : 1f);
            _rotateLoopSource.volume = _rotationEnvelope * loopMaxVolume * masterVolume;
            if (_rotationEnvelope > 0f && !_rotateLoopSource.isPlaying && worldRotateLoopClip != null)
            {
                _rotateLoopSource.timeSamples = UnityEngine.Random.Range(0, worldRotateLoopClip.samples);
                _rotateLoopSource.Play();
            }
            else if (_rotationEnvelope <= 0f && _rotateLoopSource.isPlaying) _rotateLoopSource.Stop();

            if (_rotationWorld.IsRotating)
            {
                if (!_rotationActive)
                {
                    _rotationActive = true;
                    _rotateStopSource.Stop();
                }
                _quietSeconds = 0f;
                _rotationDegrees += _pendingRotationDegrees;
                _clickAngle += _pendingRotationDegrees;
            }
            else if (_rotationActive) _quietSeconds += dt;

            // 最后一步可能跨过多个齿，停转确认期间仍按最小间隔播完已累计的咔嚓。
            if (_rotationActive)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (_clickAngle >= Mathf.Max(0.1f, clickDegrees) &&
                    now - _lastClickTime >= Mathf.Max(0.035f, minClickInterval))
                {
                    _clickAngle -= Mathf.Max(0.1f, clickDegrees);
                    _lastClickTime = now;
                    if (worldRotateClickClips.Length > 0)
                    {
                        var clip = worldRotateClickClips[UnityEngine.Random.Range(0, worldRotateClickClips.Length)];
                        _rotateClickSource.pitch = UnityEngine.Random.Range(clickPitchRange.x, clickPitchRange.y);
                        float volume = UnityEngine.Random.Range(clickVolumeRange.x, clickVolumeRange.y);
                        _rotateClickSource.PlayOneShot(clip, volume * (1f - 0.5f * _rotationEnvelope) * masterVolume);
                    }
                }
                if (!_rotationWorld.IsRotating && _quietSeconds >= rotationStopDelay)
                {
                    if (_rotationDegrees >= clickDegrees && worldRotateStopClip != null)
                    {
                        _rotateStopSource.volume = masterVolume;
                        _rotateStopSource.Play();
                    }
                    _rotationActive = false;
                    _rotationDegrees = 0f;
                    _clickAngle = 0f;
                }
            }
            _pendingRotationDegrees = 0f;
        }

        private void OnDisable() => StopWorldRotation();

        // ── 对外接口 ─────────────────────────────────────────────
        public void PlayLand(float landingSpeed, float impact01)
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
            src.PlayOneShot(landingSpeed > heavyLandSpeed ? _heavyLandClip : _landClip,
                Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(impact01)) * masterVolume);
        }

        public void PlayWallBump()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f + UnityEngine.Random.Range(-0.05f, 0.05f);
            src.PlayOneShot(_wallBumpClip, 0.4f * masterVolume);
        }

        public void PlayPivotClack(float impact01)
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f + UnityEngine.Random.Range(-0.1f, 0.1f);
            src.PlayOneShot(_pivotClackClip, Mathf.Lerp(0.25f, 0.8f, Mathf.Clamp01(impact01)) * masterVolume);
        }

        /// <summary>接收各物理步累计的绝对转角及实际角速度，不能用首尾角度相减抵消往返转动。</summary>
        public void UpdateWorldRotation(WorldRotator world, float traveledDegrees, float angularSpeed)
        {
            _rotationWorld = world;
            _pendingRotationDegrees += traveledDegrees;
            _rotationSpeed = angularSpeed;
        }

        /// <summary>预览、暂停、死亡及切场景直接静音，不触发停转收尾声。</summary>
        public void StopWorldRotation()
        {
            if (_rotateClickSource != null) _rotateClickSource.Stop();
            if (_rotateLoopSource != null)
            {
                _rotateLoopSource.Stop();
                _rotateLoopSource.volume = 0f;
            }
            if (_rotateStopSource != null) _rotateStopSource.Stop();
            _rotationWorld = null;
            _rotationActive = false;
            _quietSeconds = 0f;
            _rotationEnvelope = 0f;
            _rotationSpeed = 0f;
            _pendingRotationDegrees = 0f;
            _clickAngle = 0f;
            _rotationDegrees = 0f;
        }

        public void PlayButtonClick()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_buttonClickClip, 0.35f * masterVolume);
        }

        public void PlayButtonHover()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_buttonHoverClip, 0.2f * masterVolume);
        }

        public void PlayUIMove() => PlayClip(_uiMoveClip);
        public void PlayUIBack() => PlayClip(_uiBackClip);
        public void PlayUIError() => PlayClip(_uiErrorClip);
        public void PlayOxygenPickup() => PlayClip(_oxygenPickupClip);

        private void PlayClip(AudioClip clip)
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(clip, masterVolume);
        }

        /// <summary>开门声开始约 0.3 秒后播放的通关声。</summary>
        public void PlayStageComplete()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_stageCompleteClip, 0.6f * masterVolume);
        }

        /// <summary>所有死因统一使用同一段死亡素材。</summary>
        public void PlayPlayerDeath()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_playerDeathClip, 0.65f * masterVolume);
        }

        public void PlayDoorOpen()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_doorOpenClip, 0.6f * masterVolume);
        }

        public void PlaySceneTransition()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_transitionWhooshClip, 0.5f * masterVolume);
        }

        /// <summary>
        /// 给火把等常驻场景物体挂一个循环的火焰噼啪声。
        /// 用非空间音效（spatialBlend=0）——之前用 3D 空间音效（spatialBlend=1）+ maxDistance=8，
        /// 但这是 2D 游戏，摄像机在 Z 轴上跟场景物体通常有固定偏移（常见是 -10），Unity 的 3D
        /// 距离衰减会把这个 Z 轴偏移也算进去，导致距离经常直接超出 maxDistance，声音完全出不来
        /// ——这就是蜡烛听不到声音的根因。改成非空间音效后音量固定，不再跟距离/摄像机位置有关。
        /// 挂载时判断一次总开关，不会跟着总开关实时联动。
        /// </summary>
        public void AttachTorchLoop(Transform target, float volume = 0.35f)
        {
            if (!sfxEnabled) return;
            if (target.GetComponent<AudioSource>() != null) return;

            var src = target.gameObject.AddComponent<AudioSource>();
            src.clip = _torchLoopClip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = volume * masterVolume;
            src.Play();
        }

        private AudioSource NextOneShotSource()
        {
            for (int i = 0; i < _oneShotPool.Length; i++)
            {
                int idx = (_poolCursor + i) % _oneShotPool.Length;
                if (!_oneShotPool[idx].isPlaying)
                {
                    _poolCursor = (idx + 1) % _oneShotPool.Length;
                    return _oneShotPool[idx];
                }
            }
            _poolCursor = (_poolCursor + 1) % _oneShotPool.Length;
            return _oneShotPool[_poolCursor];
        }

        // ── 波形合成 ─────────────────────────────────────────────
        private void BuildClips()
        {
            _pivotClackClip = CreateClip("PivotClack", 0.09f, t => (NoiseRaw() * 0.6f + Sine(t, 180f) * 0.6f) * EnvAD(t, 0.09f, 0.001f));
            _buttonHoverClip     = CreateClip("ButtonHover", 0.06f, t => Sine(t, 700f) * EnvAD(t, 0.06f, 0.015f));
            _transitionWhooshClip = CreateClip("TransitionWhoosh", 0.3f,
                t => (NoiseRaw() * 0.6f + SineSweep(t, 0.3f, 800f, 150f) * 0.5f) * EnvAD(t, 0.3f, 0.02f));
            // 火堆噼啪声循环：稀疏的"啪"声事件，不是持续的沙沙噪声
            _torchLoopClip = BuildTorchLoopClip();

        }

        private AudioClip CreateClip(string name, float duration, Func<float, float> waveform)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                data[i] = Mathf.Clamp(waveform(t), -1f, 1f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float t, float freq)   => Mathf.Sin(2f * Mathf.PI * freq * t);
        private static float NoiseRaw()                  => UnityEngine.Random.Range(-1f, 1f);

        /// <summary>线性调频正弦扫频（f0→f1），相位用积分保证连续不跳变</summary>
        private static float SineSweep(float t, float duration, float f0, float f1)
        {
            float k = (f1 - f0) / duration;
            float phase = 2f * Mathf.PI * (f0 * t + 0.5f * k * t * t);
            return Mathf.Sin(phase);
        }

        /// <summary>线性起音 + 指数衰减的包络</summary>
        private static float EnvAD(float t, float duration, float attack)
        {
            if (t < attack) return attack > 0f ? t / attack : 1f;
            float rel = duration - attack;
            if (rel <= 0f) return 0f;
            float decayT = Mathf.Clamp01((t - attack) / rel);
            return Mathf.Pow(1f - decayT, 2f);
        }

        /// <summary>
        /// 木柴篝火那种慢节奏噼啪声：不是连续的沙沙噪声，而是稀疏、不规律地炸出一声声短促的"啪"，
        /// 中间大段是安静的，模拟柴火偶尔炸裂的感觉（参考 Minecraft 篝火音效）。
        /// </summary>
        private AudioClip BuildTorchLoopClip()
        {
            const float duration = 3.2f;
            int totalSamples = Mathf.RoundToInt(duration * SampleRate);
            var data = new float[totalSamples];

            float t = UnityEngine.Random.Range(0.1f, 0.4f); // 第一声啪也稍微错开一点，别一开始就响
            while (t < duration - 0.2f)
            {
                float popDuration = UnityEngine.Random.Range(0.05f, 0.16f);
                float pitch = UnityEngine.Random.Range(55f, 140f);
                float amp = UnityEngine.Random.Range(0.35f, 0.85f);

                int startSample = Mathf.RoundToInt(t * SampleRate);
                int popSamples = Mathf.RoundToInt(popDuration * SampleRate);
                for (int i = 0; i < popSamples && startSample + i < totalSamples; i++)
                {
                    float localT = (float)i / SampleRate;
                    float env = EnvAD(localT, popDuration, 0.002f);
                    float noise = NoiseRaw() * 0.6f;
                    float thump = Sine(localT, pitch) * 0.6f;
                    data[startSample + i] += (noise + thump) * env * amp;
                }

                t += UnityEngine.Random.Range(0.35f, 1.1f); // 下一声啪之前的间隔，节奏放慢
            }

            // 非常轻的低频"底噪"，给整段留一点柴火燃烧的存在感，音量很小，不会听起来像沙沙声
            for (int i = 0; i < totalSamples; i++)
            {
                float tt = (float)i / SampleRate;
                data[i] += Sine(tt, 45f) * 0.02f * (0.5f + 0.5f * Sine(tt, 0.625f));
                data[i] = Mathf.Clamp(data[i], -1f, 1f);
            }

            var clip = AudioClip.Create("TorchLoop", totalSamples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

    }
}
