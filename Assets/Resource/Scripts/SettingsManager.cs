using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 设置管理器：音量（主/音乐/音效）、分辨率、全屏，全部用 PlayerPrefs 存档，
    /// 下次进游戏自动读取。没有独立场景，随便挂一个空物体，或者用 Instance 自动创建。
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        private static SettingsManager _instance;
        public static SettingsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("SettingsManager (Auto)");
                    go.AddComponent<SettingsManager>();
                }
                return _instance;
            }
        }

        private const string KeyMasterVolume = "Settings_MasterVolume";
        private const string KeyMusicVolume  = "Settings_MusicVolume";
        private const string KeySfxVolume    = "Settings_SfxVolume";
        private const string KeyResolutionIndex = "Settings_ResolutionIndex";
        private const string KeyFullscreen   = "Settings_Fullscreen";

        [Header("── 当前值（只读，改用 SetXxx 方法）──")]
        [Range(0f, 1f)] public float masterVolume = 1f;
        [Range(0f, 1f)] public float musicVolume = 0.8f;
        [Range(0f, 1f)] public float sfxVolume = 1f;
        public bool fullscreen = true;

        /// <summary>常见分辨率列表，Options 面板的分辨率下拉框用这个</summary>
        public readonly Vector2Int[] CommonResolutions =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080),
        };
        public int resolutionIndex = 2; // 默认 1920x1080

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            LoadAndApply();
        }

        public void LoadAndApply()
        {
            masterVolume = PlayerPrefs.GetFloat(KeyMasterVolume, 1f);
            musicVolume  = PlayerPrefs.GetFloat(KeyMusicVolume, 0.8f);
            sfxVolume    = PlayerPrefs.GetFloat(KeySfxVolume, 1f);
            resolutionIndex = PlayerPrefs.GetInt(KeyResolutionIndex, CommonResolutions.Length - 1);
            fullscreen   = PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;

            ApplyAudio();
            ApplyDisplay();
        }

        public void SetMasterVolume(float v)
        {
            masterVolume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(KeyMasterVolume, masterVolume);
            ApplyAudio();
        }

        public void SetMusicVolume(float v)
        {
            musicVolume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(KeyMusicVolume, musicVolume);
            ApplyAudio();
        }

        public void SetSfxVolume(float v)
        {
            sfxVolume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(KeySfxVolume, sfxVolume);
            ApplyAudio();
        }

        public void SetResolutionIndex(int index)
        {
            resolutionIndex = Mathf.Clamp(index, 0, CommonResolutions.Length - 1);
            PlayerPrefs.SetInt(KeyResolutionIndex, resolutionIndex);
            ApplyDisplay();
        }

        public void SetFullscreen(bool value)
        {
            fullscreen = value;
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            ApplyDisplay();
        }

        private void ApplyAudio()
        {
            // 主音量 = 总闸门，音效音量 = 音效自己的闸门；两者相乘生效。
            // 音乐音量目前只存档，没有真正的背景音乐系统可以接（项目里没有音乐素材，
            // 程序合成做不出好听的背景音乐），先占位，以后接真素材时直接用这个字段。
            SfxManager.Instance.masterVolume = masterVolume * sfxVolume;
            // 音量滑条接管之后，之前那个调试用总开关就不用再单独管了，这里直接打开，
            // 真正的"静音"交给音量滑条拉到 0 就行
            SfxManager.Instance.sfxEnabled = true;
        }

        private void ApplyDisplay()
        {
            var res = CommonResolutions[resolutionIndex];
            Screen.SetResolution(res.x, res.y, fullscreen);
        }
    }
}
