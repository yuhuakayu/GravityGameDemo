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
            { "world.maze",          new[] { "迷宫", "迷宮", "The Maze" } },
            { "world.mine",          new[] { "矿坑", "鉱山", "The Mine" } },
            { "world.lava",          new[] { "岩浆", "溶岩", "The Forge" } },
            { "world.sky",           new[] { "天空", "天空", "The Sky" } },
            { "menu.hint.title",     new[] { "↑↓ 选择　Enter 确定", "↑↓ 選択　Enter 決定", "↑↓ Select   Enter Confirm" } },
            { "menu.hint.select",    new[] { "←→↑↓ 选择　Q E 换世界　Enter 进入　Esc 返回", "←→↑↓ 選択　Q E ワールド　Enter 開始　Esc 戻る", "Arrows Select   Q E World   Enter Play   Esc Back" } },
            { "level.Maze_01",       new[] { "起点", "スタート", "Start" } },
            { "level.Maze_02",       new[] { "回廊", "回廊", "Corridor" } },
            { "level.Maze_03",       new[] { "激光", "レーザー", "Laser" } },
            { "level.Maze_04",       new[] { "之字", "ジグザグ", "Zigzag" } },
            { "level.Maze_05",       new[] { "甩尾", "スイング", "Swing" } },
            { "level.Maze_06",       new[] { "闪光闸", "閃光ゲート", "Flash Gate" } },
            { "level.Maze_07",       new[] { "大回环", "大回転", "Big Loop" } },
            { "level.Maze_08",       new[] { "翻天", "天地返し", "Flip" } },
            { "level.Mine_01",       new[] { "落石", "落石", "Rockfall" } },
            { "level.Mine_02",       new[] { "塌方", "崩落", "Cave-in" } },
            { "level.Mine_03",       new[] { "矿车", "トロッコ", "Minecart" } },
            { "level.Mine_04",       new[] { "木板", "木の板", "Planks" } },
            { "level.Lava_01",       new[] { "封口", "封鎖", "Sealed" } },
            { "level.Lava_02",       new[] { "追击", "追跡", "Chase" } },
            { "level.Lava_03",       new[] { "晃开", "揺らして", "Shake Loose" } },
            { "level.Lava_04",       new[] { "冲门", "駆け込み", "Rush" } },
            { "level.Sky_01",        new[] { "跷跷板", "シーソー", "Seesaw" } },
            { "level.Sky_02",        new[] { "气球雷", "風船機雷", "Balloon Mines" } },
            { "level.Sky_03",        new[] { "散云", "ちぎれ雲", "Scattered Clouds" } },
            { "level.Sky_04",        new[] { "气球平台", "風船の足場", "Balloon Platforms" } },
            { "pause.title",         new[] { "已暂停", "一時停止", "Paused" } },
            { "pause.resume",        new[] { "继续", "再開", "Resume" } },
            { "pause.restart",       new[] { "重新开始", "リトライ", "Restart" } },
            { "pause.main_menu",     new[] { "返回主菜单", "メインメニュー", "Main Menu" } },
            { "hud.near_miss",       new[] { "险！", "危ない！", "CLOSE!" } },
            { "pop.rumble",          new[] { "轰隆隆…", "ゴゴゴ…", "RUMBLE..." } },
            { "pop.creak",           new[] { "嘎吱…", "ギシッ…", "CREAK..." } },
            { "pop.snap",            new[] { "咔嚓", "バキッ", "SNAP!" } },
            { "pop.crash",           new[] { "哐！", "ガシャン！", "CRASH!" } },
            { "pop.lava_wake",       new[] { "岩浆活了！", "溶岩が動いた！", "THE LAVA WAKES!" } },
            { "pop.tip",             new[] { "咔！", "カチッ！", "CLACK!" } },
            { "pop.cloud_fade",      new[] { "云要散了！", "雲が消える！", "FADING!" } },
            { "pop.balloon_up",      new[] { "被气球带上去了", "風船で上昇！", "UP WE GO!" } },
            { "pop.balloon_pop",     new[] { "啪！", "パン！", "POP!" } },
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
