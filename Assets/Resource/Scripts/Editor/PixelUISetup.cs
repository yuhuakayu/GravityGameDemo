using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Resource.Scripts.Editor
{
    public static class PixelUISetup
    {
        private const string ArtRoot = "Assets/Resource/Art/UI/Pixel/";
        private const string FontRoot = "Assets/Resource/Fonts/";
        private const string ThemePath = "Assets/Resource/Resources/PixelUITheme.asset";

        [MenuItem("Tools/Gravity Game/UI/Create Pixel UI Theme")]
        public static void CreateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<PixelUITheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<PixelUITheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }
            theme.buttonNormal = LoadSprite("ui_button_normal");
            theme.buttonSelected = LoadSprite("ui_button_selected");
            theme.buttonDisabled = LoadSprite("ui_button_disabled");
            theme.panel = LoadSprite("ui_panel");
            theme.arrowLeft = LoadSprite("ui_arrow_left");
            theme.arrowRight = LoadSprite("ui_arrow_right");
            theme.arrowLeftDisabled = LoadSprite("ui_arrow_left_disabled");
            theme.arrowRightDisabled = LoadSprite("ui_arrow_right_disabled");
            theme.checkOff = LoadSprite("ui_check_off");
            theme.checkOn = LoadSprite("ui_check_on");
            theme.dividerDot = LoadSprite("ui_divider_dot");
            theme.barFrame = LoadSprite("ui_bar_frame");
            theme.barFill = LoadSprite("ui_bar_fill");
            theme.logo = LoadSprite("ui_logo_gravity");
            theme.levelCardNormal = LoadSprite("Level/ui_card_normal");
            theme.levelCardSelected = LoadSprite("Level/ui_card_selected");
            theme.levelCardLocked = LoadSprite("Level/ui_card_locked");
            theme.levelThumbnail = LoadSprite("Level/ui_thumb_placeholder");
            theme.levelLock = LoadSprite("Level/ui_icon_lock");
            theme.levelCheck = LoadSprite("Level/ui_icon_check");
            theme.levelTitle = LoadSprite("Level/ui_title_select_level");

            // Prewarm the text used by the UI; dynamic atlases also support later language switches.
            string[] sources = { "LocalizationManager.cs", "MainMenuUI.cs", "GameHUD.cs", "LevelIntroUI.cs", "Oxygen/OxygenHUD.cs" };
            string characters = new string(string.Concat(sources.Select(path => File.ReadAllText("Assets/Resource/Scripts/" + path)))
                .Where(c => !char.IsControl(c)).Distinct().ToArray());
            theme.regularFont = CreateFont("NotoSerifSC-Regular-GB2312", characters);
            theme.boldFont = CreateFont("NotoSerifSC-Bold-GB2312", characters);
            theme.titleFont = CreateTitleFont();
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Debug.Log("[PixelUI] Theme and dynamic TMP font assets saved.");
        }

        private static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + name + ".png");

        private static TMP_FontAsset CreateTitleFont()
        {
            string path = FontRoot + "Upside Down Title Pixel.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;

            var source = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + "NotoSerifSC-Regular-GB2312.otf");
            font = TMP_FontAsset.CreateFontAsset(source, 24, 1, GlyphRenderMode.RASTER_HINTED, 256, 256, AtlasPopulationMode.Dynamic, false);
            font.name = "Upside Down Title Pixel";
            font.TryAddCharacters("Upside Down");
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures)
            {
                atlas.name = "Upside Down Title Atlas";
                atlas.filterMode = FilterMode.Point;
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            EditorUtility.SetDirty(font);
            return font;
        }

        private static TMP_FontAsset CreateFont(string name, string characters)
        {
            string path = FontRoot + name + " Pixel.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font != null) return font;

            var source = AssetDatabase.LoadAssetAtPath<Font>(FontRoot + name + ".otf");
            font = TMP_FontAsset.CreateFontAsset(source, 24, 1, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            font.name = name + " Pixel";
            // Missing characters are reported explicitly: Dynamic cannot recover glyphs omitted from the supplied subset.
            font.TryAddCharacters(characters, out string missing);
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures)
            {
                if (atlas == null) continue;
                atlas.name = name + " Atlas";
                atlas.filterMode = FilterMode.Point;
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            EditorUtility.SetDirty(font);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[PixelUI] Font subset missing characters (including source comments): " + missing);
            return font;
        }
    }
}
