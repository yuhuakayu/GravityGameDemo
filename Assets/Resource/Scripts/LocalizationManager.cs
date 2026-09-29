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
    /// 菜单、设置和关卡预览的中/日/英切换。每次启动默认英语，
    /// 本次运行内的语言选择跨场景保留。
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
            { "menu.start",          new[] { "开始游戏", "ゲーム開始", "Start Game" } },
            { "menu.quit",           new[] { "退出", "終了", "Quit Game" } },
            { "menu.levels",         new[] { "选择关卡", "ステージ選択", "Select Level" } },
            { "pause.title",         new[] { "已暂停", "一時停止", "Paused" } },
            { "pause.resume",        new[] { "继续", "再開", "Resume" } },
            { "pause.restart",       new[] { "重新开始", "リトライ", "Restart" } },
            { "pause.main_menu",     new[] { "返回主菜单", "メインメニュー", "Main Menu" } },
            { "settings.group.audio",    new[] { "音频", "オーディオ", "Audio" } },
            { "settings.group.display",  new[] { "显示", "画面", "Display" } },
            { "settings.group.language", new[] { "语言", "言語", "Language" } },
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

            CurrentLanguage = GameLanguage.English;
        }

        public string Get(string key) => Table.TryGetValue(key, out var arr) ? arr[(int)CurrentLanguage] : key;

        public string LanguageName(GameLanguage lang) => LanguageDisplayNames[(int)lang];

        public void SetLanguage(GameLanguage lang)
        {
            CurrentLanguage = lang;
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
