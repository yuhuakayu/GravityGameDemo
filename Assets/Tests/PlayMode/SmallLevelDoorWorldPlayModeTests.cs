using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GravityGame.Tests.PlayMode
{
    public sealed class SmallLevelDoorWorldPlayModeTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest, Timeout(25000)]
        public IEnumerator MineDoor_UsesWorldArt_LockContactAndOpeningWork() => VerifyWorld("Mine");

        [UnityTest, Timeout(25000)]
        public IEnumerator LavaDoor_UsesWorldArt_LockContactAndOpeningWork() => VerifyWorld("Lava");

        [UnityTest, Timeout(25000)]
        public IEnumerator SkyDoor_UsesWorldArt_LockContactAndOpeningWork() => VerifyWorld("Sky");

        private static IEnumerator VerifyWorld(string kind)
        {
            float previousTimeScale = Time.timeScale;
            bool previousRunInBackground = Application.runInBackground;
            Component player = null;
            Application.runInBackground = true;
            try
            {
                string sceneName = kind + "_01";
                string artFolder = "Assets/Resource/Art/SmallLevels/" + kind + "/Door/";
                yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                yield return null;
                yield return null;
                float deadline = Time.realtimeSinceStartup + 15f;
                Scene scene = SceneManager.GetActiveScene();
                player = Find(scene, "Resource.Scripts.PlayerController");
                Component door = Find(scene, "Resource.Scripts.GoalDoor");
                Component world = Find(scene, "Resource.Scripts.WorldRotator");
                Component intro = Find(scene, "Resource.Scripts.LevelIntroUI");
                foreach (var mechanism in scene.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren(TypeOf("Resource.Scripts.SmallLevels.SmallLevelRuntime"), true)))
                    ((Behaviour)mechanism).enabled = false;

                Assert.That(Field<string>(door, "nextSceneName"), Is.EqualTo(kind + "_02"));
                Assert.That(Field<bool>(door, "gravityLock"), Is.True);
                Assert.That(Field<float>(door, "unlockAngle"), Is.EqualTo(20f));
                Assert.That(Field<float>(door, "relockMargin"), Is.EqualTo(3f));
                var animator = Field<Animator>(door, "doorAnimator");
                var open = Field<AnimationClip>(door, "openAnimation");
                var idle = animator.runtimeAnimatorController.animationClips.Single(clip => clip.name == "door_idle");
                var renderer = animator.GetComponent<SpriteRenderer>();
                var overlay = Field<SpriteRenderer>(door, "lockOverlay");
                Sprite openOverlay = Field<Sprite>(door, "overlayOpen");
                Sprite lockedOverlay = Field<Sprite>(door, "overlayLocked");
                Sprite hintOverlay = Field<Sprite>(door, "overlayHint");
                Assert.That(idle.frameRate, Is.EqualTo(7f));
                Assert.That(idle.isLooping, Is.True);
                Assert.That(open.frameRate, Is.EqualTo(12f));
                Assert.That(open.isLooping, Is.False);
                Assert.That(open.length, Is.EqualTo(1f / 3f).Within(.0001f));
#if UNITY_EDITOR
                Assert.That(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController), Is.EqualTo(artFolder + "Door.controller"));
                Assert.That(AssetDatabase.GetAssetPath(idle), Is.EqualTo(artFolder + "door_idle.anim"));
                Assert.That(AssetDatabase.GetAssetPath(open), Is.EqualTo(artFolder + "door_open.anim"));
                Sprite[] expectedIdleFrames = Frames(idle);
                Sprite[] expectedOpenFrames = Frames(open);
                Assert.That(expectedIdleFrames.Length, Is.EqualTo(6));
                Assert.That(expectedOpenFrames.Length, Is.EqualTo(4));
                Assert.That(expectedIdleFrames.All(sprite => AssetDatabase.GetAssetPath(sprite) == artFolder + "door_idle.png"), Is.True);
                Assert.That(expectedOpenFrames.All(sprite => AssetDatabase.GetAssetPath(sprite) == artFolder + "door_open.png"), Is.True);
                Assert.That(AssetDatabase.GetAssetPath(openOverlay), Is.EqualTo(artFolder + "door_lock_open.png"));
                Assert.That(AssetDatabase.GetAssetPath(lockedOverlay), Is.EqualTo(artFolder + "door_lock_locked.png"));
                Assert.That(AssetDatabase.GetAssetPath(hintOverlay), Is.EqualTo(artFolder + "door_lock_hint.png"));
                var needle = Field<Transform>(door, "needle").GetComponent<SpriteRenderer>();
                Assert.That(AssetDatabase.GetAssetPath(needle.sprite),
                    Is.EqualTo("Assets/Resource/Mode/Cave Tileset/Door/door_lock_needle.png"));
#endif
                intro.GetType().GetMethod("BeginGameplayFromPreview").Invoke(intro, null);
                ((Behaviour)world).enabled = false;
                player.GetType().GetMethod("SetRiding").Invoke(player, new object[] { true });
                var playerBody = player.GetComponent<Rigidbody2D>();
                playerBody.useFullKinematicContacts = true;
                var playerCollider = player.GetComponent<Collider2D>();
                var doorCollider = door.GetComponent<Collider2D>();
                var worldBody = world.GetComponent<Rigidbody2D>();
                worldBody.interpolation = RigidbodyInterpolation2D.None;
                float previewAngle = worldBody.rotation + Mathf.DeltaAngle(door.transform.eulerAngles.z, 0f);
                worldBody.rotation = previewAngle;
                world.transform.rotation = Quaternion.Euler(0f, 0f, previewAngle);
                Physics2D.SyncTransforms();
                Assert.That(doorCollider.isTrigger, Is.True);
                SetDoorAngle(door, 0f);
                MovePlayer(playerBody, playerCollider, (Vector2)doorCollider.bounds.center + Vector2.up * 6f);
                FrameDoorCamera(doorCollider.bounds.center);
                yield return null;
                Assert.That(Property<bool>(door, "IsUnlocked"), Is.True);
                Assert.That(overlay.sprite, Is.EqualTo(openOverlay));

                var idleFrames = new HashSet<Sprite>();
                while (idleFrames.Count < 6)
                {
                    BeforeDeadline(deadline, "six idle frames");
                    Assert.That(Property<bool>(door, "IsOpening"), Is.False);
                    idleFrames.Add(renderer.sprite);
                    yield return null;
                }
#if UNITY_EDITOR
                CollectionAssert.AreEquivalent(expectedIdleFrames, idleFrames);
#endif
                string previewDirectory = Path.Combine(Path.GetTempPath(), "GravityDoorWorldPreview");
                Directory.CreateDirectory(previewDirectory);
                string previewPath = Path.Combine(previewDirectory, sceneName + ".png");
                ScreenCapture.CaptureScreenshot(previewPath);
                yield return null;
                yield return null;

                SetDoorAngle(door, 19.9f);
                yield return null;
                Assert.That(Property<bool>(door, "IsUnlocked"), Is.True, "At 20 degrees or less the door unlocks.");
                SetDoorAngle(door, 22.9f);
                yield return null;
                Assert.That(Property<bool>(door, "IsUnlocked"), Is.True, "An unlocked door stays unlocked below 23 degrees.");
                SetDoorAngle(door, 23.1f);
                yield return null;
                Assert.That(Property<bool>(door, "IsUnlocked"), Is.False, "The door relocks above 23 degrees.");
                SetDoorAngle(door, 20.1f);
                yield return null;
                Assert.That(Property<bool>(door, "IsUnlocked"), Is.False, "A locked door stays locked above 20 degrees.");
                SetDoorAngle(door, 19.9f);
                yield return null;
                Assert.That(Property<bool>(door, "IsUnlocked"), Is.True);

                SetDoorAngle(door, 30f);
                yield return null;
                Assert.That(Property<bool>(door, "IsUnlocked"), Is.False);
                Assert.That(overlay.sprite, Is.EqualTo(lockedOverlay));
                MovePlayer(playerBody, playerCollider, doorCollider.bounds.center);
                bool sawHint = false;
                float hintDeadline = Time.realtimeSinceStartup + .8f;
                while (Time.realtimeSinceStartup < hintDeadline)
                {
                    BeforeDeadline(deadline, "locked physical contact");
                    Assert.That(Property<bool>(door, "IsOpening"), Is.False);
                    sawHint |= overlay.sprite == hintOverlay;
                    yield return null;
                }
                Assert.That(Field<int>(door, "_playerColliders"), Is.GreaterThan(0), "Physics must deliver the real trigger contact.");
                Assert.That(sawHint, Is.True, "Touching a tilted, locked door must flash its red hint sprite.");

                SetDoorAngle(door, 0f);
                MovePlayer(playerBody, playerCollider, doorCollider.bounds.center);
                var openFrames = new List<Sprite>();
                while (SceneManager.GetActiveScene().name == sceneName)
                {
                    BeforeDeadline(deadline, "four opening frames and next scene");
                    if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("Open"))
                    {
                        Assert.That(overlay.sprite, Is.EqualTo(openOverlay));
                        Sprite sprite = renderer.sprite;
                        if (openFrames.Count == 0 || openFrames[openFrames.Count - 1] != sprite)
                            openFrames.Add(sprite);
                    }
                    yield return null;
                }
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(kind + "_02"));
                Assert.That(openFrames.Count, Is.EqualTo(4));
#if UNITY_EDITOR
                CollectionAssert.AreEqual(expectedOpenFrames, openFrames);
#endif
                Component transition = FindTransition();
                while (Property<bool>(transition, "IsTransitioning"))
                {
                    BeforeDeadline(deadline, "complete next-scene transition");
                    yield return null;
                }
                Assert.That(File.Exists(previewPath), Is.True, "The door preview must finish saving.");
                Debug.Log("[SmallLevelDoor " + kind + "] World art, 6 idle frames, 4 open frames, 20/23 degree lock, real red-hint contact and "
                    + kind + "_02 transition PASS. Preview: " + previewPath);
            }
            finally
            {
                if (player != null) player.GetType().GetMethod("SetRiding").Invoke(player, new object[] { false });
                Time.timeScale = previousTimeScale;
                Application.runInBackground = previousRunInBackground;
            }
        }

        private static void SetDoorAngle(Component door, float angle)
        {
            door.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            Physics2D.SyncTransforms();
        }

        private static void MovePlayer(Rigidbody2D body, Collider2D collider, Vector2 center)
        {
            body.position += center - (Vector2)collider.bounds.center;
            Physics2D.SyncTransforms();
        }

        private static void FrameDoorCamera(Vector3 center)
        {
            Camera camera = Camera.main;
            foreach (var component in camera.GetComponents<Behaviour>())
                if (component.GetType().Name == "SmallLevelCamera" || component.GetType().Name == "FollowTarget2D"
                    || component.GetType().Name == "CameraZoomController" || component.GetType().Name == "MazeCameraFit")
                    component.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(center.x, center.y, -10f), Quaternion.identity);
            camera.orthographicSize = 2.5f;
        }

#if UNITY_EDITOR
        private static Sprite[] Frames(AnimationClip clip) => AnimationUtility.GetObjectReferenceCurve(clip,
            AnimationUtility.GetObjectReferenceCurveBindings(clip).Single()).Select(key => (Sprite)key.value).ToArray();
#endif

        private static Component FindTransition() => UnityEngine.Object.FindFirstObjectByType(TypeOf("Resource.Scripts.SceneTransition")) as Component;

        private static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static Component Find(Scene scene, string name) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren(TypeOf(name), true)).Single();

        private static T Field<T>(Component component, string name) =>
            (T)component.GetType().GetField(name, Fields).GetValue(component);

        private static T Property<T>(Component component, string name) =>
            (T)component.GetType().GetProperty(name).GetValue(component);

        private static void BeforeDeadline(float deadline, string phase) =>
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Small-level door timeout: " + phase);
    }
}
