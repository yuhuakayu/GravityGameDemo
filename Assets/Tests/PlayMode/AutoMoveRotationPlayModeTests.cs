using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GravityGame.Tests.PlayMode
{
    public sealed class AutoMoveRotationPlayModeTests
    {
        [Serializable] private sealed class CaseResult { public string name, detail; public bool passed; }
        [Serializable] private sealed class Report { public bool passed; public CaseResult[] results; }

        [Test]
        public void AutoMoveRotationPause_RespectsQuietTime_AndKeepsGravityAndCrushProtection()
        {
            Type runner = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Resource.Scripts.Editor.AutoMoveRotationValidation")).FirstOrDefault(t => t != null);
            Assert.That(runner, Is.Not.Null, "Run this regression in the Unity Editor PlayMode Test Runner.");
            string json;
            try { json = (string)runner.GetMethod("RunAndSave", BindingFlags.Public | BindingFlags.Static).Invoke(null, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            var report = JsonUtility.FromJson<Report>(json);
            Assert.That(report.results, Has.Length.EqualTo(8));
            Assert.That(report.passed && report.results.All(result => result.passed), Is.True,
                string.Join("\n", report.results.Where(result => !result.passed).Select(result => result.name + ": " + result.detail)));
        }
    }
}
