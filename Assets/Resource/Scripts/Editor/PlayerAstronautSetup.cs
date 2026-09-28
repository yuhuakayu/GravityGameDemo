using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Editor
{
    public static class PlayerAstronautSetup
    {
        private const string ArtPath = "Assets/Resource/Art/Character/";
        private const string AnimationPath = ArtPath + "Animations/";
        // The approved design keeps the reference detail instead of the initial 24x24 draft.
        private const int FrameWidth = 96;
        private const int FrameHeight = 112;
        private const float IdleVisibleHeight = 91f;
        private const string UndoName = "Install astronaut idle and float";

        [MenuItem("Tools/Gravity Game/Install Astronaut Player in Game Scenes")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before installing the astronaut player.");

            Scene previous = SceneManager.GetActiveScene();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            try
            {
                Sprite[] idleSprites = PrepareSheet(ArtPath + "Player/player_idle.png", 4);
                Sprite[] floatSprites = PrepareSheet(ArtPath + "Player/player_float.png", 6);
                AnimationClip idle = PrepareClip("Astronaut_Idle", idleSprites, 0.2f);
                AnimationClip floating = PrepareClip("Astronaut_Float", floatSprites, 0.12f);
                AnimatorController controller = PrepareController(idle, floating);
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
                    InstallInScene(AssetDatabase.GUIDToAssetPath(guid), idleSprites[0], controller);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Undo.CollapseUndoOperations(undoGroup);
            }
            Debug.Log("[Astronaut] Installed Idle / Float in all player scenes.");
        }

        private static Sprite[] PrepareSheet(string path, int count)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing astronaut texture: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            provider.GetDataProvider<ITextureDataProvider>().GetTextureActualWidthAndHeight(out int width, out int height);
            if (width != count * FrameWidth || height != FrameHeight)
                throw new InvalidOperationException("Unexpected approved animation sheet dimensions: " + path);
            var existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            var rects = new SpriteRect[count];
            for (int i = 0; i < count; i++)
            {
                string name = Path.GetFileNameWithoutExtension(path) + $"_{i:00}";
                rects[i] = new SpriteRect
                {
                    name = name,
                    rect = new Rect(i * FrameWidth, 0, FrameWidth, FrameHeight),
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2(58.5f / FrameWidth, 0f),
                    spriteID = existing.TryGetValue(name, out var id) ? id : GUID.Generate()
                };
            }
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(
                rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            if (sprites.Length != count) throw new InvalidOperationException("Sprite slicing failed: " + path);
            return sprites;
        }

        private static AnimationClip PrepareClip(string name, Sprite[] sprites, float secondsPerFrame)
        {
            string path = AnimationPath + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, path);
            }
            Undo.RecordObject(clip, UndoName);
            clip.ClearCurves();
            clip.frameRate = 1f / secondsPerFrame;
            var keys = new ObjectReferenceKeyframe[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i * secondsPerFrame, value = sprites[i] };
            float duration = sprites.Length * secondsPerFrame;
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = duration;
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimatorController PrepareController(AnimationClip idle, AnimationClip floating)
        {
            string path = AnimationPath + "Astronaut.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            Undo.RegisterCompleteObjectUndo(controller, UndoName);
            controller.parameters = new[] { new AnimatorControllerParameter
                { name = "IsFloating", type = AnimatorControllerParameterType.Bool } };
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Undo.RegisterCompleteObjectUndo(machine, UndoName);
            foreach (var child in machine.states)
                if (child.state.name != "Idle" && child.state.name != "Float") machine.RemoveState(child.state);
            AnimatorState idleState = machine.states.FirstOrDefault(s => s.state.name == "Idle").state
                ?? machine.AddState("Idle", new Vector3(240f, 80f));
            AnimatorState floatState = machine.states.FirstOrDefault(s => s.state.name == "Float").state
                ?? machine.AddState("Float", new Vector3(500f, 80f));
            idleState.motion = idle;
            floatState.motion = floating;
            machine.defaultState = idleState;
            ConfigureTransition(idleState, floatState, AnimatorConditionMode.If);
            ConfigureTransition(floatState, idleState, AnimatorConditionMode.IfNot);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void ConfigureTransition(AnimatorState source, AnimatorState destination, AnimatorConditionMode mode)
        {
            foreach (var transition in source.transitions) source.RemoveTransition(transition);
            var next = source.AddTransition(destination);
            next.hasExitTime = false;
            next.hasFixedDuration = true;
            next.duration = 0f;
            next.offset = 0f;
            next.canTransitionToSelf = false;
            next.AddCondition(mode, 0f, "IsFloating");
        }

        private static void InstallInScene(string path, Sprite idle, AnimatorController controller)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var players = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerController>(true)).ToArray();
                int installed = 0;
                foreach (var player in players)
                {
                    Transform art = player.transform.Find("PlayerIM");
                    if (art == null) continue;
                    var renderer = art.GetComponent<SpriteRenderer>();
                    var capsule = player.GetComponent<CapsuleCollider2D>();
                    var box = player.GetComponent<BoxCollider2D>();
                    if (renderer == null || (capsule == null && box == null))
                        throw new InvalidOperationException("Expected player renderer and capsule/box in " + path);
                    float colliderHeight = capsule != null ? capsule.size.y : box.size.y;
                    Vector2 colliderOffset = capsule != null ? capsule.offset : box.offset;
                    Undo.RegisterFullObjectHierarchyUndo(player.gameObject, UndoName);
                    renderer.sprite = idle;
                    renderer.flipX = false;
                    art.localRotation = Quaternion.identity;
                    art.localScale = Vector3.one * (colliderHeight * 16f / IdleVisibleHeight);
                    art.localPosition = new Vector3(colliderOffset.x,
                        colliderOffset.y - colliderHeight * 0.5f, art.localPosition.z);
                    var animator = art.GetComponent<Animator>();
                    if (animator == null) animator = Undo.AddComponent<Animator>(art.gameObject);
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var movement = player.GetComponent<PlayerMovementAnimation>();
                    if (movement == null) movement = Undo.AddComponent<PlayerMovementAnimation>(player.gameObject);
                    var movementSettings = new SerializedObject(movement);
                    movementSettings.FindProperty("player").objectReferenceValue = player;
                    movementSettings.FindProperty("animator").objectReferenceValue = animator;
                    movementSettings.ApplyModifiedProperties();
                    installed++;
                }
                if (installed == 0) return;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save astronaut player in " + path);
                Debug.Log("[Astronaut] Saved " + path);
            }
            finally
            {
                if (openedHere && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
