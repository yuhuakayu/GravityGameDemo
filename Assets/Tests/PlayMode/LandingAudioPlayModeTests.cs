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
    public sealed class LandingAudioPlayModeTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest, Timeout(15000)]
        public IEnumerator Maze01_StandingIsSilent_EachRealLandingPlaysOnce()
        {
            float oldTime = Time.timeScale;
            bool oldBackground = Application.runInBackground;
            Component sfx = null;
            bool oldEnabled = false;
            Application.runInBackground = true;
            try
            {
                yield return SceneManager.LoadSceneAsync("Maze_01", LoadSceneMode.Single);
                yield return null;
                yield return null;
                Component player = Find("PlayerController");
                ((Behaviour)Find("WorldRotator")).enabled = false;
                Set(player, "autoMoveMode", false);
                Set(player, "maxMoveSpeed", 0f);
                Set(player, "jumpEnabled", false);
                Call(Find("LevelIntroUI"), "BeginGameplayFromPreview");
                sfx = (Component)TypeOf("SfxManager").GetProperty("Instance").GetValue(null);
                oldEnabled = Get<bool>(sfx, "sfxEnabled");
                Set(sfx, "sfxEnabled", true);
                var body = player.GetComponent<Rigidbody2D>();
                var wait = new WaitForFixedUpdate();

                // Use the real tilemap, controller, collision callbacks and CrushGuard.
                for (int i = 0; i < 25; i++) yield return wait;
                Assert.That(Property<bool>(player, "IsGrounded"), Is.True);
                Assert.That(Get<bool>(player, "antiPushEnabled"), Is.True);
                int previous = Get<int>(sfx, "_poolCursor"), standingSounds = 0;
                for (int i = 0; i < 150; i++)
                {
                    yield return wait;
                    standingSounds += CountSounds(sfx, ref previous);
                }
                Assert.That(standingSounds, Is.Zero, "Resting contact separation must not replay landing audio.");

                for (int drop = 0; drop < 2; drop++)
                {
                    body.position += Vector2.up * 2f;
                    Call(player, "SetIntendedVelocity", Vector2.zero);
                    Physics2D.SyncTransforms();
                    bool leftGround = false;
                    int landingSounds = 0;
                    for (int i = 0; i < 100; i++)
                    {
                        yield return wait;
                        leftGround |= !Property<bool>(player, "IsGrounded");
                        landingSounds += CountSounds(sfx, ref previous);
                    }
                    Assert.That(leftGround, Is.True, "The regression check must include a real fall.");
                    Assert.That(Property<bool>(player, "IsGrounded"), Is.True);
                    Assert.That(landingSounds, Is.EqualTo(1), "Each physical fall/landing must play exactly once.");
                }
                Debug.Log("[LandingAudio] stationary 150 physics steps: 0 sounds; two real drops: 1 sound each.");
            }
            finally
            {
                if (sfx != null) Set(sfx, "sfxEnabled", oldEnabled);
                Time.timeScale = oldTime;
                Application.runInBackground = oldBackground;
            }
        }

        private static int CountSounds(Component sfx, ref int previous)
        {
            int now = Get<int>(sfx, "_poolCursor");
            int count = (now - previous + Get<AudioSource[]>(sfx, "_oneShotPool").Length) % Get<AudioSource[]>(sfx, "_oneShotPool").Length;
            previous = now;
            return count;
        }

        private static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Resource.Scripts." + name)).First(t => t != null);
        private static Component Find(string name) => (Component)UnityEngine.Object.FindFirstObjectByType(TypeOf(name));
        private static T Get<T>(Component c, string name) => (T)c.GetType().GetField(name, Fields).GetValue(c);
        private static T Property<T>(Component c, string name) => (T)c.GetType().GetProperty(name).GetValue(c);
        private static void Set(Component c, string name, object value) => c.GetType().GetField(name, Fields).SetValue(c, value);
        private static void Call(Component c, string name, params object[] args) => c.GetType().GetMethod(name).Invoke(c, args);
    }
}
