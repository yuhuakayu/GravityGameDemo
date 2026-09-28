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
    public sealed class PendulumStagePlayModeTests
    {
        [Serializable] private sealed class CaseResult { public string name, error; public bool passed; public float maxHingeGap, maxPenetration, maxCorrection, maxDepenetrationPerStep; public int originalSideCrossings, maxRemainingOverlap; }
        [Serializable] private sealed class Report { public bool passed; public CaseResult[] results; }
        [Serializable] private sealed class ScenarioResult { public string scenario; public int targetContactFrames, surfaceCrossings; public CaseResult physics; }
        [Serializable] private sealed class ScenarioReport { public bool passed; public ScenarioResult[] results; }

        [UnityTest]
        public IEnumerator Stage1_Clocks_RotateTenTurnsInBothDirections_WithoutGapOverlapOrCrossing()
        {
            float previousTimeScale = Time.timeScale;
            bool loadedHere = !SceneManager.GetSceneByName("Stage1").isLoaded;
            if (loadedHere) yield return SceneManager.LoadSceneAsync("Stage1", LoadSceneMode.Additive);
            yield return null; // Let the real Stage1 components initialize and enter preview.
            Exception failure = null;
            try
            {
                string json = RunEditorValidation("PendulumStageValidation", "test-runner");
                var report = JsonUtility.FromJson<Report>(json);
                var additional = JsonUtility.FromJson<ScenarioReport>(RunEditorValidation("PendulumScenarioValidation", "test-runner-additional"));
                Assert.That(report.results, Has.Length.EqualTo(8), "Two clocks x two directions x two simulated render schedules.");
                Assert.That(additional.results, Has.Length.EqualTo(8), "Two plank sides plus playable far wall with automatic and fixed-origin pivots x two simulated render schedules.");
                string details = string.Join("\n", report.results.Where(r => !r.passed).Select(r =>
                    FailureDetails(r)).Concat(additional.results.Where(r => !r.physics.passed).Select(r =>
                    FailureDetails(r.physics) + ", target contacts=" + r.targetContactFrames + ", surface crossings=" + r.surfaceCrossings)));
                Assert.That(report.passed && additional.passed && report.results.All(r => r.passed) && additional.results.All(r => r.physics.passed), Is.True, details);
            }
            catch (Exception ex) { failure = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex; }
            if (loadedHere) yield return SceneManager.UnloadSceneAsync("Stage1");
            Time.timeScale = previousTimeScale;
            if (failure != null) throw failure;
        }

        private static string RunEditorValidation(string className, string label)
        {
            Type runner = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Resource.Scripts.Editor." + className)).FirstOrDefault(t => t != null);
            Assert.That(runner, Is.Not.Null, "This regression uses an Editor-only geometry-cloning helper in the Unity Editor PlayMode Test Runner: " + className);
            return (string)runner.GetMethod("RunAndSave", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { label, 10 });
        }

        private static string FailureDetails(CaseResult r)
        {
            return r.name + ": hinge=" + r.maxHingeGap + ", penetration=" + r.maxPenetration +
                ", original-side crossings=" + r.originalSideCrossings + ", residual overlaps=" + r.maxRemainingOverlap +
                ", correction/budget=" + r.maxCorrection + "/" + r.maxDepenetrationPerStep + ", " + r.error;
        }
    }
}
