using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts
{
    public enum GameLanguage
    {
        Chinese = 0,
        Japanese = 1,
        English = 2
    }

    /// <summary>
    /// 极简中/日/英切换，只覆盖设置相关的文字（主菜单 Options 面板 + 游戏内设置面板），
    /// 不是全项目的完整本地化系统。PlayerPrefs 记住上次选的语言，下次进游戏自动生效。
    /// </summary>
    public class LocalizationManager : MonoBehaviour
    {
        private static LocalizationManager _instance;
        public static LocalizationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LocalizationManager (Auto)");
                    go.AddComponent<LocalizationManager>();
                }
                return _instance;
            }
        }

        private const string KeyLanguage = "Settings_Language";

        public GameLanguage CurrentLanguage { get; private set; } = GameLanguage.English;

        /// <summary>语言切换后触发，UI 订阅这个事件来刷新已经显示出来的文字</summary>
        public event Action OnLanguageChanged;

        private static readonly string[] LanguageDisplayNames = { "中文", "日本語", "English" };

        // key -> [中文, 日本語, English]
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "settings.title",      new[] { "设置", "設定", "Settings" } },
            { "settings.master",     new[] { "主音量", "マスター音量", "Master Volume" } },
            { "settings.music",      new[] { "音乐音量", "音楽音量", "Music Volume" } },
            { "settings.sfx",        new[] { "音效音量", "効果音音量", "SFX Volume" } },
            { "settings.display",    new[] { "分辨率", "解像度", "Display" } },
            { "settings.fullscreen", new[] { "全屏", "フルスクリーン", "Fullscreen" } },
            { "settings.language",   new[] { "语言", "言語", "Language" } },
            { "settings.close",      new[] { "关闭", "閉じる", "Close" } },
            { "settings.back",       new[] { "返回", "戻る", "Back" } },
            { "menu.options",        new[] { "设置", "設定", "Options" } },
        };

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            CurrentLanguage = (GameLanguage)PlayerPrefs.GetInt(KeyLanguage, (int)GameLanguage.English);
        }

        public string Get(string key) => Table.TryGetValue(key, out var arr) ? arr[(int)CurrentLanguage] : key;

        public string LanguageName(GameLanguage lang) => LanguageDisplayNames[(int)lang];

        public void SetLanguage(GameLanguage lang)
        {
            CurrentLanguage = lang;
            PlayerPrefs.SetInt(KeyLanguage, (int)lang);
            OnLanguageChanged?.Invoke();
        }

        public void CycleLanguage(int dir)
        {
            int count = LanguageDisplayNames.Length;
            int next = ((int)CurrentLanguage + dir + count) % count;
            SetLanguage((GameLanguage)next);
        }
    }
}
