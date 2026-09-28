using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Editor
{
    public static class PlayerAstronautSetup
    {
        private const string ArtPath = "Assets/Resource/Art/Character/";
        private const string AnimationPath = ArtPath + "Animations/";
        private const string UndoName = "Install astronaut player";

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
                Sprite idleSprite = PrepareSprite(ArtPath + "Design/astronaut_reference_pose_1x.png");
                var runSprites = new Sprite[8];
                for (int i = 0; i < runSprites.Length; i++)
                    runSprites[i] = PrepareSprite(AnimationPath + $"Run/Frames/astronaut_run_{i:00}.png");

                AnimationClip run = PrepareRun(runSprites);
                AnimationClip idle = PrepareIdle(idleSprite);
                AnimatorController controller = PrepareController(idle, run);
                InstallInScene("Assets/Scenes/Stage1.unity", idleSprite, controller);
                InstallInScene("Assets/Scenes/Stage2.unity", idleSprite, controller);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Undo.CollapseUndoOperations(undoGroup);
            }
            Debug.Log("[Astronaut] Installed and saved Stage1 and Stage2.");
        }

        private static Sprite PrepareSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing astronaut texture: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
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
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(39.5f / 88f, 23f / 121f);
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Sprite import failed: " + path);
            return sprite;
        }

        private static AnimationClip PrepareRun(Sprite[] sprites)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationPath + "Run/Astronaut_Run.anim");
            if (clip == null) throw new FileNotFoundException("Missing Astronaut_Run.anim.");
            if (AnimationUtility.GetCurveBindings(clip).Length != 0 ||
                AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 1)
                throw new InvalidOperationException("Astronaut_Run must animate only SpriteRenderer.m_Sprite.");
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            if (keys == null || keys.Length != sprites.Length)
                throw new InvalidOperationException("Astronaut_Run must contain eight sprite keys.");
            for (int i = 0; i < sprites.Length; i++)
                if (keys[i].value != sprites[i] || Mathf.Abs(keys[i].time - i / 12f) > 0.00001f)
                    throw new InvalidOperationException("Invalid Astronaut_Run sprite or timing at frame " + i);

            Undo.RecordObject(clip, UndoName);
            clip.frameRate = 12f;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = 8f / 12f;
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        private static AnimationClip PrepareIdle(Sprite sprite)
        {
            string path = AnimationPath + "Astronaut_Idle.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = "Astronaut_Idle" };
                AssetDatabase.CreateAsset(clip, path);
            }
            Undo.RecordObject(clip, UndoName);
            clip.frameRate = 12f;
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding,
                new[] { new ObjectReferenceKeyframe { time = 0f, value = sprite } });
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = 1f / 12f;
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        private static AnimatorController PrepareController(AnimationClip idle, AnimationClip run)
        {
            string path = AnimationPath + "Astronaut.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            Undo.RegisterCompleteObjectUndo(controller, UndoName);
            var parameter = controller.parameters.FirstOrDefault(p => p.name == "IsMoving");
            if (parameter == null) controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            else if (parameter.type != AnimatorControllerParameterType.Bool)
                throw new InvalidOperationException("Astronaut controller IsMoving must be a bool.");

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Undo.RegisterCompleteObjectUndo(machine, UndoName);
            AnimatorState idleState = machine.states.FirstOrDefault(s => s.state.name == "Idle").state;
            AnimatorState runState = machine.states.FirstOrDefault(s => s.state.name == "Run").state;
            if (idleState == null) idleState = machine.AddState("Idle", new Vector3(240f, 80f));
            if (runState == null) runState = machine.AddState("Run", new Vector3(500f, 80f));
            Undo.RecordObjects(new UnityEngine.Object[] { idleState, runState }, UndoName);
            idleState.motion = idle;
            runState.motion = run;
            machine.defaultState = idleState;
            ConfigureTransition(idleState, runState, AnimatorConditionMode.If);
            ConfigureTransition(runState, idleState, AnimatorConditionMode.IfNot);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        private static void ConfigureTransition(AnimatorState source, AnimatorState destination, AnimatorConditionMode mode)
        {
            AnimatorStateTransition transition = source.transitions.FirstOrDefault(t => t.destinationState == destination);
            if (transition == null) transition = source.AddTransition(destination);
            Undo.RecordObject(transition, UndoName);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.offset = 0f;
            transition.canTransitionToSelf = false;
            foreach (AnimatorCondition condition in transition.conditions) transition.RemoveCondition(condition);
            transition.AddCondition(mode, 0f, "IsMoving");
        }

        private static void InstallInScene(string path, Sprite idle, AnimatorController controller)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                PlayerController player = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<PlayerController>(true)).Single();
                Transform art = player.transform.Find("PlayerIM");
                var renderer = art != null ? art.GetComponent<SpriteRenderer>() : null;
                var capsule = player.GetComponent<CapsuleCollider2D>();
                if (renderer == null || capsule == null)
                    throw new InvalidOperationException("Expected PlayerIM SpriteRenderer and player capsule in " + path);

                Undo.RegisterFullObjectHierarchyUndo(player.gameObject, UndoName);
                renderer.sprite = idle;
                renderer.flipX = false;
                art.localScale = Vector3.one * (76f / 74f);
                art.localPosition = new Vector3(capsule.offset.x,
                    capsule.offset.y - capsule.size.y * 0.5f, art.localPosition.z);
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
                var playerSettings = new SerializedObject(player);
                playerSettings.FindProperty("spriteFacesRight").boolValue = true;
                playerSettings.ApplyModifiedProperties();

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Cannot save astronaut player in " + path);
                Debug.Log("[Astronaut] Saved " + path);
            }
            finally
            {
                // Keep unsaved work open, including partial changes if installation fails.
                if (openedHere && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
