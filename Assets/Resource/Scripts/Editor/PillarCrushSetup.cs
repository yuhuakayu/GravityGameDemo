using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Editor
{
    /// <summary>Explicit, repeatable acceptance scene generation; never runs on asset import.</summary>
    public static class PillarCrushSetup
    {
        public const string ScenePath = "Assets/Scenes/Test_PillarCrush.unity";
        private const string SpritePath = "Assets/Resource/Art/PhysicsTest/White.png";

        [MenuItem("Tools/Gravity Game/Physics/Build Test_PillarCrush Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before building the test scene.");
            if (SceneManager.GetSceneByPath(ScenePath).isLoaded)
                throw new InvalidOperationException("Close Test_PillarCrush before rebuilding it; other scenes remain untouched.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                Sprite sprite = EnsureSprite();
                var cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                cameraGo.transform.position = new Vector3(0f, 1f, -10f);
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 6f;
                camera.backgroundColor = new Color(.025f, .035f, .055f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                cameraGo.AddComponent<AudioListener>();
                var light = new GameObject("Global Light 2D").AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Global;
                light.intensity = 1f;

                var world = new GameObject("WorldRoot");
                var rootBody = world.AddComponent<Rigidbody2D>();
                rootBody.bodyType = RigidbodyType2D.Kinematic;
                rootBody.useFullKinematicContacts = true;
                rootBody.interpolation = RigidbodyInterpolation2D.Interpolate;
                MakeBox("Floor", new Vector2(0f, -2.5f), new Vector2(18f, 1f), new Color(.28f, .34f, .42f), sprite, world.transform);
                MakeBox("Corner Wall", new Vector2(4.65f, .5f), new Vector2(.6f, 5f), new Color(.28f, .34f, .42f), sprite, world.transform);
                var pillar = MakeBox("Pillar - 180 deg per second", new Vector2(0f, 1.4f), new Vector2(8f, .55f), new Color(1f, .55f, .16f), sprite, world.transform);
                var pillarBody = pillar.AddComponent<Rigidbody2D>();
                pillarBody.bodyType = RigidbodyType2D.Kinematic;
                pillarBody.useFullKinematicContacts = true;
                pillarBody.interpolation = RigidbodyInterpolation2D.Interpolate;
                pillar.AddComponent<Rotator>().degreesPerSecond = 180f;

                var player = new GameObject("Player");
                player.layer = LayerMask.NameToLayer("Player");
                player.transform.position = new Vector3(2.8f, -1.54f, 0f);
                var body = player.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.gravityScale = 1f;
                var collider = player.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(.6f, .9f);
                var art = new GameObject("PlayerIM");
                art.transform.SetParent(player.transform, false);
                art.transform.localScale = new Vector3(.6f, .9f, 1f);
                var renderer = art.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = new Color(.3f, .85f, 1f);
                renderer.sortingOrder = 10;
                var controller = player.AddComponent<PlayerController>();
                controller.maxMoveSpeed = 4.5f;
                controller.autoMoveMode = false;
                controller.rumbleEnabled = false;
                controller.groundLayer = 1 << LayerMask.NameToLayer("Wall");
                controller.wallLayer = controller.groundLayer;
                controller.groundCheck = CheckPoint(player.transform, "GroundCheck", new Vector2(0f, -.47f));
                controller.wallCheckLeft = CheckPoint(player.transform, "WallCheckLeft", new Vector2(-.32f, 0f));
                controller.wallCheckRight = CheckPoint(player.transform, "WallCheckRight", new Vector2(.32f, 0f));
                controller.groundCheckRadius = .05f;
                controller.wallCheckRadius = .05f;
                controller.antiPushEnabled = true;
                controller.logVelocityDelta = true;
                var guard = player.GetComponent<CrushGuard>();
                if (guard == null) guard = player.AddComponent<CrushGuard>();
                guard.playerCollider = collider;
                guard.worldGeometryMask = controller.groundLayer;
                guard.drawDepenetrationGizmo = true;
                // No oxygen component: a stationary collision experiment must not end from oxygen drain.
                var intro = new GameObject("Level Preview - Enter to begin").AddComponent<LevelIntroUI>();
                intro.boundsCenter = new Vector2(0f, 1f);
                intro.boundsSize = new Vector2(18f, 12f);
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Cannot save " + ScenePath);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[PillarCrush] Built " + ScenePath + ". Open scene, Play, press Enter. Toggle antiPushEnabled in ~ console for comparison.");
        }

        [MenuItem("Tools/Gravity Game/Physics/Add Test_PillarCrush to Build Settings")]
        public static void RegisterForBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) throw new InvalidOperationException("Build the test scene first.");
            var scenes = EditorBuildSettings.scenes.ToList();
            var existing = scenes.Find(s => s.path == ScenePath);
            if (existing == null) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            else existing.enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }

        private static Transform CheckPoint(Transform parent, string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static GameObject MakeBox(string name, Vector2 position, Vector2 size, Color color, Sprite sprite, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = LayerMask.NameToLayer("Wall");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = size;
            renderer.color = color;
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = size;
            return go;
        }

        private static Sprite EnsureSprite()
        {
            if (!File.Exists(SpritePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SpritePath));
                var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                var pixels = Enumerable.Repeat(Color.white, 64).ToArray();
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(SpritePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(SpritePath);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 8;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }
    }
}
