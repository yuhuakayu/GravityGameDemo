using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Resource.Scripts.Editor
{
    /// <summary>Builds Assets/Scenes/Cave_Replica.unity from the ASCII layout below (one-time, rerunnable).</summary>
    public static class CaveReplicaSceneBuilder
    {
        private const string Root = PowerStationTilesetImporter.CaveRoot;
        private const string ScenePath = "Assets/Scenes/Cave_Replica.unity";
        private const int Margin = 3;

        // Room interior, top row first. '#' rock (attached to outer wall), 'd' inner platform (drawn dimmer), '.' air.
        private static readonly string[] Room =
        {
            "#......#",
            "........",
            "........",
            "........",
            "........",
            "##......",
            "##ddd...",
            "....d...",
            "....d...",
            "....d.##",
            "....d.##",
            "ddddd...",
            "...dd...",
            "##.dd...",
            "##.ddddd",
            "...d....",
            "...d....",
            "...d....",
            "...d....",
            "#..d...#",
        };

        // Decorations: (col, row) in room coordinates, vegetation sprite index. Vines use index and index+8 below it.
        private static readonly (int c, int r, int v)[] Vines = { (1, 0, 0), (2, 0, 2), (6, 0, 6), (0, 7, 1), (1, 7, 3), (7, 11, 0), (0, 15, 2), (1, 15, 1) };
        private static readonly (int c, int r, int v)[] Plants =
        {
            (1, 19, 16), (2, 19, 24), (3, 19, 25), (5, 19, 17), (6, 19, 24),
            (2, 5, 17), (4, 5, 26), (0, 10, 18), (1, 10, 22), (2, 10, 16), (3, 10, 24),
            (5, 13, 19), (6, 13, 16), (7, 13, 25), (6, 8, 23), (1, 12, 26),
        };

        [MenuItem("Tools/Gravity Game/像素素材/生成洞穴复刻场景")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            int w = Room[0].Length + Margin * 2, h = Room.Length + Margin * 2;
            char Cell(int x, int y) // y = 0 at top
            {
                int rx = x - Margin, ry = y - Margin;
                if (rx < 0 || ry < 0 || rx >= Room[0].Length || ry >= Room.Length) return '#';
                return Room[ry][rx];
            }

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(Room.Length * 0.5f + 0.5f,
                (Room[0].Length * 0.5f + 0.5f) / cam.aspect);
            cam.transform.position = new Vector3(w / 2f, -h / 2f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(19, 18, 31, 255); // cave air
            cam.gameObject.AddComponent<AudioListener>();

            var grid = new GameObject("Grid").AddComponent<Grid>();
            Tilemap inner = MakeTilemap(grid, "Inner Platforms", 0, true);
            inner.color = new Color(0.62f, 0.62f, 0.72f);
            Tilemap rock = MakeTilemap(grid, "Rock", 1, true);
            Tilemap deco = MakeTilemap(grid, "Decoration", 2, false);

            Sprite[] ts = Sprites("tileset.png", 15);
            Sprite[] veg = Sprites("vegetation.png", 32);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = Cell(x, y);
                if (c == '#') rock.SetTile(Pos(x, y), TileFor(ts, AutoIndex(x, y, (a, b) => Cell(a, b) == '#')));
                else if (c == 'd') inner.SetTile(Pos(x, y), TileFor(ts, AutoIndex(x, y, (a, b) => Cell(a, b) == 'd')));
            }
            foreach (var (c, r, v) in Vines)
            {
                deco.SetTile(Pos(c + Margin, r + Margin), TileFor(veg, v));
                deco.SetTile(Pos(c + Margin, r + Margin + 1), TileFor(veg, v + 8));
            }
            foreach (var (c, r, v) in Plants) deco.SetTile(Pos(c + Margin, r + Margin), TileFor(veg, v));

            ExteriorTilePadding.Apply(rock, TileFor(ts, 8), cam, new Vector2(Room[0].Length, Room.Length), cam.orthographicSize);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[CaveReplica] 已生成 " + ScenePath);
        }

        private static Vector3Int Pos(int x, int y) => new Vector3Int(x, -y - 1, 0);

        // Picks a tileset index (row-major, 5 per row) from which neighbours are solid.
        internal static int AutoIndex(int x, int y, System.Func<int, int, bool> solid)
        {
            bool n = solid(x, y - 1), s = solid(x, y + 1), wl = solid(x - 1, y), e = solid(x + 1, y);
            int col = !wl ? 2 : !e ? 4 : 3;
            int row = !n ? 0 : !s ? 2 : 1;
            if (col != 3 || row != 1) return row * 5 + col;           // edges and outer corners (3x3 block)
            if (!solid(x - 1, y - 1)) return 1 * 5 + 1;               // inner corners (2x2 block)
            if (!solid(x + 1, y - 1)) return 1 * 5 + 0;
            if (!solid(x - 1, y + 1)) return 0 * 5 + 1;
            if (!solid(x + 1, y + 1)) return 0 * 5 + 0;
            return 1 * 5 + 3;                                          // centre
        }

        private static Tilemap MakeTilemap(Grid grid, string name, int order, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(grid.transform, false);
            var map = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>().sortingOrder = order;
            if (collide)
            {
                int wall = LayerMask.NameToLayer("Wall");
                if (wall >= 0) go.layer = wall;
                go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
                go.AddComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
                go.AddComponent<CompositeCollider2D>();
            }
            return map;
        }

        private static Sprite[] Sprites(string file, int count)
        {
            string path = Root + "/" + file;
            string stem = System.IO.Path.GetFileNameWithoutExtension(file);
            var byName = AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            var result = new Sprite[count];
            for (int i = 0; i < count; i++) byName.TryGetValue($"{stem}_{i:00}", out result[i]);
            if (result.All(s => s == null)) throw new System.InvalidOperationException(path + " 还没有切片，请先让 Unity 完成导入。");
            return result;
        }

        private static readonly Dictionary<Sprite, Tile> Cache = new Dictionary<Sprite, Tile>();

        private static Tile TileFor(Sprite[] sheet, int index)
        {
            Sprite sprite = sheet[index];
            if (sprite == null) return null;
            if (Cache.TryGetValue(sprite, out Tile cached) && cached != null) return cached;
            string folder = Root + "/Tiles";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Tiles");
            string path = folder + "/" + sprite.name + ".asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.colliderType = sprite.name.StartsWith("tileset") ? Tile.ColliderType.Grid : Tile.ColliderType.None;
                AssetDatabase.CreateAsset(tile, path);
            }
            Cache[sprite] = tile;
            return tile;
        }
    }
}
