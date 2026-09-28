using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Resource.Scripts.Editor
{
    /// <summary>Explicit Stage1 geometry regression. Only private local physics scenes are simulated.</summary>
    public static class PendulumStageValidation
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public const string Folder = "Docs/PendulumFix";

        [Serializable] public sealed class Result
        {
            public string name, error;
            public bool passed;
            public int simulatedRenderHz, revolutions, physicsSteps, contactFrames, trackedBoardContactFrames, contactEpisodes, overlapFrames, originalSideCrossings;
            public int maxRemainingOverlap;
            public float maxHingeGap, maxBoardAxisErrorDegrees, maxPenetration, minimumSideClearance = float.MaxValue;
            public float actualClockwiseTravel, accumulatedClockwiseDegrees, accumulatedCounterClockwiseDegrees, maxPlayerSpeed, maxCorrection;
            public int completedHalfTurns;
            public float simulatedSeconds;
            public float requestedAngularSpeed;
            public float maxDepenetrationPerStep;
            public int clonedWorldColliders;
            public List<OverlapDiagnostic> firstOverlapByCollider = new List<OverlapDiagnostic>();
            public List<string> crossings = new List<string>();
        }
        [Serializable] public sealed class OverlapDiagnostic
        {
            public string collider, type, lastConstraintCollider;
            public int layer, worldGeometryMask, physicsStep, shapeCount, guardRemaining;
            public bool includedInGuardMask, collisionIgnored, consumedByComposite;
            public float distance, worldClockwiseAngle, entryPlaneDepth, mtvDepth, mtvAlignment;
            public Vector2 playerPosition, playerVelocity, intendedVelocity, correction, normal, pointA, pointB, solverVelocityDelta;
            public Vector2 entryNormal, mtvNormal, requestedCorrection;
        }
        [Serializable] public sealed class Geometry
        {
            public string name;
            public Vector3 boardPosition, boardScale, pivot, hinge, gravityPoint, strandPosition, strandScale;
            public float boardZ, strandZ, hingeGap, armLength, gravity, damping, bounce, minAngle, maxAngle;
        }
        [Serializable] public sealed class Report
        {
            public string utc, version, label;
            public string scheduling = "Synchronous simulation of 60 Hz and 240 Hz render scheduling with the project's fixed timestep. This is not a measured real rendering FPS test.";
            public float fixedTimestep;
            public bool passed;
            public Geometry[] geometry;
            public List<Result> results = new List<Result>();
        }

        [MenuItem("Tools/Gravity Game/Physics/Validate Stage1 Pendulums")]
        public static void RunMenu() { Debug.Log(RunAndSave("current", 10)); }

        public static string RunAndSave(string label = "current", int revolutions = 10)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play with Stage1 loaded; local PhysicsScene2D creation requires Play mode.");
            var scene = SceneManager.GetSceneByName("Stage1");
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Stage1 must be loaded to clone its real geometry.");
            var sourceWorld = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WorldRotator>(true)).Single();
            var sourcePlayer = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Single();
            var sourcePendulums = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PivotPendulum>(true)).OrderBy(p => p.name).ToArray();
            if (sourcePendulums.Length != 2) throw new InvalidOperationException("Expected Stage1's Clock and Clock (1).");
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), version = Application.unityVersion, label = label,
                fixedTimestep = Time.fixedDeltaTime, geometry = sourcePendulums.Select(InspectGeometry).ToArray()
            };
            float savedScale = Time.timeScale;
            try
            {
                Time.timeScale = 1f;
                foreach (int hz in new[] { 60, 240 })
                    foreach (int sign in new[] { 1, -1 })
                        foreach (int board in new[] { 0, 1 })
                        {
                            var result = new Result
                            {
                                name = sourcePendulums[board].name + (sign > 0 ? " / CW" : " / CCW") + " / simulated " + hz + " Hz",
                                simulatedRenderHz = hz, revolutions = revolutions
                            };
                            try
                            {
                                using (var fixture = new Fixture(sourceWorld, sourcePlayer, sourcePendulums, board))
                                    RunFixture(fixture, result, sign, revolutions);
                            }
                            catch (Exception ex) { result.error = ex.ToString(); result.passed = false; }
                            report.results.Add(result);
                        }
            }
            finally { Time.timeScale = savedScale; }
            report.passed = report.results.All(r => r.passed);
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Folder);
            string safeLabel = new string(label.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            File.WriteAllText(Folder + "/Pendulum-" + safeLabel + ".json", json);
            return json;
        }

        public static Geometry InspectGeometry(PivotPendulum p)
        {
            Transform hinge = p.transform.Find("Circle");
            if (hinge == null) throw new InvalidOperationException(p.name + " has no Circle hinge marker.");
            return new Geometry
            {
                name = p.name, boardPosition = p.transform.position, boardScale = p.transform.lossyScale,
                boardZ = p.transform.eulerAngles.z, hinge = hinge.position, pivot = p.pivot.position,
                gravityPoint = p.gravityPoint.position, hingeGap = Vector3.Distance(hinge.position, p.pivot.position),
                armLength = Vector3.Distance(hinge.position, p.gravityPoint.position),
                strandPosition = p.pivot.parent.localPosition, strandScale = p.pivot.parent.localScale,
                strandZ = p.pivot.parent.localEulerAngles.z, gravity = p.gravity, damping = p.angularDamping,
                bounce = p.bounciness, minAngle = p.minAngle, maxAngle = p.maxAngle
            };
        }

        private static void RunFixture(Fixture f, Result result, int direction, int revolutions)
        {
            var session = new Session(f, result, direction, revolutions);
            result.clonedWorldColliders = f.Geometry.Count;
            while (!session.Finished) session.AdvanceFrame(1.0 / result.simulatedRenderHz);
            session.Finish();
        }

        internal sealed class Session
        {
            internal readonly Fixture Fixture;
            internal readonly Result Result;
            private readonly int _direction, _revolutions;
            private readonly float _speed;
            private double _accumulator, _segmentTime;
            private float _lastAngle;
            internal bool Finished => Result.completedHalfTurns >= _revolutions * 2;
            internal Session(Fixture f, Result result, int direction, int revolutions)
            {
                Fixture = f; Result = result; _direction = direction; _revolutions = revolutions;
                _speed = f.World.maxAngularSpeed;
                result.requestedAngularSpeed = _speed;
                result.maxDepenetrationPerStep = f.Guard.maxDepenetrationPerStep;
                if (_speed <= 0f) throw new InvalidOperationException("World speed must be positive.");
                f.PlacePlayerAtBoard(); // Once only: every following revolution is continuous.
                result.contactEpisodes = 1;
                _lastAngle = f.World.ClockwiseAngleReadout;
            }
            internal void PrepareFrame(double elapsedSeconds)
            {
                if (Finished) return;
                _segmentTime += elapsedSeconds;
                Result.simulatedSeconds += (float)elapsedSeconds;
                float goal = Result.completedHalfTurns % 2 == 0 ? _direction * 360f : 0f;
                float start = Result.completedHalfTurns % 2 == 0 ? 0f : _direction * 360f;
                Fixture.World.SetTargetAngle(Mathf.MoveTowards(start, goal, _speed * (float)_segmentTime));
                SetField(Fixture.World, "_pivotFrame", -1);
                if (_segmentTime > 120)
                    throw new InvalidOperationException("120 simulated seconds elapsed without reaching a 360-degree target. actual=" + Fixture.World.ClockwiseAngleReadout + ", target=" + goal);
            }
            internal void AdvanceFrame(double elapsedSeconds, Action afterPhysicsStep = null)
            {
                PrepareFrame(elapsedSeconds);
                // Synchronous fixtures do not receive Unity Update. Advance the automatic
                // airborne pivot queue once per simulated render frame, including frames
                // with no physics tick, as the real WorldRotator.Update does.
                if (Fixture.World.pivot == null)
                    typeof(WorldRotator).GetMethod("ResolveFramePivot", Any)?.Invoke(Fixture.World, null);
                _accumulator += elapsedSeconds;
                float dt = Time.fixedDeltaTime;
                while (_accumulator + 1e-9 >= dt && !Finished)
                {
                    _accumulator -= dt;
                    InvokeStep(Fixture.World, "StepPhysics", "FixedUpdate", dt);
                    foreach (var p in Fixture.Pendulums) InvokeStep(p, "Step", "FixedUpdate", dt);
                    Fixture.Player.BeginPhysicsStep(dt);
                    Fixture.Physics.Simulate(dt);
                    Fixture.Player.CompletePhysicsStep(dt);
                    RecordPhysicsStep();
                    afterPhysicsStep?.Invoke();
                }
            }
            internal void RecordPhysicsStep()
            {
                Measure(Fixture, Result);
                Result.physicsSteps++;
                float angle = Fixture.World.ClockwiseAngleReadout;
                Result.accumulatedClockwiseDegrees += Mathf.Max(0f, angle - _lastAngle);
                Result.accumulatedCounterClockwiseDegrees += Mathf.Max(0f, _lastAngle - angle);
                _lastAngle = angle;
                Result.actualClockwiseTravel = angle;
                float goal = Result.completedHalfTurns % 2 == 0 ? _direction * 360f : 0f;
                if (Mathf.Abs(angle - goal) < .03f && _segmentTime >= 360f / _speed)
                { Result.completedHalfTurns++; _segmentTime = 0; }
                if (Fixture.Player.IsDead) throw new InvalidOperationException("Fixture player died before finishing the continuous run.");
            }
            internal void Finish()
            {
                Result.passed = Finished && Result.accumulatedClockwiseDegrees >= 360f * _revolutions - 1f &&
                    Result.accumulatedCounterClockwiseDegrees >= 360f * _revolutions - 1f &&
                    Result.maxHingeGap < .01f && Result.maxBoardAxisErrorDegrees < .1f && Result.trackedBoardContactFrames > 0 &&
                    Result.overlapFrames == 0 && Result.maxRemainingOverlap == 0 && Result.originalSideCrossings == 0 &&
                    Result.maxCorrection <= Result.maxDepenetrationPerStep + .00001f;
            }
        }

        private static void Measure(Fixture f, Result result)
        {
            foreach (var pendulum in f.Pendulums)
            {
                var hinge = pendulum.transform.Find("Circle");
                result.maxHingeGap = Mathf.Max(result.maxHingeGap, Vector2.Distance(PhysicalPoint(hinge, Vector2.zero), PhysicalPoint(pendulum.pivot, Vector2.zero)));
                var body = pendulum.GetComponent<Rigidbody2D>();
                float angle = (float)typeof(PivotPendulum).GetField("_angle", Any).GetValue(pendulum);
                Vector2 expectedAxis = Quaternion.Euler(0f, 0f, -angle) * PhysicalDirection(pendulum.worldRoot, Vector2.up);
                Vector2 actualAxis = Quaternion.Euler(0f, 0f, body.rotation) * Vector2.right;
                float axis = Mathf.Abs(Vector2.SignedAngle(actualAxis, expectedAxis));
                result.maxBoardAxisErrorDegrees = Mathf.Max(result.maxBoardAxisErrorDegrees, axis);
            }
            result.maxPlayerSpeed = Mathf.Max(result.maxPlayerSpeed, f.PlayerBody.linearVelocity.magnitude);
            result.maxCorrection = Mathf.Max(result.maxCorrection, f.Guard.LastCorrectionDistance);
            result.maxRemainingOverlap = Mathf.Max(result.maxRemainingOverlap, f.Guard.RemainingOverlapCount);
            bool overlap = false, touching = false;
            foreach (var other in f.Geometry)
            {
                if (other == null || !other.enabled || other.isTrigger || other.attachedRigidbody == f.PlayerBody) continue;
                ColliderDistance2D distance = f.PlayerCollider.Distance(other);
                if (!distance.isValid) continue;
                bool consumed = !(other is CompositeCollider2D) && other.compositeOperation != Collider2D.CompositeOperation.None;
                bool ignored = Physics2D.GetIgnoreCollision(f.PlayerCollider, other) || Physics2D.GetIgnoreLayerCollision(f.PlayerCollider.gameObject.layer, other.gameObject.layer);
                if (distance.isOverlapped && distance.distance < -.0001f && f.RecordedOverlaps.Add(other))
                    result.firstOverlapByCollider.Add(new OverlapDiagnostic
                    {
                        collider = other.name, type = other.GetType().Name, layer = other.gameObject.layer,
                        worldGeometryMask = f.Guard.worldGeometryMask.value, physicsStep = result.physicsSteps,
                        shapeCount = other.shapeCount, guardRemaining = f.Guard.RemainingOverlapCount,
                        includedInGuardMask = (f.Guard.worldGeometryMask.value & (1 << other.gameObject.layer)) != 0,
                        collisionIgnored = ignored, consumedByComposite = consumed,
                        distance = distance.distance, worldClockwiseAngle = f.World.ClockwiseAngleReadout,
                        playerPosition = f.PlayerBody.position, playerVelocity = f.PlayerBody.linearVelocity,
                        intendedVelocity = f.Player.IntendedVelocity, correction = f.Guard.LastCorrection,
                        normal = distance.normal, pointA = distance.pointA, pointB = distance.pointB,
                        solverVelocityDelta = f.Player.LastPhysicsVelocityDelta,
                        lastConstraintCollider = f.Guard.LastConstraintCollider != null ? f.Guard.LastConstraintCollider.name : "",
                        entryNormal = f.Guard.LastEntryNormal, mtvNormal = f.Guard.LastMtvNormal,
                        entryPlaneDepth = f.Guard.LastEntryPlaneDepth, mtvDepth = f.Guard.LastMtvDepth,
                        mtvAlignment = f.Guard.LastMtvAlignment, requestedCorrection = f.Guard.LastRequestedCorrection
                    });
                // A geometry mask controls the guard, not the actual solver. Missing mask layers
                // still fail this physical overlap audit; only genuinely unused shapes are excluded.
                if (consumed || ignored || other.shapeCount == 0) continue;
                if (distance.distance <= .01f) touching = true;
                if (distance.isOverlapped && distance.distance < -.0001f)
                {
                    overlap = true;
                    result.maxPenetration = Mathf.Max(result.maxPenetration, -distance.distance);
                }
            }
            if (overlap) result.overlapFrames++;
            if (touching) result.contactFrames++;
            var board = f.Pendulums[f.BoardIndex].GetComponent<BoxCollider2D>();
            var boardDistance = f.PlayerCollider.Distance(board);
            if (boardDistance.isValid && boardDistance.distance <= .01f) result.trackedBoardContactFrames++;
            Vector2 normal = PhysicalDirection(board.transform, Vector2.up);
            Vector2 axisDirection = PhysicalDirection(board.transform, Vector2.right);
            Vector2 center = PhysicalPoint(board.transform, board.offset);
            Vector2 relative = (Vector2)f.PlayerCollider.bounds.center - center;
            var halfPlayer = f.PlayerCollider.bounds.extents;
            float playerHalfNormal = Mathf.Abs(normal.x) * halfPlayer.x + Mathf.Abs(normal.y) * halfPlayer.y;
            float playerHalfAxis = Mathf.Abs(axisDirection.x) * halfPlayer.x + Mathf.Abs(axisDirection.y) * halfPlayer.y;
            float boardHalfAxis = board.size.x * Mathf.Abs(board.transform.lossyScale.x) * .5f;
            float boardHalfNormal = board.size.y * Mathf.Abs(board.transform.lossyScale.y) * .5f;
            bool withinEnds = Mathf.Abs(Vector2.Dot(relative, axisDirection)) < boardHalfAxis + playerHalfAxis - .01f;
            float centerCoordinate = Vector2.Dot(relative, normal);
            float longitudinal = Vector2.Dot(relative, axisDirection);
            float absoluteClearance = Mathf.Abs(centerCoordinate) - boardHalfNormal - playerHalfNormal;
            if (withinEnds && !f.TrackOriginalSide && absoluteClearance <= .05f && Mathf.Abs(centerCoordinate) > .001f)
            {
                f.OriginalSide = Mathf.Sign(centerCoordinate);
                f.TrackOriginalSide = true;
                f.CountedCrossing = false;
                f.HasPreviousCenter = false;
                result.contactEpisodes++;
            }
            float signedCenter = f.OriginalSide * centerCoordinate;
            if (withinEnds && f.TrackOriginalSide)
            {
                result.minimumSideClearance = Mathf.Min(result.minimumSideClearance, signedCenter - boardHalfNormal - playerHalfNormal);
                float previousSigned = f.OriginalSide * f.PreviousCenterCoordinate;
                if (f.HasPreviousCenter && previousSigned >= 0f && signedCenter < 0f && !f.CountedCrossing)
                {
                    float t = previousSigned / Mathf.Max(.000001f, previousSigned - signedCenter);
                    float crossingLongitudinal = Mathf.Lerp(f.PreviousLongitudinal, longitudinal, t);
                    // A centre crossing beyond a physical end can be a legitimate route around
                    // the corner. Require the crossing to pass through the plank's actual span.
                    if (Mathf.Abs(crossingLongitudinal) < boardHalfAxis - .001f)
                    {
                        result.originalSideCrossings++;
                        f.CountedCrossing = true;
                        result.crossings.Add("step=" + result.physicsSteps + ", board=" + board.name +
                            ", normal before/after=" + previousSigned + "/" + signedCenter +
                            ", crossing longitudinal=" + crossingLongitudinal + ", board half length=" + boardHalfAxis +
                            ", player=" + f.PlayerBody.position + ", world=" + f.World.ClockwiseAngleReadout);
                    }
                }
            }
            if (!withinEnds || absoluteClearance > .25f) f.TrackOriginalSide = false; // A new encounter can lock a new original side after genuine separation.
            f.PreviousCenterCoordinate = centerCoordinate;
            f.PreviousLongitudinal = longitudinal;
            f.HasPreviousCenter = true;
        }

        private static Vector2 PhysicalPoint(Transform point, Vector2 localOffset)
        {
            var body = point.GetComponentInParent<Rigidbody2D>();
            Vector2 world = point.TransformPoint(localOffset);
            if (body == null) return world;
            Vector2 local = Quaternion.Inverse(body.transform.rotation) * (Vector3)(world - (Vector2)body.transform.position);
            return body.position + (Vector2)(Quaternion.Euler(0f, 0f, body.rotation) * local);
        }
        private static Vector2 PhysicalDirection(Transform point, Vector2 localDirection)
        {
            var body = point.GetComponentInParent<Rigidbody2D>();
            Vector2 world = point.TransformDirection(localDirection);
            if (body == null) return world;
            Vector2 local = Quaternion.Inverse(body.transform.rotation) * (Vector3)world;
            return Quaternion.Euler(0f, 0f, body.rotation) * local;
        }

        private static void InvokeStep(object target, string currentName, string legacyName, float dt)
        {
            MethodInfo current = target.GetType().GetMethod(currentName, Any, null, new[] { typeof(float) }, null);
            if (current != null) current.Invoke(target, new object[] { dt });
            else target.GetType().GetMethod(legacyName, Any).Invoke(target, null);
        }
        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, Any);
            if (field != null) field.SetValue(target, value);
        }

        internal sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly PhysicsScene2D Physics;
            public readonly WorldRotator World;
            public readonly PlayerController Player;
            public readonly Rigidbody2D PlayerBody;
            public readonly Collider2D PlayerCollider;
            public readonly CrushGuard Guard;
            public readonly PivotPendulum[] Pendulums;
            public readonly List<Collider2D> Geometry = new List<Collider2D>();
            public readonly int BoardIndex;
            public float OriginalSide;
            public bool TrackOriginalSide, CountedCrossing;
            public bool HasPreviousCenter;
            public float PreviousCenterCoordinate, PreviousLongitudinal;
            public readonly HashSet<Collider2D> RecordedOverlaps = new HashSet<Collider2D>();
            private readonly Scene _previous;
            private readonly Dictionary<Transform, Transform> _transforms = new Dictionary<Transform, Transform>();
            private readonly Dictionary<Component, Component> _components = new Dictionary<Component, Component>();

            public Fixture(WorldRotator world, PlayerController player, PivotPendulum[] pendulums, int board)
            {
                _previous = SceneManager.GetActiveScene();
                Scene = SceneManager.CreateScene("PendulumValidation_" + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                SceneManager.SetActiveScene(Scene);
                Physics = Scene.GetPhysicsScene2D();
                BoardIndex = board;
                try
                {
                foreach (var collider in world.GetComponentsInChildren<Collider2D>(true))
                    if (collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy) CloneCollider(collider);
                foreach (var p in pendulums)
                {
                    CloneTransform(p.transform.Find("Circle"));
                    CloneTransform(p.gravityPoint);
                    CloneTransform(p.pivot);
                    foreach (var collider in p.GetComponentsInChildren<Collider2D>())
                        if (collider.enabled && !collider.isTrigger) CloneCollider(collider);
                }
                foreach (var tilemap in _components.Values.OfType<TilemapCollider2D>()) tilemap.ProcessTilemapChanges();
                foreach (var composite in _components.Values.OfType<CompositeCollider2D>()) composite.GenerateGeometry();
                PlayerBody = (Rigidbody2D)CloneComponent(player.GetComponent<Rigidbody2D>());
                PlayerBody.gravityScale = player.SimulatedGravityScale;
                var sourceCollider = player.GetComponentsInChildren<Collider2D>().First(c => !c.isTrigger && c.attachedRigidbody == player.GetComponent<Rigidbody2D>());
                PlayerCollider = CloneCollider(sourceCollider, false);
                Player = PlayerBody.gameObject.AddComponent<PlayerController>();
                EditorUtility.CopySerialized(player, Player);
                Player.enabled = true;
                Player.groundCheck = Player.wallCheckLeft = Player.wallCheckRight = null;
                Player.autoMoveMode = true;
                Player.autoMoveSpeed = 0f;
                Player.rumbleEnabled = false;
                Player.logVelocityDelta = false;
                Guard = Player.GetComponent<CrushGuard>();
                var sourceGuard = player.GetComponent<CrushGuard>();
                if (sourceGuard != null) EditorUtility.CopySerialized(sourceGuard, Guard);
                Guard.playerCollider = PlayerCollider;
                Guard.drawDepenetrationGizmo = false;
                Player.BeginGameplay();
                World = CloneTransform(world.transform).gameObject.AddComponent<WorldRotator>();
                EditorUtility.CopySerialized(world, World);
                World.attachedGeometryBodies = world.attachedGeometryBodies.Where(b => b != null).Select(b => (Rigidbody2D)CloneComponent(b)).ToArray();
                World.pivot = Player.transform;
                World.limitSteeringRange = false;
                World.enabled = true;
                SetField(World.RotationInput, "_player", Player);
                typeof(WorldRotator).GetMethod("CacheAttachedGeometryBodies", Any)?.Invoke(World, null);
                Pendulums = new PivotPendulum[pendulums.Length];
                for (int i = 0; i < pendulums.Length; ++i)
                {
                    var source = pendulums[i];
                    var target = CloneTransform(source.transform).gameObject.AddComponent<PivotPendulum>();
                    EditorUtility.CopySerialized(source, target);
                    target.pivot = CloneTransform(source.pivot);
                    target.gravityPoint = CloneTransform(source.gravityPoint);
                    target.worldRoot = World.transform;
                    target.player = Player.transform;
                    target.impactSfxMinSpeed = float.MaxValue;
                    target.isDebugLog = target.isDebugGizmos = false;
                    var hingeField = typeof(PivotPendulum).GetField("hingePoint", Any);
                    if (hingeField != null) hingeField.SetValue(target, target.transform.Find("Circle"));
                    MethodInfo init = typeof(PivotPendulum).GetMethod("InitializePendulum", Any) ?? typeof(PivotPendulum).GetMethod("Start", Any);
                    init.Invoke(target, null);
                    Pendulums[i] = target;
                }
                SetField(Player, "autoMoveRotationSource", World);
                typeof(WorldRotator).GetMethod("RefreshMotionGeometry", Any)?.Invoke(World, null);
                Physics2D.SyncTransforms();
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void PlacePlayerAtBoard()
            {
                var board = Pendulums[BoardIndex].GetComponent<BoxCollider2D>();
                Vector2 normal = PhysicalDirection(board.transform, Vector2.up);
                OriginalSide = Vector2.Dot(normal, -Physics2D.gravity) >= 0f ? 1f : -1f;
                Vector2 outward = normal * OriginalSide;
                var playerExtent = PlayerCollider.bounds.extents;
                float halfPlayer = Mathf.Abs(outward.x) * playerExtent.x + Mathf.Abs(outward.y) * playerExtent.y;
                float halfBoard = board.size.y * Mathf.Abs(board.transform.lossyScale.y) * .5f;
                Vector2 center = PhysicalPoint(board.transform, board.offset);
                Vector2 playerOffset = (Vector2)PlayerCollider.bounds.center - PlayerBody.position;
                PlayerBody.position = center + outward * (halfBoard + halfPlayer + .005f) - playerOffset;
                PlayerBody.transform.position = PlayerBody.position; // One initial placement, including the pivot-visible pose.
                Player.SetIntendedVelocity(Vector2.zero);
                Guard.ResetState();
                TrackOriginalSide = true;
                CountedCrossing = false;
                HasPreviousCenter = false;
                Physics2D.SyncTransforms();
            }

            private Transform CloneTransform(Transform source)
            {
                if (_transforms.TryGetValue(source, out var existing)) return existing;
                var go = new GameObject(source.name) { layer = source.gameObject.layer };
                var target = go.transform;
                _transforms.Add(source, target);
                if (source.parent != null) target.SetParent(CloneTransform(source.parent), false);
                target.localPosition = source.localPosition;
                target.localRotation = source.localRotation;
                target.localScale = source.localScale;
                var grid = source.GetComponent<Grid>();
                if (grid != null) CloneComponent(grid);
                return target;
            }

            private Component CloneComponent(Component source)
            {
                if (_components.TryGetValue(source, out var existing)) return existing;
                var go = CloneTransform(source.transform).gameObject;
                Component target = go.GetComponent(source.GetType());
                if (target == null) target = go.AddComponent(source.GetType());
                _components.Add(source, target);
                EditorUtility.CopySerialized(source, target);
                if (target is Rigidbody2D body) body.simulated = true;
                return target;
            }

            private Collider2D CloneCollider(Collider2D source, bool geometry = true)
            {
                if (source.attachedRigidbody != null) CloneComponent(source.attachedRigidbody);
                var tilemap = source.GetComponent<Tilemap>();
                if (tilemap != null) CloneComponent(tilemap);
                var target = (Collider2D)CloneComponent(source);
                if (geometry && !Geometry.Contains(target)) Geometry.Add(target);
                return target;
            }

            public void Dispose()
            {
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                foreach (var root in Scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
