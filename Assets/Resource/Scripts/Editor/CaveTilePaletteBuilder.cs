using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Resource.Scripts.Editor
{
    /// <summary>
    /// Builds the Cave Tileset tile palette (Tile assets + palette prefab).
    /// Runs once automatically when the palette is missing; can be rebuilt from the menu.
    /// </summary>
    public static class CaveTilePaletteBuilder
    {
        private const string Root = PowerStationTilesetImporter.CaveRoot;
        private const string TileFolder = Root + "/Tiles";
        private const string PaletteName = "Cave Palette";
        private static readonly string[] Sheets = { Root + "/tileset.png", Root + "/vegetation.png" };

        [InitializeOnLoadMethod]
        private static void AutoBuildOnce()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + PaletteName + ".prefab") != null) return;
                if (Sheets.Any(p => AssetDatabase.LoadAllAssetRepresentationsAtPath(p).OfType<Sprite>().Count() < 2)) return; // not sliced yet
                Build();
            };
        }

        [MenuItem("Tools/Gravity Game/像素素材/重新生成洞穴调色盘")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(TileFolder)) AssetDatabase.CreateFolder(Root, "Tiles");

            string palettePath = Root + "/" + PaletteName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(palettePath) == null)
                GridPaletteUtility.CreateNewPalette(Root, PaletteName, GridLayout.CellLayout.Rectangle,
                    GridPalette.CellSizing.Automatic, Vector3.one, GridLayout.CellSwizzle.XYZ);

            GameObject contents = PrefabUtility.LoadPrefabContents(palettePath);
            try
            {
                Tilemap tilemap = contents.GetComponentInChildren<Tilemap>();
                tilemap.ClearAllTiles();
                int rowOffset = 0;
                foreach (string sheet in Sheets)
                {
                    var pixels = new Texture2D(2, 2);
                    pixels.LoadImage(File.ReadAllBytes(sheet)); // readable copy for empty-cell check
                    int maxRow = 0;
                    foreach (Sprite sprite in AssetDatabase.LoadAllAssetRepresentationsAtPath(sheet).OfType<Sprite>())
                    {
                        Rect r = sprite.rect;
                        if (IsEmpty(pixels, r)) continue;
                        Tile tile = LoadOrCreateTile(sprite, sheet == Sheets[0]); // vegetation is decoration, no collider
                        int col = Mathf.RoundToInt(r.x / r.width);
                        int rowFromTop = Mathf.RoundToInt((pixels.height - r.yMax) / r.height);
                        maxRow = Mathf.Max(maxRow, rowFromTop);
                        tilemap.SetTile(new Vector3Int(col, -(rowOffset + rowFromTop), 0), tile);
                    }
                    Object.DestroyImmediate(pixels);
                    rowOffset += maxRow + 2; // one empty row between sheets
                }
                PrefabUtility.SaveAsPrefabAsset(contents, palettePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[CaveTilePalette] 已生成调色盘：" + palettePath);
        }

        private static bool IsEmpty(Texture2D tex, Rect r)
        {
            Color[] block = tex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
            return block.All(c => c.a < 0.01f);
        }

        private static Tile LoadOrCreateTile(Sprite sprite, bool solid)
        {
            string path = TileFolder + "/" + sprite.name + ".asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.sprite = sprite;
            tile.colliderType = solid ? Tile.ColliderType.Grid : Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            return tile;
        }
    }
}
