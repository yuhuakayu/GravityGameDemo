using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class GoalDoorPlayModeTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest, Timeout(30000)]
        public IEnumerator Stage2_IdleGlows_ContactOpensOnce_ThenLoadsOriginalDestination()
        {
            float previousTimeScale = Time.timeScale;
            bool previousRunInBackground = Application.runInBackground;
            Component player = null, oxygen = null;
            bool previousAutoMove = false;
            float previousDrain = 0f;
            Application.runInBackground = true;
            try
            {
                yield return SceneManager.LoadSceneAsync("Stage2", LoadSceneMode.Single);
                yield return null;
                yield return null;
                Scene scene = SceneManager.GetActiveScene();
                player = Find(scene, "Resource.Scripts.PlayerController");
                oxygen = Find(scene, "Resource.Scripts.PlayerOxygen");
                Component door = Find(scene, "Resource.Scripts.GoalDoor");
                Component intro = Find(scene, "Resource.Scripts.LevelIntroUI");
                previousAutoMove = Field<bool>(player, "autoMoveMode");
                previousDrain = (float)oxygen.GetType().GetProperty("DrainPerSecond").GetValue(oxygen);
                SetField(player, "autoMoveMode", false);
                oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, 0f);

                Assert.That(Field<string>(door, "nextSceneName"), Is.EqualTo("MainMenu"));
                var animator = Field<Animator>(door, "doorAnimator");
                var open = Field<AnimationClip>(door, "openAnimation");
                Assert.That(animator, Is.Not.Null);
                Assert.That(open, Is.Not.Null);
                var renderer = animator.GetComponent<SpriteRenderer>();
                var idle = animator.runtimeAnimatorController.animationClips.Single(c => c.name == "door_idle");
                Assert.That(idle.isLooping, Is.True);
                Assert.That(open.isLooping, Is.False);
                float openDuration = open.length;
                Assert.That(openDuration, Is.EqualTo(1f / 3f).Within(.0001f));
#if UNITY_EDITOR
                var openBinding = AnimationUtility.GetObjectReferenceCurveBindings(open).Single();
                Sprite[] expectedOpenFrames = AnimationUtility.GetObjectReferenceCurve(open, openBinding)
                    .Select(key => (Sprite)key.value).Distinct().ToArray();
                Assert.That(expectedOpenFrames.Length, Is.EqualTo(4));
#endif
                intro.GetType().GetMethod("BeginGameplayFromPreview").Invoke(intro, null);
                float deadline = Time.realtimeSinceStartup + 12f;
                var idleFrames = new HashSet<Sprite>();
                while (idleFrames.Count < 6)
                {
                    AssertBeforeDeadline(deadline, "idle animation");
                    Assert.That(Field<bool>(door, "_triggered"), Is.False);
                    idleFrames.Add(renderer.sprite);
                    yield return null;
                }

                // Only the test moves the body into the exit; the real trigger callback runs in physics.
                var body = player.GetComponent<Rigidbody2D>();
                var playerCollider = player.GetComponent<Collider2D>();
                var doorCollider = door.GetComponent<Collider2D>();
                Assert.That(doorCollider.isTrigger, Is.True);
                body.position += (Vector2)(doorCollider.bounds.center - playerCollider.bounds.center);
                Physics2D.SyncTransforms();
                while (!Field<bool>(door, "_triggered"))
                {
                    AssertBeforeDeadline(deadline, "physical door contact");
                    yield return null;
                }

                float contactTime = Time.realtimeSinceStartup;
                var openFrames = new List<Sprite>();
                float completedAt = -1f;
                while (SceneManager.GetActiveScene().name == "Stage2")
                {
                    AssertBeforeDeadline(deadline, "open animation and original transition");
                    if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("Open"))
                    {
                        Sprite sprite = renderer.sprite;
                        if (openFrames.Count == 0 || openFrames[openFrames.Count - 1] != sprite)
                            openFrames.Add(sprite);
                        if (completedAt < 0f && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f)
                            completedAt = Time.realtimeSinceStartup - contactTime;
                    }
                    yield return null;
                }

                float transitionElapsed = Time.realtimeSinceStartup - contactTime;
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MainMenu"));
                Assert.That(completedAt, Is.GreaterThanOrEqualTo(openDuration - .04f));
                Assert.That(transitionElapsed, Is.GreaterThanOrEqualTo(openDuration + .6f - .04f),
                    "Opening must finish before the existing completion delay and scene transition.");
                Assert.That(openFrames.Count, Is.EqualTo(4), "Open must show all four frames once, then hold its last frame.");
#if UNITY_EDITOR
                CollectionAssert.AreEqual(expectedOpenFrames, openFrames);
#endif
                Debug.Log("[GoalDoor Stage2] Idle frames=" + idleFrames.Count + ", Open frames=" + openFrames.Count +
                    ", Open duration=" + openDuration.ToString("F4") + "s, completed after=" + completedAt.ToString("F3") +
                    "s, original destination=MainMenu after=" + transitionElapsed.ToString("F3") + "s.");
            }
            finally
            {
                if (player != null) SetField(player, "autoMoveMode", previousAutoMove);
                if (oxygen != null) oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, previousDrain);
                Time.timeScale = previousTimeScale;
                Application.runInBackground = previousRunInBackground;
            }
        }

        private static void AssertBeforeDeadline(float deadline, string phase) =>
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "GoalDoor timeout: " + phase);

        private static Component Find(Scene scene, string typeName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, typeName);
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(type, true)).Single();
        }

        private static T Field<T>(Component component, string name) =>
            (T)component.GetType().GetField(name, Fields).GetValue(component);
        private static void SetField(Component component, string name, object value) =>
            component.GetType().GetField(name, Fields).SetValue(component, value);
    }
}
