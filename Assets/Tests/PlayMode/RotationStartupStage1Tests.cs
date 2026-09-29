using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GravityGame.Tests.PlayMode
{
    public sealed class RotationStartupStage1Tests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest, Timeout(20000)]
        public IEnumerator StartupOpensSilently_Stage1RotatesBeyondThreeTurns()
        {
            float oldTimeScale = Time.timeScale;
            bool oldRunInBackground = Application.runInBackground;
            Type transitionType = FindType("SceneTransition");
            FieldInfo startup = transitionType.GetField("_startupOpeningPlayed", Fields);
            FieldInfo entered = FindType("GameFlowState").GetField("HasEnteredGame", Fields);
            bool oldStartup = (bool)startup.GetValue(null), oldEntered = (bool)entered.GetValue(null);
            Component sfx = (Component)FindType("SfxManager").GetProperty("Instance").GetValue(null);
            bool oldSfxEnabled = Field<bool>(sfx, "sfxEnabled");
            Component player = null, oxygen = null, world = null;
            Rigidbody2D playerBody = null;
            bool oldAutoMove = false, oldSimulated = false;
            float oldDrain = 0f, deadline = Time.realtimeSinceStartup + 18f;
            Application.runInBackground = true;
            try
            {
                foreach (Component existing in UnityEngine.Object.FindObjectsByType(transitionType, FindObjectsSortMode.None))
                    UnityEngine.Object.Destroy(existing.gameObject);
                yield return null;
                startup.SetValue(null, false);
                entered.SetValue(null, false);
                SetField(sfx, "sfxEnabled", true);
                AudioSource[] pool = Field<AudioSource[]>(sfx, "_oneShotPool");
                foreach (AudioSource source in pool) source.Stop();
                yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
                Component menu = Find("MainMenuUI");
                Component transition = (Component)transitionType.GetProperty("Instance").GetValue(null);
                RectTransform iris = Field<RectTransform>(transition, "_iris");
                bool sawOpening = false, sawCover = false;
                float previousScale = float.PositiveInfinity;
                while (!sawOpening || Property<bool>(transition, "IsTransitioning"))
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Startup opening timed out.");
                    if (Property<bool>(transition, "IsTransitioning"))
                    {
                        sawOpening = true;
                        sawCover |= iris.localScale.x > 0f;
                        Assert.That(iris.localScale.x, Is.LessThanOrEqualTo(previousScale + .001f));
                        previousScale = iris.localScale.x;
                        CanvasGroup group = Field<CanvasGroup>(menu, "_menuGroup");
                        Assert.That(group.interactable || group.blocksRaycasts, Is.False, "Opening must block menu input.");
                        Assert.That(pool.Any(source => source.isPlaying), Is.False, "Startup must not play the transition sound.");
                    }
                    yield return null;
                }
                Assert.That(sawCover, Is.True, "Startup must show the iris before opening.");
                Assert.That(iris.localScale.x, Is.Zero);
                entered.SetValue(null, true);
                yield return SceneManager.LoadSceneAsync("Stage1", LoadSceneMode.Single);
                yield return null;
                player = Find("PlayerController");
                oxygen = Find("PlayerOxygen");
                world = Find("WorldRotator");
                Component intro = Find("LevelIntroUI");
                intro.GetType().GetMethod("BeginGameplayFromPreview").Invoke(intro, null);
                yield return null;
                yield return null;
                oldAutoMove = Field<bool>(player, "autoMoveMode");
                oldDrain = Property<float>(oxygen, "DrainPerSecond");
                playerBody = player.GetComponent<Rigidbody2D>();
                oldSimulated = playerBody.simulated;
                SetField(player, "autoMoveMode", false);
                oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, 0f);
                playerBody.simulated = false;
                float start = Property<float>(world, "ClockwiseAngleReadout"), previous = start;
                MethodInfo setTarget = world.GetType().GetMethod("SetTargetAngle");
                do
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Stage1 did not complete three turns in time.");
                    Assert.That(Property<bool>(player, "IsDead"), Is.False);
                    Assert.That(Field<bool>(world, "limitSteeringRange"), Is.False);
                    setTarget.Invoke(world, new object[] { start + 1110f });
                    yield return null;
                    float angle = Property<float>(world, "ClockwiseAngleReadout");
                    Assert.That(angle, Is.GreaterThanOrEqualTo(previous - .001f), "Continuous rotation must not reverse.");
                    previous = angle;
                } while (previous - start < 1109.9f);
                Debug.Log("[StartupRotation] silent opening and menu lock passed; Stage1 actual clockwise travel=" +
                    (previous - start).ToString("F2") + " deg, no reversal.");
            }
            finally
            {
                if (world != null) world.GetType().GetMethod("DiscardPendingRotation").Invoke(world, null);
                if (playerBody != null) { playerBody.simulated = oldSimulated; SetField(player, "autoMoveMode", oldAutoMove); }
                if (playerBody != null && oxygen != null) oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, oldDrain);
                if (sfx != null) SetField(sfx, "sfxEnabled", oldSfxEnabled);
                startup.SetValue(null, oldStartup);
                entered.SetValue(null, oldEntered);
                Time.timeScale = oldTimeScale;
                Application.runInBackground = oldRunInBackground;
            }
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Resource.Scripts." + name)).First(type => type != null);
        private static Component Find(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren(FindType(name), true)).Single();
        private static T Field<T>(Component component, string name) => (T)component.GetType().GetField(name, Fields).GetValue(component);
        private static T Property<T>(Component component, string name) => (T)component.GetType().GetProperty(name, Fields).GetValue(component);
        private static void SetField(Component component, string name, object value) => component.GetType().GetField(name, Fields).SetValue(component, value);
    }
}
