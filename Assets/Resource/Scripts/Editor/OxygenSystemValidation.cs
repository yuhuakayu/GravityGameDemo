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
            var dash = player.GetComponent<PlayerJetpack>();
            var body = player.GetComponent<Rigidbody2D>();
            var hud = Object.FindFirstObjectByType<OxygenHUD>();
            Require(oxygen != null && dash != null && body != null && hud != null, "Install the oxygen system first.");
            Require(!player.IsDead && !dash.IsDashing, "Start checks with a living player outside a dash.");
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
            float savedCooldown = dash.CooldownRemaining;
            var nozzle = player.transform.Find("JetpackNozzle");
            Vector3 savedNozzlePosition = nozzle != null ? nozzle.localPosition : Vector3.zero;
            Quaternion savedNozzleRotation = nozzle != null ? nozzle.localRotation : Quaternion.identity;
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
                Require(oxygen.MaxOxygen > oxygen.DashCost * 2f && oxygen.DashCost > 0f,
                    "Checks require capacity for at least two dashes and a positive dash cost.");

                var tankObject = Object.Instantiate(prefab);
                temporary.Add(tankObject);
                tankObject.transform.position = new Vector3(20000f, 20000f, 0f);
                var tank = tankObject.GetComponent<OxygenTank>();
                Require(tank != null, "Prefab has no OxygenTank component.");

                player.EnterPreview();
                player.enabled = false;
                Time.timeScale = 1f; // Preview must remain safe even if a menu restores time.
                float before = oxygen.CurrentOxygen;
                Require(!oxygen.TryConsume(1f) && !dash.TryDash(Vector2.right) && !tank.TryCollect(oxygen),
                    "Preview accepted consume, dash, or pickup.");
                Near(oxygen.CurrentOxygen, before, "Preview changed oxygen.");
                Pass(report, "Preview rejects consumption, dash and pickup even when timeScale is 1.");

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

                oxygen.AddOxygen(oxygen.MaxOxygen);
                Set(dash, "_cooldownRemaining", 0f);
                // Check the controller-owned gravity, not the deliberately zero engine gravity.
                // Use nonzero gravity even if this particular scene was configured weightless.
                float gravityBefore = player.SimulatedGravityScale;
                if (gravityBefore <= 0f) player.SimulatedGravityScale = gravityBefore = 1f;
                var collisionBefore = body.collisionDetectionMode;
                before = oxygen.CurrentOxygen;
                Require(dash.TryDash(Vector2.right), "Dash did not start.");
                Near(oxygen.CurrentOxygen, before - oxygen.DashCost, "Dash oxygen charge is incorrect.");
                Near(player.SimulatedGravityScale, gravityBefore * Get<float>(dash, "gravityMultiplier"), "Dash gravity multiplier is incorrect.");
                dash.CancelDash();
                Near(player.SimulatedGravityScale, gravityBefore, "Cancel did not restore gravity.");
                Require(body.collisionDetectionMode == collisionBefore, "Cancel did not restore collision mode.");
                before = oxygen.CurrentOxygen;
                Require(!dash.TryDash(Vector2.right), "Cooldown accepted another dash.");
                Near(oxygen.CurrentOxygen, before, "Rejected cooldown dash consumed oxygen.");
                Pass(report, "Dash charges once, rejects cooldown input and restores gravity/collision mode on cancel.");

                Set(dash, "_cooldownRemaining", 0f);
                player.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                body.rotation = 90f;
                Physics2D.SyncTransforms();
                Require(dash.TryDash(Vector2.right), "Rotated local-direction dash failed.");
                Require(Vector2.Dot(dash.DashDirection, Vector2.up) > .999f,
                    "Player-local right at 90 degrees did not produce world-up dash.");
                Time.timeScale = 0f;
                dash.TickDash(.02f);
                Require(!dash.IsDashing, "Paused gameplay did not cancel dash.");
                Near(player.SimulatedGravityScale, gravityBefore, "Pause cancellation did not restore gravity.");
                Time.timeScale = 1f;
                Pass(report, "Direction follows player-local rotation; pausing cancels dash and restores gravity.");

                // Exercise the real collider and Physics2D solver, isolated far from the level.
                Vector2 testOrigin = new Vector2(10000f, 10000f);
                Teleport(player, body, testOrigin);
                Bounds bounds = SolidBounds(body);
                int groundLayer = CollidableLayer(body);
                var floor = MakeBox("Oxygen Validation Floor", groundLayer,
                    new Vector2(bounds.center.x, bounds.min.y - .503f), new Vector2(100f, 1f), temporary);
                Physics2D.SyncTransforms();
                for (int i = 0; i < 25; i++) Physics2D.Simulate(.02f);
                Vector2 restingPosition = body.position;
                oxygen.AddOxygen(oxygen.MaxOxygen);
                Set(dash, "_cooldownRemaining", 0f);
                Require(dash.TryDash(Vector2.right), "Grounded horizontal dash did not start.");
                StepDash(dash);
                float travel = body.position.x - restingPosition.x;
                Require(travel > .2f, "Ground contact cancelled horizontal dash; travel=" + travel);
                Require(SolidBounds(body).min.y >= floor.bounds.max.y - .08f, "Dash penetrated the supporting floor.");
                Pass(report, "Physics: grounded horizontal dash advances " + travel.ToString("F3") + " units without floor penetration.");

                Teleport(player, body, restingPosition);
                bounds = SolidBounds(body);
                var wall = MakeBox("Oxygen Validation Thin Wall", groundLayer,
                    new Vector2(bounds.max.x + .48f, bounds.center.y), new Vector2(.06f, bounds.size.y + 10f), temporary);
                Physics2D.SyncTransforms();
                oxygen.AddOxygen(oxygen.MaxOxygen);
                Set(dash, "_cooldownRemaining", 0f);
                Require(dash.TryDash(Vector2.right), "Wall-check dash did not start.");
                StepDash(dash);
                bounds = SolidBounds(body);
                Require(body.position.x > restingPosition.x + .1f, "Wall check never approached the wall.");
                Require(bounds.max.x <= wall.bounds.min.x + .025f, "Dash tunneled through or penetrated the thin wall.");
                Require(!dash.IsDashing, "Wall impact failed to end the dash.");
                Pass(report, "Physics: dash stops before a 0.06-unit solid wall using the existing player colliders.");
            }
            finally
            {
                oxygen.OxygenChanged -= listener;
                dash.CancelDash();
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
                Set(dash, "_cooldownRemaining", savedCooldown);
                if (nozzle != null)
                {
                    nozzle.localPosition = savedNozzlePosition;
                    nozzle.localRotation = savedNozzleRotation;
                    var particles = nozzle.GetComponent<ParticleSystem>();
                    if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var trail = nozzle.GetComponent<TrailRenderer>();
                    if (trail != null) trail.Clear();
                }
                foreach (var pair in otherBodies)
                    if (pair.Key != null) pair.Key.simulated = pair.Value;
                hud.Bind(oxygen);
                Physics2D.SyncTransforms();
                Physics2D.simulationMode = savedSimulation;
                Time.timeScale = savedTimeScale;
            }
            return report.ToString().TrimEnd();
        }

        private static void StepDash(PlayerJetpack dash)
        {
            for (int i = 0; i < 100 && dash.IsDashing; i++)
            {
                dash.TickDash(.02f);
                Physics2D.Simulate(.02f);
            }
            Require(!dash.IsDashing, "Dash failed to end within 2 seconds of manual physics.");
        }

        private static void Teleport(PlayerController player, Rigidbody2D body, Vector2 position)
        {
            player.transform.SetPositionAndRotation(new Vector3(position.x, position.y, player.transform.position.z), Quaternion.identity);
            body.position = position;
            body.rotation = 0f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.WakeUp();
            Physics2D.SyncTransforms();
        }

        private static Bounds SolidBounds(Rigidbody2D body)
        {
            bool found = false;
            Bounds result = default;
            foreach (var collider in body.GetComponentsInChildren<Collider2D>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                if (!found) { result = collider.bounds; found = true; }
                else result.Encapsulate(collider.bounds);
            }
            Require(found, "Player has no enabled solid collider.");
            return result;
        }

        private static int CollidableLayer(Rigidbody2D body)
        {
            foreach (var collider in body.GetComponentsInChildren<Collider2D>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body) continue;
                int mask = Physics2D.GetLayerCollisionMask(collider.gameObject.layer);
                for (int layer = 0; layer < 32; layer++)
                    if ((mask & (1 << layer)) != 0) return layer;
            }
            throw new InvalidOperationException("[Oxygen validation] Player has no collidable layer.");
        }

        private static BoxCollider2D MakeBox(string name, int layer, Vector2 position, Vector2 size, List<GameObject> temporary)
        {
            var go = new GameObject(name) { layer = layer };
            temporary.Add(go);
            go.transform.position = position;
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = size;
            return collider;
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
