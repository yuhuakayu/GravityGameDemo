using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Editor
{
    /// <summary>Checks the automatic-input rotation gate without pausing local physics.</summary>
    public static class AutoMoveRotationValidation
    {
        public const string ReportPath = "Docs/PendulumFix/AutoMove.json";
        // A 10 ms local test step measures the 150 ms threshold exactly; project settings stay intact.
        private const float Dt = .01f;
        private static readonly FieldInfo Rotating = typeof(WorldRotator).GetField("<IsRotating>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        [Serializable] public sealed class Result
        {
            public string name, detail;
            public bool passed;
            public int steps;
            public float maxHorizontalContribution, gravityError, resumeAfterQuietSeconds;
            public float solverInjectedSpeed, finalSpeed, correctionDistance, remainingPenetration;
        }

        [Serializable] public sealed class Report
        {
            public string utc, unityVersion;
            public string method = "Isolated Play-mode PhysicsScene2D; fixture WorldRotator.IsRotating is set explicitly, then production PlayerController.BeginPhysicsStep -> local Simulate -> CompletePhysicsStep. No main-scene step, PlayerLoop frame, or fixture Start runs.";
            public float timestep = Dt;
            public bool passed;
            public List<Result> results = new List<Result>();
        }

        [MenuItem("Tools/Gravity Game/Physics/Validate Auto Move Rotation Pause")]
        public static void RunMenu() { Debug.Log(RunAndSave()); }

        public static string RunAndSave()
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("Run automatic-movement checks in Play mode.");
            if (Rotating == null) throw new MissingFieldException("WorldRotator.IsRotating backing field was not found.");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            float savedTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 1f;
                Run(report, "Rotation pauses automatic X while gravity continues / anti-push on", () => GravityWhilePaused(true));
                Run(report, "Rotation pauses automatic X while gravity continues / anti-push off", () => GravityWhilePaused(false));
                Run(report, "A single false IsRotating sample does not resume input", SingleFalseSample);
                Run(report, "Continuous false resumes at 0.15 seconds", ResumeAfterDelay);
                Run(report, "A new true sample resets the quiet-time countdown", QuietTimeResets);
                Run(report, "Turning the feature off resumes immediately / anti-push on", () => DisableSwitch(true));
                Run(report, "Turning the feature off resumes immediately / anti-push off", () => DisableSwitch(false));
                Run(report, "Paused automatic input still removes a real wall impulse and repairs overlap", CollisionWhilePaused);
            }
            finally { Time.timeScale = savedTimeScale; }
            report.passed = report.results.TrueForAll(result => result.passed);
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, json);
            return json;
        }

        private static void Run(Report report, string name, Func<Result> test)
        {
            try
            {
                Result result = test();
                result.name = name;
                report.results.Add(result);
            }
            catch (Exception ex)
            { report.results.Add(new Result { name = name, passed = false, detail = ex.ToString() }); }
        }

        private static Result GravityWhilePaused(bool antiPush)
        {
            using (var f = new Fixture(antiPush, 1f))
            {
                var result = new Result { steps = 12 };
                bool allPaused = true;
                for (int i = 0; i < result.steps; ++i)
                {
                    f.Step(true);
                    allPaused &= f.Player.AutoMovePausedForRotation;
                    result.maxHorizontalContribution = Mathf.Max(result.maxHorizontalContribution,
                        Mathf.Abs(f.Player.AutoMoveVelocityContribution));
                }
                float expectedY = Physics2D.gravity.y * result.steps * Dt;
                result.gravityError = Mathf.Abs(f.Body.linearVelocity.y - expectedY);
                result.passed = allPaused && result.maxHorizontalContribution < .0001f &&
                    Mathf.Abs(f.Body.linearVelocity.x) < .0001f && result.gravityError < .002f &&
                    expectedY < 0f && f.Body.position.y < -.01f;
                result.detail = "Only automatic horizontal contribution is held. Dynamic-body free fall continues; final velocity=" + f.Body.linearVelocity;
                return result;
            }
        }

        private static Result SingleFalseSample()
        {
            using (var f = new Fixture(true, 1f))
            {
                f.Step(true);
                f.Step(false);
                bool stayedPaused = f.Player.AutoMovePausedForRotation && Mathf.Abs(f.Player.AutoMoveVelocityContribution) < .0001f;
                f.Step(true);
                return new Result { passed = stayedPaused && f.Player.AutoMovePausedForRotation,
                    steps = 3, detail = "true -> false for one 10 ms step -> true must not introduce an automatic X pulse." };
            }
        }

        private static Result ResumeAfterDelay()
        {
            using (var f = new Fixture(true, 1f))
            {
                f.Step(true);
                bool stayedPausedBeforeThreshold = true;
                for (int i = 0; i < 14; ++i)
                {
                    f.Step(false);
                    stayedPausedBeforeThreshold &= f.Player.AutoMovePausedForRotation &&
                        Mathf.Abs(f.Player.AutoMoveVelocityContribution) < .0001f;
                }
                f.Step(false);
                bool resumed = !f.Player.AutoMovePausedForRotation &&
                    Mathf.Abs(f.Player.AutoMoveVelocityContribution - f.Player.autoMoveSpeed) < .0001f;
                return new Result { passed = stayedPausedBeforeThreshold && resumed, steps = 16,
                    resumeAfterQuietSeconds = 15 * Dt,
                    detail = "No contribution during the first 0.14 s of continuous false; the next step reaches 0.15 s and restores configured autoMoveSpeed." };
            }
        }

        private static Result QuietTimeResets()
        {
            using (var f = new Fixture(true, 1f))
            {
                f.Step(true);
                for (int i = 0; i < 10; ++i) f.Step(false);
                f.Step(true);
                for (int i = 0; i < 10; ++i) f.Step(false);
                bool reset = f.Player.AutoMovePausedForRotation && Mathf.Abs(f.Player.AutoMoveVelocityContribution) < .0001f;
                for (int i = 0; i < 5; ++i) f.Step(false);
                return new Result { passed = reset && !f.Player.AutoMovePausedForRotation &&
                    Mathf.Abs(f.Player.AutoMoveVelocityContribution - f.Player.autoMoveSpeed) < .0001f,
                    steps = 27, resumeAfterQuietSeconds = 15 * Dt,
                    detail = "Two interrupted 0.10 s false periods cannot be added together; only the uninterrupted last 0.15 s resumes input." };
            }
        }

        private static Result DisableSwitch(bool antiPush)
        {
            using (var f = new Fixture(antiPush, 1f))
            {
                f.Step(true);
                bool wasPaused = f.Player.AutoMovePausedForRotation;
                f.Player.pauseAutoMoveWhileRotating = false;
                f.Step(true);
                return new Result { passed = wasPaused && !f.Player.AutoMovePausedForRotation &&
                    Mathf.Abs(f.Player.AutoMoveVelocityContribution - f.Player.autoMoveSpeed) < .0001f &&
                    Mathf.Abs(f.Body.linearVelocity.x - f.Player.autoMoveSpeed) < .002f,
                    steps = 2, detail = "Disabling the option restores legacy automatic X on the next physics step even while IsRotating remains true." };
            }
        }

        private static Result CollisionWhilePaused()
        {
            using (var f = new Fixture(true, 0f))
            {
                var wallObject = new GameObject("Real moving-wall impulse");
                wallObject.layer = 6;
                wallObject.transform.position = new Vector3(-.86f, 0f, 0f);
                var wallBody = wallObject.AddComponent<Rigidbody2D>();
                wallBody.bodyType = RigidbodyType2D.Kinematic;
                wallBody.useFullKinematicContacts = true;
                wallBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var wall = wallObject.AddComponent<BoxCollider2D>();
                wall.size = new Vector2(1f, 3f);
                Physics2D.SyncTransforms();
                Rotating.SetValue(f.World, true);
                f.Player.BeginPhysicsStep(Dt);
                wallBody.MovePosition(new Vector2(-.76f, 0f));
                if (!f.Physics.Simulate(Dt)) throw new InvalidOperationException("Local physics did not advance.");
                float rawSpeed = f.Body.linearVelocity.magnitude;
                f.Player.CompletePhysicsStep(Dt);
                var distance = f.Collider.Distance(wall);
                var result = new Result
                {
                    steps = 1, solverInjectedSpeed = rawSpeed, finalSpeed = f.Body.linearVelocity.magnitude,
                    correctionDistance = f.Guard.LastCorrectionDistance,
                    remainingPenetration = distance.isOverlapped ? -distance.distance : 0f
                };
                result.passed = f.Player.AutoMovePausedForRotation && Mathf.Abs(f.Player.AutoMoveVelocityContribution) < .0001f &&
                    rawSpeed > .1f && result.finalSpeed < .002f && result.correctionDistance > .005f &&
                    result.correctionDistance <= f.Guard.maxDepenetrationPerStep + .0001f && result.remainingPenetration < .0002f;
                result.detail = "The kinematic wall creates a real solver impulse while automatic input is paused. The player removes its injected velocity, and CrushGuard still performs bounded position repair.";
                return result;
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _previous;
            private readonly Scene _scene;
            public readonly PhysicsScene2D Physics;
            public PlayerController Player { get; private set; }
            public WorldRotator World { get; private set; }
            public Rigidbody2D Body { get; private set; }
            public BoxCollider2D Collider { get; private set; }
            public CrushGuard Guard { get; private set; }

            public Fixture(bool antiPush, float gravityScale)
            {
                _previous = SceneManager.GetActiveScene();
                _scene = SceneManager.CreateScene("AutoMoveRotationValidation_" + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                Physics = _scene.GetPhysicsScene2D();
                try
                {
                    SceneManager.SetActiveScene(_scene);
                    var worldObject = new GameObject("Fixture rotation source");
                    worldObject.transform.position = new Vector3(50f, 50f, 0f);
                    World = worldObject.AddComponent<WorldRotator>();
                    var playerObject = new GameObject("Fixture auto-moving player");
                    playerObject.layer = 3;
                    Body = playerObject.AddComponent<Rigidbody2D>();
                    Body.gravityScale = gravityScale;
                    Collider = playerObject.AddComponent<BoxCollider2D>();
                    Collider.size = new Vector2(.6f, .9f);
                    Player = playerObject.AddComponent<PlayerController>();
                    Player.autoMoveMode = true;
                    Player.autoMoveSpeed = 4.5f;
                    Player.autoMoveStartAngle = 0f;
                    Player.autoMoveRotationSource = World;
                    Player.pauseAutoMoveWhileRotating = true;
                    Player.autoMoveResumeDelay = .15f;
                    Player.antiPushEnabled = antiPush;
                    Player.rumbleEnabled = false;
                    Guard = playerObject.GetComponent<CrushGuard>();
                    Guard.playerCollider = Collider;
                    Guard.worldGeometryMask = 1 << 6;
                    Guard.crushFrameThreshold = 10000;
                    Player.BeginGameplay();
                    Physics2D.SyncTransforms();
                }
                catch { Dispose(); throw; }
            }

            public void Step(bool rotating)
            {
                Rotating.SetValue(World, rotating);
                Player.BeginPhysicsStep(Dt);
                if (!Physics.Simulate(Dt)) throw new InvalidOperationException("Local physics did not advance.");
                Player.CompletePhysicsStep(Dt);
            }

            public void Dispose()
            {
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                if (!_scene.IsValid() || !_scene.isLoaded) return;
                foreach (GameObject root in _scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(_scene);
            }
        }
    }
}
