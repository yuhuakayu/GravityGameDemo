using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Resource.Scripts.Gyro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Editor
{
    /// <summary>Explicit, synchronous tests of WorldRotator in disposable local physics scenes.</summary>
    public static class WorldKinematicValidation
    {
        public const string ReportPath = "Docs/PillarCrush/WorldKinematicValidation.json";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo FixedStep = typeof(WorldRotator).GetMethod("FixedUpdate", PrivateInstance);
        private static readonly MethodInfo QueueDelta = typeof(WorldRotator).GetMethod("QueueClockwiseDelta", PrivateInstance);
        private static readonly FieldInfo InputPlayer = typeof(WorldRotationInput).GetField("_player", PrivateInstance);

        [Serializable]
        public sealed class Result
        {
            public string name;
            public bool passed;
            public string detail;
            public int steps;
            public float maxStepAngle;
            public float maxDegreesPerSecond;
            public float finalTargetError;
            public float maxRootPositionError;
            public float maxChildPositionError;
            public float maxChildAngleError;
            public float pendingBeforeBlock;
            public float pendingAfterBlock;
            public float movementAfterResume;
        }

        [Serializable]
        public sealed class Report
        {
            public string utc;
            public string unityVersion;
            public float timestep;
            public string method = "Play-mode isolated PhysicsScene2D. Invoke the production WorldRotator.FixedUpdate, then simulate only the local physics scene. Each request is a separate actual simulation step; no game PlayerLoop frame or fixture Start runs.";
            public bool passed;
            public List<Result> results = new List<Result>();
        }

        [MenuItem("Tools/Gravity Game/Physics/Run World Kinematic Validation")]
        public static void RunMenu() { Debug.Log(RunAndSave()); }

        public static string RunAndSave()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Run these isolated PhysicsScene2D checks in Play mode.");
            if (SceneTransition.Instance.IsTransitioning)
                throw new InvalidOperationException("Wait until the current scene transition finishes before validating world motion.");
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                timestep = Time.fixedDeltaTime
            };
            float previousTimeScale = Time.timeScale;
            // Recenter during pulse tests touches the shared mapper; restore all mutable mapper
            // fields afterwards. The synchronous call never consumes a native sensor sample.
            object mapper = GyroRuntime.Current != null ? GyroRuntime.Current.Processor.AngleMapper : null;
            var mapperSnapshot = new Dictionary<FieldInfo, object>();
            if (mapper != null)
                foreach (FieldInfo field in mapper.GetType().GetFields(PrivateInstance | BindingFlags.Public))
                    if (!field.IsInitOnly) mapperSnapshot.Add(field, field.GetValue(mapper));
            try
            {
                Time.timeScale = 1f;
                Run(report, "Default root motion stays within 120 degrees per second", () => AngularLimit(120f, 3f));
                Run(report, "Large target is consumed in actual steps of at most three degrees", () => AngularLimit(10000f, 3f));
                Run(report, "Latest absolute target replaces the previous target", LatestTarget);
                Run(report, "Rate input backlog is bounded", Backlog);
                Run(report, "Rate input expires after release even when geometry slows rotation", ExpiredBacklog);
                Run(report, "Configured composite / identity pose", () => CompositePose(Vector2.zero, 0f, Vector2.one, true));
                Run(report, "Configured composite / translated rotated nonuniform root", () => CompositePose(new Vector2(4f, -2f), 33f, new Vector2(1.8f, .7f), true));
                Run(report, "Legacy Static composite is automatically made Kinematic", () => CompositePose(new Vector2(-3f, 2f), -41f, new Vector2(.8f, 1.4f), false));
                Run(report, "Pause clears pending movement without resume catch-up", () => BlockAndResume(false));
                Run(report, "Solar pulse clears pending movement without resume catch-up", () => BlockAndResume(true));
                Run(report, "Preview and disable clear pending movement", PreviewAndDisable);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                foreach (var entry in mapperSnapshot) entry.Key.SetValue(mapper, entry.Value);
            }
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
            {
                report.results.Add(new Result { name = name, passed = false, detail = ex.ToString() });
            }
        }

        private static Result AngularLimit(float maxSpeed, float maxStep)
        {
            using (var f = new Fixture(Vector2.zero, 0f, Vector2.one, true))
            {
                f.World.maxAngularSpeed = maxSpeed;
                f.World.maxStepAngle = maxStep;
                float initialAngle = f.World.ClockwiseAngleReadout;
                float target = initialAngle + 90f;
                f.World.SetTargetAngle(target);
                bool deferred = Mathf.Abs(f.World.ClockwiseAngleReadout - initialAngle) < .0001f;
                var result = new Result();
                float expectedStep = Mathf.Min(maxSpeed * Time.fixedDeltaTime, maxStep);
                int count = Mathf.CeilToInt(90f / expectedStep) + 3;
                for (int i = 0; i < count; ++i) TrackStep(f, result);
                result.finalTargetError = Mathf.Abs(f.World.ClockwiseAngleReadout - target);
                result.passed = deferred && result.finalTargetError < .002f &&
                    result.maxStepAngle <= expectedStep + .002f && result.maxStepAngle >= expectedStep - .002f &&
                    result.maxDegreesPerSecond <= maxSpeed + .15f &&
                    f.Body.bodyType == RigidbodyType2D.Kinematic && f.Body.useFullKinematicContacts &&
                    f.Body.interpolation == RigidbodyInterpolation2D.Interpolate;
                result.detail = "SetTargetAngle must defer movement; each sample follows a real local simulation. Expected saturated step=" + expectedStep;
                return result;
            }
        }

        private static Result LatestTarget()
        {
            using (var f = new Fixture(Vector2.zero, 0f, Vector2.one, true))
            {
                float initial = f.World.ClockwiseAngleReadout;
                f.World.SetTargetAngle(initial + 90f);
                f.World.SetTargetAngle(initial - 30f);
                f.Step();
                float first = f.World.ClockwiseAngleReadout;
                for (int i = 0; i < 30; ++i) f.Step();
                float error = Mathf.Abs(f.World.ClockwiseAngleReadout - (initial - 30f));
                return new Result { passed = first < initial && error < .002f, finalTargetError = error,
                    detail = "A new angle-mode target reverses immediately instead of appending an old target to a queue." };
            }
        }

        private static Result Backlog()
        {
            using (var f = new Fixture(Vector2.zero, 0f, Vector2.one, true))
            {
                for (int i = 0; i < 100; ++i) QueueDelta.Invoke(f.World, new object[] { 90f });
                float pending = Mathf.Abs(f.World.TargetClockwiseAngleReadout - f.World.ClockwiseAngleReadout);
                float allowed = Mathf.Min(f.World.maxAngularSpeed, f.World.maxStepAngle / Time.fixedDeltaTime) * f.World.maxInputLagSeconds;
                return new Result { passed = pending > 0f && pending <= allowed + .001f,
                    pendingBeforeBlock = pending, detail = "100 queued rate-input bursts remain within the configured input-lag budget of " + allowed + " degrees." };
            }
        }

        private static Result ExpiredBacklog()
        {
            using (var f = new Fixture(Vector2.zero, 0f, Vector2.one, true))
            {
                QueueDelta.Invoke(f.World, new object[] { 30f });
                float before = f.World.TargetClockwiseAngleReadout - f.World.ClockwiseAngleReadout;
                typeof(WorldRotator).GetField("_lastIncrementalInputTime", PrivateInstance).SetValue(f.World,
                    Time.realtimeSinceStartupAsDouble - f.World.maxInputLagSeconds - .01);
                float initial = f.World.ClockwiseAngleReadout;
                f.Step();
                float staleMovement = Mathf.Abs(f.World.ClockwiseAngleReadout - initial);
                float pending = Mathf.Abs(f.World.TargetClockwiseAngleReadout - f.World.ClockwiseAngleReadout);
                f.World.SetTargetAngle(initial + 10f);
                f.Step();
                return new Result
                {
                    passed = before > 0f && staleMovement < .001f && pending < .001f &&
                        f.World.ClockwiseAngleReadout > initial,
                    pendingBeforeBlock = before, pendingAfterBlock = pending, movementAfterResume = staleMovement,
                    detail = "Age the last nonzero rate sample beyond its configured lifetime. Stale rate input must stop; a new absolute target must still move."
                };
            }
        }

        private static Result CompositePose(Vector2 rootPosition, float rootAngle, Vector2 scale, bool configured)
        {
            using (var f = new Fixture(rootPosition, rootAngle, scale, configured))
            {
                var result = new Result();
                Vector2 rootFromPivot = f.Body.position - f.PivotPosition;
                float initialRootAngle = f.Body.rotation;
                Vector2 childOffset = Quaternion.Euler(0f, 0f, -initialRootAngle) * (Vector3)(f.Child.position - f.Body.position);
                float childRelativeAngle = Mathf.DeltaAngle(initialRootAngle, f.Child.rotation);
                f.World.SetTargetAngle(f.World.ClockwiseAngleReadout + 90f);
                int count = Mathf.CeilToInt(90f / Mathf.Min(120f * Time.fixedDeltaTime, 3f)) + 4;
                for (int i = 0; i < count; ++i)
                {
                    TrackStep(f, result);
                    float moved = Mathf.DeltaAngle(initialRootAngle, f.Body.rotation);
                    Vector2 expectedRoot = f.PivotPosition + (Vector2)(Quaternion.Euler(0f, 0f, moved) * (Vector3)rootFromPivot);
                    Vector2 expectedChild = f.Body.position + (Vector2)(Quaternion.Euler(0f, 0f, f.Body.rotation) * (Vector3)childOffset);
                    result.maxRootPositionError = Mathf.Max(result.maxRootPositionError, Vector2.Distance(expectedRoot, f.Body.position));
                    result.maxChildPositionError = Mathf.Max(result.maxChildPositionError, Vector2.Distance(expectedChild, f.Child.position));
                    result.maxChildAngleError = Mathf.Max(result.maxChildAngleError,
                        Mathf.Abs(Mathf.DeltaAngle(f.Body.rotation + childRelativeAngle, f.Child.rotation)));
                }
                result.passed = result.maxRootPositionError < .003f && result.maxChildPositionError < .003f &&
                    result.maxChildAngleError < .003f && f.Composite.pathCount > 0 &&
                    f.Child.bodyType == RigidbodyType2D.Kinematic && f.Child.useFullKinematicContacts &&
                    f.Child.interpolation == RigidbodyInterpolation2D.Interpolate;
                result.detail = "Composite retains generated geometry and follows an absolute root-relative body pose. Root scale=" + scale + "; explicitly configured=" + configured;
                return result;
            }
        }

        private static Result BlockAndResume(bool pulse)
        {
            using (var f = new Fixture(new Vector2(2f, -1f), 19f, Vector2.one, true))
            {
                f.World.SetTargetAngle(f.World.ClockwiseAngleReadout + 90f);
                f.Step();
                var result = new Result { pendingBeforeBlock = Mathf.Abs(f.World.TargetClockwiseAngleReadout - f.World.ClockwiseAngleReadout) };
                // The production input entry point changes the pulse gate and recenters; FixedUpdate
                // must also discard movement, even if no render Update occurs while it is blocked.
                if (pulse) f.World.RotationInput.SetSolarPulseActive(true);
                else Time.timeScale = 0f;
                f.Step();
                float blockedAngle = f.World.ClockwiseAngleReadout;
                Vector2 blockedChildPosition = f.Child.position;
                result.pendingAfterBlock = Mathf.Abs(f.World.TargetClockwiseAngleReadout - blockedAngle);
                if (pulse) f.World.RotationInput.SetSolarPulseActive(false);
                else Time.timeScale = 1f;
                for (int i = 0; i < 5; ++i) f.Step();
                result.movementAfterResume = Mathf.Abs(f.World.ClockwiseAngleReadout - blockedAngle);
                result.passed = result.pendingBeforeBlock > 30f && result.pendingAfterBlock < .001f &&
                    result.movementAfterResume < .001f && Vector2.Distance(f.Child.position, blockedChildPosition) < .001f;
                result.detail = "Pending root and attached Composite movement must be discarded rather than replayed after the gate opens.";
                return result;
            }
        }

        private static Result PreviewAndDisable()
        {
            using (var f = new Fixture(Vector2.zero, 0f, Vector2.one, true))
            {
                f.World.SetTargetAngle(90f);
                f.Player.EnterPreview();
                f.Step();
                bool previewCleared = Mathf.Abs(f.World.TargetClockwiseAngleReadout - f.World.ClockwiseAngleReadout) < .001f;
                f.Player.BeginGameplay();
                f.World.SetTargetAngle(90f);
                f.World.enabled = false;
                bool disabledCleared = Mathf.Abs(f.World.TargetClockwiseAngleReadout - f.World.ClockwiseAngleReadout) < .001f;
                f.World.enabled = true;
                float before = f.World.ClockwiseAngleReadout;
                f.Step();
                return new Result { passed = previewCleared && disabledCleared && Mathf.Abs(f.World.ClockwiseAngleReadout - before) < .001f,
                    detail = "Preview and component disable both clear queued targets; enabling does not resume stale movement." };
            }
        }

        private static void TrackStep(Fixture f, Result result)
        {
            float before = f.Body.rotation;
            f.Step();
            float delta = Mathf.Abs(Mathf.DeltaAngle(before, f.Body.rotation));
            result.maxStepAngle = Mathf.Max(result.maxStepAngle, delta);
            result.maxDegreesPerSecond = Mathf.Max(result.maxDegreesPerSecond, delta / Time.fixedDeltaTime);
            ++result.steps;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _previous;
            private readonly Scene _scene;
            private readonly PhysicsScene2D _physics;
            public WorldRotator World { get; private set; }
            public Rigidbody2D Body { get; private set; }
            public Rigidbody2D Child { get; private set; }
            public CompositeCollider2D Composite { get; private set; }
            public PlayerController Player { get; private set; }
            public Vector2 PivotPosition { get; private set; }

            public Fixture(Vector2 position, float angle, Vector2 scale, bool configured)
            {
                _previous = SceneManager.GetActiveScene();
                _scene = SceneManager.CreateScene("WorldKinematicValidation_" + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                _physics = _scene.GetPhysicsScene2D();
                try
                {
                    SceneManager.SetActiveScene(_scene);
                    var playerObject = new GameObject("Fixture gameplay gate");
                    playerObject.transform.position = new Vector3(100f, 100f, 0f);
                    var playerBody = playerObject.AddComponent<Rigidbody2D>();
                    playerBody.gravityScale = 0f;
                    playerObject.AddComponent<BoxCollider2D>();
                    Player = playerObject.AddComponent<PlayerController>();
                    playerBody.simulated = false;
                    Player.rumbleEnabled = false;
                    Player.BeginGameplay();
                    var pivotObject = new GameObject("Explicit rotation pivot");
                    PivotPosition = position + new Vector2(-1.2f, .7f);
                    pivotObject.transform.position = PivotPosition;

                    var root = new GameObject("Fixture world root");
                    root.SetActive(false);
                    root.transform.position = position;
                    root.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                    root.transform.localScale = new Vector3(scale.x, scale.y, 1f);
                    Body = root.AddComponent<Rigidbody2D>();
                    Body.position = position;
                    Body.rotation = angle;
                    var childObject = new GameObject("Composite world geometry");
                    childObject.transform.SetParent(root.transform, false);
                    childObject.transform.localPosition = new Vector3(2.3f, -.4f, 0f);
                    childObject.transform.localRotation = Quaternion.Euler(0f, 0f, 17f);
                    Child = childObject.AddComponent<Rigidbody2D>();
                    Child.bodyType = configured ? RigidbodyType2D.Kinematic : RigidbodyType2D.Static;
                    Child.position = childObject.transform.position;
                    Child.rotation = childObject.transform.eulerAngles.z;
                    Composite = childObject.AddComponent<CompositeCollider2D>();
                    Composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                    var box = childObject.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(2f, .4f);
                    box.compositeOperation = Collider2D.CompositeOperation.Merge;
                    World = root.AddComponent<WorldRotator>();
                    World.pivot = pivotObject.transform;
                    World.limitSteeringRange = false;
                    if (configured) World.attachedGeometryBodies = new[] { Child };
                    root.SetActive(true);
                    InputPlayer.SetValue(World.RotationInput, Player);
                    Physics2D.SyncTransforms();
                }
                catch { Dispose(); throw; }
            }

            public void Step()
            {
                FixedStep.Invoke(World, null);
                if (!_physics.Simulate(Time.fixedDeltaTime))
                    throw new InvalidOperationException("Local 2D simulation did not advance.");
            }

            public void Dispose()
            {
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                if (!_scene.IsValid() || !_scene.isLoaded) return;
                // Immediate destruction prevents fixture Start, player effects, or audio setup
                // from executing after this synchronous validation returns to the PlayerLoop.
                foreach (GameObject root in _scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(_scene);
            }
        }
    }
}
