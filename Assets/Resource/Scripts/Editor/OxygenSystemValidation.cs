using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Resource.Scripts.Editor
{
    /// <summary>Explicit Play-mode checks against installed scene components; never runs on import.</summary>
    public static class OxygenSystemValidation
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static string RunImmediateChecks()
        {
            Require(EditorApplication.isPlaying && EditorApplication.isPaused,
                "Run these checks only in Play mode with the Editor paused.");
            var player = Object.FindFirstObjectByType<PlayerController>();
            Require(player != null, "Scene player is missing.");
            var oxygen = player.GetComponent<PlayerOxygen>();
            var body = player.GetComponent<Rigidbody2D>();
            var hud = Object.FindFirstObjectByType<OxygenHUD>();
            Require(oxygen != null && body != null && hud != null, "Install the oxygen system first.");
            Require(!player.IsDead, "Start checks with a living player.");
            Require(Get<PlayerOxygen>(hud, "oxygen") == oxygen, "HUD must reference the scene player.");
            var fill = Get<Image>(hud, "fillImage");
            Require(fill != null && fill.type == Image.Type.Filled, "HUD must use a filled Image.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OxygenSystemSetup.TankPath);
            Require(prefab != null, "OxygenTank prefab is missing.");

            var report = new StringBuilder();
            var temporary = new List<GameObject>();
            var otherBodies = new Dictionary<Rigidbody2D, bool>();
            float savedTimeScale = Time.timeScale;
            var savedSimulation = Physics2D.simulationMode;
            Vector3 savedPosition = player.transform.position;
            Quaternion savedRotation = player.transform.rotation;
            Vector2 savedVelocity = body.linearVelocity;
            float savedAngularVelocity = body.angularVelocity;
            float savedGravity = player.SimulatedGravityScale;
            Vector2 savedIntendedVelocity = player.IntendedVelocity;
            bool savedJumpQueued = Get<bool>(player, "_jumpQueued");
            bool savedPhysicsStepPending = Get<bool>(player, "_physicsStepPending");
            Vector2 savedStepStartPosition = Get<Vector2>(player, "_stepStartPosition");
            Vector2 savedSubmittedVelocity = Get<Vector2>(player, "_submittedVelocity");
            var savedBodyType = body.bodyType;
            bool savedSimulated = body.simulated;
            var savedCollision = body.collisionDetectionMode;
            bool savedEnabled = player.enabled;
            bool savedGameplay = Get<bool>(player, "_gameplayStarted");
            bool savedGrounded = Get<bool>(player, "isGrounded");
            float savedCurrent = oxygen.CurrentOxygen;
            int eventCount = 0;
            Action<float, float> listener = (_, __) => eventCount++;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                foreach (var other in Object.FindObjectsByType<Rigidbody2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (other == body) continue;
                    otherBodies.Add(other, other.simulated);
                    other.simulated = false;
                }
                body.bodyType = RigidbodyType2D.Dynamic;
                body.simulated = true;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                oxygen.OxygenChanged += listener;
                oxygen.AddOxygen(oxygen.MaxOxygen);

                var tankObject = Object.Instantiate(prefab);
                temporary.Add(tankObject);
                tankObject.transform.position = new Vector3(20000f, 20000f, 0f);
                var tank = tankObject.GetComponent<OxygenTank>();
                Require(tank != null, "Prefab has no OxygenTank component.");

                player.EnterPreview();
                player.enabled = false;
                Time.timeScale = 1f; // Preview must remain safe even if a menu restores time.
                float before = oxygen.CurrentOxygen;
                Require(!oxygen.TryConsume(1f) && !tank.TryCollect(oxygen),
                    "Preview accepted consumption or pickup.");
                Near(oxygen.CurrentOxygen, before, "Preview changed oxygen.");
                Pass(report, "Preview rejects consumption and pickup even when timeScale is 1.");

                player.enabled = true;
                player.BeginGameplay();
                Time.timeScale = 1f;
                int countBefore = eventCount;
                float spend = oxygen.MaxOxygen * .1f;
                Require(oxygen.TryConsume(spend), "Active gameplay rejected consumption.");
                Near(oxygen.CurrentOxygen, before - spend, "Consumption amount is incorrect.");
                Require(eventCount == countBefore + 1, "Oxygen change did not emit one event.");
                Near(fill.fillAmount, oxygen.NormalizedOxygen, "HUD did not update from the oxygen event.");
                Pass(report, "Consumption emits an event and immediately updates the real HUD fill.");

                float lowTarget = oxygen.MaxOxygen * Get<float>(hud, "lowOxygenThreshold") * .5f;
                Require(lowTarget > 0f && lowTarget < oxygen.CurrentOxygen, "Low-oxygen warning threshold must be above zero.");
                Require(oxygen.TryConsume(oxygen.CurrentOxygen - lowTarget), "Cannot reach low-oxygen state.");
                Color expectedLow = Get<Color>(hud, "lowOxygenColor");
                Near(fill.color.r, expectedLow.r, "Low-oxygen warning red channel is incorrect.");
                Near(fill.color.g, expectedLow.g, "Low-oxygen warning green channel is incorrect.");
                Near(fill.color.b, expectedLow.b, "Low-oxygen warning blue channel is incorrect.");
                Pass(report, "Below-threshold oxygen changes the actual fill to its warning color.");

                before = oxygen.CurrentOxygen;
                countBefore = eventCount;
                Require(!oxygen.TryConsume(float.NaN) && !oxygen.TryConsume(float.PositiveInfinity) &&
                    !oxygen.TryConsume(-1f) && !oxygen.TryConsume(oxygen.MaxOxygen + 1f),
                    "Invalid or unaffordable consumption was accepted.");
                oxygen.AddOxygen(float.NaN);
                oxygen.AddOxygen(float.PositiveInfinity);
                oxygen.AddOxygen(-1f);
                Near(oxygen.CurrentOxygen, before, "Invalid amounts changed oxygen.");
                Require(eventCount == countBefore, "Invalid amounts emitted a change event.");
                oxygen.AddOxygen(oxygen.MaxOxygen * 2f);
                Near(oxygen.CurrentOxygen, oxygen.MaxOxygen, "Oxygen overflowed its maximum.");
                Near(fill.fillAmount, 1f, "HUD did not return to full.");
                Pass(report, "Invalid and insufficient amounts are rejected; refill clamps at maximum.");

                Require(oxygen.TryConsume(oxygen.MaxOxygen * .5f), "Cannot prepare pickup check.");
                before = oxygen.CurrentOxygen;
                float restore = Get<float>(tank, "restoreAmount");
                Require(tank.TryCollect(oxygen), "Live prefab pickup failed.");
                Near(oxygen.CurrentOxygen, Mathf.Min(oxygen.MaxOxygen, before + restore), "Pickup restored the wrong amount.");
                Require(!tank.TryCollect(oxygen), "The same tank can be collected twice.");
                foreach (var collider in tankObject.GetComponents<Collider2D>())
                    Require(!collider.enabled, "Collected tank still has an active trigger.");
                Pass(report, "Real tank prefab restores oxygen once and disables its trigger during pickup animation.");
            }
            finally
            {
                oxygen.OxygenChanged -= listener;
                foreach (var go in temporary)
                    if (go != null) Object.DestroyImmediate(go);
                body.bodyType = savedBodyType;
                body.simulated = savedSimulated;
                player.SimulatedGravityScale = savedGravity;
                body.collisionDetectionMode = savedCollision;
                player.transform.SetPositionAndRotation(savedPosition, savedRotation);
                body.position = savedPosition;
                body.rotation = savedRotation.eulerAngles.z;
                player.SetIntendedVelocity(savedIntendedVelocity);
                body.linearVelocity = savedVelocity;
                body.angularVelocity = savedAngularVelocity;
                player.enabled = savedEnabled;
                Set(player, "_jumpQueued", savedJumpQueued);
                Set(player, "_physicsStepPending", savedPhysicsStepPending);
                Set(player, "_stepStartPosition", savedStepStartPosition);
                Set(player, "_submittedVelocity", savedSubmittedVelocity);
                Set(player, "_gameplayStarted", savedGameplay);
                Set(player, "isGrounded", savedGrounded);
                Set(oxygen, "_currentOxygen", savedCurrent);
                foreach (var pair in otherBodies)
                    if (pair.Key != null) pair.Key.simulated = pair.Value;
                hud.Bind(oxygen);
                Physics2D.SyncTransforms();
                Physics2D.simulationMode = savedSimulation;
                Time.timeScale = savedTimeScale;
            }
            return report.ToString().TrimEnd();
        }

        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static void Near(float actual, float expected, string message) => Require(Mathf.Abs(actual - expected) < .002f, message + " Expected " + expected + ", got " + actual + ".");
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Oxygen validation] " + message);
        }
        private static void Pass(StringBuilder report, string message) => report.AppendLine("PASS " + message);
    }
}
