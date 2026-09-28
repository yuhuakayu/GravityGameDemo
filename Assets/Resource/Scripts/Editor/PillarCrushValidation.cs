using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Editor
{
    /// <summary>Runs only when explicitly requested, in an isolated 2D physics scene.</summary>
    public static class PillarCrushValidation
    {
        public const string ReportPath = "Docs/PillarCrush/Validation.json";
        private const float Dt = 1f / 60f;

        [Serializable]
        public sealed class Sample
        {
            public int frame;
            public Vector2 beforeVelocity, intendedVelocity, submittedVelocity, solverVelocity, finalVelocity;
            public Vector2 beforePosition, solverPosition, finalPosition, correction;
            public float solverVelocityDelta, cleanVelocityDelta, contactVelocityChange;
            public float correctionDistance, positionBeyondOwnMotion, unexpectedSpeedGain;
            public int overlaps;
        }

        [Serializable]
        public sealed class Result
        {
            public string name;
            public bool passed;
            public string detail;
            public float maxSolverVelocityDelta, maxCleanVelocityDelta, maxContactVelocityChange;
            public float maxCorrectionDistance, maxPositionBeyondOwnMotion, maxSpeed, maxUnexpectedSpeedGain, maxUpwardOrLateralSpeed;
            public float lateralDisplacement;
            public int contactFrames, wallContactFrames;
            public List<Sample> samples = new List<Sample>();
        }

        [Serializable]
        public sealed class Report
        {
            public string utc;
            public string unityVersion;
            public string method = "Isolated PhysicsScene2D; BeginPhysicsStep -> MoveRotation/MovePosition -> Simulate -> CompletePhysicsStep. Raw solver values sampled before cleanup.";
            public float timestep = Dt;
            public float sweepDegreesPerSecond = 180f;
            public bool passed;
            public List<Result> results = new List<Result>();
        }

        [MenuItem("Tools/Gravity Game/Physics/Run Pillar Crush Validation")]
        public static void RunMenu() { Debug.Log(RunAndSave()); }

        public static string RunAndSave()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Run isolated physics checks in Play mode; Unity requires Play mode for local PhysicsScene2D creation. The loop is synchronous and never simulates the main scene.");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            float previousTimeScale = Time.timeScale;
            try
            {
            // Preview may own a zero time scale. No PlayerLoop frame advances during this call.
            Time.timeScale = 1f;
            Run(report, "180 degree sweep / anti-push on", () => Sweep(true, false));
            Run(report, "180 degree sweep / anti-push off", () => Sweep(false, false));
            Run(report, "Sweep against wall corner", () => Sweep(true, true));
            Run(report, "Standing on rotating pillar / zero input and gravity", RotatingTop);
            Run(report, "180 degree first impact on stationary player / normal gravity", StationaryImpact);
            Run(report, "Moving platform velocity is not inherited", TranslatingTop);
            Run(report, "Left overlap correction sign and whole-step budget", () => Correction(-1f));
            Run(report, "Right overlap correction sign and whole-step budget", () => Correction(1f));
            Run(report, "Opposed contacts enter bounded upward escape after ten steps", CrushEscape);
            Run(report, "IgnoreCollision pairs are restored when protection is disabled", IgnorePolicy);
            Run(report, "Free-fall integrates gravity once", Gravity);
            Run(report, "Static floor preserves diagonal motion up to first impact", StaticLanding);
            Run(report, "Static wall preserves approach up to first impact", StaticWall);
            Run(report, "Static slope walking without hovering", StaticSlope);
            }
            finally { Time.timeScale = previousTimeScale; }
            report.passed = report.results.TrueForAll(r => r.passed);
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, json);
            return json;
        }

        private static void Run(Report report, string name, Func<Result> action)
        {
            try
            {
                var result = action();
                result.name = name;
                report.results.Add(result);
            }
            catch (Exception ex)
            {
                report.results.Add(new Result { name = name, passed = false, detail = ex.ToString() });
            }
        }

        private static Result Sweep(bool antiPush, bool corner)
        {
            using (var f = new Fixture(new Vector2(2.8f, .46f), antiPush, 1f))
            {
                f.Box("Floor", new Vector2(0f, -.5f), new Vector2(20f, 1f));
                if (corner) f.TrackedWall = f.Box("Wall", new Vector2(3.35f, 2f), new Vector2(.5f, 4f));
                var pillar = f.Box("Pillar", Vector2.zero, new Vector2(8f, .55f), true);
                pillar.attachedRigidbody.rotation = -25f;
                var rotator = pillar.gameObject.AddComponent<Rotator>();
                rotator.degreesPerSecond = 180f;
                var result = new Result();
                for (int i = 0; i < 120; ++i) Step(f, result, i, () => rotator.Step(Dt));
                if (antiPush)
                {
                    result.passed = result.contactFrames > 0 && result.maxSolverVelocityDelta > .25f &&
                        result.maxCleanVelocityDelta < .002f && result.maxUnexpectedSpeedGain < .002f && result.maxCorrectionDistance <= .1501f &&
                        result.maxPositionBeyondOwnMotion <= .151f && (!corner || result.wallContactFrames > 0);
                    result.detail = "Must see actual solver injection, remove it after Simulate, and bound the position correction across all iterations. Clean delta compares final velocity with the controller-owned velocity after contact-normal blocking; contact-before/after delta is separately recorded.";
                }
                else
                {
                    result.passed = result.contactFrames > 0 && result.maxSolverVelocityDelta > 1f && result.maxUpwardOrLateralSpeed > 1f;
                    result.detail = "Disabled protection must retain at least 1 unit/s of upward or lateral speed from contact while input is zero. Downward free-fall alone cannot pass this reproduction check.";
                }
                return result;
            }
        }

        private static Result RotatingTop()
        {
            using (var f = new Fixture(new Vector2(2.5f, .73f), true, 0f))
            {
                var pillar = f.Box("Rotating top", Vector2.zero, new Vector2(8f, .55f), true);
                var rotator = pillar.gameObject.AddComponent<Rotator>();
                rotator.degreesPerSecond = 180f;
                var result = new Result();
                for (int i = 0; i < 60; ++i) Step(f, result, i, () => rotator.Step(Dt));
                result.passed = result.contactFrames > 0 && result.maxSpeed < .002f && result.maxCorrectionDistance <= .1501f;
                result.detail = "Gravity is disabled only on this fixture to isolate inherited angular/tangential velocity. Expected final speed is zero, although bounded positional separation is allowed.";
                return result;
            }
        }

        private static Result TranslatingTop()
        {
            using (var f = new Fixture(new Vector2(0f, .71f), true, 1f))
            {
                var platform = f.Box("Translating top", Vector2.zero, new Vector2(8f, .5f), true).attachedRigidbody;
                var result = new Result();
                for (int i = 0; i < 60; ++i)
                    Step(f, result, i, () => platform.MovePosition(platform.position + Vector2.right * .04f));
                result.lateralDisplacement = Mathf.Abs(f.Body.position.x);
                result.passed = result.contactFrames > 0 && result.maxCleanVelocityDelta < .002f && result.lateralDisplacement < .02f;
                result.detail = "A 2.4 unit/s translating platform must not carry the player horizontally; gravity and contact-normal blocking remain active.";
                return result;
            }
        }

        private static Result StationaryImpact()
        {
            using (var f = new Fixture(new Vector2(2.5f, .73f), true, 1f))
            {
                var pillar = f.Box("Rotating top", Vector2.zero, new Vector2(8f, .55f), true);
                var rotator = pillar.gameObject.AddComponent<Rotator>();
                rotator.degreesPerSecond = 180f;
                var result = new Result();
                Step(f, result, 0, () => rotator.Step(Dt));
                float gravityContribution = Physics2D.gravity.magnitude * Dt;
                result.passed = result.contactFrames > 0 && result.maxSolverVelocityDelta > 1f &&
                    result.maxContactVelocityChange <= gravityContribution + .0001f;
                result.detail = "A stationary player, with normal gravity, receives an actual 180-degree/s sweep contact. Full before/after velocity-vector change=" +
                    result.maxContactVelocityChange + ", gravity allowance=" + gravityContribution + ", raw solver change=" + result.maxSolverVelocityDelta;
                return result;
            }
        }

        private static Result Correction(float side)
        {
            using (var f = new Fixture(Vector2.zero, true, 0f))
            {
                var obstacle = f.Box("Overlap", new Vector2(side * .55f, 0f), new Vector2(1f, 3f));
                Physics2D.SyncTransforms();
                f.Player.BeginPhysicsStep(Dt);
                f.Body.linearVelocity = new Vector2(2f, -3f);
                var before = f.Body.position;
                var initialDistance = f.Collider.Distance(obstacle);
                f.Guard.ResolveAfterPhysics(Dt);
                var finalDistance = f.Collider.Distance(obstacle);
                var displacement = f.Body.position - before;
                bool direction = displacement.x * side < -.01f;
                bool unchangedVelocity = (f.Body.linearVelocity - new Vector2(2f, -3f)).sqrMagnitude < .000001f;
                return new Result
                {
                    passed = initialDistance.isOverlapped && direction && unchangedVelocity &&
                        finalDistance.distance > initialDistance.distance && f.Guard.LastCorrectionDistance <= .1501f,
                    maxCorrectionDistance = f.Guard.LastCorrectionDistance,
                    detail = "Initial distance=" + initialDistance.distance + ", final distance=" + finalDistance.distance +
                        ", normal=" + initialDistance.normal + ", position correction=" + displacement +
                        ", velocity unchanged=" + unchangedVelocity + ". Distance query must immediately observe the Rigidbody2D.position update."
                };
            }
        }

        private static Result Gravity()
        {
            using (var f = new Fixture(new Vector2(0f, 100f), true, 1f))
            {
                var result = new Result();
                for (int i = 0; i < 60; ++i) Step(f, result, i, null);
                var expected = Physics2D.gravity;
                float error = (f.Body.linearVelocity - expected).magnitude;
                result.passed = error < .002f && result.maxCorrectionDistance < .00001f;
                result.detail = "After 60 x 1/60 seconds, expected=" + expected + ", actual=" + f.Body.linearVelocity + ", error=" + error;
                return result;
            }
        }

        private static Result CrushEscape()
        {
            using (var f = PinchedFixture(CrushResponse.PushAlongGravity))
            {
                for (int i = 0; i < 10; ++i) f.Guard.ResolveAfterPhysics(Dt);
                bool detected = f.Guard.IsCrushed;
                int count = f.Guard.CrushFrames;
                Vector2 before = f.Body.position;
                f.Guard.ResolveAfterPhysics(Dt);
                Vector2 escape = f.Body.position - before;
                return new Result
                {
                    passed = detected && count == 10 && escape.y > .149f && Mathf.Abs(escape.x) < .0001f &&
                        f.Guard.LastCorrectionDistance <= .1501f && f.Body.linearVelocity.sqrMagnitude < .000001f,
                    maxCorrectionDistance = f.Guard.LastCorrectionDistance,
                    detail = "Opposite walls continuously overlap, correction alternates direction. Detected=" + detected +
                        ", frame=" + count + ", next-step escape=" + escape + ". Death/restart after another ten blocked steps requires the Play-mode scene check."
                };
            }
        }

        private static Result StaticLanding()
        {
            using (var f = new Fixture(new Vector2(0f, .5f), true, 1f))
            {
                f.Box("Static floor", new Vector2(0f, -.5f), new Vector2(20f, 1f));
                f.Player.autoMoveMode = true;
                f.Player.autoMoveSpeed = 2f;
                f.Player.SetIntendedVelocity(new Vector2(2f, -4f));
                var result = new Result();
                Step(f, result, 0, null);
                var after = f.Body.position;
                result.passed = after.y < .49f && after.y >= .435f && after.x > .01f && after.x < .04f;
                result.detail = "At diagonal landing, legitimate motion before impact must remain. Start=(0,.5), velocity=(2,-4), final=" + after + ". Do not project away the entire pre-impact descent.";
                return result;
            }
        }

        private static Result StaticWall()
        {
            using (var f = new Fixture(new Vector2(.65f, 1f), true, 0f))
            {
                f.Box("Static wall", new Vector2(1.5f, 1f), new Vector2(1f, 8f));
                f.Player.autoMoveMode = true;
                f.Player.autoMoveSpeed = 6f;
                f.Player.SetIntendedVelocity(new Vector2(6f, 2f));
                var result = new Result();
                Step(f, result, 0, null);
                var after = f.Body.position;
                result.passed = after.x > .675f && after.x <= .706f && after.y > 1.01f;
                result.detail = "At diagonal wall impact, preserve the approach to wall and legal upward sliding. Start=(.65,1), velocity=(6,2), final=" + after;
                return result;
            }
        }

        private static Result StaticSlope()
        {
            using (var f = new Fixture(new Vector2(0f, 1f), true, 1f))
            {
                var slope = f.Box("Static slope", new Vector2(0f, -.5f), new Vector2(12f, 1f));
                slope.transform.rotation = Quaternion.Euler(0f, 0f, 15f);
                f.Player.autoMoveMode = true;
                f.Player.autoMoveSpeed = 2f;
                Physics2D.SyncTransforms();
                var result = new Result();
                for (int i = 0; i < 120; ++i) Step(f, result, i, null);
                float gap = f.Collider.Distance(slope).distance;
                result.passed = gap > -.02f && gap < .04f && f.Body.position.x > 2f && result.maxCleanVelocityDelta < .002f;
                result.detail = "Walking at 2 units/s onto a 15-degree static slope must keep normal contact and make forward progress, without an artificial hover gap. Final signed gap=" + gap + ", position=" + f.Body.position;
                return result;
            }
        }

        private static Result IgnorePolicy()
        {
            using (var f = PinchedFixture(CrushResponse.IgnoreCollision))
            {
                var walls = new List<Collider2D>();
                foreach (var go in f.Scene.GetRootGameObjects())
                {
                    var collider = go.GetComponent<Collider2D>();
                    if (collider != null && collider != f.Collider) walls.Add(collider);
                }
                for (int i = 0; i < 10; ++i) f.Guard.ResolveAfterPhysics(Dt);
                int ignored = f.Guard.IgnoredCollisionCount;
                bool pairsIgnored = walls.TrueForAll(w => Physics2D.GetIgnoreCollision(f.Collider, w));
                f.Player.antiPushEnabled = false;
                f.Guard.ResolveAfterPhysics(Dt);
                bool pairsRestored = walls.TrueForAll(w => !Physics2D.GetIgnoreCollision(f.Collider, w));
                return new Result
                {
                    passed = ignored == 2 && pairsIgnored && pairsRestored && f.Guard.IgnoredCollisionCount == 0,
                    detail = "Ignored pairs=" + ignored + ", actual pair flags=" + pairsIgnored + ", restored when toggled off=" + pairsRestored
                };
            }
        }

        private static Fixture PinchedFixture(CrushResponse response)
        {
            var f = new Fixture(Vector2.zero, true, 0f);
            f.Box("Left squeeze", new Vector2(-.55f, 0f), new Vector2(1f, 4f));
            f.Box("Right squeeze", new Vector2(.55f, 0f), new Vector2(1f, 4f));
            f.Guard.crushFrameThreshold = 10;
            f.Guard.crushResponse = response;
            Physics2D.SyncTransforms();
            f.Player.BeginPhysicsStep(Dt);
            return f;
        }

        private static void Step(Fixture f, Result result, int frame, Action move)
        {
            var sample = new Sample { frame = frame, beforePosition = f.Body.position, beforeVelocity = f.Body.linearVelocity };
            f.Player.BeginPhysicsStep(Dt);
            sample.intendedVelocity = f.Player.IntendedVelocity;
            sample.submittedVelocity = f.Body.linearVelocity;
            if (move != null) move();
            f.Physics.Simulate(Dt);
            sample.solverPosition = f.Body.position;
            sample.solverVelocity = f.Body.linearVelocity;
            if (f.Collider.IsTouchingLayers(1 << 6)) result.contactFrames++;
            if (f.TrackedWall != null && f.Collider.IsTouching(f.TrackedWall)) result.wallContactFrames++;
            f.Player.CompletePhysicsStep(Dt);
            sample.solverVelocityDelta = f.Player.LastPhysicsVelocityDelta.magnitude;
            sample.finalPosition = f.Body.position;
            sample.finalVelocity = f.Body.linearVelocity;
            sample.correction = f.Guard.LastCorrection;
            sample.correctionDistance = f.Guard.LastCorrectionDistance;
            sample.overlaps = f.Guard.RemainingOverlapCount;
            sample.cleanVelocityDelta = f.Player.antiPushEnabled
                ? (sample.finalVelocity - f.Player.IntendedVelocity).magnitude
                : sample.solverVelocityDelta;
            sample.contactVelocityChange = (sample.finalVelocity - sample.beforeVelocity).magnitude;
            sample.positionBeyondOwnMotion = Mathf.Max(0f, (sample.finalPosition - sample.beforePosition).magnitude - sample.submittedVelocity.magnitude * Dt);
            sample.unexpectedSpeedGain = Mathf.Max(0f, sample.finalVelocity.magnitude - sample.beforeVelocity.magnitude - Physics2D.gravity.magnitude * f.Player.SimulatedGravityScale * Dt);
            result.maxSolverVelocityDelta = Mathf.Max(result.maxSolverVelocityDelta, sample.solverVelocityDelta);
            result.maxCleanVelocityDelta = Mathf.Max(result.maxCleanVelocityDelta, sample.cleanVelocityDelta);
            result.maxContactVelocityChange = Mathf.Max(result.maxContactVelocityChange, sample.contactVelocityChange);
            result.maxCorrectionDistance = Mathf.Max(result.maxCorrectionDistance, sample.correctionDistance);
            result.maxPositionBeyondOwnMotion = Mathf.Max(result.maxPositionBeyondOwnMotion, sample.positionBeyondOwnMotion);
            result.maxSpeed = Mathf.Max(result.maxSpeed, sample.finalVelocity.magnitude);
            result.maxUnexpectedSpeedGain = Mathf.Max(result.maxUnexpectedSpeedGain, sample.unexpectedSpeedGain);
            result.maxUpwardOrLateralSpeed = Mathf.Max(result.maxUpwardOrLateralSpeed, Mathf.Abs(sample.finalVelocity.x), sample.finalVelocity.y);
            result.samples.Add(sample);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly PhysicsScene2D Physics;
            public readonly Rigidbody2D Body;
            public readonly BoxCollider2D Collider;
            public readonly PlayerController Player;
            public readonly CrushGuard Guard;
            public Collider2D TrackedWall;
            private readonly Scene _previous;

            public Fixture(Vector2 position, bool antiPush, float gravityScale)
            {
                _previous = SceneManager.GetActiveScene();
                Scene = SceneManager.CreateScene("PillarCrushValidation_" + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                SceneManager.SetActiveScene(Scene);
                Physics = Scene.GetPhysicsScene2D();
                var go = new GameObject("Validation Player");
                go.layer = 3;
                go.transform.position = position;
                Body = go.AddComponent<Rigidbody2D>();
                Body.gravityScale = gravityScale;
                Body.constraints = RigidbodyConstraints2D.FreezeRotation;
                Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                Body.interpolation = RigidbodyInterpolation2D.Interpolate;
                Collider = go.AddComponent<BoxCollider2D>();
                Collider.size = new Vector2(.6f, .9f);
                Player = go.AddComponent<PlayerController>();
                Player.maxMoveSpeed = 0f;
                Player.autoMoveMode = true;
                Player.autoMoveSpeed = 0f;
                Player.rumbleEnabled = false;
                Player.antiPushEnabled = antiPush;
                Player.logVelocityDelta = false;
                Guard = go.GetComponent<CrushGuard>();
                if (Guard == null) Guard = go.AddComponent<CrushGuard>();
                Guard.playerCollider = Collider;
                Guard.worldGeometryMask = 1 << 6;
                Guard.crushFrameThreshold = 10000; // Individual physics cases do not launch the scene-death coroutine.
                Player.BeginGameplay();
            }

            public BoxCollider2D Box(string name, Vector2 position, Vector2 size, bool kinematic = false)
            {
                var go = new GameObject(name);
                go.layer = 6;
                go.transform.position = position;
                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = size;
                if (kinematic)
                {
                    var body = go.AddComponent<Rigidbody2D>();
                    body.bodyType = RigidbodyType2D.Kinematic;
                    body.useFullKinematicContacts = true;
                    body.interpolation = RigidbodyInterpolation2D.Interpolate;
                }
                return collider;
            }

            public void Dispose()
            {
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                if (Scene.IsValid() && Scene.isLoaded)
                {
                    // Destroy now so no deferred Start, audio, or gameplay setup can run on fixtures.
                    foreach (var root in Scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
                    SceneManager.UnloadSceneAsync(Scene);
                }
            }
        }
    }
}
