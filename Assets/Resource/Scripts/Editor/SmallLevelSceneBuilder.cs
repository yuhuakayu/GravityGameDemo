using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Resource.Scripts.SmallLevels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Resource.Scripts.Editor
{
    public static class SmallLevelSceneBuilder
    {
        private const string Source = "Docs/LevelSpec/小关卡全集";
        private const string Art = "Assets/Resource/Art/SmallLevels";
        private const string DataRoot = "Assets/Resource/LevelData";
        private const string Template = "Assets/Scenes/Stage2.unity";

        [MenuItem("Tools/Gravity Game/Levels/Build Small Levels")]
        public static void BuildAll() => Build(new[] { "Mine", "Lava", "Sky" });

        public static void BuildWorld(string kind) => Build(new[] { kind });

        public static string ValidateGeneratedLevels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play，再验证生成场景。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前修改的场景。");
            string[] expected = LevelOrder.Scenes.Select(name => "Assets/Scenes/" + name + ".unity").ToArray();
            foreach (string path in expected)
                if (!File.Exists(path)) throw new FileNotFoundException("缺少生成场景：" + path);
            string[] built = EditorBuildSettings.scenes.Where(s => s.enabled && expected.Contains(s.path)).Select(s => s.path).ToArray();
            if (!built.SequenceEqual(expected)) throw new InvalidOperationException("Build Settings 的 " + LevelOrder.Scenes.Length + " 关顺序不符。");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string name in LevelOrder.Scenes)
                {
                    var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity", OpenSceneMode.Single);
                    var door = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GoalDoor>(true)).Single();
                    if (door.nextSceneName != LevelOrder.Next(name))
                        throw new InvalidOperationException(name + " 的出口下一关不符：" + door.nextSceneName);
                }
                var menuScene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
                var menu = menuScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuUI>(true)).Single();
                if (menu.levels == null || menu.levels.Count != LevelOrder.Scenes.Length)
                    throw new InvalidOperationException("选关列表不是 " + LevelOrder.Scenes.Length + " 项。");
                for (int i = 0; i < LevelOrder.Scenes.Length; i++)
                    if (menu.levels[i].sceneName != LevelOrder.Scenes[i] || menu.levels[i].displayName != "Level " + (i + 1) || !menu.levels[i].unlocked)
                        throw new InvalidOperationException("选关列表第 " + (i + 1) + " 项不符。");
                return $"{LevelOrder.Scenes.Length} scenes,{LevelOrder.Scenes.Length} door links,{LevelOrder.Scenes.Length} menu entries PASS";
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        private static void Build(string[] kinds)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play，再生成小关卡。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前修改的场景。");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string kind in kinds)
                {
                    if (kind != "Mine" && kind != "Lava" && kind != "Sky")
                        throw new ArgumentException("未知小关卡世界：" + kind);
                    JObject json = ImportWorld(kind);
                    foreach (JObject entry in json["levels"])
                        BuildScene(kind, Parse(entry, kind == "Sky"), (float?)json["backdropMargin"] ?? 6f);
                }
                LevelOrder.RegisterBuildScenes();
                LevelOrder.UpdateLevelSelection();
                AssetDatabase.SaveAssets();
                Debug.Log("[SmallLevelSceneBuilder] 生成完成：" + string.Join(", ", kinds));
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        private static JObject ImportWorld(string kind)
        {
            string folder = kind == "Mine" ? "矿坑" : kind == "Lava" ? "岩浆" : "天空";
            string file = kind.ToLowerInvariant() + "_levels.json";
            EnsureFolder(DataRoot);
            CopyChanged(Source + "/数据/" + file, DataRoot + "/" + file);
            AssetDatabase.ImportAsset(DataRoot + "/" + file, ImportAssetOptions.ForceSynchronousImport);
            var json = JObject.Parse(File.ReadAllText(DataRoot + "/" + file));
            string sourceRoot = Source + "/素材/" + folder;
            string destinationRoot = Art + "/" + kind;
            foreach (string sourceFile in Directory.GetFiles(sourceRoot, "*.png", SearchOption.AllDirectories))
            {
                string relative = sourceFile.Substring(sourceRoot.Length + 1).Replace('\\', '/');
                string path = destinationRoot + "/" + relative;
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                CopyChanged(sourceFile, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                string spriteName = Path.GetFileNameWithoutExtension(path);
                JToken pivotData = json["sprites"]?[spriteName]?["pivot"];
                Vector2 pivot = relative.StartsWith("Door/") ? new Vector2(.5f, .5f)
                    : pivotData != null ? V2(pivotData) : new Vector2(0f, 1f);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 16f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.spritePivot = pivot;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = pivot;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            return json;
        }

        private static SmallLevelData Parse(JObject entry, bool sky)
        {
            var data = new SmallLevelData
            {
                scene = (string)entry["scene"], w = (int)entry["w"], h = (int)entry["h"], sky = sky,
                carve = Rects(entry["carve"]), fill = Rects(entry["fill"]), isles = Rects(entry["isles"]), spikes = Rects(entry["spikes"]),
                door = entry["door"].ToObject<DoorSeed>(),
                rockDelay = (float?)entry["rockDelay"] ?? 1.5f, cornerKeep = (float?)entry["cornerKeep"] ?? .7f,
                dragMul = (float?)entry["dragMul"] ?? 1f,
                track = entry["track"]?.Select(V2).ToArray() ?? Array.Empty<Vector2>(),
                planks = ReadArray<PlankSeed>(entry["planks"]), sees = ReadArray<SeeSeed>(entry["sees"]),
                clouds = ReadArray<PlatformSeed>(entry["clouds"]), plats = ReadArray<PlatformSeed>(entry["plats"]),
                mines = entry["mines"]?.Select(t => new Vector3((float)t[0], (float)t[1], (float)t[2])).ToArray() ?? Array.Empty<Vector3>(),
                vent = entry["vent"]?.ToObject<VentSeed>(), wakes = ReadArray<WakeSeed>(entry["wakes"])
            };
            data.weak = Rects(entry["weak"]);
            data.soft = Rects(entry["soft"]);
            data.gas = Rects(entry["gas"]);
            data.drills = entry["drills"]?.Select(V2).ToArray() ?? Array.Empty<Vector2>();
            data.gasVents = entry["vents"]?.Select(v => new GasVentSeed { x = (int)v[0], y = (int)v[1],
                rate = (float)v[2], max = v.Count() > 3 ? (int)v[3] : int.MaxValue }).ToArray() ?? Array.Empty<GasVentSeed>();
            data.tnts = entry["tnts"]?.Select(t => new TntSeed { x = (float)t[0], y = (float)t[1], direction = (string)t[2],
                delay = t.Count() > 3 ? (float)t[3] : .2f, hasArm = t.Count() > 4, arm = t.Count() > 4 ? V4(t[4]) : Vector4.zero }).ToArray() ?? Array.Empty<TntSeed>();
            if (entry["branch"] is JObject branch)
                data.branch = new RailBranchSeed { at = (int)branch["at"], track = branch["track"].Select(V2).ToArray() };
            data.tntG = (float?)entry["tntG"] ?? 1f;
            data.tntGravitySpecified = entry["tntG"] != null;
            data.tntFuse = (float?)entry["tntFuse"] ?? 0f;
            data.tntHit = (float?)entry["tntHit"] ?? 7f;
            data.blastKill = (float?)entry["blastKill"] ?? 2f;
            data.ventDelay = (float?)entry["ventDelay"] ?? float.MaxValue;
            data.gasTick = (float?)entry["gasTick"] ?? .06f;
            data.gasTol = (float?)entry["gasTol"] ?? .25f;
            data.crystals = entry["crystals"]?.Select(V2).ToArray() ?? Array.Empty<Vector2>();
            data.kegs = entry["kegs"]?.Select(V2).ToArray() ?? Array.Empty<Vector2>();
            data.freezeT = (float?)entry["freezeT"] ?? 4.5f;
            data.kegKill = (float?)entry["kegKill"] ?? 2f;
            data.winds = entry["winds"]?.Select(w => new WindSeed { rect = V4(w), force = new Vector2((float)w[4], (float)w[5]) }).ToArray() ?? Array.Empty<WindSeed>();
            data.seeds = entry["seeds"]?.Select(V2).ToArray() ?? Array.Empty<Vector2>();
            data.portals = entry["portals"]?.Select(p => new PortalSeed {
                a = new Vector3((float)p["a"][0], (float)p["a"][1], (float)p["a"][2]),
                b = new Vector3((float)p["b"][0], (float)p["b"][1], (float)p["b"][2]), r = (float?)p["r"] ?? .75f }).ToArray() ?? Array.Empty<PortalSeed>();
            JToken start = entry["start"];
            data.riding = !sky && ((bool?)start["riding"] ?? false);
            data.start = sky ? V2(start) : data.riding ? data.track[0] + new Vector2(0f, 1.05f) : V2(start["feet"]);
            if (entry["rocks"] != null)
                data.rocks = entry["rocks"].Select(r => new RockSeed { x = (float)r[0], y = (float)r[1], r = (float)r[2], direction = (string)r[3], delay = r.Count() > 4 ? (float?)r[4] ?? data.rockDelay : data.rockDelay, hasArm = r.Count() > 5, arm = r.Count() > 5 ? V4(r[5]) : Vector4.zero }).ToArray();
            if (entry["cave"] is JObject cave)
                data.cave = new CaveSeed
                {
                    route = cave["route"].Select(r => new RouteSeed { rect = V4(r), direction = (string)r[4] }).ToArray(),
                    delay = (float?)cave["delay"] ?? 0f, lag = (float?)cave["lag"] ?? 0f,
                    speed = (float?)cave["speed"] ?? 0f, lagEnd = (float?)cave["lagEnd"] ?? 0f,
                    nearRide = (float?)cave["nearRide"] ?? 0f
                };
            if (entry["bound"] is JObject bound)
                data.bound = new Vector4((float)bound["x0"], (float)bound["y0"], (float)bound["x1"], (float)bound["y1"]);
            if (entry["lavaStart"] is JObject lava)
                data.lavaStart = new LavaSeed
                {
                    n = (int)lava["n"], x = lava["x"].ToObject<float[]>(), y = lava["y"].ToObject<float[]>(),
                    g = lava["g"].ToObject<int[]>(), frozen = Bools(lava["frozen"]), crust = Bools(lava["crust"]), water = Bools(lava["water"])
                };
            return data;
        }

        private static void BuildScene(string kind, SmallLevelData data, float backdropMargin)
        {
            var scene = EditorSceneManager.OpenScene(Template, OpenSceneMode.Single);
            string path = "Assets/Scenes/" + data.scene + ".unity";
            EditorSceneManager.SaveScene(scene, path);
            var roots = scene.GetRootGameObjects();
            var player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Single();
            var world = roots.SelectMany(r => r.GetComponentsInChildren<WorldRotator>(true)).Single();
            var door = roots.SelectMany(r => r.GetComponentsInChildren<GoalDoor>(true)).Single();
            var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
            var intro = roots.SelectMany(r => r.GetComponentsInChildren<LevelIntroUI>(true)).Single();
            player.transform.SetParent(null, true);
            door.transform.SetParent(null, true);
            ClearChildren(world.transform);
            world.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            world.transform.localScale = Vector3.one;
            world.pivot = null;
            world.rotateSpeed = world.maxAngularSpeed = 180f;
            world.maxStepAngle = 3.6f;
            ConfigureBody(world.GetComponent<Rigidbody2D>());
            foreach (var atmosphere in roots.SelectMany(r => r.GetComponentsInChildren<LevelAtmosphere>(true)))
            { atmosphere.enabled = false; ClearChildren(atmosphere.transform); }
            foreach (var candles in roots.SelectMany(r => r.GetComponentsInChildren<CandleSpawner>(true)))
            { candles.enabled = false; ClearChildren(candles.transform); }
            foreach (var light in roots.SelectMany(r => r.GetComponentsInChildren<Light2D>(true)))
                if (light.lightType == Light2D.LightType.Global) { light.color = Color.white; light.intensity = 1f; }

            Material unlit = UnlitMaterial();
            var level = new GameObject("Level").transform;
            level.SetParent(world.transform, false);
            level.localPosition = new Vector3(-data.w * .5f, data.h * .5f, 0f);
            var grid = new GameObject("Grid").AddComponent<Grid>();
            grid.transform.SetParent(level, false);
            var walls = MakeMap(grid.transform, "Walls", 0, unlit);
            var wallBody = walls.gameObject.AddComponent<Rigidbody2D>();
            ConfigureBody(wallBody);
            var wallCollider = walls.gameObject.AddComponent<TilemapCollider2D>();
            wallCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            var composite = walls.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            var bodies = new List<Rigidbody2D> { wallBody };
            Tile collisionTile = CollisionTile();
            var solid = new bool[data.w * data.h];
            if (!data.sky) for (int i = 0; i < solid.Length; i++) solid[i] = true;
            Fill(solid, data, data.carve, false); Fill(solid, data, data.fill, true); Fill(solid, data, data.isles, true);
            Fill(solid, data, data.weak, false); Fill(solid, data, data.soft, false);
            for (int y = 0; y < data.h; y++) for (int x = 0; x < data.w; x++)
                if (solid[y * data.w + x]) walls.SetTile(new Vector3Int(x, -y - 1), collisionTile);
            walls.CompressBounds();
            wallCollider.ProcessTilemapChanges();
            composite.GenerateGeometry();
            walls.GetComponent<TilemapRenderer>().enabled = false;

            string art = Art + "/" + kind;
            MakeImage(level, "LevelArt", art + "/level_" + data.scene + ".png", Vector2.zero, 0, unlit);
            if (data.sky)
                MakeImage(level, "Backdrop", art + "/backdrop_" + data.scene + ".png",
                    new Vector2(data.bound.x - backdropMargin, data.bound.y - backdropMargin), -10, unlit);
            var sprites = AssetDatabase.IsValidFolder(art + "/Sprites")
                ? AssetDatabase.FindAssets("t:Sprite", new[] { art + "/Sprites" })
                    .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Sprite>).OrderBy(s => s.name).ToArray()
                : Array.Empty<Sprite>();
            Tilemap rubbleMap = null; TileBase[] rubbleTiles = Array.Empty<TileBase>();
            if (kind == "Mine")
            {
                rubbleMap = MakeMap(grid.transform, "Rubble", 4, unlit);
                rubbleMap.tileAnchor = new Vector3(0f, 1f, 0f);
                var rubbleBody = rubbleMap.gameObject.AddComponent<Rigidbody2D>();
                ConfigureBody(rubbleBody); bodies.Add(rubbleBody);
                rubbleMap.gameObject.AddComponent<TilemapCollider2D>();
                rubbleTiles = Enumerable.Range(0, 8).Select(i => RubbleTile(sprites.Single(s => s.name == "rubble_" + i), art)).ToArray();
            }
            world.attachedGeometryBodies = bodies.ToArray();

            door.transform.SetParent(level, false);
            MazeSceneBuilder.PlaceDoor(door, new Vector2(data.door.bx, -data.door.by), data.door.dir);
            door.nextSceneName = LevelOrder.Next(data.scene);
            foreach (var renderer in door.GetComponentsInChildren<Renderer>(true)) renderer.sortingOrder = 10;
            string doorArt = art + "/Door/";
            GoalDoorSetup.Apply(door, doorArt);
            GravityLockDoorSetup.Apply(door, doorArt);
            foreach (var renderer in door.GetComponentsInChildren<SpriteRenderer>(true)) renderer.sharedMaterial = unlit;
            foreach (var group in door.GetComponentsInChildren<SortingGroup>(true)) group.sortingOrder = 10;

            player.maxFallSpeed = data.sky ? 11f : 16f;
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var playerBody = player.GetComponent<Rigidbody2D>();
            playerBody.bodyType = RigidbodyType2D.Dynamic;
            playerBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            playerBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            playerBody.constraints |= RigidbodyConstraints2D.FreezeRotation;
            Physics2D.SyncTransforms();
            Bounds playerBounds = player.GetComponent<Collider2D>().bounds;
            Vector3 feet = level.TransformPoint(new Vector3(data.start.x, -data.start.y));
            player.transform.position = new Vector3(feet.x - playerBounds.center.x, feet.y + .02f - playerBounds.min.y, 0f);
            foreach (var renderer in player.GetComponentsInChildren<SpriteRenderer>(true)) renderer.sortingOrder = 30;
            var grade = player.GetComponent<PlayerColorGrade>() ?? player.gameObject.AddComponent<PlayerColorGrade>();
            grade.Configure(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resource/Art/Character/Player/PlayerUnlit.mat") ?? unlit);

            var feedback = new GameObject("NearMissFeedback").AddComponent<NearMissFeedback>();
            feedback.worldKind = kind;
            feedback.cooldown = data.sky ? 1.4f : 1.6f;
            var fit = camera.GetComponent<MazeCameraFit>();
            if (fit != null) Object.DestroyImmediate(fit);
            var follow = camera.GetComponent<FollowTarget2D>(); if (follow != null) follow.enabled = false;
            var zoom = camera.GetComponent<CameraZoomController>(); if (zoom != null) zoom.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = kind == "Mine" ? new Color32(43, 32, 25, 255) : kind == "Lava" ? new Color32(38, 18, 15, 255) : new Color32(238, 242, 249, 255);
            var smallCamera = camera.GetComponent<SmallLevelCamera>() ?? camera.gameObject.AddComponent<SmallLevelCamera>();
            smallCamera.level = level; smallCamera.player = player; smallCamera.sky = data.sky;
            smallCamera.boundsCenter = data.sky ? new Vector2((data.bound.x + data.bound.z) * .5f, (data.bound.y + data.bound.w) * .5f) : new Vector2(data.w * .5f, data.h * .5f);
            smallCamera.boundsSize = data.sky ? new Vector2(data.bound.z - data.bound.x, data.bound.w - data.bound.y) : new Vector2(data.w, data.h);
            smallCamera.RestoreDefault();
            intro.boundsCenter = level.TransformPoint(new Vector3(smallCamera.boundsCenter.x, -smallCamera.boundsCenter.y));
            intro.boundsSize = smallCamera.boundsSize;
            intro.minOrthoSize = 4f;
            intro.maxOrthoSize = Mathf.Max(20f, camera.orthographicSize);

            Type mechanismType = typeof(PlayerController).Assembly.GetType("Resource.Scripts.SmallLevels." + kind + "Mechanisms", true);
            var runtime = (SmallLevelRuntime)level.gameObject.AddComponent(mechanismType);
            runtime.data = data; runtime.world = world; runtime.level = level; runtime.player = player; runtime.feedback = feedback;
            runtime.sprites = sprites; runtime.rubbleMap = rubbleMap; runtime.rubbleTiles = rubbleTiles;
            if (runtime is MineMechanisms mine)
            {
                mine.weakRockTiles = Enumerable.Range(0, 4).Select(i => RubbleTile(sprites.Single(s => s.name == "weak_rock_" + i), art)).ToArray();
                mine.softDirtTiles = Enumerable.Range(0, 4).Select(i => RubbleTile(sprites.Single(s => s.name == "soft_dirt_" + i), art)).ToArray();
                mine.gasTile = RubbleTile(sprites.Single(s => s.name == "gas_cell"), art);
                mine.gasRimTile = RubbleTile(sprites.Single(s => s.name == "gas_rim"), art);
            }
            runtime.BuildVisuals();
            Physics2D.SyncTransforms();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, path);
        }

        private static void ConfigureBody(Rigidbody2D body)
        {
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f; body.useFullKinematicContacts = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
        private static Tilemap MakeMap(Transform parent, string name, int order, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = LayerMask.NameToLayer("Wall");
            var map = go.AddComponent<Tilemap>(); var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = order; renderer.sharedMaterial = material; return map;
        }
        private static void MakeImage(Transform parent, string name, string path, Vector2 point, int order, Material material)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new FileNotFoundException("缺少关卡整图：" + path);
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(point.x, -point.y);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order; renderer.sharedMaterial = material;
        }
        private static Material UnlitMaterial()
        {
            EnsureFolder(Art);
            string path = Art + "/SmallLevelUnlit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")); AssetDatabase.CreateAsset(material, path); }
            return material;
        }
        private static Tile CollisionTile()
        {
            string path = Art + "/Collision.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) { tile = ScriptableObject.CreateInstance<Tile>(); tile.colliderType = Tile.ColliderType.Grid; AssetDatabase.CreateAsset(tile, path); }
            return tile;
        }
        private static Tile RubbleTile(Sprite sprite, string art)
        {
            EnsureFolder(art + "/Tiles"); string path = art + "/Tiles/" + sprite.name + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, path); }
            tile.sprite = sprite; tile.colliderType = Tile.ColliderType.Grid; EditorUtility.SetDirty(tile); return tile;
        }
        private static void Fill(bool[] solid, SmallLevelData data, Vector4[] rects, bool value)
        {
            foreach (Vector4 r in rects) for (int y = Mathf.FloorToInt(r.y); y < r.w; y++) for (int x = Mathf.FloorToInt(r.x); x < r.z; x++)
                if (x >= 0 && y >= 0 && x < data.w && y < data.h) solid[y * data.w + x] = value;
        }
        private static Vector2 V2(JToken t) => new Vector2((float)t[0], (float)t[1]);
        private static Vector4 V4(JToken t) => new Vector4((float)t[0], (float)t[1], (float)t[2], (float)t[3]);
        private static Vector4[] Rects(JToken t) => t?.Select(V4).ToArray() ?? Array.Empty<Vector4>();
        private static T[] ReadArray<T>(JToken t) => t?.ToObject<T[]>() ?? Array.Empty<T>();
        private static bool[] Bools(JToken t) => t?.Select(v => v.Type == JTokenType.Boolean ? (bool)v : (int)v != 0).ToArray() ?? Array.Empty<bool>();
        private static void CopyChanged(string source, string destination)
        {
            if (!File.Exists(destination) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(destination))) File.Copy(source, destination, true);
        }
        private static void ClearChildren(Transform parent)
        {
            while (parent.childCount > 0) Object.DestroyImmediate(parent.GetChild(0).gameObject);
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            AssetDatabase.CreateFolder(path.Substring(0, path.LastIndexOf('/')), Path.GetFileName(path));
        }
    }
}
