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
    public sealed class WorldRotationSoundStage1Tests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [UnityTest, Timeout(20000)]
        public IEnumerator Stage1_AngleClicks_FastLoop_StopOnce_PauseMutes()
        {
            float previousTimeScale = Time.timeScale;
            bool previousRunInBackground = Application.runInBackground;
            Component player = null, oxygen = null, sfx = null;
            bool oldAutoMove = false, oldRumble = false, oldEnabled = false, capturedSettings = false;
            float oldDrain = 0f, oldVolume = 0f;
            Application.runInBackground = true;
            try
            {
                yield return SceneManager.LoadSceneAsync("Stage1", LoadSceneMode.Single);
                yield return null;
                yield return null;
                Scene scene = SceneManager.GetActiveScene();
                player = Find(scene, "Resource.Scripts.PlayerController");
                oxygen = Find(scene, "Resource.Scripts.PlayerOxygen");
                Component world = Find(scene, "Resource.Scripts.WorldRotator");
                Component intro = Find(scene, "Resource.Scripts.LevelIntroUI");
                sfx = (Component)FindType("Resource.Scripts.SfxManager").GetProperty("Instance").GetValue(null);
                oldAutoMove = Field<bool>(player, "autoMoveMode");
                oldRumble = Field<bool>(player, "rumbleEnabled");
                oldDrain = Property<float>(oxygen, "DrainPerSecond");
                oldEnabled = Field<bool>(sfx, "sfxEnabled");
                oldVolume = Field<float>(sfx, "masterVolume");
                capturedSettings = true;
                SetField(player, "autoMoveMode", false);
                SetField(player, "rumbleEnabled", false);
                oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, 0f);
                SetField(sfx, "sfxEnabled", true);
                SetField(sfx, "masterVolume", 1f);

                var loop = Field<AudioSource>(sfx, "_rotateLoopSource");
                var stop = Field<AudioSource>(sfx, "_rotateStopSource");
                var click = Field<AudioSource>(sfx, "_rotateClickSource");
                AudioClip[] clickClips = Field<AudioClip[]>(sfx, "worldRotateClickClips");
                Assert.That(clickClips.Length, Is.EqualTo(6));
                for (int i = 0; i < clickClips.Length; i++)
                {
                    Assert.That(clickClips[i].name, Is.EqualTo("sfx_world_rotate_click_" + (i + 1)));
                    Assert.That(clickClips[i].loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
                }
                Assert.That(loop.clip.name, Is.EqualTo("sfx_world_rotate_loop"));
                Assert.That(stop.clip.name, Is.EqualTo("sfx_world_rotate_stop"));
                Assert.That(loop.clip.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
                Assert.That(stop.clip.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad));
                Assert.That(loop.loop, Is.True);
                Assert.That(stop.loop, Is.False);
                Assert.That(loop.isPlaying || stop.isPlaying || click.isPlaying, Is.False, "Preview must be silent.");

                intro.GetType().GetMethod("BeginGameplayFromPreview").Invoke(intro, null);
                // Let preview-exit input revision changes discard stale targets before feeding test motion.
                yield return null;
                yield return null;
                float deadline = Time.realtimeSinceStartup + 10f;
                float initialAngle = Property<float>(world, "ClockwiseAngleReadout");
                Rigidbody2D worldBody = world.GetComponent<Rigidbody2D>();
                MethodInfo setTarget = world.GetType().GetMethod("SetTargetAngle");
                MethodInfo releaseTarget = world.GetType().GetMethod("DiscardPendingRotation");
                float loopPeak = 0f, stopPeak = 0f, clickPeak = 0f;
                int totalClicks = 0, totalStops = 0;
                var samples = new float[256];
                float[] targets = { initialAngle + 18f, initialAngle - 30f, initialAngle - 26f };
                for (int phase = 0; phase < targets.Length; phase++)
                {
                    float target = targets[phase];
                    float command = Property<float>(world, "ClockwiseAngleReadout");
                    bool loopPlayed = false, previousStopPlaying = false, targetReleased = false;
                    int previousStopSample = 0, stopStarts = 0, clickStarts = 0;
                    double lastClick = Field<double>(sfx, "_lastClickTime");
                    float settledAt = -1f;
                    float previousBodyAngle = worldBody.rotation;
                    float previousFixedTime = Time.fixedTime;
                    float maxActualSpeed = 0f, maxReportedSpeed = 0f;
                    float settleDuration = phase == 2 ? .3f : stop.clip.length + .35f;
                    // Feed slow targets per physics step to avoid render/physics sampling aliasing.
                    // The second sweep lets the real body reach its ordinary 120 deg/s limit.
                    while (settledAt < 0f || Time.realtimeSinceStartup - settledAt < settleDuration)
                    {
                        AssertAlive(player, deadline, world, target, "sweep " + phase);
                        if (!targetReleased)
                        {
                            command = phase == 1 ? target : Mathf.MoveTowards(command, target, 60f * Time.fixedDeltaTime);
                            setTarget.Invoke(world, new object[] { command });
                        }
                        if (phase == 1 || targetReleased) yield return null;
                        else yield return new WaitForFixedUpdate();
                        float physicsElapsed = Time.fixedTime - previousFixedTime;
                        float actualSpeed = physicsElapsed > 0f
                            ? Mathf.Abs(Mathf.DeltaAngle(previousBodyAngle, worldBody.rotation)) / physicsElapsed : 0f;
                        previousBodyAngle = worldBody.rotation;
                        previousFixedTime = Time.fixedTime;
                        float actualAngle = Property<float>(world, "ClockwiseAngleReadout");
                        float reportedSpeed = Field<float>(world, "_lastRotateSpeed");
                        maxActualSpeed = Mathf.Max(maxActualSpeed, actualSpeed);
                        maxReportedSpeed = Mathf.Max(maxReportedSpeed, reportedSpeed);
                        if (loop.isPlaying && loop.volume > 0f)
                        {
                            if (!loopPlayed)
                                Debug.Log("[RotationSfx loop onset] phase=" + phase + ", actual/reported speed=" +
                                    actualSpeed.ToString("F3") + "/" + reportedSpeed.ToString("F3") +
                                    " deg/s, maxima=" + maxActualSpeed.ToString("F3") + "/" + maxReportedSpeed.ToString("F3") +
                                    ", command/actual angle=" + command.ToString("F3") + "/" + actualAngle.ToString("F3") +
                                    ", loop volume=" + loop.volume.ToString("F4"));
                            loopPlayed = true;
                            loopPeak = Mathf.Max(loopPeak, OutputPeak(loop, samples));
                        }
                        double clickTime = Field<double>(sfx, "_lastClickTime");
                        if (clickTime > lastClick)
                        {
                            if (clickStarts > 0)
                                Assert.That(clickTime - lastClick, Is.GreaterThanOrEqualTo(.0349d), "Clicks must respect the minimum interval.");
                            clickStarts++;
                            lastClick = clickTime;
                        }
                        if (click.isPlaying) clickPeak = Mathf.Max(clickPeak, OutputPeak(click, samples));
                        bool playing = stop.isPlaying;
                        int sample = stop.timeSamples;
                        if (playing && (!previousStopPlaying || sample < previousStopSample)) stopStarts++;
                        if (playing) stopPeak = Mathf.Max(stopPeak, OutputPeak(stop, samples));
                        previousStopPlaying = playing;
                        previousStopSample = sample;
                        if (!targetReleased && Mathf.Abs(actualAngle - target) < .01f)
                        {
                            Debug.Log("[RotationSfx target reached] " + MotionState(world, target, "sweep " + phase));
                            // Release the input once, instead of repeatedly requesting a float-rounded target.
                            releaseTarget.Invoke(world, null);
                            targetReleased = true;
                        }
                        if (settledAt < 0f && targetReleased && !Property<bool>(world, "IsRotating"))
                            settledAt = Time.realtimeSinceStartup;
                    }
                    Debug.Log("[RotationSfx phase] phase=" + phase + ", max actual/reported speed=" +
                        maxActualSpeed.ToString("F3") + "/" + maxReportedSpeed.ToString("F3") +
                        " deg/s, clicks=" + clickStarts + ", stops=" + stopStarts + ", loop=" + loopPlayed);
                    if (phase == 0)
                    {
                        Assert.That(maxActualSpeed, Is.LessThanOrEqualTo(60.1f), "The slow test input must produce real 60 deg/s physics motion.");
                        Assert.That(maxReportedSpeed, Is.LessThanOrEqualTo(60.1f), "Reported sound speed must agree with the real slow motion.");
                    }
                    Assert.That(loopPlayed, Is.EqualTo(phase == 1), "Only the fast sweep should play the continuous loop; phase=" + phase);
                    if (phase == 0) Assert.That(clickStarts, Is.EqualTo(2), "18 degrees should make two 8-degree clicks.");
                    if (phase == 1) Assert.That(clickStarts, Is.GreaterThanOrEqualTo(5), "The fast 48-degree sweep should also emit repeated clicks.");
                    if (phase == 2) Assert.That(clickStarts, Is.Zero, "A fresh 4-degree motion is below the click threshold.");
                    Assert.That(stopStarts, Is.EqualTo(phase == 2 ? 0 : 1), "Each substantial motion stops once; tiny motion has no stop sound.");
                    Assert.That(loop.isPlaying || stop.isPlaying, Is.False, "Loop and stop clip must finish without repeating.");
                    totalClicks += clickStarts;
                    totalStops += stopStarts;
                }
                do
                {
                    AssertAlive(player, deadline, world, initialAngle + 20f, "pause-start");
                    setTarget.Invoke(world, new object[] { initialAngle + 20f });
                    yield return null;
                } while (!loop.isPlaying || loop.volume <= 0f);
                Time.timeScale = 0f;
                // A null yield resumes before LateUpdate; allow one complete frame for the pause gate.
                yield return null;
                yield return null;
                Assert.That(loop.isPlaying || stop.isPlaying || click.isPlaying, Is.False, "Pause must immediately stop all rotation sources.");
                Assert.That(Field<float>(sfx, "_clickAngle"), Is.Zero);
                Assert.That(Field<float>(sfx, "_rotationDegrees"), Is.Zero);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.That(loop.isPlaying || stop.isPlaying || click.isPlaying, Is.False, "Pause must not emit a delayed click or stop sound.");
                Debug.Log("[RotationSfx Stage1] slow 18 deg=2 clicks/no loop, fast 48 deg=clicks+loop, tiny 4 deg=no click/stop; total clicks=" + totalClicks +
                    ", stop starts=" + totalStops + ", pause=three sources stopped/angles cleared; captured click/loop/stop peaks=" +
                    clickPeak.ToString("F6") + "/" + loopPeak.ToString("F6") + "/" + stopPeak.ToString("F6") +
                    (loopPeak > 0f && stopPeak > 0f && clickPeak > 0f
                        ? " (nonzero AudioSource output captured)."
                        : " (audio output not observed for one or more sources; playback state was verified)."));
            }
            finally
            {
                if (capturedSettings)
                {
                    if (player != null)
                    {
                        SetField(player, "autoMoveMode", oldAutoMove);
                        SetField(player, "rumbleEnabled", oldRumble);
                    }
                    if (oxygen != null) oxygen.GetType().GetProperty("DrainPerSecond").SetValue(oxygen, oldDrain);
                    if (sfx != null)
                    {
                        SetField(sfx, "sfxEnabled", oldEnabled);
                        SetField(sfx, "masterVolume", oldVolume);
                    }
                }
                Time.timeScale = previousTimeScale;
                Application.runInBackground = previousRunInBackground;
            }
        }

        private static float OutputPeak(AudioSource source, float[] samples)
        {
            source.GetOutputData(samples, 0);
            float peak = 0f;
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            return peak;
        }

        private static void AssertAlive(Component player, float deadline, Component world, float target, string phase)
        {
            if (Time.realtimeSinceStartup >= deadline)
                Assert.Fail("Stage1 rotation sound check timed out: " + MotionState(world, target, phase));
            if (player == null || Property<bool>(player, "IsDead"))
                Assert.Fail("Player died before the audio check completed: " + MotionState(world, target, phase));
        }

        private static string MotionState(Component world, float target, string phase) => world == null
            ? "phase=" + phase + ", world destroyed"
            : "phase=" + phase + ", target/current=" + target.ToString("G9") + "/" +
              Property<float>(world, "ClockwiseAngleReadout").ToString("G9") +
              ", IsRotating=" + Property<bool>(world, "IsRotating") +
              ", body.angularVelocity=" + world.GetComponent<Rigidbody2D>().angularVelocity.ToString("G9") +
              ", lastRotateSpeed=" + Field<float>(world, "_lastRotateSpeed").ToString("G9");

        private static Component Find(Scene scene, string name) =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(FindType(name), true)).Single();
        private static Type FindType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).First(type => type != null);
        private static T Property<T>(Component component, string name) =>
            (T)component.GetType().GetProperty(name, Fields).GetValue(component);
        private static T Field<T>(Component component, string name) =>
            (T)component.GetType().GetField(name, Fields).GetValue(component);
        private static void SetField(Component component, string name, object value) =>
            component.GetType().GetField(name, Fields).SetValue(component, value);
    }
}
