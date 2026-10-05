using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Resource.Scripts.Editor
{
    /// <summary>
    /// Import settings for the Power Station pixel-art pack:
    /// 32 PPU, point filter, no compression; sprite sheets are sliced and looping clips are created.
    /// Settings are applied automatically only on first import (no .meta yet), so later manual tweaks are kept.
    /// Use the menu item to force-reapply.
    /// </summary>
    public class PowerStationTilesetImporter : AssetPostprocessor
    {
        public const string Root = "Assets/Resource/Mode/Power Station Tileset";
        public const string CaveRoot = "Assets/Resource/Mode/Cave Tileset";
        public const string UiRoot = "Assets/Resource/Art/UI/Pixel";
        // UI: 100/3 so one art pixel = 3 canvas units at the 1280x720 reference resolution.
        private static readonly Dictionary<string, float> RootPixelsPerUnit = new Dictionary<string, float>
        {
            { Root, 32f },
            { CaveRoot, 16f },
            { UiRoot, 100f / 3f },
        };

        // 9-slice borders (left, bottom, right, top) in art pixels.
        private static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { UiRoot + "/ui_button_normal.png", new Vector4(3, 5, 3, 3) },
            { UiRoot + "/ui_button_selected.png", new Vector4(3, 5, 3, 3) },
            { UiRoot + "/ui_button_disabled.png", new Vector4(3, 5, 3, 3) },
            { UiRoot + "/ui_panel.png", new Vector4(2, 2, 2, 2) },
            { UiRoot + "/ui_bar_frame.png", new Vector4(2, 2, 2, 2) },
            { UiRoot + "/ui_bar_fill.png", new Vector4(1, 1, 1, 1) },
        };

        private static string FindRoot(string path)
        {
            foreach (var root in RootPixelsPerUnit.Keys)
                if (path.StartsWith(root + "/")) return root;
            return null;
        }

        private readonly struct Sheet
        {
            public readonly int FrameWidth;
            public readonly int FrameHeight;
            public readonly float Fps; // 0 = no animation clip
            public readonly int FrameCount;

            public Sheet(int frameWidth, int frameHeight, float fps, int frameCount = 0)
            {
                FrameWidth = frameWidth;
                FrameHeight = frameHeight;
                Fps = fps;
                FrameCount = frameCount;
            }
        }

        private static readonly Dictionary<string, Sheet> Sheets = new Dictionary<string, Sheet>
        {
            { Root + "/Tiles/Tileset.png", new Sheet(32, 32, 0f) },
            { Root + "/Animated/Trap.png", new Sheet(32, 48, 10f) },
            { Root + "/Animated/Card.png", new Sheet(24, 24, 10f) },
            { CaveRoot + "/tileset.png", new Sheet(16, 16, 0f) },
            { CaveRoot + "/vegetation.png", new Sheet(16, 16, 0f) },
            { CaveRoot + "/Door/door_idle.png", new Sheet(32, 32, 7f) },
            { CaveRoot + "/Door/door_open.png", new Sheet(32, 32, 12f) },
        };

        private static bool TryGetSheet(string path, out Sheet sheet)
        {
            if (Sheets.TryGetValue(path, out sheet)) return true;
            if (!path.StartsWith(CaveRoot + "/Sway/") && !path.StartsWith(CaveRoot + "/Foreground/")) return false;
            var match = Regex.Match(Path.GetFileName(path), @"_f(\d+)\.png$");
            if (!match.Success) return false;
            sheet = new Sheet(0, 0, 0f, int.Parse(match.Groups[1].Value));
            return true;
        }

        private void OnPreprocessTexture()
        {
            if (FindRoot(assetPath) == null) return;
            var importer = (TextureImporter)assetImporter;
            if (!importer.importSettingsMissing) return; // only first import
            ApplyBaseSettings(importer, assetPath);
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var pending = imported.Where(p => TryGetSheet(p, out _)).ToList();
            if (pending.Count == 0) return;
            EditorApplication.delayCall += () =>
            {
                foreach (var path in pending) SliceAndAnimate(path, false);
            };
        }

        [MenuItem("Tools/Gravity Game/像素素材/重新应用导入设置")]
        public static void ReapplyAll()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", RootPixelsPerUnit.Keys.ToArray());
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    ApplyBaseSettings(importer, path);
                    importer.SaveAndReimport();
                }
            }
            foreach (var guid in guids) SliceAndAnimate(AssetDatabase.GUIDToAssetPath(guid), true);
            Debug.Log($"[PowerStationTileset] 已重新应用导入设置：{guids.Length} 张图片。");
        }

        internal static void ApplyBaseSettings(TextureImporter importer, string path)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = TryGetSheet(path, out _) ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            importer.spritePixelsPerUnit = RootPixelsPerUnit[FindRoot(path)];
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            if (path.StartsWith(CaveRoot + "/Exterior/") || path.StartsWith(CaveRoot + "/Background/"))
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.TopLeft;
                settings.spritePivot = new Vector2(0f, 1f);
                importer.SetTextureSettings(settings);
            }
            if (Borders.TryGetValue(path, out Vector4 border)) importer.spriteBorder = border;
        }

        internal static void SliceAndAnimate(string path, bool force)
        {
            if (!TryGetSheet(path, out var sheet)) return;
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            if (force || provider.GetSpriteRects().Length == 0)
            {
                provider.GetDataProvider<ITextureDataProvider>().GetTextureActualWidthAndHeight(out int width, out int height);
                int frameWidth = sheet.FrameCount > 0 ? width / sheet.FrameCount : sheet.FrameWidth;
                int frameHeight = sheet.FrameCount > 0 ? height : sheet.FrameHeight;
                int columns = width / frameWidth;
                int rows = height / frameHeight;
                string baseName = Path.GetFileNameWithoutExtension(path);
                if (sheet.FrameCount > 0) baseName = baseName.Substring(0, baseName.LastIndexOf("_f"));

                // Keep existing sprite IDs when re-slicing so references in scenes/tiles survive.
                var existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);

                var rects = new List<SpriteRect>();
                int index = 0;
                for (int row = 0; row < rows; row++)          // top row first
                {
                    for (int col = 0; col < columns; col++)
                    {
                        string name = $"{baseName}_{index:00}";
                        rects.Add(new SpriteRect
                        {
                            name = name,
                            rect = new Rect(col * frameWidth, height - (row + 1) * frameHeight,
                                frameWidth, frameHeight),
                            alignment = sheet.FrameCount > 0 ? SpriteAlignment.TopCenter : SpriteAlignment.Center,
                            pivot = new Vector2(0.5f, sheet.FrameCount > 0 ? 1f : 0.5f),
                            spriteID = existing.TryGetValue(name, out var id) ? id : GUID.Generate(),
                        });
                        index++;
                    }
                }

                provider.SetSpriteRects(rects.ToArray());
                var nameFileIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
                nameFileIds?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                provider.Apply();
                importer.SaveAndReimport();
            }

            if (sheet.Fps > 0f) CreateLoopClip(path, sheet.Fps);
        }

        private static void CreateLoopClip(string sheetPath, float fps)
        {
            string clipPath = Path.ChangeExtension(sheetPath, ".anim");
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null) return;

            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(sheetPath)
                .OfType<Sprite>()
                .OrderBy(s => s.name)
                .ToArray();
            if (sprites.Length == 0) return;

            var clip = new AnimationClip { frameRate = fps };
            bool isDoor = sheetPath == CaveRoot + "/Door/door_idle.png" || sheetPath == CaveRoot + "/Door/door_open.png";
            var keys = new ObjectReferenceKeyframe[sprites.Length + (isDoor ? 0 : 1)];
            for (int i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = sprites[i] };
            if (!isDoor)
                keys[sprites.Length] = new ObjectReferenceKeyframe { time = sprites.Length / fps, value = sprites[sprites.Length - 1] };

            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = sheetPath != CaveRoot + "/Door/door_open.png";
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PowerStationTileset] 已生成动画：{clipPath}");
        }
    }
}
