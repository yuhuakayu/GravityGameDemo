using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Resource.Scripts.SceneFX;
using Resource.Scripts.SmallLevels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Resource.Scripts.Editor
{
    /// <summary>按关卡仕样书的字符地图生成独立场景，复用 Stage2 的玩家及玩法配置。</summary>
    public static class MazeSceneBuilder
    {
        private const string TemplateScene = "Assets/Scenes/Stage2.unity";
        private const string ArtRoot = "Assets/Resource/Art/Maze";
        private const string LaserPrefabPath = "Assets/Resource/Prefabs/LaserEmitter.prefab";
        private const string CameraSettingsPath = "Assets/Resource/Settings/MazeCameraSettings.asset";
        private const int TilesPerCell = 2;
        private const int MarginCells = 3;

        public static readonly string[][] Maps =
        {
            new[] { "######", "#U...#", "#....#", "#....#", "#.P..#", "######" },
            new[] { "######", "#...P#", "#.####", "#....#", "#...D#", "######" },
            new[] { "########", "#..P...#", "#..##>.#", "#......#", "#<####.#", "#......#", "#D.....#", "########" },
            new[] { "#########", "#P.....X#", "#####..##", "#X......#", "##..#####", "#.....X##", "####..###", "####.]###", "#########" },
            new[] { "##########", "#P......X#", "######..##", "######..##", "######..##", "#.......##", "#.......##", "#.......##", "#DXXXXXX##", "##########" },
            new[] { "##########", "#.....####", "#..#>.####", "#..#..####", "#..#..####", "#..#..####", "#..#..v..#", "#P.#....]#", "##########" },
            new[] { "##########", "#......^.#", "#<XXXXXX.#", "#.X####X.#", "#.X####X.#", "#.X####X.#", "#.X####X.#", "#.XXXXXX>#", "#D#Pv....#", "##########" },
            new[] { "##########", "##U#P....#", "##.####>.#", "##.####..#", "##.####..#", "##.......#", "##.......#", "##.......#", "##XXXXXXX#", "##########" },
            new[] { "##########", "#P......X#", "######..##", "######..##", "######..##", "#.......##", "#.......##", "#.......##", "#.......##", "#.XXXXXX##", "#.########", "#........#", "#........#", "#........#", "#.......]#", "#XXXXXXXX#", "##########" },
            new[] { "##########", "#UXXXXXX##", "#.......##", "#.......##", "#.......##", "######..##", "######>.##", "######..##", "#P......##", "##########" },
            new[] { "##########", "#P......X#", "######..##", "#X.......#", "##..######", "##..######", "##..######", "#........#", "#........#", "#........#", "#........#", "#XXXXXXXD#", "##########" },
            new[] { "##########", "#P......##", "######..##", "######..##", "######..##", "######..##", "######..##", "#...v...##", "#.......##", "#.......##", "#.......##", "#DXXXXXX##", "##########" },
            new[] { "##########", "#P..v...X#", "######..##", "#X...v...#", "##..######", "#....v..]#", "##########" },
            new[] { "#########", "####.]###", "####..###", "#.....X##", "##..#####", "#X......#", "#####..##", "#P.....X#", "#########" },
            new[] { "##########", "#........#", "#P.......#", "#####.####", "#####.####", "#.....####", "#D....####", "##########" },
            new[] { "##########", "#P...#####", "####.#####", "##X...####", "##X......#", "##X......#", "##X...X#D#", "##X...X###", "##X...X###", "##X...X###", "##X...X###", "##XJJJX###", "##########" },
            new[] { "##XXXJJXX##", "X...P.....X", "X.........J", "#[........J", "X.........X", "X.........X", "X.........X", "X.........X", "X.........X", "##XXJXXXX##" },
            new[] { "############", "#P........X#", "#iii.iiDiii#", "####X#######", "############" },
            new[] { "############", "#........P.#", "#.######.#.#", "#.#>....m#.#", "#.######.#.#", "#[.........#", "############" },
            new[] { "##########", "#P....####", "#####.####", "#####.####", "#####.####", "###M..####", "###.#.####", "###^#.####", "#####.####", "#####.####", "#####..M##", "#####.#.##", "#####.#^##", "#####.####", "#####.####", "###M..####", "###.#.####", "###^#.####", "#####.####", "#####.####", "#####..M##", "#####.#.##", "#####.#^##", "#####D####", "##########" },
            new[] { "#######", "#.....#", "#.XXX.#", "#.XXX.#", "#P....#", "#Z#####", "#D#####", "#######" },
            new[] { "########", "####...#", "####.X.#", "####P..#", "####Z###", "##...###", "##.X.###", "##...###", "####z###", "####D###", "########" }
        };

        [MenuItem("Tools/Gravity Game/Levels/Build Maze Scenes")]
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
                EnsureFolder("Assets/Resource/Settings");
                var cameraSettings = AssetDatabase.LoadAssetAtPath<MazeCameraSettings>(CameraSettingsPath);
                if (cameraSettings == null)
                {
                    cameraSettings = ScriptableObject.CreateInstance<MazeCameraSettings>();
                    AssetDatabase.CreateAsset(cameraSettings, CameraSettingsPath);
                }
                var serializedSettings = new SerializedObject(cameraSettings);
                var sizes = serializedSettings.FindProperty("sceneSizes");
                for (int i = sizes.arraySize; i < Maps.Length; i++)
                {
                    sizes.arraySize = i + 1;
                    sizes.GetArrayElementAtIndex(i).FindPropertyRelative("sceneName").stringValue = "Maze_" + (i + 1).ToString("00");
                    sizes.GetArrayElementAtIndex(i).FindPropertyRelative("size").floatValue = 0f;
                }
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                CreateMazeTiles();
                CreateLaserPrefab();
                AssetDatabase.SaveAssets();
                for (int i = 0; i < Maps.Length; i++) BuildScene(i);
                LevelOrder.RegisterBuildScenes();
                LevelOrder.UpdateLevelSelection();
                AssetDatabase.SaveAssets();
                Debug.Log($"[MazeSceneBuilder] 已生成 {Maps.Length} 个迷宫，并按 {LevelOrder.Scenes.Length} 关顺序更新构建和选关列表。");
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
            var cameraSettings = AssetDatabase.LoadAssetAtPath<MazeCameraSettings>(CameraSettingsPath);
            // Single 模式切场景会卸载上一场景用过的 Tile 实例，必须在打开场景后重新加载。
            Tile[] tiles = Enumerable.Range(0, 15).Select(i => AssetDatabase.LoadAssetAtPath<Tile>(
                ArtRoot + "/Tiles/tileset_" + i.ToString("00") + ".asset")).ToArray();
            if (tiles.Any(t => t == null)) throw new InvalidOperationException("迷宫 Tile 资源未完整加载。");
            var laserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LaserPrefabPath);
            string scenePath = "Assets/Scenes/Maze_" + (index + 1).ToString("00") + ".unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var roots = scene.GetRootGameObjects();
            var player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Single();
            player.maxFallSpeed = index < 3 ? 0f : 16f;
            var grade = player.GetComponent<PlayerColorGrade>() ?? player.gameObject.AddComponent<PlayerColorGrade>();
            string playerMaterialPath = "Assets/Resource/Art/Character/Player/PlayerUnlit.mat";
            var playerMaterial = AssetDatabase.LoadAssetAtPath<Material>(playerMaterialPath);
            if (playerMaterial == null)
            {
                playerMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
                AssetDatabase.CreateAsset(playerMaterial, playerMaterialPath);
            }
            grade.Configure(playerMaterial);
            var world = roots.SelectMany(r => r.GetComponentsInChildren<WorldRotator>(true)).Single();
            world.rotateSpeed = world.maxAngularSpeed = 150f;
            world.maxStepAngle = 3f;
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

            bool OriginalSolid(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return true;
                char cell = map[y / TilesPerCell][x / TilesPerCell];
                return cell == '#' || cell == 'i';
            }
            bool Solid(int x, int y) => OriginalSolid(x, y)
                || (map[y / TilesPerCell][x / TilesPerCell] == 'X' && SpikeTile(map, x, y) == null);
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

            BuildDecoration(grid.transform, map, index, OriginalSolid);
            BuildSceneEffects(grid.transform, walls, index, width, height);
            foreach (var renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, 2);
            foreach (var renderer in door.GetComponentsInChildren<Renderer>(true))
                renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, 2);

            door.transform.SetParent(grid.transform, false);
            door.transform.localRotation = Quaternion.identity;
            door.nextSceneName = LevelOrder.Next("Maze_" + (index + 1).ToString("00"));
            var feedback = new GameObject("NearMissFeedback").AddComponent<NearMissFeedback>();
            feedback.worldKind = "Maze";
            feedback.cooldown = 1.4f;
            feedback.gameObject.AddComponent<MazeNearMiss>();
            var emitters = new List<LaserEmitter>();
            for (int row = 0; row < map.Length; row++)
            for (int column = 0; column < map[row].Length; column++)
            {
                char cell = map[row][column];
                Vector2 center = new Vector2(column * TilesPerCell + 1f - width / 2f, height / 2f - row * TilesPerCell - 1f);
                if (cell == 'P') PlacePlayer(player, center);
                else if (cell == 'D' || cell == 'U' || cell == '[' || cell == ']') PlaceDoor(door, center, cell);
                else if (cell == 'X') BuildSpikeCell(grid.transform, center, column, row, map);
                else if (cell == '>' || cell == '<' || cell == '^' || cell == 'v')
                {
                    Vector2 direction = cell == '>' ? Vector2.right : cell == '<' ? Vector2.left : cell == '^' ? Vector2.up : Vector2.down;
                    var emitterObject = (GameObject)PrefabUtility.InstantiatePrefab(laserPrefab, world.transform);
                    emitterObject.name = "Laser_" + column + "_" + row;
                    emitterObject.transform.localPosition = center - direction * (1f - 0.125f);
                    emitterObject.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                    var emitter = emitterObject.GetComponent<LaserEmitter>();
                    if (index == 5 && column == 4 && row == 2) emitter.SetCycle(1.3f, 1.7f);
                    else if (index == 5 && column == 6 && row == 6) emitter.SetCycle(1f, 1.8f);
                    else if (index == 6) emitter.SetCycle(1f, 1.6f);
                    else if (index == 7 && column == 7 && row == 2) emitter.SetCycle(2.3f, 1.6f);
                    else if (index == 9 && column == 6 && row == 6) emitter.SetCycle(1.2f, 1.3f);
                    else if (index == 11 && column == 4 && row == 7) emitter.SetCycle(2f, 2.8f);
                    else if (index == 12 && column == 4 && row == 1) emitter.SetCycle(1.4f, 2f);
                    else if (index == 12 && column == 5 && row == 3) emitter.SetCycle(1.6f, 1.8f);
                    else if (index == 12 && column == 5 && row == 5) emitter.SetCycle(1.2f, 2f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(emitter);
                    emitters.Add(emitter);
                }
            }

            if (index >= 14)
            {
                var mechanisms = grid.gameObject.AddComponent<MazeMechanisms>();
                mechanisms.map = map; mechanisms.world = world; mechanisms.player = player; mechanisms.feedback = feedback;
                mechanisms.sprites = AssetDatabase.FindAssets("t:Sprite", new[] { ArtRoot + "/Sprites" })
                    .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Sprite>).ToArray();
                mechanisms.emitters = emitters.ToArray(); mechanisms.BuildVisuals();
                if (index == 14)
                {
                    var pendulum = grid.gameObject.AddComponent<SpikeBallPendulum>();
                    pendulum.map = map; pendulum.world = world; pendulum.player = player; pendulum.feedback = feedback;
                    pendulum.BuildVisuals();
                }
            }

            GravityLockDoorSetup.Apply(door);

            var intro = roots.SelectMany(r => r.GetComponentsInChildren<LevelIntroUI>(true)).Single();
            intro.boundsCenter = Vector2.zero;
            intro.boundsSize = new Vector2(width, height);
            intro.minOrthoSize = 4f;
            intro.maxOrthoSize = 20f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            camera.orthographic = true;
            var fit = camera.GetComponent<MazeCameraFit>() ?? camera.gameObject.AddComponent<MazeCameraFit>();
            fit.world = world.transform;
            fit.mazeSize = new Vector2(width, height);
            fit.settings = cameraSettings;
            fit.RestoreDefault();
            fit.FitImmediate();
            var zoom = camera.GetComponent<CameraZoomController>() ?? camera.gameObject.AddComponent<CameraZoomController>();
            zoom.enabled = false;
            zoom.minOrthoSize = 4f;
            zoom.maxOrthoSize = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(20, 19, 36, 255);
            var follow = camera.GetComponent<FollowTarget2D>();
            if (follow != null)
            {
                follow.target = world.transform;
                follow.offset = Vector2.zero;
                follow.smoothTime = 0.1f;
            }
            ExteriorTilePadding.Apply(walls, tiles[8], camera, intro.boundsSize, 20f);
            walls.transform.Find("Outer Fill").GetComponent<Tilemap>().color = new Color(0.513f, 0.487f, 0.529f, 1f);
            Physics2D.SyncTransforms();
            foreach (var emitter in emitters) emitter.RefreshBeam();
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void BuildDecoration(Transform parent, string[] map, int index, Func<int, int, bool> solid)
        {
            int width = map[0].Length * TilesPerCell, height = map.Length * TilesPerCell;
            var decoration = new GameObject("Decorations").transform;
            decoration.SetParent(parent, false);
            var occupied = new bool[width, height];
            var frames = Directory.GetFiles(PowerStationTilesetImporter.CaveRoot + "/Sway", "sway_veg_*_f*.png")
                .ToDictionary(p => int.Parse(Path.GetFileName(p).Split('_')[2]), p => LoadSwayFrames(p.Replace('\\', '/')));
            var phaseRandom = new System.Random(1000 + index);

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
                && !solid(x, y) && !blocked[x, y] && !occupied[x, y];
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
                    occupied[x, y + 1] = true;
                }
                else continue;
                occupied[x, y] = true;
                if (sprite == 7) continue;
                var plant = new GameObject("Veg_" + sprite.ToString("00"));
                plant.transform.SetParent(decoration, false);
                plant.transform.localPosition = new Vector3(x - width / 2f + 0.5f, height / 2f - y, 0f);
                var renderer = plant.AddComponent<SpriteRenderer>();
                renderer.sprite = frames[sprite][frames[sprite].Length / 2];
                renderer.sortingOrder = -1;
                var sway = plant.AddComponent<SwayItem>();
                sway.frames = frames[sprite];
                sway.phase = (float)(phaseRandom.NextDouble() * 1.2 - 0.6);
                var kind = sprite < 8 ? SwayKind.Vine
                    : sprite == 16 || sprite == 17 || sprite == 24 || sprite == 25 ? SwayKind.Grass
                    : sprite == 26 ? SwayKind.Mushroom
                    : sprite == 20 || sprite == 21 || sprite == 28 || sprite == 29 ? SwayKind.Corner
                    : SwayKind.Bush;
                sway.ApplyPreset(kind);
            }
        }

        private static Sprite[] LoadSwayFrames(string path)
        {
            var frames = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            if (frames.Length == 0) throw new InvalidOperationException("未切分摆动素材：" + path);
            return frames;
        }

        private sealed class ForegroundPlacement
        {
            public string file, kind;
            public float x, y, phase;
            public bool flip, back;
        }

        private static void BuildSceneEffects(Transform grid, Tilemap walls, int index, int width, int height)
        {
            string sceneName = "Maze_" + (index + 1).ToString("00");
            string root = PowerStationTilesetImporter.CaveRoot;
            var parallax = grid.gameObject.AddComponent<BackgroundParallax>();
            parallax.mapCenter = grid;
            parallax.layers = new BackgroundParallax.Layer[2];
            for (int i = 0; i < parallax.layers.Length; i++)
            {
                var background = new GameObject(i == 0 ? "BackgroundFar" : "BackgroundMid");
                background.transform.SetParent(grid, false);
                background.transform.localPosition = new Vector3(-12f - width / 2f, height / 2f + 12f, 0f);
                var renderer = background.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(root + "/Background/bg_" +
                    (i == 0 ? "far_" : "mid_") + sceneName + ".png");
                renderer.sortingOrder = -3 + i;
                parallax.layers[i] = new BackgroundParallax.Layer
                {
                    target = background.transform,
                    factor = i == 0 ? 0.18f : 0.072f,
                    maxOffset = i == 0 ? 2f : 0.9f
                };
            }

            var spores = new GameObject("Spores");
            spores.transform.SetParent(grid, false);
            spores.AddComponent<SporeField>().walls = walls;

            var shade = new GameObject("ExteriorShade");
            shade.transform.SetParent(grid, false);
            shade.transform.localPosition = new Vector3(-12f - width / 2f, height / 2f + 12f, 0f);
            var shadeRenderer = shade.AddComponent<SpriteRenderer>();
            shadeRenderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(root + "/Exterior/exterior_shade_" + sceneName + ".png");
            shadeRenderer.sortingOrder = 1;

            var foreground = new GameObject("Foreground");
            foreground.transform.SetParent(grid, false);
            var layer = foreground.AddComponent<ForegroundLayer>();
            layer.back = new GameObject("FgBack").transform;
            layer.front = new GameObject("FgFront").transform;
            layer.back.SetParent(foreground.transform, false);
            layer.front.SetParent(foreground.transform, false);
            layer.mapCenter = grid;
            var layout = JsonConvert.DeserializeObject<Dictionary<string, ForegroundPlacement[]>>(
                File.ReadAllText(root + "/Foreground/fg_layout.json"));
            foreach (var entry in layout[sceneName])
            {
                var plant = new GameObject(entry.file);
                plant.transform.SetParent(entry.back ? layer.back : layer.front, false);
                plant.transform.localPosition = new Vector3(entry.x - width / 2f, height / 2f - entry.y, 0f);
                var frames = LoadSwayFrames(root + "/Foreground/" + entry.file + ".png");
                var renderer = plant.AddComponent<SpriteRenderer>();
                renderer.sprite = frames[frames.Length / 2];
                renderer.flipX = entry.flip;
                renderer.sortingOrder = entry.back ? 10 : 11;
                var sway = plant.AddComponent<SwayItem>();
                sway.frames = frames;
                sway.mirrored = entry.flip;
                sway.phase = entry.phase;
                var kind = entry.kind == "vine" ? SwayKind.FgVine
                    : entry.kind == "roots" ? SwayKind.FgRoots
                    : entry.kind == "moss" ? SwayKind.FgMoss
                    : entry.kind == "clump" ? SwayKind.FgClump : SwayKind.FgCorner;
                sway.ApplyPreset(kind);
            }
        }

        internal static void PlacePlayer(PlayerController player, Vector2 center)
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

        private static void PlaceDoor(GoalDoor door, Vector2 center, char cell)
        {
            float angle = cell == 'U' ? 180f : cell == '[' ? -90f : cell == ']' ? 90f : 0f;
            Vector2 bottom = center + (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector2.down);
            PlaceDoor(door, bottom, angle);
        }

        internal static void PlaceDoor(GoalDoor door, Vector2 bottom, float angle)
        {
            door.transform.localPosition = Vector3.zero;
            door.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            var visual = door.transform.Find("DoorVisual");
            var renderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
            if (renderer != null)
            {
                Bounds spriteBounds = renderer.localBounds;
                Vector3 spriteBottom = visual.TransformPoint(new Vector3(spriteBounds.center.x, spriteBounds.min.y, spriteBounds.center.z));
                Vector3 localBottom = door.transform.parent.InverseTransformPoint(spriteBottom);
                door.transform.localPosition = new Vector3(bottom.x - localBottom.x, bottom.y - localBottom.y, 0f);
                return;
            }

            var map = door.GetComponent<Tilemap>();
            map.CompressBounds();
            Bounds bounds = map.localBounds;
            Vector3 tileBottom = door.transform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            Vector3 localTileBottom = door.transform.parent.InverseTransformPoint(tileBottom);
            door.transform.localPosition = new Vector3(bottom.x - localTileBottom.x, bottom.y - localTileBottom.y, 0f);
        }

        // 0 = wall/closed, 1 = open, 2 = laser emitter mounted on this face.
        private static int SpikeFace(string[] map, int column, int row, int dx, int dy)
        {
            int x = column + dx, y = row + dy;
            if (x < 0 || y < 0 || y >= map.Length || x >= map[y].Length) return 0;
            char cell = map[y][x];
            if (cell == '#' || cell == 'X' || cell == 'i') return 0;
            char outward = dx < 0 ? '<' : dx > 0 ? '>' : dy < 0 ? '^' : 'v';
            return cell == outward ? 2 : 1;
        }

        private static string SpikeTile(string[] map, int x, int y)
        {
            int dx = x % TilesPerCell == 0 ? -1 : 1;
            int dy = y % TilesPerCell == 0 ? -1 : 1;
            int horizontal = SpikeFace(map, x / TilesPerCell, y / TilesPerCell, dx, 0);
            int vertical = SpikeFace(map, x / TilesPerCell, y / TilesPerCell, 0, dy);
            if (horizontal == 2 || vertical == 2 || (horizontal != 1 && vertical != 1)) return null;
            string v = dy < 0 ? "up" : "down", h = dx < 0 ? "left" : "right";
            return "spike_" + (vertical == 1 ? v + (horizontal == 1 ? "_" + h : "") : h);
        }

        private static void BuildSpikeCell(Transform parent, Vector2 center, int column, int row, string[] map)
        {
            for (int y = 0; y < TilesPerCell; y++)
            for (int x = 0; x < TilesPerCell; x++)
            {
                string tile = SpikeTile(map, column * TilesPerCell + x, row * TilesPerCell + y);
                if (tile == null) continue;
                var spike = new GameObject("Spikes_" + column + "_" + row + "_" + x + "_" + y);
                spike.transform.SetParent(parent, false);
                spike.transform.localPosition = center + new Vector2(x - 0.5f, 0.5f - y);
                var renderer = spike.AddComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PowerStationTilesetImporter.CaveRoot + "/SpikeTiles/" + tile + ".png");
                renderer.sortingOrder = 2;
                spike.AddComponent<HazardKill>();
                var trigger = spike.AddComponent<BoxCollider2D>();
                trigger.size = Vector2.one;
                trigger.isTrigger = true;
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
