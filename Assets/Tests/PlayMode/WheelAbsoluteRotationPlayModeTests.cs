using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GravityGame.Tests.PlayMode
{
    public sealed class WheelAbsoluteRotationPlayModeTests
    {
        private const float Dt = .02f;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(-180f)]
        [TestCase(360f)]
        [TestCase(-360f)]
        public void Wheel_AbsoluteTargetReachesInOneRealStep_AndHoldingDoesNotChase(float target)
        {
            using (var f = new Fixture())
            {
                Set(f.World, "useGeometrySafetyClamp", true);
                f.Target(target);
                f.RequestStep();
                Assert.That(f.Angle, Is.EqualTo(0f).Within(.001f),
                    "Scheduling a MoveRotation must not count an unsimulated full turn as actual motion.");
                Assert.That(f.Body.position, Is.EqualTo(f.InitialPosition));
                f.SimulateAndComplete();
                f.AssertPose(target);
                Vector2 stoppedPosition = f.Body.position;
                float stoppedAngle = f.Body.rotation;
                for (int i = 0; i < 10; ++i)
                {
                    f.Step();
                    f.AssertPose(target);
                    Assert.That(Vector2.Distance(f.Body.position, stoppedPosition), Is.LessThan(.001f));
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(stoppedAngle, f.Body.rotation)), Is.LessThan(.001f));
                    Assert.That(Mathf.Abs(f.Body.angularVelocity), Is.LessThan(.01f));
                }
            }
        }

        [Test]
        public void Wheel_ContinuousFullTravelAndReturnKeepSignedAbsoluteAngle()
        {
            using (var f = new Fixture())
            {
                for (int target = -180; target <= 180; target += 15)
                {
                    f.Target(target);
                    f.Step();
                    f.AssertPose(target);
                }
                f.Target(0f);
                f.Step();
                f.AssertPose(0f);
                Assert.That(Vector2.Distance(f.Body.position, f.InitialPosition), Is.LessThan(.001f));
            }
        }

        [TestCase(150f, 3f, 3f)]
        [TestCase(75f, 3f, 1.5f)]
        [TestCase(10000f, 3f, 3f)]
        public void OtherModes_KeepTheirExistingAngularAndStepLimits(float speed, float stepLimit, float expected)
        {
            using (var f = new Fixture())
            {
                f.Direct = false;
                Set(f.World, "maxAngularSpeed", speed);
                Set(f.World, "maxStepAngle", stepLimit);
                float translation = Physics2D.maxTranslationSpeed;
                float rotation = Physics2D.maxRotationSpeed;
                f.Target(90f);
                f.Step();
                f.AssertPose(expected);
                Assert.That(Physics2D.maxTranslationSpeed, Is.EqualTo(translation));
                Assert.That(Physics2D.maxRotationSpeed, Is.EqualTo(rotation));
            }
        }

        [Test]
        public void Wheel_LatestTargetWins_AndEngineLimitsAreRestoredWhenLeavingMode()
        {
            using (var f = new Fixture())
            {
                Physics2D.maxTranslationSpeed = 1f;
                Physics2D.maxRotationSpeed = 1f;
                f.Target(180f);
                f.Target(90f);
                f.RequestStep();
                Assert.That(Physics2D.maxTranslationSpeed, Is.GreaterThan(1f));
                Assert.That(Physics2D.maxRotationSpeed, Is.GreaterThanOrEqualTo(90f));
                f.SimulateAndComplete();
                f.AssertPose(90f);
                f.Direct = false;
                f.Step();
                Assert.That(Physics2D.maxTranslationSpeed, Is.EqualTo(1f));
                Assert.That(Physics2D.maxRotationSpeed, Is.EqualTo(1f));
                f.AssertPose(90f);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Wheel_PauseOrDisableDiscardsTheTargetAndRestoresEngineLimits(bool disable)
        {
            using (var f = new Fixture())
            {
                Physics2D.maxTranslationSpeed = 1f;
                Physics2D.maxRotationSpeed = 1f;
                f.Target(90f);
                f.Step();
                f.AssertPose(90f);
                f.Target(180f);
                if (disable) ((Behaviour)f.World).enabled = false;
                else
                {
                    Time.timeScale = 0f;
                    f.RequestStep();
                }
                Assert.That(Physics2D.maxTranslationSpeed, Is.EqualTo(1f));
                Assert.That(Physics2D.maxRotationSpeed, Is.EqualTo(1f));
                Assert.That(Get<float>(f.World, "TargetClockwiseAngleReadout"), Is.EqualTo(f.Angle).Within(.001f));
                Time.timeScale = 1f;
                if (disable) ((Behaviour)f.World).enabled = true;
                f.Direct = false;
                f.Step();
                f.AssertPose(90f);
            }
        }

        [Test]
        public void Wheel_RotatedCompositeStillMakesRealContactWithDynamicPlayer()
        {
            using (var f = new Fixture())
            {
                f.Target(90f);
                f.Step();
                f.AssertPose(90f);
                Vector2 normal = Quaternion.Euler(0f, 0f, f.Child.rotation) * Vector2.up;
                f.PlayerBody.constraints = RigidbodyConstraints2D.FreezeRotation;
                f.PlayerBody.simulated = true;
                Vector2 contactPosition = f.Child.position + normal * .4f;
                f.PlayerBody.position = contactPosition;
                Physics2D.SyncTransforms();
                Assert.That(Vector2.Distance(f.PlayerBody.position, contactPosition), Is.LessThan(.001f),
                    "Enable the physical body before positioning it; enabling restores its previous Transform pose.");
                f.Step();
                f.Step();
                Assert.That(f.PlayerCollider.GetContacts(new ContactPoint2D[8]), Is.GreaterThan(0),
                    "The rotated Kinematic Composite must still collide with the real Dynamic player collider.");
                Assert.That(f.PlayerBody.bodyType, Is.EqualTo(RigidbodyType2D.Dynamic));
                Assert.That(f.PlayerCollider.enabled && !f.PlayerCollider.isTrigger, Is.True);
                Assert.That(f.Child.bodyType, Is.EqualTo(RigidbodyType2D.Kinematic));
                Assert.That(f.Composite.enabled && !f.Composite.isTrigger, Is.True);
                Assert.That(f.PlayerBody.transform.IsChildOf(f.World.transform), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Wheel_DisableCountsOnlyAnAlreadySimulatedDirectStep(bool simulateBeforeDisable)
        {
            using (var f = new Fixture())
            {
                Physics2D.maxTranslationSpeed = 1f;
                Physics2D.maxRotationSpeed = 1f;
                f.Target(90f);
                f.RequestStep();
                Assert.That(f.Angle, Is.EqualTo(0f).Within(.001f));
                if (simulateBeforeDisable) f.SimulateWithoutCompleting();
                ((Behaviour)f.World).enabled = false;
                float expected = simulateBeforeDisable ? 90f : 0f;
                Assert.That(f.Angle, Is.EqualTo(expected).Within(.005f));
                Assert.That(Physics2D.maxTranslationSpeed, Is.EqualTo(1f));
                Assert.That(Physics2D.maxRotationSpeed, Is.EqualTo(1f));
                f.SimulateAndComplete();
                f.AssertPose(expected);
            }
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, Members).SetValue(target, value);
        private static T Get<T>(object target, string name) =>
            (T)target.GetType().GetProperty(name, Members).GetValue(target);
        private static object Call(object target, string name, params object[] values) =>
            target.GetType().GetMethod(name, Members).Invoke(target, values);
        private static Type RuntimeType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _previousScene;
            private readonly Scene _scene;
            private readonly float _previousTimeScale;
            private readonly float _previousTranslation;
            private readonly float _previousRotation;
            private FieldInfo _transitionField;
            private object _previousTransition;
            private object _input;
            public Component World { get; private set; }
            public Rigidbody2D Body { get; private set; }
            public Rigidbody2D Child { get; private set; }
            public CompositeCollider2D Composite { get; private set; }
            public Rigidbody2D PlayerBody { get; private set; }
            public BoxCollider2D PlayerCollider { get; private set; }
            public Vector2 InitialPosition { get; private set; }
            public Vector2 PivotPosition { get; private set; }
            private Vector2 _initialChildPosition;
            private float _initialChildAngle;
            public float Angle => Get<float>(World, "ClockwiseAngleReadout");
            public bool Direct { set => _input.GetType().GetProperty("DirectAngleActive").SetValue(_input, value); }

            public Fixture()
            {
                _previousScene = SceneManager.GetActiveScene();
                _previousTimeScale = Time.timeScale;
                _previousTranslation = Physics2D.maxTranslationSpeed;
                _previousRotation = Physics2D.maxRotationSpeed;
                _scene = SceneManager.CreateScene("WheelAbsolute_" + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                try
                {
                    SceneManager.SetActiveScene(_scene);
                    Time.timeScale = 1f;
                    Type transitionType = RuntimeType("Resource.Scripts.SceneTransition");
                    _transitionField = transitionType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
                    _previousTransition = _transitionField.GetValue(null);
                    var transition = new GameObject("Inactive fixture transition gate");
                    transition.SetActive(false);
                    _transitionField.SetValue(null, transition.AddComponent(transitionType));

                    var playerObject = new GameObject("Fixture player");
                    playerObject.layer = 3;
                    playerObject.transform.position = new Vector3(200f, 200f, 0f);
                    PlayerBody = playerObject.AddComponent<Rigidbody2D>();
                    PlayerBody.gravityScale = 0f;
                    PlayerBody.simulated = false;
                    PlayerCollider = playerObject.AddComponent<BoxCollider2D>();
                    PlayerCollider.size = new Vector2(.6f, .9f);
                    Component player = playerObject.AddComponent(RuntimeType("Resource.Scripts.PlayerController"));
                    Set(player, "rumbleEnabled", false);
                    Call(player, "BeginGameplay");

                    PivotPosition = new Vector2(-1f, 3f);
                    Transform pivot = new GameObject("Fixed physical wheel pivot").transform;
                    pivot.position = PivotPosition;
                    var root = new GameObject("Wheel world");
                    root.SetActive(false);
                    InitialPosition = new Vector2(4f, -2f);
                    root.transform.position = InitialPosition;
                    Body = root.AddComponent<Rigidbody2D>();
                    var child = new GameObject("Independent Composite geometry");
                    child.layer = 6;
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = new Vector3(2.3f, -.4f, 0f);
                    child.transform.localRotation = Quaternion.Euler(0f, 0f, 17f);
                    Child = child.AddComponent<Rigidbody2D>();
                    Child.bodyType = RigidbodyType2D.Kinematic;
                    Composite = child.AddComponent<CompositeCollider2D>();
                    Composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                    BoxCollider2D box = child.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(2f, .4f);
                    box.compositeOperation = Collider2D.CompositeOperation.Merge;
                    _initialChildPosition = child.transform.position;
                    _initialChildAngle = child.transform.eulerAngles.z;
                    World = root.AddComponent(RuntimeType("Resource.Scripts.WorldRotator"));
                    Set(World, "pivot", pivot);
                    Set(World, "attachedGeometryBodies", new[] { Child });
                    Set(World, "maxAngularSpeed", 150f);
                    Set(World, "maxStepAngle", 3f);
                    root.SetActive(true);
                    _input = Get<object>(World, "RotationInput");
                    Set(_input, "_player", player);
                    Direct = true;
                    Physics2D.SyncTransforms();
                }
                catch { Dispose(); throw; }
            }

            public void Target(float angle) => Call(World, "SetTargetAngle", angle);
            public void RequestStep() => Call(World, "StepPhysics", Dt);
            public void SimulateWithoutCompleting()
            {
                Assert.That(_scene.GetPhysicsScene2D().Simulate(Dt), Is.True,
                    "Each checked request must advance an actual isolated physics simulation.");
            }
            public void SimulateAndComplete()
            {
                SimulateWithoutCompleting();
                Call(World, "SynchronizeActualAngle", true);
            }
            public void Step() { RequestStep(); SimulateAndComplete(); }
            public void AssertPose(float target)
            {
                Quaternion rotation = Quaternion.Euler(0f, 0f, -target);
                Vector2 expectedRoot = PivotPosition + (Vector2)(rotation * (Vector3)(InitialPosition - PivotPosition));
                Vector2 expectedChild = PivotPosition + (Vector2)(rotation * (Vector3)(_initialChildPosition - PivotPosition));
                Assert.That(Angle, Is.EqualTo(target).Within(.005f), "Signed absolute angle must match, including full turns.");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(Body.rotation, -target)), Is.LessThan(.005f));
                Assert.That(Vector2.Distance(Body.position, expectedRoot), Is.LessThan(.002f));
                Assert.That(Vector2.Distance(Child.position, expectedChild), Is.LessThan(.002f), "Composite must reach the same root pose.");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(Child.rotation, _initialChildAngle - target)), Is.LessThan(.005f));
            }

            public void Dispose()
            {
                if (_transitionField != null) _transitionField.SetValue(null, _previousTransition);
                if (_previousScene.IsValid() && _previousScene.isLoaded) SceneManager.SetActiveScene(_previousScene);
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    foreach (GameObject root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                    SceneManager.UnloadSceneAsync(_scene);
                }
                Physics2D.maxTranslationSpeed = _previousTranslation;
                Physics2D.maxRotationSpeed = _previousRotation;
                Time.timeScale = _previousTimeScale;
            }
        }
    }
}
