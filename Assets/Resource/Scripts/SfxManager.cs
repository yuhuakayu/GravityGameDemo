using System;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 音效管理器（运行时程序化合成版）
    ///
    /// 项目里目前没有任何音频素材文件，所以这里不加载 wav/mp3，
    /// 而是用波形函数在运行时直接生成 AudioClip（正弦波/方波/噪声混合），
    /// 音量、音高都能跟游戏状态（速度、冲击力）实时联动。
    /// 后续如果有美术/音效外包资源，直接把 PlayXxx 里的 AudioClip.PlayOneShot
    /// 换成加载好的素材即可，调用方（PlayerController/WorldRoot/PivotPendulum）不用改。
    ///
    /// 用法：SfxManager.Instance.PlayJump(); 首次访问会自动创建常驻 GameObject。
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
                    var go = new GameObject("SfxManager (Auto)");
                    go.AddComponent<SfxManager>(); // Awake 里会赋值 _instance
                }
                return _instance;
            }
        }

        [Header("总开关")]
        [Tooltip("关闭后所有音效不播放（包括正在淡出的旋转吱呀声），合成逻辑保留，随时可以重新打开")]
        public bool sfxEnabled = false;
        [Tooltip("主音量倍率（0~1），由 SettingsManager 的 Master/SFX 音量滑条驱动")]
        [Range(0f, 1f)] public float masterVolume = 1f;

        private const int SampleRate = 44100;
        private const int OneShotPoolSize = 4;

        private AudioSource[] _oneShotPool;
        private int _poolCursor;
        private AudioSource _creakSource;

        private AudioClip _footstepClip;
        private AudioClip _jumpClip;
        private AudioClip _landClip;
        private AudioClip _wallBumpClip;
        private AudioClip _pivotClackClip;
        private AudioClip _creakLoopClip;
        private AudioClip _buttonClickClip;
        private AudioClip _buttonHoverClip;
        private AudioClip _doorOpenClip;
        private AudioClip _transitionWhooshClip;
        private AudioClip _torchLoopClip;
        private AudioClip _gearClickClip;
        private AudioClip _stageCompleteClip;
        private AudioClip _playerDeathClip;

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

            _creakSource = gameObject.AddComponent<AudioSource>();
            _creakSource.playOnAwake  = false;
            _creakSource.spatialBlend = 0f;
            _creakSource.loop         = true;
            _creakSource.volume       = 0f;

            BuildClips();

            _creakSource.clip = _creakLoopClip;
            _creakSource.Play();
        }

        // ── 对外接口 ─────────────────────────────────────────────
        public void PlayFootstep(float speed01)
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f + UnityEngine.Random.Range(-0.08f, 0.08f);
            src.PlayOneShot(_footstepClip, Mathf.Lerp(0.15f, 0.4f, Mathf.Clamp01(speed01)) * masterVolume);
        }

        public void PlayJump()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_jumpClip, 0.5f * masterVolume);
        }

        public void PlayLand(float impact01)
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_landClip, Mathf.Lerp(0.2f, 0.7f, Mathf.Clamp01(impact01)) * masterVolume);
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

        /// <summary>世界旋转的"吱呀"摩擦音，每帧调用，音量/音高跟旋转速度联动，无输入时自动淡出（不停止播放，避免每次重启有起音瞬态）</summary>
        public void UpdateRotateCreak(float speed01)
        {
            if (!sfxEnabled)
            {
                _creakSource.volume = 0f;
                return;
            }
            speed01 = Mathf.Clamp01(speed01);
            float targetVol = (speed01 > 0.02f ? Mathf.Lerp(0.05f, 0.5f, speed01) : 0f) * masterVolume;
            _creakSource.volume = Mathf.Lerp(_creakSource.volume, targetVol, Time.deltaTime * 8f);
            _creakSource.pitch  = Mathf.Lerp(0.7f, 1.4f, speed01);
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

        /// <summary>世界旋转每转过 90° 播放一次的机械"咔嚓"声，impact01 可以传旋转速度来控制音量</summary>
        public void PlayGearClick(float impact01 = 1f)
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f + UnityEngine.Random.Range(-0.03f, 0.03f);
            src.PlayOneShot(_gearClickClip, Mathf.Lerp(0.3f, 0.7f, Mathf.Clamp01(impact01)) * masterVolume);
        }

        /// <summary>通关时的上升音阶提示音</summary>
        public void PlayStageComplete()
        {
            if (!sfxEnabled) return;
            var src = NextOneShotSource();
            src.pitch = 1f;
            src.PlayOneShot(_stageCompleteClip, 0.6f * masterVolume);
        }

        /// <summary>玩家死亡（撞到尖刺等致命物体）时的下坠音效</summary>
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
            _footstepClip   = CreateClip("Footstep",   0.08f, t => NoiseBurst(t, 0.08f, 0.005f));
            _jumpClip       = CreateClip("Jump",       0.15f, t => SineSweep(t, 0.15f, 300f, 900f) * EnvAD(t, 0.15f, 0.01f));
            _landClip       = CreateClip("Land",       0.14f, t => Sine(t, 90f) * EnvAD(t, 0.14f, 0.002f));
            _wallBumpClip   = CreateClip("WallBump",   0.06f, t => Square(t, 220f) * EnvAD(t, 0.06f, 0.002f));
            _pivotClackClip = CreateClip("PivotClack", 0.09f, t => (NoiseRaw() * 0.6f + Sine(t, 180f) * 0.6f) * EnvAD(t, 0.09f, 0.001f));
            // 循环用素材：分量频率都取 1/duration 的整数倍，保证首尾波形连续、循环无爆音
            _creakLoopClip  = CreateClip("CreakLoop",  0.5f,  WoodCreak);

            _buttonClickClip     = CreateClip("ButtonClick", 0.05f, t => Sine(t, 1200f) * EnvAD(t, 0.05f, 0.002f));
            _buttonHoverClip     = CreateClip("ButtonHover", 0.06f, t => Sine(t, 700f) * EnvAD(t, 0.06f, 0.015f));
            _doorOpenClip        = CreateClip("DoorOpen", 0.6f,
                t => (SineSweep(t, 0.6f, 500f, 120f) * 0.7f + NoiseRaw() * 0.2f) * EnvAD(t, 0.6f, 0.05f));
            _transitionWhooshClip = CreateClip("TransitionWhoosh", 0.3f,
                t => (NoiseRaw() * 0.6f + SineSweep(t, 0.3f, 800f, 150f) * 0.5f) * EnvAD(t, 0.3f, 0.02f));
            // 火堆噼啪声循环：稀疏的"啪"声事件，不是持续的沙沙噪声
            _torchLoopClip = BuildTorchLoopClip();

            // 齿轮"咔嚓"：短促噪声 + 低频闷响叠在一起，模拟机械阻尼感
            _gearClickClip = CreateClip("GearClick", 0.14f,
                t => NoiseRaw() * 0.6f * EnvAD(t, 0.04f, 0.001f) + Sine(t, 65f) * 0.8f * EnvAD(t, 0.14f, 0.005f));
            // 通关上升音阶：四个音依次播放，each note 用自己的局部时间起相位，避免音符之间相位跳变的爆音
            _stageCompleteClip = CreateClip("StageComplete", 0.56f, StageCompleteArp);
            // 死亡音效：下坠音阶 + 噪声，短促但明显区别于普通撞墙声
            _playerDeathClip = CreateClip("PlayerDeath", 0.45f,
                t => SineSweep(t, 0.4f, 500f, 90f) * 0.7f * EnvAD(t, 0.4f, 0.01f)
                   + NoiseRaw() * 0.35f * EnvAD(t, 0.1f, 0.002f));
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
        private static float Square(float t, float freq) => Mathf.Sign(Sine(t, freq));
        private static float NoiseRaw()                  => UnityEngine.Random.Range(-1f, 1f);

        private static float NoiseBurst(float t, float duration, float attack) =>
            NoiseRaw() * EnvAD(t, duration, attack);

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

        private static float WoodCreak(float t)
        {
            float tone = Sine(t, 40f) * 0.25f + Sine(t, 180f) * 0.35f
                       + Sine(t, 220f) * 0.25f + Sine(t, 260f) * 0.15f;
            float noise = NoiseRaw() * 0.18f;
            return tone + noise;
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

        /// <summary>四音上升琶音（C5-E5-G5-C6），每个音符独立起相位，避免拼接处爆音</summary>
        private static float StageCompleteArp(float t)
        {
            const float noteDur = 0.14f;
            if (t < noteDur * 1f) return NoteEnv(t - noteDur * 0f, noteDur, 523f);
            if (t < noteDur * 2f) return NoteEnv(t - noteDur * 1f, noteDur, 659f);
            if (t < noteDur * 3f) return NoteEnv(t - noteDur * 2f, noteDur, 784f);
            if (t < noteDur * 4f) return NoteEnv(t - noteDur * 3f, noteDur, 1047f);
            return 0f;
        }

        private static float NoteEnv(float localT, float duration, float freq) =>
            Sine(localT, freq) * EnvAD(localT, duration, 0.005f);
    }
}
