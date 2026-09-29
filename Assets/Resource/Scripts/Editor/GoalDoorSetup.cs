using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Resource.Scripts.Editor
{
    public static class GoalDoorSetup
    {
        private const string Art = "Assets/Resource/Mode/Cave Tileset/Door/";
        private const string LegacyAtlasGuid = "26d435702dedb784694895af12418eff";

        [MenuItem("Tools/Gravity Game/Replace Exit Doors in Game Scenes")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before replacing exit doors.");

            AnimationClip idle = PrepareClip("door_idle", 6, 7f, true);
            AnimationClip open = PrepareClip("door_open", 4, 12f, false);
            string controllerPath = Art + "Door.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            var idleState = machine.states.FirstOrDefault(s => s.state.name == "Idle").state;
            if (idleState == null) idleState = machine.AddState("Idle");
            var openState = machine.states.FirstOrDefault(s => s.state.name == "Open").state;
            if (openState == null) openState = machine.AddState("Open");
            idleState.motion = idle;
            openState.motion = open;
            machine.defaultState = idleState;
            EditorUtility.SetDirty(controller);
            Sprite first = AssetDatabase.LoadAllAssetsAtPath(Art + "door_idle.png")
                .OfType<Sprite>().OrderBy(s => s.name).First();
            Texture2D legacyAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(LegacyAtlasGuid));
            Scene previous = SceneManager.GetActiveScene();
            int count = 0;
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Scene scene = SceneManager.GetSceneByPath(path);
                    bool openedHere = !scene.isLoaded;
                    if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try
                    {
                        var doors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GoalDoor>(true)).ToArray();
                        if (doors.Length == 0) continue;
                        foreach (GoalDoor door in doors)
                        {
                            InstallDoor(door, first, controller, open, legacyAtlas);
                            count++;
                        }
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save " + path);
                    }
                    finally
                    {
                        if (openedHere && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                    }
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            Debug.Log("[GoalDoor] Replaced " + count + " exits; opening duration=" + open.length.ToString("F3") + "s.");
        }

        private static AnimationClip PrepareClip(string name, int count, float fps, bool loop)
        {
            string sheetPath = Art + name + ".png";
            var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing door texture: " + sheetPath);
            PowerStationTilesetImporter.ApplyBaseSettings(importer, sheetPath);
            importer.SaveAndReimport();
            PowerStationTilesetImporter.SliceAndAnimate(sheetPath, false);
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
                .OfType<Sprite>().OrderBy(s => s.name).ToArray();
            if (sprites.Length != count) throw new InvalidOperationException("Wait for door sprite import: " + name);
            string path = Art + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = fps;
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
                sprites.Select((sprite, index) => new ObjectReferenceKeyframe { time = index / fps, value = sprite }).ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = count / fps;
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void InstallDoor(GoalDoor door, Sprite first, AnimatorController controller, AnimationClip open, Texture2D legacyAtlas)
        {
            Undo.RegisterFullObjectHierarchyUndo(door.gameObject, "Replace exit door");
            Transform visual = door.transform.Find("DoorVisual");
            var map = door.GetComponent<Tilemap>();
            if (visual == null)
            {
                var cells = new List<Vector3Int>();
                foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
                {
                    Sprite sprite = map.GetSprite(cell);
                    if (sprite != null && sprite.texture == legacyAtlas) cells.Add(cell);
                }
                if (cells.Count != 22) throw new InvalidOperationException("Unexpected legacy door tiles on " + door.name);
                Vector3 lower = map.CellToLocal(new Vector3Int(cells.Min(p => p.x), cells.Min(p => p.y), 0));
                Vector3 upper = map.CellToLocal(new Vector3Int(cells.Max(p => p.x) + 1, cells.Max(p => p.y) + 1, 0));
                foreach (var cell in cells) map.SetTile(cell, null);
                map.CompressBounds(); // Background tiles stored on this same map must stay intact.
                var child = new GameObject("DoorVisual");
                Undo.RegisterCreatedObjectUndo(child, "Replace exit door");
                visual = child.transform;
                visual.SetParent(door.transform, false);
                visual.localPosition = new Vector3((lower.x + upper.x) * 0.5f,
                    lower.y + 1f / Mathf.Abs(door.transform.lossyScale.y), 0f);
            }
            Vector3 parentScale = door.transform.lossyScale;
            visual.localScale = new Vector3(1f / Mathf.Abs(parentScale.x), 1f / Mathf.Abs(parentScale.y), 1f);
            visual.localRotation = Quaternion.identity;
            var renderer = visual.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = Undo.AddComponent<SpriteRenderer>(visual.gameObject);
            renderer.sprite = first;
            var oldRenderer = map.GetComponent<TilemapRenderer>();
            renderer.sharedMaterial = oldRenderer.sharedMaterial;
            renderer.sortingLayerID = oldRenderer.sortingLayerID;
            renderer.sortingOrder = oldRenderer.sortingOrder;
            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(visual.gameObject);
            animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var polygon = door.GetComponent<PolygonCollider2D>();
            if (polygon != null) Undo.DestroyObjectImmediate(polygon);
            var trigger = door.GetComponent<BoxCollider2D>();
            if (trigger == null) trigger = Undo.AddComponent<BoxCollider2D>(door.gameObject);
            trigger.isTrigger = true;
            trigger.size = new Vector2(2f * visual.localScale.x, 2f * visual.localScale.y);
            trigger.offset = visual.localPosition;
            var settings = new SerializedObject(door);
            settings.FindProperty("doorAnimator").objectReferenceValue = animator;
            settings.FindProperty("openAnimation").objectReferenceValue = open;
            settings.ApplyModifiedProperties();
        }
    }
}
