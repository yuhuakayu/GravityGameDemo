using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Resource.Scripts.Editor
{
    /// <summary>按关卡仕样书的字符地图生成五个独立场景，复用 Stage2 的玩家及玩法配置。</summary>
    public static class MazeSceneBuilder
    {
        private const string TemplateScene = "Assets/Scenes/Stage2.unity";
        private const string ArtRoot = "Assets/Resource/Art/Maze";
        private const string LaserPrefabPath = "Assets/Resource/Prefabs/LaserEmitter.prefab";
        private const int TilesPerCell = 2;
        private const int MarginCells = 3;

        private static readonly string[][] Maps =
        {
            new[] { "######", "#....#", "#....#", "#....#", "#.P.D#", "######" },
            new[] { "######", "#...P#", "#.####", "#....#", "#...D#", "######" },
            new[] { "########", "#.P....#", "#..##>.#", "#......#", "#<####.#", "#......#", "#D.....#", "########" },
            new[] { "#######", "##XD.X#", "##...##", "#..####", "#..####", "#..####", "#.P####", "#######" },
            new[] { "########", "#P..#D##", "###.v.v#", "###....#", "###.#..#", "###....#", "####.###", "########" }
        };

        private static readonly RectInt[][] FarRegions =
        {
            new[] { new RectInt(4, 2, 2, 5), new RectInt(2, 7, 3, 1) },
            new[] { new RectInt(2, 2, 2, 4), new RectInt(6, 2, 1, 1) },
            new[] { new RectInt(9, 2, 2, 3), new RectInt(3, 9, 2, 3), new RectInt(10, 11, 4, 1) },
            new[] { new RectInt(3, 8, 1, 4), new RectInt(6, 4, 2, 1) },
            new[] { new RectInt(10, 6, 2, 3), new RectInt(11, 4, 1, 2) }
        };

        private static readonly RectInt[][] MidRegions =
        {
            new[] { new RectInt(8, 4, 2, 2), new RectInt(6, 4, 2, 1) },
            new[] { new RectInt(2, 6, 3, 1), new RectInt(7, 7, 3, 1), new RectInt(8, 6, 2, 1) },
            new[] { new RectInt(2, 6, 2, 1), new RectInt(12, 9, 2, 2), new RectInt(6, 11, 2, 3) },
            new[] { new RectInt(2, 11, 1, 2), new RectInt(4, 6, 1, 1) },
            new[] { new RectInt(6, 10, 2, 1), new RectInt(12, 9, 2, 2), new RectInt(7, 6, 1, 2) }
        };

        [MenuItem("Tools/Gravity Game/Levels/Build Five Maze Scenes")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play，再生成迷宫场景。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前修改的场景，生成器不会覆盖未保存的内容。");

            var previousScenes = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EnsureFolder(ArtRoot);
                CreateMazeTiles();
                CreateLaserPrefab();
                AssetDatabase.SaveAssets();
                for (int i = 0; i < Maps.Length; i++) BuildScene(i);
                RegisterBuildScenes();
                UpdateLevelSelection();
                AssetDatabase.SaveAssets();
                Debug.Log("[MazeSceneBuilder] 已生成 Maze_01～Maze_05，并加入构建及 Level 1～5 选关列表。");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            }
        }

        private static void BuildScene(int index)
        {
            // 先另存副本；以下编辑只会写入新的 Maze 场景。
            var scene = EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);
            // Single 模式切场景会卸载上一场景用过的 Tile 实例，必须在打开场景后重新加载。
            Tile[] tiles = Enumerable.Range(0, 15).Select(i => AssetDatabase.LoadAssetAtPath<Tile>(
                ArtRoot + "/Tiles/tileset_" + i.ToString("00") + ".asset")).ToArray();
            if (tiles.Any(t => t == null)) throw new InvalidOperationException("迷宫 Tile 资源未完整加载。");
            var laserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LaserPrefabPath);
            string scenePath = "Assets/Scenes/Maze_" + (index + 1).ToString("00") + ".unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var roots = scene.GetRootGameObjects();
            var player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Single();
            var world = roots.SelectMany(r => r.GetComponentsInChildren<WorldRotator>(true)).Single();
            var door = roots.SelectMany(r => r.GetComponentsInChildren<GoalDoor>(true)).Single();
            var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));

            player.transform.SetParent(null, true);
            door.transform.SetParent(null, true);
            ClearChildren(world.transform);
            world.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            world.transform.localScale = Vector3.one;
            world.pivot = null;
            var worldBody = world.GetComponent<Rigidbody2D>();
            worldBody.bodyType = RigidbodyType2D.Kinematic;
            worldBody.useFullKinematicContacts = true;
            worldBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            worldBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // 保留组件作为已有自动安装入口的标记，避免运行时重新生成城堡背景及蜡烛。
            foreach (var atmosphere in roots.SelectMany(r => r.GetComponentsInChildren<LevelAtmosphere>(true)))
            {
                atmosphere.enabled = false;
                ClearChildren(atmosphere.transform);
            }
            foreach (var candles in roots.SelectMany(r => r.GetComponentsInChildren<CandleSpawner>(true)))
            {
                candles.enabled = false;
                ClearChildren(candles.transform);
            }
            foreach (var light in roots.SelectMany(r => r.GetComponentsInChildren<Light2D>(true)))
                if (light.lightType == Light2D.LightType.Global) { light.color = Color.white; light.intensity = 1f; }

            string[] map = Maps[index];
            int width = map[0].Length * TilesPerCell, height = map.Length * TilesPerCell;
            var grid = new GameObject("MazeGrid").AddComponent<Grid>();
            grid.transform.SetParent(world.transform, false);
            var background = MakeTilemap(grid.transform, "Cave Background", -10);
            background.color = new Color(0.46f, 0.46f, 0.58f, 1f);
            var walls = MakeTilemap(grid.transform, "Walls", 0);
            walls.gameObject.layer = LayerMask.NameToLayer("Wall");
            var wallBody = walls.gameObject.AddComponent<Rigidbody2D>();
            wallBody.bodyType = RigidbodyType2D.Kinematic;
            wallBody.gravityScale = 0f;
            wallBody.useFullKinematicContacts = true;
            wallBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            wallBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var wallCollider = walls.gameObject.AddComponent<TilemapCollider2D>();
            wallCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            var composite = walls.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            world.attachedGeometryBodies = new[] { wallBody };

            bool Solid(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return true;
                return map[y / TilesPerCell][x / TilesPerCell] == '#';
            }
            int margin = MarginCells * TilesPerCell;
            for (int y = -margin; y < height + margin; y++)
            for (int x = -margin; x < width + margin; x++)
            {
                var position = new Vector3Int(x - width / 2, height / 2 - y - 1, 0);
                background.SetTile(position, tiles[8]);
                if (Solid(x, y)) walls.SetTile(position, tiles[CaveReplicaSceneBuilder.AutoIndex(x, y, Solid)]);
            }
            walls.CompressBounds();
            walls.RefreshAllTiles();
            wallCollider.ProcessTilemapChanges();
            composite.GenerateGeometry();
            if (walls.GetUsedTilesCount() == 0 || composite.pathCount == 0)
                throw new InvalidOperationException(scenePath + " 未生成有效墙体碰撞，停止保存。");

            BuildDecoration(grid.transform, tiles, map, index, Solid);
            foreach (var renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, 2);
            foreach (var renderer in door.GetComponentsInChildren<Renderer>(true))
                renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, 2);

            door.transform.SetParent(grid.transform, false);
            door.transform.localRotation = Quaternion.identity;
            door.nextSceneName = index < Maps.Length - 1 ? "Maze_" + (index + 2).ToString("00") : "MainMenu";
            var emitters = new List<LaserEmitter>();
            for (int row = 0; row < map.Length; row++)
            for (int column = 0; column < map[row].Length; column++)
            {
                char cell = map[row][column];
                Vector2 center = new Vector2(column * TilesPerCell + 1f - width / 2f, height / 2f - row * TilesPerCell - 1f);
                if (cell == 'P') PlacePlayer(player, center);
                else if (cell == 'D') PlaceDoor(door, center);
                else if (cell == 'X') BuildSpikeCell(grid.transform, center, column, row);
                else if (cell == '>' || cell == '<' || cell == '^' || cell == 'v')
                {
                    Vector2 direction = cell == '>' ? Vector2.right : cell == '<' ? Vector2.left : cell == '^' ? Vector2.up : Vector2.down;
                    var emitterObject = (GameObject)PrefabUtility.InstantiatePrefab(laserPrefab, world.transform);
                    emitterObject.name = "Laser_" + column + "_" + row;
                    emitterObject.transform.localPosition = center - direction * (1f - 0.125f);
                    emitterObject.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                    emitters.Add(emitterObject.GetComponent<LaserEmitter>());
                }
            }

            var intro = roots.SelectMany(r => r.GetComponentsInChildren<LevelIntroUI>(true)).Single();
            intro.boundsCenter = Vector2.zero;
            intro.boundsSize = new Vector2(width, height);
            intro.maxOrthoSize = Mathf.Max(intro.maxOrthoSize, height);
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(height * 0.5f + 0.5f, (width * 0.5f + 0.5f) / camera.aspect);
            intro.maxOrthoSize = Mathf.Max(intro.maxOrthoSize, camera.orthographicSize);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(19, 18, 31, 255);
            var follow = camera.GetComponent<FollowTarget2D>();
            if (follow != null) { follow.target = player.transform; follow.offset = Vector2.zero; }
            ExteriorTilePadding.Apply(walls, tiles[8], camera, intro.boundsSize, intro.maxOrthoSize);
            Physics2D.SyncTransforms();
            foreach (var emitter in emitters) emitter.RefreshBeam();
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void BuildDecoration(Transform parent, Tile[] tiles, string[] map, int index, Func<int, int, bool> solid)
        {
            int width = map[0].Length * TilesPerCell, height = map.Length * TilesPerCell;
            Vector3Int Position(int x, int y) => new Vector3Int(x - width / 2, height / 2 - y - 1, 0);

            void FillSpace(string name, int order, Color color, RectInt[] regions)
            {
                var layer = MakeTilemap(parent, name, order);
                layer.color = color;
                bool InRegion(int x, int y) => regions.Any(r => r.Contains(new Vector2Int(x, y)));
                bool Connected(int x, int y) => solid(x, y) || InRegion(x, y);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (InRegion(x, y) && !solid(x, y))
                        layer.SetTile(Position(x, y), tiles[CaveReplicaSceneBuilder.AutoIndex(x, y, Connected)]);
            }

            FillSpace("BackgroundFar", -2, new Color(0.75f, 0.75f, 0.75f, 0.30f), FarRegions[index]);
            FillSpace("BackgroundMid", -1, new Color(0.85f, 0.85f, 0.85f, 0.55f), MidRegions[index]);
            var decoration = MakeTilemap(parent, "Decoration", 1);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(PowerStationTilesetImporter.CaveRoot + "/vegetation.png")
                .OfType<Sprite>().ToDictionary(s => s.name);
            var vegetation = new Tile[32];
            for (int i = 0; i < vegetation.Length; i++)
            {
                string name = "vegetation_" + i.ToString("00");
                string path = PowerStationTilesetImporter.CaveRoot + "/tiles/" + name + ".asset";
                vegetation[i] = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (vegetation[i] != null) continue;
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprites[name];
                tile.colliderType = Tile.ColliderType.None;
                AssetDatabase.CreateAsset(tile, path);
                vegetation[i] = tile;
            }

            var blocked = new bool[width, height];
            void BlockCell(int column, int row)
            {
                for (int y = 0; y < TilesPerCell; y++)
                for (int x = 0; x < TilesPerCell; x++)
                    blocked[column * TilesPerCell + x, row * TilesPerCell + y] = true;
            }
            for (int row = 0; row < map.Length; row++)
            for (int column = 0; column < map[row].Length; column++)
            {
                char cell = map[row][column];
                if (cell != '.' && cell != '#') BlockCell(column, row);
                int dx = cell == '>' ? 1 : cell == '<' ? -1 : 0;
                int dy = cell == 'v' ? 1 : cell == '^' ? -1 : 0;
                if (dx == 0 && dy == 0) continue;
                // 光束位于 2x2 格的中线，宽度覆盖中线两侧的地砖；一直排除到真墙。
                for (int x = column + dx, y = row + dy;
                    x >= 0 && y >= 0 && x < map[0].Length && y < map.Length && map[y][x] != '#';
                    x += dx, y += dy)
                    BlockCell(x, y);
            }

            var random = new System.Random(index + 1);
            int[] plants = { 16, 17, 24, 25, 18, 19, 22, 23, 27, 30, 31, 26 };
            int[] vines = { 0, 1, 2, 3, 6, 7 };
            bool Available(int x, int y) => x >= 0 && y >= 0 && x < width && y < height
                && !solid(x, y) && !blocked[x, y] && !decoration.HasTile(Position(x, y));
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (!Available(x, y)) continue;
                bool above = solid(x, y - 1), below = solid(x, y + 1);
                bool left = solid(x - 1, y), right = solid(x + 1, y);
                int sprite;
                if (above && left && random.NextDouble() < 0.80) sprite = 20;
                else if (above && right && random.NextDouble() < 0.80) sprite = 21;
                else if (below && left && random.NextDouble() < 0.70) sprite = 28;
                else if (below && right && random.NextDouble() < 0.70) sprite = 29;
                else if (below && random.NextDouble() < 0.60) sprite = plants[random.Next(plants.Length)];
                else if (above && Available(x, y + 1) && random.NextDouble() < 0.45)
                {
                    sprite = vines[random.Next(vines.Length)];
                    decoration.SetTile(Position(x, y + 1), vegetation[sprite + 8]);
                }
                else continue;
                decoration.SetTile(Position(x, y), vegetation[sprite]);
            }
        }

        private static void PlacePlayer(PlayerController player, Vector2 center)
        {
            player.transform.position = Vector3.zero;
            player.transform.rotation = Quaternion.identity;
            var body = player.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            Physics2D.SyncTransforms();
            Bounds bounds = player.GetComponent<Collider2D>().bounds;
            player.transform.position = new Vector3(center.x - bounds.center.x, center.y - 1f + 0.02f - bounds.min.y, 0f);
            // 手动 A/D、不跳跃及玩家尺寸直接沿用 Stage2，不重新调参。
        }

        private static void PlaceDoor(GoalDoor door, Vector2 center)
        {
            door.transform.localPosition = Vector3.zero;
            var visual = door.transform.Find("DoorVisual");
            var renderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
            if (renderer != null)
            {
                Bounds spriteBounds = renderer.localBounds;
                Vector3 bottom = visual.TransformPoint(new Vector3(spriteBounds.center.x, spriteBounds.min.y, spriteBounds.center.z));
                Vector3 localBottom = door.transform.parent.InverseTransformPoint(bottom);
                door.transform.localPosition = new Vector3(center.x - localBottom.x, center.y - 1f - localBottom.y, 0f);
                return;
            }

            var map = door.GetComponent<Tilemap>();
            map.CompressBounds();
            Bounds bounds = map.localBounds;
            Vector3 scale = door.transform.localScale;
            door.transform.localPosition = new Vector3(center.x - bounds.center.x * scale.x, center.y - 1f - bounds.min.y * scale.y, 0f);
        }

        private static void BuildSpikeCell(Transform parent, Vector2 center, int column, int row)
        {
            var cell = new GameObject("Spikes_" + column + "_" + row);
            cell.transform.SetParent(parent, false);
            cell.transform.localPosition = center;
            cell.AddComponent<HazardKill>();
            cell.AddComponent<BoxCollider2D>().isTrigger = true;
            cell.GetComponent<BoxCollider2D>().size = Vector2.one * TilesPerCell;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resource/Prefabs/Spike.prefab");
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                var spike = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cell.transform);
                // 整格 Box Trigger 已负责死亡判定，禁用重复且复杂的尖刺轮廓碰撞。
                foreach (var collider in spike.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
                Vector2 size = spike.GetComponent<SpriteRenderer>().sprite.bounds.size;
                spike.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);
                spike.transform.localPosition = new Vector3(x - 0.5f, y - 0.5f, 0f);
                spike.GetComponent<SpriteRenderer>().sortingOrder = 2;
            }
        }

        private static Tilemap MakeTilemap(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var map = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>().sortingOrder = order;
            return map;
        }

        private static Tile[] CreateMazeTiles()
        {
            EnsureFolder(ArtRoot + "/Tiles");
            var sprites = AssetDatabase.LoadAllAssetsAtPath(PowerStationTilesetImporter.CaveRoot + "/tileset.png").OfType<Sprite>().ToDictionary(s => s.name);
            var tiles = new Tile[15];
            for (int i = 0; i < tiles.Length; i++)
            {
                string name = "tileset_" + i.ToString("00");
                string path = ArtRoot + "/Tiles/" + name + ".asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
                if (tile == null) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, path); }
                tile.sprite = sprites[name];
                tile.colliderType = Tile.ColliderType.Grid;
                EditorUtility.SetDirty(tile);
                tiles[i] = tile;
            }
            return tiles;
        }

        private static GameObject CreateLaserPrefab()
        {
            string spritePath = ArtRoot + "/laser_emitter.png";
            // 四像素宽蓝色底座，橙色箭头朝本地 +X；无滤波、渐变或半透明边缘。
            var texture = new Texture2D(4, 10, TextureFormat.RGBA32, false);
            var pixels = new Color32[40];
            for (int y = 0; y < 10; y++)
            for (int x = 0; x < 4; x++)
                pixels[y * 4 + x] = x == 0 || y == 0 || y == 9 ? new Color32(39, 53, 91, 255) : new Color32(83, 125, 177, 255);
            for (int y = 2; y <= 7; y++)
            for (int x = 1; x <= 3; x++)
                if (Mathf.Abs(y - 4.5f) <= 3.5f - x) pixels[y * 4 + x] = new Color32(239, 168, 69, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(spritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(spritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            string materialPath = ArtRoot + "/LaserBeam.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var go = new GameObject("LaserEmitter");
            try
            {
                go.layer = LayerMask.NameToLayer("Wall");
                var emitter = go.AddComponent<LaserEmitter>();
                go.GetComponent<SpriteRenderer>().sortingOrder = 2;
                emitter.Configure(AssetDatabase.LoadAssetAtPath<Sprite>(spritePath), material, LayerMask.GetMask("Wall", "Box"));
                return PrefabUtility.SaveAsPrefabAsset(go, LaserPrefabPath);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static void RegisterBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            for (int i = 0; i < Maps.Length; i++)
            {
                string path = "Assets/Scenes/Maze_" + (i + 1).ToString("00") + ".unity";
                var entry = scenes.FirstOrDefault(s => s.path == path);
                if (entry != null) entry.enabled = true;
                else scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void UpdateLevelSelection()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
            var menu = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MainMenuUI>(true)).Single();
            menu.levels = Enumerable.Range(1, Maps.Length).Select(i => new LevelEntry
            {
                displayName = "Level " + i,
                sceneName = "Maze_" + i.ToString("00"),
                unlocked = true
            }).ToList();
            EditorUtility.SetDirty(menu);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ClearChildren(Transform parent)
        {
            while (parent.childCount > 0) Object.DestroyImmediate(parent.GetChild(0).gameObject);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
