using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GravityGame.Tests.PlayMode
{
    public sealed class KinematicMotionSafetyPlayModeTests
    {
        private const float Dt = .02f;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [TestCase(1f)]
        [TestCase(-1f)]
        public void DistantRotatingBox_ActualPhysicsCornersMoveLessThanHalfPlayerThickness(float direction)
        {
            using (var f = new Fixture())
            {
                Rigidbody2D body = f.CreateBox("Distant rotating geometry", new Vector2(80f, 8f), 23f,
                    new Vector2(8f, 1f), out BoxCollider2D collider);
                Vector2[] corners = BoxCorners(new Vector2(8f, 1f));
                Vector2 pivot = new Vector2(-2f, 3f);
                float thickness = Mathf.Min(f.PlayerCollider.bounds.size.x, f.PlayerCollider.bounds.size.y);
                Assert.That(thickness, Is.GreaterThan(.5f));

                // A real unbounded MovePosition/MoveRotation step must violate the requirement;
                // otherwise a missing simulation or stationary fixture could falsely pass.
                Vector2 initialPosition = body.position;
                float initialRotation = body.rotation;
                Vector2[] beforeControl = PhysicalPoints(body, corners);
                RequestRotation(body, pivot, direction * 3.6f);
                f.Simulate();
                float controlTravel = LargestDisplacement(beforeControl, PhysicalPoints(body, corners));
                Assert.That(controlTravel, Is.GreaterThan(thickness * .5f),
                    "The raw moving body must actually move too far for this fixture to test the safety clamp.");

                body.position = initialPosition;
                body.rotation = initialRotation;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                Physics2D.SyncTransforms();
                float largestTravel = 0f;
                float smallestTravel = float.MaxValue;
                for (int step = 0; step < 40; ++step)
                {
                    Vector2[] before = PhysicalPoints(body, corners);
                    float safeAngle = (float)f.Safety.GetMethod("ClampRotationStep").Invoke(null,
                        new object[] { new Collider2D[] { collider }, pivot, direction * 3.6f, f.Player,
                            .45f, .4f, null, Vector2.zero, 0f });
                    RequestRotation(body, pivot, safeAngle);
                    f.Simulate();
                    float measured = LargestDisplacement(before, PhysicalPoints(body, corners));
                    largestTravel = Mathf.Max(largestTravel, measured);
                    smallestTravel = Mathf.Min(smallestTravel, measured);
                }
                Assert.That(largestTravel, Is.LessThan(thickness * .5f),
                    "Measured post-simulation world-space corner travel exceeds half of the real player's minimum thickness.");
                Assert.That(smallestTravel, Is.GreaterThan(.01f), "The clamp must continue making physical progress.");
                TestContext.WriteLine("direction=" + direction + ", raw corner travel=" + controlTravel +
                    ", safe maximum=" + largestTravel + ", safe minimum=" + smallestTravel + ", player thickness=" + thickness);
            }
        }

        [Test]
        public void RotatingWorld_LeavesTravelBudgetForRealIndependentPendulumSwing()
        {
            using (var f = new Fixture())
            {
                f.InstallIsolatedTransitionGate();
                var worldObject = new GameObject("World transporting its independent plank");
                Component world = worldObject.AddComponent(f.Type("Resource.Scripts.WorldRotator"));
                Rigidbody2D worldBody = worldObject.GetComponent<Rigidbody2D>();
                var rootPivot = new GameObject("World rotation pivot").transform;
                Set(world, "pivot", rootPivot);
                Set(world, "limitSteeringRange", false);
                object input = world.GetType().GetProperty("RotationInput").GetValue(world);
                Set(input, "_player", f.Player);
                var hingeAnchor = new GameObject("Hinge on rotating world").transform;
                hingeAnchor.SetParent(worldObject.transform, false);
                hingeAnchor.localPosition = new Vector3(75f, 0f, 0f);

                var plank = new GameObject("Independent gravity plank");
                plank.layer = 6;
                plank.transform.position = hingeAnchor.position;
                plank.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                plank.transform.localScale = new Vector3(8f, 1f, 1f);
                Rigidbody2D plankBody = plank.AddComponent<Rigidbody2D>();
                plankBody.bodyType = RigidbodyType2D.Kinematic;
                var box = plank.AddComponent<BoxCollider2D>();
                box.size = Vector2.one;
                Transform hinge = new GameObject("Circle").transform;
                hinge.SetParent(plank.transform, false);
                hinge.localPosition = new Vector3(-.45f, 0f, 0f);
                Transform gravityPoint = new GameObject("Grivity").transform;
                gravityPoint.SetParent(plank.transform, false);
                gravityPoint.localPosition = new Vector3(.45f, 0f, 0f);
                Component pendulum = plank.AddComponent(f.Type("Resource.Scripts.PivotPendulum"));
                Set(pendulum, "hingePoint", hinge);
                Set(pendulum, "gravityPoint", gravityPoint);
                Set(pendulum, "pivot", hingeAnchor);
                Set(pendulum, "worldRoot", worldObject.transform);
                Set(pendulum, "player", f.Player.transform);
                Set(pendulum, "minAngle", -85f);
                Set(pendulum, "maxAngle", 85f);
                Set(pendulum, "gravity", 120f);
                Set(pendulum, "angularDamping", 3f);
                Set(pendulum, "bounciness", .08f);
                Set(pendulum, "impactSfxMinSpeed", float.MaxValue);
                Assert.That((bool)Call(pendulum, "InitializePendulum"), Is.True);
                Call(world, "RefreshMotionGeometry");
                Physics2D.SyncTransforms();

                float initialRelativeAngle = RelativeBoardAngle(worldBody, plankBody);
                Vector2[] corners = BoxCorners(new Vector2(8f, 1f));
                float maximumTravel = 0f;
                float worldTravel = 0f;
                Call(world, "SetTargetAngle", 180f);
                for (int step = 0; step < 50; ++step)
                {
                    Vector2[] before = PhysicalPoints(plankBody, corners);
                    float beforeRootAngle = worldBody.rotation;
                    Call(world, "StepPhysics", Dt);
                    Call(pendulum, "Step", Dt);
                    f.Simulate();
                    worldTravel += Mathf.Abs(Mathf.DeltaAngle(beforeRootAngle, worldBody.rotation));
                    maximumTravel = Mathf.Max(maximumTravel, LargestDisplacement(before, PhysicalPoints(plankBody, corners)));
                }
                float relativeSwing = Mathf.Abs(Mathf.DeltaAngle(initialRelativeAngle, RelativeBoardAngle(worldBody, plankBody)));
                float thickness = Mathf.Min(f.PlayerCollider.bounds.size.x, f.PlayerCollider.bounds.size.y);
                Assert.That(worldTravel, Is.GreaterThan(1f), "The root must be actively rotating throughout the budget test.");
                Assert.That(relativeSwing, Is.GreaterThan(1f),
                    "The physical board must swing relative to the moving root; inspecting a reservation field alone does not prove gravity can act.");
                Assert.That(maximumTravel, Is.LessThan(thickness * .5f),
                    "Root transport plus independent swing must share the same total corner-travel budget.");
                TestContext.WriteLine("root travel=" + worldTravel + ", real relative swing=" + relativeSwing +
                    ", maximum combined corner travel=" + maximumTravel + ", player thickness=" + thickness);
            }
        }

        private static void RequestRotation(Rigidbody2D body, Vector2 pivot, float angle)
        {
            body.MovePosition(pivot + (Vector2)(Quaternion.Euler(0f, 0f, angle) * (Vector3)(body.position - pivot)));
            body.MoveRotation(body.rotation + angle);
        }

        private static float RelativeBoardAngle(Rigidbody2D world, Rigidbody2D board)
        {
            Vector2 up = Quaternion.Euler(0f, 0f, world.rotation) * Vector2.up;
            Vector2 right = Quaternion.Euler(0f, 0f, board.rotation) * Vector2.right;
            return -Vector2.SignedAngle(up, right);
        }

        private static Vector2[] BoxCorners(Vector2 physicalSize) => new[]
        {
            new Vector2(-physicalSize.x, -physicalSize.y) * .5f,
            new Vector2(-physicalSize.x, physicalSize.y) * .5f,
            new Vector2(physicalSize.x, -physicalSize.y) * .5f,
            new Vector2(physicalSize.x, physicalSize.y) * .5f
        };

        private static Vector2[] PhysicalPoints(Rigidbody2D body, Vector2[] localCorners)
        {
            // Native Rigidbody2D point conversion samples the actual simulated pose, including
            // any engine motion clamping. It does not reuse the safety helper's radius/formula.
            return localCorners.Select(body.GetRelativePoint).ToArray();
        }

        private static float LargestDisplacement(Vector2[] before, Vector2[] after)
        {
            float largest = 0f;
            for (int i = 0; i < before.Length; ++i) largest = Mathf.Max(largest, Vector2.Distance(before[i], after[i]));
            return largest;
        }

        private static void Set(object target, string field, object value)
        { target.GetType().GetField(field, Fields).SetValue(target, value); }

        private static object Call(object target, string method, params object[] arguments)
        { return target.GetType().GetMethod(method, Fields).Invoke(target, arguments); }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _previous;
            private readonly Scene _scene;
            private readonly float _previousTimeScale;
            private FieldInfo _transitionInstance;
            private object _previousTransition;
            public readonly Type Safety;
            public Component Player { get; private set; }
            public BoxCollider2D PlayerCollider { get; private set; }

            public Fixture()
            {
                Safety = Type("Resource.Scripts.KinematicMotionSafety");
                _previous = SceneManager.GetActiveScene();
                _previousTimeScale = Time.timeScale;
                _scene = SceneManager.CreateScene("KinematicMotionSafety_" + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                try
                {
                    SceneManager.SetActiveScene(_scene);
                    Time.timeScale = 1f;
                    var go = new GameObject("Player thickness and recovery reference");
                    go.layer = 3;
                    go.transform.position = new Vector3(-200f, -200f, 0f);
                    var body = go.AddComponent<Rigidbody2D>();
                    body.gravityScale = 0f;
                    body.constraints = RigidbodyConstraints2D.FreezeAll;
                    PlayerCollider = go.AddComponent<BoxCollider2D>();
                    PlayerCollider.size = new Vector2(.6f, .9f);
                    Player = go.AddComponent(Type("Resource.Scripts.PlayerController"));
                    Set(Player, "rumbleEnabled", false);
                    Call(Player, "BeginGameplay");
                    Physics2D.SyncTransforms();
                }
                catch { Dispose(); throw; }
            }

            public Type Type(string name)
            {
                Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
                Assert.That(type, Is.Not.Null, "Runtime type missing: " + name);
                return type;
            }

            public Rigidbody2D CreateBox(string name, Vector2 position, float angle, Vector2 size, out BoxCollider2D collider)
            {
                var go = new GameObject(name);
                go.layer = 6;
                go.transform.position = position;
                go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.useFullKinematicContacts = true;
                body.interpolation = RigidbodyInterpolation2D.None;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                collider = go.AddComponent<BoxCollider2D>();
                collider.size = size;
                Physics2D.SyncTransforms();
                return body;
            }

            public void InstallIsolatedTransitionGate()
            {
                // The world checks this singleton as a gameplay gate. Use a temporary inactive
                // component so no transition UI is built and the main scene's gate is untouched.
                Type transitionType = Type("Resource.Scripts.SceneTransition");
                _transitionInstance = transitionType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
                _previousTransition = _transitionInstance.GetValue(null);
                var go = new GameObject("Isolated inactive transition gate");
                go.SetActive(false);
                Component gate = go.AddComponent(transitionType);
                _transitionInstance.SetValue(null, gate);
            }

            public void Simulate()
            { Assert.That(_scene.GetPhysicsScene2D().Simulate(Dt), Is.True, "The isolated physics scene must really advance."); }

            public void Dispose()
            {
                if (_transitionInstance != null) _transitionInstance.SetValue(null, _previousTransition);
                Time.timeScale = _previousTimeScale;
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                if (!_scene.IsValid() || !_scene.isLoaded) return;
                foreach (GameObject root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(_scene);
            }
        }
    }
}
