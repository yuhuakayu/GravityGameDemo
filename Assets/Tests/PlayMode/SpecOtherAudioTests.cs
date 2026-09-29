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
    public sealed class SpecOtherAudioTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest, Timeout(20000)]
        public IEnumerator Stage1_OtherSounds_And_Maze03_LaserHum()
        {
            float oldTime = Time.timeScale;
            bool oldBackground = Application.runInBackground;
            Component sfx = null, door = null, player = null;
            bool oldEnabled = false;
            float oldVolume = 1f;
            Application.runInBackground = true;
            try
            {
                yield return SceneManager.LoadSceneAsync("Stage1", LoadSceneMode.Single);
                yield return null;
                yield return null;
                sfx = (Component)TypeOf("SfxManager").GetProperty("Instance").GetValue(null);
                oldEnabled = Field<bool>(sfx, "sfxEnabled");
                oldVolume = Field<float>(sfx, "masterVolume");
                Set(sfx, "sfxEnabled", true);
                Set(sfx, "masterVolume", 1f);
                string[,] clips = {
                    { "_landClip", "sfx_land" }, { "_heavyLandClip", "sfx_land_heavy" },
                    { "_wallBumpClip", "sfx_wall_bump" }, { "_playerDeathClip", "sfx_death_suffocate" },
                    { "_oxygenPickupClip", "sfx_oxygen_pickup" }, { "_doorOpenClip", "sfx_door_open" },
                    { "_uiMoveClip", "sfx_ui_move" }, { "_buttonClickClip", "sfx_ui_confirm" },
                    { "_uiBackClip", "sfx_ui_back" }, { "_uiErrorClip", "sfx_ui_error" },
                    { "_stageCompleteClip", "sfx_level_clear" }, { "ambientCaveClip", "sfx_ambient_cave_loop" },
                    { "fallWindClip", "sfx_fall_wind_loop" }, { "doorHumClip", "sfx_door_hum_loop" },
                    { "laserHumClip", "sfx_laser_hum_loop" }
                };
                for (int i = 0; i < clips.GetLength(0); i++)
                    Assert.That(Field<AudioClip>(sfx, clips[i, 0])?.name, Is.EqualTo(clips[i, 1]), clips[i, 0]);

                Component loops = sfx.GetComponent(TypeOf("GameplayLoopAudio"));
                Assert.That(Field<AudioSource>(loops, "_ambient").isPlaying, Is.False, "Preview is silent.");
                player = StartFrozenGameplay();
                yield return null;
                yield return null;
                AudioSource ambient = Field<AudioSource>(loops, "_ambient");
                Assert.That(ambient.isPlaying && ambient.volume > 0f, Is.True);
                Set(player, "isGrounded", false);
                player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.down * 15f;
                yield return null;
                yield return null;
                AudioSource fall = Field<AudioSource>(loops, "_fall");
                Assert.That(fall.isPlaying && fall.volume > 0f, Is.True, "Runtime falling-speed loop.");
                Debug.Log("[OtherSfx loops] ambient/fall output peaks=" + Peak(ambient) + "/" + Peak(fall));
                player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;

                // These are sound API checks, not simulated physical landing/wall collisions.
                yield return Observe(sfx, "ordinary landing API", () => Call(sfx, "PlayLand", 3f, .3f));
                yield return Observe(sfx, "heavy landing API", () => Call(sfx, "PlayLand", Field<float>(sfx, "heavyLandSpeed") + 1f, 1f));
                yield return Observe(sfx, "wall API", () => Call(sfx, "PlayWallBump"));
                yield return Observe(sfx, "UI move API", () => Call(sfx, "PlayUIMove"));
                yield return Observe(sfx, "UI confirm API", () => Call(sfx, "PlayButtonClick"));

                Component oxygen = Find("PlayerOxygen"), tank = Find("OxygenTank"), hud = Find("GameHUD");
                Call(oxygen, "TryConsume", 20f);
                float beforeOxygen = Property<float>(oxygen, "CurrentOxygen");
                yield return Observe(sfx, "OxygenTank.TryCollect", () =>
                    Assert.That((bool)Call(tank, "TryCollect", oxygen), Is.True));
                Assert.That(Property<float>(oxygen, "CurrentOxygen"), Is.GreaterThan(beforeOxygen));
                yield return Observe(sfx, "GameHUD.OpenPause", () => Call(hud, "OpenPause"));
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(ambient.isPlaying || fall.isPlaying, Is.False);
                yield return Observe(sfx, "GameHUD.ClosePause / back", () => Call(hud, "ClosePause"));
                Assert.That(Time.timeScale, Is.GreaterThan(0f));

                // Invoke the real trigger handler with the scene's HazardKill collider.
                Component hazard = Find("HazardKill");
                Collider2D hazardCollider = hazard.GetComponentInChildren<Collider2D>();
                Assert.That(hazardCollider, Is.Not.Null);
                yield return Observe(sfx, "HazardKill trigger handler / unified death", () => Call(player, "OnTriggerEnter2D", hazardCollider));
                Assert.That(Property<bool>(player, "IsDead"), Is.True);
                ((MonoBehaviour)player).StopAllCoroutines();
                yield return SceneManager.LoadSceneAsync("Stage1", LoadSceneMode.Single);
                yield return null;
                yield return null;
                player = StartFrozenGameplay();
                door = Find("GoalDoor");
                player.transform.position = door.transform.position + Vector3.left;
                yield return null;
                yield return null;
                AudioSource doorHum = Field<AudioSource[]>(loops, "_doorSources").Single();
                Assert.That(doorHum.clip.name, Is.EqualTo("sfx_door_hum_loop"));
                Assert.That(doorHum.isPlaying && doorHum.volume > 0f, Is.True);
                yield return Observe(sfx, "GoalDoor.OnTriggerEnter2D / open", () =>
                    Call(door, "OnTriggerEnter2D", player.GetComponent<Collider2D>()));
                Assert.That(Property<bool>(door, "IsOpening"), Is.True);
                Assert.That(doorHum.isPlaying, Is.False, "Opening immediately silences the door hum.");
                int afterOpen = Field<int>(sfx, "_poolCursor");
                float deadline = Time.realtimeSinceStartup + .5f;
                while (Field<int>(sfx, "_poolCursor") == afterOpen && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(Field<int>(sfx, "_poolCursor"), Is.Not.EqualTo(afterOpen), "Level-clear one-shot starts after the 0.3 s delay.");
                Debug.Log("[OtherSfx clear] output peak=" + Peak(LastSource(sfx)));
                ((MonoBehaviour)door).StopAllCoroutines();

                yield return SceneManager.LoadSceneAsync("Maze_03", LoadSceneMode.Single);
                yield return null;
                yield return null;
                player = StartFrozenGameplay();
                Component[] lasers = All("LaserEmitter");
                Assert.That(lasers.Length, Is.GreaterThan(0));
                Component laser = lasers.First(x => Property<bool>(x, "IsBeamActive"));
                player.transform.position = laser.transform.position + Vector3.up;
                yield return null;
                yield return null;
                AudioSource[] hums = Field<AudioSource[]>(loops, "_laserSources");
                Assert.That(hums.Length, Is.EqualTo(lasers.Length));
                Assert.That(hums.Distinct().Count(), Is.EqualTo(hums.Length));
                Component[] cachedLasers = Field<Component[]>(loops, "_lasers");
                AudioSource nearHum = hums[Array.IndexOf(cachedLasers, laser)];
                Assert.That(nearHum.clip.name, Is.EqualTo("sfx_laser_hum_loop"));
                Assert.That(nearHum.isPlaying && nearHum.volume > 0f && nearHum.volume <= .3f, Is.True);
                Debug.Log("[OtherSfx Maze_03] emitter sources=" + hums.Length + ", nearby hum output peak=" + Peak(nearHum));
            }
            finally
            {
                if (door != null) ((MonoBehaviour)door).StopAllCoroutines();
                if (player != null) ((MonoBehaviour)player).StopAllCoroutines();
                if (sfx != null)
                {
                    foreach (var source in Field<AudioSource[]>(sfx, "_oneShotPool")) source.Stop();
                    Set(sfx, "sfxEnabled", oldEnabled);
                    Set(sfx, "masterVolume", oldVolume);
                }
                Time.timeScale = oldTime;
                Application.runInBackground = oldBackground;
            }
        }

        private static Component StartFrozenGameplay()
        {
            Call(Find("LevelIntroUI"), "BeginGameplayFromPreview");
            Component player = Find("PlayerController");
            Set(player, "autoMoveMode", false);
            Set(player, "rumbleEnabled", false);
            player.GetComponent<Rigidbody2D>().simulated = false;
            Find("PlayerOxygen").GetType().GetProperty("DrainPerSecond").SetValue(Find("PlayerOxygen"), 0f);
            return player;
        }

        private static IEnumerator Observe(Component sfx, string label, Action action)
        {
            foreach (var source in Field<AudioSource[]>(sfx, "_oneShotPool")) source.Stop();
            action();
            AudioSource played = LastSource(sfx);
            bool playing = played.isPlaying;
            float peak = 0f, until = Time.realtimeSinceStartup + .16f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                playing |= played.isPlaying;
                peak = Mathf.Max(peak, Peak(played));
            }
            Assert.That(playing, Is.True, label);
            Debug.Log("[OtherSfx] " + label + ": source played, output peak=" + peak.ToString("F6"));
        }

        private static AudioSource LastSource(Component sfx)
        {
            var pool = Field<AudioSource[]>(sfx, "_oneShotPool");
            return pool[(Field<int>(sfx, "_poolCursor") + pool.Length - 1) % pool.Length];
        }

        private static float Peak(AudioSource source)
        {
            var samples = new float[256];
            source.GetOutputData(samples, 0);
            return samples.Max(x => Mathf.Abs(x));
        }

        private static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(x => x.GetType("Resource.Scripts." + name)).First(x => x != null);
        private static Component[] All(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(x => x.GetComponentsInChildren(TypeOf(name), true)).ToArray();
        private static Component Find(string name) => All(name).First();
        private static T Field<T>(Component target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(Component target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static T Property<T>(Component target, string name) => (T)target.GetType().GetProperty(name, Flags).GetValue(target);
        private static object Call(Component target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    }
}
