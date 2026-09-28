using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.DualShock.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GravityGame.Tests.PlayMode
{
    public sealed class PlayerIdleFloatStage1Tests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest, Timeout(30000)]
        public IEnumerator Stage1_GroundedBreathes_RotationFloats_LandingRestartsIdle_TouchpadHasNoEffect()
        {
            float previousTimeScale = Time.timeScale;
            bool previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            Gamepad previousGamepad = Gamepad.current;
            DualSenseGamepadHID pad = null;
            Component player = null, oxygen = null;
            bool oldAutoMove = false, oldRumble = false;
            float oldDrain = 0f;
            try
            {
                yield return SceneManager.LoadSceneAsync("Stage1", LoadSceneMode.Single);
                yield return null;
                yield return null;
                Scene scene = SceneManager.GetActiveScene();
                player = Find(scene, "Resource.Scripts.PlayerController");
                Component animation = player.GetComponent(FindType("Resource.Scripts.PlayerMovementAnimation"));
                Component world = Find(scene, "Resource.Scripts.WorldRotator");
                Component intro = Find(scene, "Resource.Scripts.LevelIntroUI");
                oxygen = player.GetComponent(FindType("Resource.Scripts.PlayerOxygen"));
                Assert.That(animation, Is.Not.Null);
                Assert.That(oxygen, Is.Not.Null);
#if UNITY_EDITOR
                int missing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                Assert.That(missing, Is.Zero, "Stage1 must not contain missing scripts.");
#endif
                oldAutoMove = Field<bool>(player, "autoMoveMode");
                oldRumble = Field<bool>(player, "rumbleEnabled");
                oldDrain = Property<float>(oxygen, "DrainPerSecond");
                SetField(player, "autoMoveMode", false);
                SetField(player, "rumbleEnabled", false);
                oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, 0f);
                var body = player.GetComponent<Rigidbody2D>();
                var animator = player.GetComponentInChildren<Animator>();
                var renderer = animator.GetComponent<SpriteRenderer>();
                AnimationClip idle = animator.runtimeAnimatorController.animationClips.Single(c => c.name == "Astronaut_Idle");
                AnimationClip floating = animator.runtimeAnimatorController.animationClips.Single(c => c.name == "Astronaut_Float");
                Assert.That(idle.length, Is.EqualTo(.8f).Within(.0001f));
                Assert.That(floating.length, Is.EqualTo(.72f).Within(.0001f));
                Assert.That(idle.isLooping && floating.isLooping, Is.True);
#if UNITY_EDITOR
                var floatBinding = AnimationUtility.GetObjectReferenceCurveBindings(floating).Single();
                Assert.That(AnimationUtility.GetObjectReferenceCurve(floating, floatBinding)
                    .Select(key => key.value).Distinct().Count(), Is.EqualTo(6));
#endif
                Sprite idleFirst = renderer.sprite;
                pad = InputSystem.AddDevice<DualSenseGamepadHID>();
                pad.MakeCurrent();
                QueueTouchpad(pad, false);
                intro.GetType().GetMethod("BeginGameplayFromPreview").Invoke(intro, null);
                float deadline = Time.realtimeSinceStartup + 20f;
                var idleFrames = new HashSet<Sprite>();
                while (idleFrames.Count < 4)
                {
                    AssertAlive(player, deadline, "collect grounded Idle frames");
                    if (Property<bool>(player, "IsGrounded") && !Property<bool>(animation, "IsFloating"))
                        idleFrames.Add(renderer.sprite);
                    yield return null;
                }

                // Send a real button edge through InputSystem while the player is resting on the floor.
                Vector2 velocityBefore = body.linearVelocity;
                Vector2 positionBefore = body.position;
                float oxygenBefore = Property<float>(oxygen, "CurrentOxygen");
                float fixedTimeBefore = Time.fixedTime;
                QueueTouchpad(pad, true);
                float touchpadUntil = Time.time + .2f;
                float maxHorizontalDelta = 0f, maxVerticalDelta = 0f;
                bool sawTouchpad = false;
                while (Time.time < touchpadUntil)
                {
                    AssertAlive(player, deadline, "touchpad input");
                    sawTouchpad |= pad.touchpadButton.isPressed;
                    Vector2 velocityDelta = body.linearVelocity - velocityBefore;
                    maxHorizontalDelta = Mathf.Max(maxHorizontalDelta, Mathf.Abs(velocityDelta.x));
                    maxVerticalDelta = Mathf.Max(maxVerticalDelta, Mathf.Abs(velocityDelta.y));
                    yield return null;
                }
                QueueTouchpad(pad, false);
                yield return null;
                // A render sample can cover several physics steps. Collisions can
                // also remove the initial downward speed while standing still.
                float gravityAllowance = Mathf.Abs(velocityBefore.y) + Mathf.Abs(Physics2D.gravity.y) *
                    Property<float>(player, "SimulatedGravityScale") * (Time.fixedTime - fixedTimeBefore);
                Assert.That(sawTouchpad, Is.True, "The virtual touchpad press must reach InputSystem.");
                Assert.That(Property<float>(oxygen, "CurrentOxygen"), Is.EqualTo(oxygenBefore));
                Assert.That(maxHorizontalDelta, Is.LessThan(.05f), "Touchpad must not inject horizontal dash velocity.");
                Assert.That(maxVerticalDelta, Is.LessThanOrEqualTo(gravityAllowance + .05f),
                    "Touchpad must not inject vertical velocity beyond gravity during the sampled interval.");
                Assert.That(Vector2.Distance(body.position, positionBefore), Is.LessThan(.05f));
                Assert.That(pad.touchpadButton.isPressed, Is.False);

                var floatFrames = new HashSet<Sprite>();
                float startingAngle = Property<float>(world, "ClockwiseAngleReadout");
                float targetAngle = startingAngle + 120f;
                bool sawAirborne = false, sawFloat = false, landed = false;
                float maxAngleChange = 0f;
                while (!landed)
                {
                    AssertAlive(player, deadline, "rotation / Float / landing; Float frames=" + floatFrames.Count);
                    world.GetType().GetMethod("SetTargetAngle").Invoke(world, new object[] { targetAngle });
                    float angle = Property<float>(world, "ClockwiseAngleReadout");
                    maxAngleChange = Mathf.Max(maxAngleChange, Mathf.Abs(angle - startingAngle));
                    bool grounded = Property<bool>(player, "IsGrounded");
                    bool isFloating = Property<bool>(animation, "IsFloating");
                    sawAirborne |= !grounded;
                    if (isFloating && animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Float"))
                    {
                        sawFloat = true;
                        floatFrames.Add(renderer.sprite);
                    }
                    if (sawFloat && grounded && !isFloating)
                    {
                        Assert.That(renderer.sprite, Is.SameAs(idleFirst), "Landing must begin with Idle frame 00.");
                        landed = true;
                    }
                    Assert.That(renderer.flipX, Is.False);
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, renderer.transform.eulerAngles.z)), Is.LessThan(.01f));
                    yield return null;
                }
                Assert.That(sawAirborne && sawFloat && landed, Is.True);
                Assert.That(maxAngleChange, Is.GreaterThan(30f), "The real world body must rotate before the falling check passes.");
                Debug.Log("[IdleFloat Stage1] Idle frames=" + idleFrames.Count + ", Float frames=" + floatFrames.Count +
                    ", actual world rotation=" + maxAngleChange.ToString("F2") +
                    " deg, touchpad max horizontal/vertical velocity delta=" + maxHorizontalDelta.ToString("F4") +
                    "/" + maxVerticalDelta.ToString("F4") + " (gravity allowance=" + gravityAllowance.ToString("F4") + ")" +
                    ", oxygen delta=" + (Property<float>(oxygen, "CurrentOxygen") - oxygenBefore).ToString("F4") +
                    ", landing=Idle frame 00, missing scripts=0.");
            }
            finally
            {
                if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
                if (previousGamepad != null && previousGamepad.added) previousGamepad.MakeCurrent();
                if (player != null)
                {
                    SetField(player, "autoMoveMode", oldAutoMove);
                    SetField(player, "rumbleEnabled", oldRumble);
                }
                if (oxygen != null) oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, oldDrain);
                Time.timeScale = previousTimeScale;
                Application.runInBackground = previousRunInBackground;
            }
        }

        private static void QueueTouchpad(DualSenseGamepadHID pad, bool pressed)
        {
            InputSystem.QueueStateEvent(pad, new DualSenseHIDInputReport
            {
                leftStickX = 128, leftStickY = 128, rightStickX = 128, rightStickY = 128,
                buttons0 = 8, buttons2 = (byte)(pressed ? 2 : 0)
            });
        }

        private static void AssertAlive(Component player, float deadline, string phase)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Stage1 timeout: " + phase);
            Assert.That(player != null && !Property<bool>(player, "IsDead"), Is.True,
                "Player died before finishing Stage1 check: " + phase);
        }

        private static Component Find(Scene scene, string typeName)
        {
            Type type = FindType(typeName);
            Component result = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren(type, true)).Single();
            Assert.That(result, Is.Not.Null, typeName);
            return result;
        }

        private static Type FindType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        private static T Property<T>(Component component, string name) =>
            (T)component.GetType().GetProperty(name, Fields).GetValue(component);
        private static T Field<T>(Component component, string name) =>
            (T)component.GetType().GetField(name, Fields).GetValue(component);
        private static void SetField(Component component, string name, object value) =>
            component.GetType().GetField(name, Fields).SetValue(component, value);
    }
}
