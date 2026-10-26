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
            { "menu.hint.title.nintendo", new[] { "摇杆 选择　[A] 确定", "スティック 選択　[A] 決定", "Stick Select   [A] Confirm" } },
            { "menu.hint.select.nintendo", new[] { "摇杆 选择　[{worldLeft}] [{worldRight}] 换世界　[A] 进入　[B] 返回", "スティック 選択　[{worldLeft}] [{worldRight}] ワールド　[A] 開始　[B] 戻る", "Stick Select   [{worldLeft}] [{worldRight}] World   [A] Play   [B] Back" } },
            { "input.start", new[] { "开始游戏 [{confirm}]", "ゲーム開始 [{confirm}]", "Start Game [{confirm}]" } },
            { "input.back", new[] { "返回 [{back}]", "戻る [{back}]", "Back [{back}]" } },
            { "input.close", new[] { "关闭 [{back}]", "閉じる [{back}]", "Close [{back}]" } },
            { "input.menu", new[] { "↑↓ 选择　[{confirm}] 确定　[{back}] 返回", "↑↓ 選択　[{confirm}] 決定　[{back}] 戻る", "Arrows Select   [{confirm}] Confirm   [{back}] Back" } },
            { "input.menu.nintendo", new[] { "摇杆 选择　[A] 确定　[B] 返回", "スティック 選択　[A] 決定　[B] 戻る", "Stick Select   [A] Confirm   [B] Back" } },
            { "level.Maze_01",       new[] { "起点", "スタート", "Start" } },
            { "level.Maze_02",       new[] { "回廊", "回廊", "Corridor" } },
            { "level.Maze_03",       new[] { "激光", "レーザー", "Laser" } },
            { "level.Maze_04",       new[] { "之字", "ジグザグ", "Zigzag" } },
            { "level.Maze_05",       new[] { "甩尾", "スイング", "Swing" } },
            { "level.Maze_06",       new[] { "闪光闸", "閃光ゲート", "Flash Gate" } },
            { "level.Maze_07",       new[] { "大回环", "大回転", "Big Loop" } },
            { "level.Maze_08",       new[] { "翻天", "天地返し", "Flip" } },
            { "level.Maze_09", new[] { "双甩", "ダブルスイング", "Double Swing" } },
            { "level.Maze_10", new[] { "倒甩", "逆さスイング", "Reverse Swing" } },
            { "level.Mine_05", new[] { "石雨", "石の雨", "Rock Rain" } },
            { "level.Mine_06", new[] { "断轨", "途切れたレール", "Broken Rail" } },
            { "level.Mine_07", new[] { "塌井", "崩れる縦穴", "Shaft Collapse" } },
            { "level.Mine_08", new[] { "滚石塌方", "落石と崩落", "Rockslide" } },
            { "level.Mine_09", new[] { "大逃亡", "大脱出", "Great Escape" } },
            { "level.Lava_05", new[] { "晃开再追", "揺らして逃げろ", "Shake and Run" } },
            { "level.Lava_06", new[] { "倒灌", "逆流", "Backflow" } },
            { "level.Lava_07", new[] { "封口追击", "封鎖と追跡", "Seal and Chase" } },
            { "level.Lava_08", new[] { "两间熔炉", "ふたつの炉", "Twin Furnaces" } },
            { "level.Sky_05", new[] { "翻板接云", "シーソーと雲", "Seesaw Cloud" } },
            { "level.Sky_06", new[] { "雷云", "機雷雲", "Mine Cloud" } },
            { "level.Sky_07", new[] { "升降雷阵", "浮かぶ機雷原", "Rising Mines" } },
            { "level.Sky_08", new[] { "滑降", "滑降", "Slalom" } },
            { "level.Sky_09", new[] { "翻板雷阵", "シーソー機雷", "Seesaw Mines" } },
            { "level.Sky_10", new[] { "双浮台", "ふたつの足場", "Twin Lifts" } },
            { "level.Sky_11", new[] { "天空大回环", "空の大回転", "Sky Loop" } },
            { "level.Maze_11", new[] { "之字甩", "ジグザグスイング", "Zigzag Swing" } },
            { "level.Maze_12", new[] { "激光甩", "レーザースイング", "Laser Swing" } },
            { "level.Mine_10", new[] { "滚石矿车", "落石トロッコ", "Rock Cart" } },
            { "level.Lava_09", new[] { "熔岩大逃亡", "溶岩大脱出", "Lava Escape" } },
            { "level.Maze_13", new[] { "激光之字", "レーザージグザグ", "Laser Zigzag" } },
            { "level.Maze_14", new[] { "倒之字", "逆さジグザグ", "Upside Zigzag" } },
            { "level.Sky_12", new[] { "翻板浮台", "シーソーと浮き台", "Seesaw Lift" } },
            { "level.Lava_10", new[] { "边灌边晃", "注ぎながら揺らせ", "Pour and Shake" } },
            { "level.Sky_13", new[] { "云台接力", "雲と足場のリレー", "Cloud Relay" } },
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
            { "level.Mine_12", new[] { "道岔", "分岐レール", "Rail Switch" } },
            { "level.Mine_11", new[] { "炸开", "爆破", "Blast Open" } },
            { "level.Mine_13", new[] { "钻头", "ドリル", "Drill" } },
            { "level.Mine_14", new[] { "瓦斯", "ガス噴出", "Gas Leak" } },
            { "level.Lava_11", new[] { "冰晶", "氷の結晶", "Ice Crystal" } },
            { "level.Lava_12", new[] { "冻住浪头", "波を凍らせろ", "Freeze the Wave" } },
            { "level.Lava_13", new[] { "火药桶", "火薬樽", "Powder Keg" } },
            { "level.Lava_14", new[] { "冷水封井", "冷水で封じろ", "Water Seal" } },
            { "level.Sky_14", new[] { "气流电梯", "上昇気流エレベーター", "Updraft Lift" } },
            { "level.Sky_15", new[] { "气球", "風船", "Balloon" } },
            { "level.Sky_16", new[] { "传送门", "ワープゲート", "Portal" } },
            { "level.Sky_17", new[] { "弹射", "ワープ射出", "Portal Launch" } },
            { "level.Maze_15", new[] { "摆锤门", "振り子の門", "Pendulum Gate" } },
            { "level.Maze_16", new[] { "弹弹块", "ゼリーブロック", "Jelly Bounce" } },
            { "level.Maze_17", new[] { "弹弹屋", "ゼリーの部屋", "Jelly Room" } },
            { "level.Maze_18", new[] { "冰面", "アイスフロア", "Ice Floor" } },
            { "level.Maze_19", new[] { "翻镜", "ゆれる鏡", "Swing Mirror" } },
            { "level.Maze_20", new[] { "抖镜", "鏡ゆらし", "Mirror Shake" } },
            { "level.Maze_21", new[] { "拧门", "ネジ扉", "Screw Gate" } },
            { "level.Maze_22", new[] { "双圈拧门", "二重ネジ扉", "Twin Screw Gates" } },
            { "pop.boom", new[] { "轰！", "ドカン！", "BOOM!" } },
            { "pop.tnt_fuse", new[] { "滋——要炸了！", "ジジッ…爆発するぞ！", "FUSE LIT!" } },
            { "pop.rail_turn", new[] { "咔嗒！拐了", "カチッ！曲がった", "CLICK! TURN" } },
            { "pop.rail_straight", new[] { "咔嗒！直走", "カチッ！まっすぐ", "CLICK! STRAIGHT" } },
            { "pop.drill_snap", new[] { "钻头撞断了", "ドリルが折れた", "DRILL SNAPPED" } },
            { "pop.gas_vent", new[] { "瓦斯口裂开了！", "ガスが噴き出した！", "GAS LEAK!" } },
            { "pop.cough", new[] { "咳！", "ゴホッ！", "COUGH!" } },
            { "pop.freeze", new[] { "冰晶碎了！岩浆冻住了", "氷が砕けた！溶岩が凍った", "ICE! THE LAVA FREEZES" } },
            { "pop.thaw", new[] { "岩浆化开了！", "氷が溶けた！", "IT'S THAWING!" } },
            { "pop.keg_lit", new[] { "点着了！", "着火！", "LIT!" } },
            { "pop.obsidian", new[] { "嗤——结成黑石了！", "ジュッ…黒石になった！", "HISS... OBSIDIAN!" } },
            { "pop.vent_sealed", new[] { "井口封住了！", "噴出口が塞がった！", "VENT SEALED!" } },
            { "pop.balloon_grab", new[] { "抓住气球！", "風船をつかんだ！", "GOT A BALLOON!" } },
            { "pop.balloon_out", new[] { "气球瘪了", "風船がしぼんだ", "OUT OF AIR" } },
            { "pop.warp", new[] { "嗖！", "シュッ！", "WHOOSH!" } },
            { "pop.jelly_big", new[] { "嘣！", "ボヨン！", "BOING!" } },
            { "pop.jelly_small", new[] { "啵", "ポヨ", "BOP" } },
            { "pop.mirror_flip", new[] { "镜子翻了", "鏡がひっくり返った", "MIRROR FLIP" } },
            { "pop.screw_1", new[] { "拧开了 1", "4", "1/4 回った / 1/4 TURNED" } },
            { "pop.screw_2", new[] { "拧开了 2", "4", "2/4 回った / 2/4 TURNED" } },
            { "pop.screw_3", new[] { "拧开了 3", "4", "3/4 回った / 3/4 TURNED" } },
            { "pop.screw_open", new[] { "拧开了！门闩松了", "ネジが外れた！", "UNSCREWED!" } },
            { "pop.screw_open_cw", new[] { "顺时针那道拧开了！", "右回りの扉が開いた！", "CLOCKWISE GATE OPEN!" } },
            { "pop.screw_open_ccw", new[] { "逆时针那道拧开了！", "左回りの扉が開いた！", "COUNTERCLOCKWISE GATE OPEN!" } },
            { "pop.screw_close", new[] { "又拧上了", "また締まった", "LOCKED AGAIN" } },
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

        public string Get(string key)
        {
            string promptKey = InputPrompts.UsesNintendo && Table.ContainsKey(key + ".nintendo") ? key + ".nintendo" : key;
            return InputPrompts.Resolve(Table.TryGetValue(promptKey, out var arr) ? arr[(int)CurrentLanguage] : key);
        }

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
