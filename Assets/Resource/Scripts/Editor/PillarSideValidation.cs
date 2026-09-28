using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Editor
{
    /// <summary>Explicit original-side regression checks in isolated physics scenes.</summary>
    public static class PillarSideValidation
    {
        public const string ReportPath = "Docs/PillarCrush/SideValidation.json";
        private const float Dt = 1f / 60f;

        [Serializable]
        public sealed class Result
        {
            public string name;
            public bool passed;
            public string detail;
            public Vector2 oldShortestCorrection, originalSideNormal, firstCorrection, finalPosition;
            public float firstBudgetUsed, maximumBudgetUsed, finalGap;
            public bool velocityUnchanged;
        }

        [Serializable]
        public sealed class Report
        {
            public string utc;
            public bool passed;
            public List<Result> results = new List<Result>();
        }

        [MenuItem("Tools/Gravity Game/Physics/Validate Pillar Original Side")]
        public static void RunMenu() { Debug.Log(RunAndSave()); }

        public static string RunAndSave()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run in Play mode for isolated PhysicsScene2D checks.");
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            float oldTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 1f;
                Run(report, "Thin wall crosses centre: preserve left entry side", () => CrossCentre(-1f));
                Run(report, "Thin wall crosses centre: preserve right entry side", () => CrossCentre(1f));
                Run(report, "Rotating thin platform preserves capsule top-side contact", () => RotatingPlatform(false));
                Run(report, "Rotating scaled platform transforms original normal correctly", () => RotatingPlatform(true));
            }
            finally { Time.timeScale = oldTimeScale; }
            report.passed = report.results.TrueForAll(r => r.passed);
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, json);
            return json;
        }

        private static void Run(Report report, string name, Func<Result> run)
        {
            try
            {
                Result result = run();
                result.name = name;
                report.results.Add(result);
            }
            catch (Exception exception)
            {
                report.results.Add(new Result { name = name, detail = exception.ToString() });
            }
        }

        private static Result CrossCentre(float side)
        {
            using (var fixture = new Fixture(new Vector2(side * .38f, 0f), false))
            {
                Rigidbody2D wall = fixture.Platform(new Vector2(.1f, 4f), Vector2.one, 0f);
                Collider2D geometry = wall.GetComponent<Collider2D>();
                ColliderDistance2D initialDistance = fixture.Collider.Distance(geometry);
                fixture.Guard.CaptureBeforePhysics(Dt);
                Vector2 originalPosition = fixture.Body.position;
                wall.MovePosition(new Vector2(side * .5f, 0f));
                fixture.Physics.Simulate(Dt);
                // Reproduce the controller rejecting geometry-injected position. This deliberately
                // exceeds the production motion budget to prove the fallback never switches sides.
                fixture.Body.position = originalPosition;
                ColliderDistance2D distance = fixture.Collider.Distance(geometry);
                Vector2 oldShortest = distance.normal * distance.distance;
                Vector2 point, normal;
                float beforeGap;
                bool captured = fixture.Guard.TryGetCapturedSide(geometry, out point, out normal, out beforeGap);
                fixture.Body.linearVelocity = new Vector2(2f, -3f);
                fixture.Guard.ResolveAfterPhysics(Dt);
                var result = new Result
                {
                    oldShortestCorrection = oldShortest,
                    originalSideNormal = normal,
                    firstCorrection = fixture.Guard.LastCorrection,
                    firstBudgetUsed = fixture.Guard.LastCorrectionDistance,
                    maximumBudgetUsed = fixture.Guard.LastCorrectionDistance
                };
                bool everyStepOriginalSide = fixture.Guard.LastCorrection.x * side > 0f;
                for (int step = 0; step < 6 && fixture.Collider.Distance(geometry).isOverlapped; ++step)
                {
                    fixture.Guard.CaptureBeforePhysics(Dt);
                    fixture.Guard.ResolveAfterPhysics(Dt);
                    everyStepOriginalSide &= fixture.Guard.LastCorrection.x * side >= -0.00001f;
                    result.maximumBudgetUsed = Mathf.Max(result.maximumBudgetUsed, fixture.Guard.LastCorrectionDistance);
                }
                result.finalPosition = fixture.Body.position;
                result.finalGap = fixture.Collider.Distance(geometry).distance;
                result.velocityUnchanged = (fixture.Body.linearVelocity - new Vector2(2f, -3f)).sqrMagnitude < 0.000001f;
                result.passed = captured && distance.isOverlapped && oldShortest.x * side < -.01f &&
                    result.firstCorrection.x * side > .01f && everyStepOriginalSide && result.maximumBudgetUsed <= .1501f &&
                    result.finalGap >= -.001f && (fixture.Body.position.x - wall.position.x) * side > .3f && result.velocityUnchanged;
                result.detail = "The original shortest-distance fallback exits the wrong face after the thin wall passes the player's centre. " +
                    "Entry-side recovery must persist across unresolved steps, never exceed 0.15 total per step, and leave velocity intact. " +
                    "Initial captured gap=" + beforeGap + ", pre-step raw normal=" + initialDistance.normal +
                    ", pre-step pointA-pointB=" + (initialDistance.pointA - initialDistance.pointB) +
                    ", all correction directions retain original side=" + everyStepOriginalSide;
                return result;
            }
        }

        private static Result RotatingPlatform(bool scaled)
        {
            float angle = scaled ? 30f : 0f;
            float delta = scaled ? 2f : 3f;
            Vector2 normal = Rotate(Vector2.up, angle);
            Vector2 tangent = Rotate(Vector2.right, angle);
            Vector2 scale = scaled ? new Vector2(1.6f, .5f) : Vector2.one;
            float thickness = scaled ? .2f : .15f;
            float support = .3f + .15f * Mathf.Abs(normal.y);
            Vector2 position = tangent * (scaled ? 2f : 2.5f) + normal * (thickness * scale.y * .5f + support + .025f);
            using (var fixture = new Fixture(position, true))
            {
                Rigidbody2D platform = fixture.Platform(new Vector2(8f, thickness), scale, angle);
                Collider2D geometry = platform.GetComponent<Collider2D>();
                ColliderDistance2D initialDistance = fixture.Collider.Distance(geometry);
                fixture.Guard.CaptureBeforePhysics(Dt);
                Vector2 initialPosition = fixture.Body.position;
                platform.MoveRotation(angle + delta);
                fixture.Physics.Simulate(Dt);
                fixture.Body.position = initialPosition;
                ColliderDistance2D before = fixture.Collider.Distance(geometry);
                Vector2 point, capturedNormal;
                float beforeGap;
                bool captured = fixture.Guard.TryGetCapturedSide(geometry, out point, out capturedNormal, out beforeGap);
                fixture.Body.linearVelocity = new Vector2(2f, -3f);
                fixture.Guard.ResolveAfterPhysics(Dt);
                float gap = fixture.Collider.Distance(geometry).distance;
                bool unchanged = (fixture.Body.linearVelocity - new Vector2(2f, -3f)).sqrMagnitude < .000001f;
                Vector2 expectedNormal = Rotate(Vector2.up, angle + delta);
                return new Result
                {
                    passed = captured && before.isOverlapped && Vector2.Dot(capturedNormal, expectedNormal) > .99999f &&
                        Vector2.Dot(fixture.Guard.LastCorrection, expectedNormal) > .001f &&
                        fixture.Guard.LastCorrectionDistance <= .1501f && gap >= -.001f && gap < .003f && unchanged,
                    oldShortestCorrection = before.normal * before.distance,
                    originalSideNormal = capturedNormal,
                    firstCorrection = fixture.Guard.LastCorrection,
                    firstBudgetUsed = fixture.Guard.LastCorrectionDistance,
                    maximumBudgetUsed = fixture.Guard.LastCorrectionDistance,
                    finalPosition = fixture.Body.position,
                    finalGap = gap,
                    velocityUnchanged = unchanged,
                    detail = "Capsule support is taken from native shape vertices and radii, not AABB; transformed entry plane must " +
                        "remain tangent after rotation and non-uniform scale. Expected normal=" + expectedNormal +
                        ", captured pre-step gap=" + beforeGap + ", required entry-side budget <= 0.15. " +
                        "Interpolated geometry physics angle=" + platform.rotation + ", render angle=" + platform.transform.eulerAngles.z +
                        ", pre-step raw normal=" + initialDistance.normal + ", pre-step pointA-pointB=" + (initialDistance.pointA - initialDistance.pointB)
                };
            }
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
            return new Vector2(vector.x * cosine - vector.y * sine, vector.x * sine + vector.y * cosine);
        }

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly PhysicsScene2D Physics;
            public readonly Rigidbody2D Body;
            public readonly Collider2D Collider;
            public readonly CrushGuard Guard;
            private readonly Scene _previous;

            public Fixture(Vector2 position, bool capsule)
            {
                _previous = SceneManager.GetActiveScene();
                Scene = SceneManager.CreateScene("PillarSideValidation_" + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                SceneManager.SetActiveScene(Scene);
                Physics = Scene.GetPhysicsScene2D();
                var go = new GameObject("Original side validation player");
                go.layer = 3;
                go.transform.position = position;
                Body = go.AddComponent<Rigidbody2D>();
                Body.gravityScale = 0f;
                if (capsule)
                {
                    var shape = go.AddComponent<CapsuleCollider2D>();
                    shape.size = new Vector2(.6f, .9f);
                    Collider = shape;
                }
                else
                {
                    var shape = go.AddComponent<BoxCollider2D>();
                    shape.size = new Vector2(.6f, .9f);
                    Collider = shape;
                }
                var player = go.AddComponent<PlayerController>();
                player.autoMoveMode = true;
                player.autoMoveSpeed = 0f;
                player.rumbleEnabled = false;
                player.logVelocityDelta = false;
                Guard = go.GetComponent<CrushGuard>();
                Guard.playerCollider = Collider;
                Guard.worldGeometryMask = 1 << 6;
                Guard.crushFrameThreshold = 10000;
                player.BeginGameplay();
            }

            public Rigidbody2D Platform(Vector2 size, Vector2 scale, float angle)
            {
                var go = new GameObject("Thin test geometry");
                go.layer = 6;
                go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
                go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = size;
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.useFullKinematicContacts = true;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                Physics2D.SyncTransforms();
                return body;
            }

            public void Dispose()
            {
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                if (!Scene.IsValid() || !Scene.isLoaded) return;
                foreach (GameObject root in Scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
