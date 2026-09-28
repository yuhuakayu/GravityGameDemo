using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GravityGame.Tests.PlayMode
{
    /// <summary>Small geometry regressions independent of the long Stage1 contact sweep.</summary>
    public sealed class PendulumGeometryPlayModeTests
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void MirroredPlanks_ClampToParallelEndpoints_AndBothRealHingesStayOnTheirPivots()
        {
            using (var f = new Fixture())
            {
                Board left = f.Board(-.808f, 0f, 90f, new Vector2(-5f, 2f), -.09f);
                Board right = f.Board(181.148f, -90f, 0f, new Vector2(4f, 2f), -.09f);
                f.Simulate();
                Assert.That(left.Angle, Is.EqualTo(90f).Within(.001f));
                Assert.That(right.Angle, Is.EqualTo(-90f).Within(.001f));
                Assert.That(Mathf.Abs(Vector2.Dot(left.PhysicalRight, right.PhysicalRight)), Is.GreaterThan(.99999f),
                    "The physical plank axes must be parallel, independently of the marker positions.");
                Assert.That(Vector2.Dot(left.PhysicalRight, right.PhysicalRight), Is.LessThan(-.99999f),
                    "The boards extend inward from opposite hinges.");
                AssertHingeAndAxis(left, f.Reference.up, 1f);
                AssertHingeAndAxis(right, f.Reference.up, 1f);
            }
        }

        [Test]
        public void OffAxisGravityMarker_ChangesArmLength_WithoutChangingBoardAngleOrHingePose()
        {
            using (var f = new Fixture())
            {
                Board aligned = f.Board(-.808f, 0f, 90f, new Vector2(-5f, 2f), -.09f);
                Board offAxis = f.Board(-.808f, 0f, 90f, new Vector2(-5f, 2f), .6f);
                f.Simulate();
                Assert.That(Mathf.Abs(offAxis.ArmLength - aligned.ArmLength), Is.GreaterThan(.01f),
                    "Grivity must still affect the gravity model's arm length.");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(aligned.Body.rotation, offAxis.Body.rotation)), Is.LessThan(.001f),
                    "Grivity cannot define the plank angle; it deliberately no longer lies on the long axis.");
                Assert.That(Vector2.Distance(aligned.Body.position, offAxis.Body.position), Is.LessThan(.001f),
                    "Grivity cannot define the body-position offset; the actual Circle hinge owns it.");
                AssertHingeAndAxis(aligned, f.Reference.up, 1f);
                AssertHingeAndAxis(offAxis, f.Reference.up, 1f);
            }
        }

        [Test]
        public void HingeAtOppositeEnd_SelectsTheLongAxisFacingInsideTheBoard()
        {
            using (var f = new Fixture())
            {
                Board board = f.Board(0f, -90f, 0f, new Vector2(4f, 2f), .3f, true);
                f.Simulate();
                Assert.That(board.Angle, Is.EqualTo(-90f).Within(.001f));
                AssertHingeAndAxis(board, f.Reference.up, -1f);
                Assert.That(Vector2.Dot(-board.PhysicalRight, board.Body.position - board.PivotPosition), Is.GreaterThan(0f),
                    "With Circle on local +X, the inward axis must become local -X.");
            }
        }

        private static void AssertHingeAndAxis(Board board, Vector2 up, float inwardSign)
        {
            Assert.That(Vector2.Distance(board.PhysicalHinge, board.PivotPosition), Is.LessThan(.001f),
                "Hinge is reconstructed from Rigidbody2D pose and the actual scaled local Circle marker.");
            Vector2 targetAxis = Quaternion.Euler(0f, 0f, -board.Angle) * (Vector3)up;
            Assert.That(Mathf.Abs(Vector2.SignedAngle(targetAxis, board.PhysicalRight * inwardSign)), Is.LessThan(.001f),
                "AngleReadout must describe the plank long axis, not the gravity marker vector.");
        }

        private sealed class Board
        {
            public readonly Rigidbody2D Body;
            public readonly Component Pendulum;
            public readonly Vector2 ScaledHingeOffset;
            public readonly Vector2 PivotPosition;
            public Board(Rigidbody2D body, Component pendulum, Vector2 offset, Vector2 pivot)
            { Body = body; Pendulum = pendulum; ScaledHingeOffset = offset; PivotPosition = pivot; }
            public float Angle => (float)Pendulum.GetType().GetProperty("AngleReadout").GetValue(Pendulum);
            public float ArmLength => (float)Pendulum.GetType().GetField("_armLength", InstanceFields).GetValue(Pendulum);
            public Vector2 PhysicalRight => Quaternion.Euler(0f, 0f, Body.rotation) * Vector2.right;
            public Vector2 PhysicalHinge => Body.position + (Vector2)(Quaternion.Euler(0f, 0f, Body.rotation) * (Vector3)ScaledHingeOffset);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _previous;
            private readonly Scene _scene;
            private readonly Type _pendulumType;
            public readonly Transform Reference;

            public Fixture()
            {
                _pendulumType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Resource.Scripts.PivotPendulum"))
                    .FirstOrDefault(t => t != null);
                Assert.That(_pendulumType, Is.Not.Null);
                _previous = SceneManager.GetActiveScene();
                _scene = SceneManager.CreateScene("PendulumGeometry_" + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                SceneManager.SetActiveScene(_scene);
                Reference = new GameObject("Reference world").transform;
                Reference.position = new Vector3(2f, -1f, 0f);
                Reference.rotation = Quaternion.Euler(0f, 0f, 27f);
            }

            public Board Board(float initialRelativeRotation, float minimum, float maximum, Vector2 pivotLocal,
                float gravityMarkerY, bool oppositeHinge = false)
            {
                var pivot = new GameObject("External pivot").transform;
                pivot.SetParent(Reference, false);
                pivot.localPosition = pivotLocal;
                var go = new GameObject("Fixture plank");
                go.transform.position = new Vector3(10f, 4f, 0f); // Intentionally misaligned before initialization.
                go.transform.rotation = Quaternion.Euler(0f, 0f, 27f + initialRelativeRotation);
                var scale = new Vector3(5.6417975f, 1.3f, 1f);
                go.transform.localScale = scale;
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                go.AddComponent<BoxCollider2D>();
                var hinge = new GameObject("Circle").transform;
                hinge.SetParent(go.transform, false);
                hinge.localPosition = new Vector3(oppositeHinge ? .434f : -.434f, -.09f, 0f);
                var gravityPoint = new GameObject("Grivity").transform;
                gravityPoint.SetParent(go.transform, false);
                gravityPoint.localPosition = new Vector3(oppositeHinge ? -.451f : .451f, gravityMarkerY, 0f);
                Component pendulum = go.AddComponent(_pendulumType);
                Set(pendulum, "pivot", pivot);
                Set(pendulum, "hingePoint", hinge);
                Set(pendulum, "gravityPoint", gravityPoint);
                Set(pendulum, "worldRoot", Reference);
                Set(pendulum, "minAngle", minimum);
                Set(pendulum, "maxAngle", maximum);
                Set(pendulum, "isDebugGizmos", false);
                Vector2 scaledOffset = Vector2.Scale(hinge.localPosition, scale);
                bool initialized = (bool)_pendulumType.GetMethod("InitializePendulum").Invoke(pendulum, null);
                Assert.That(initialized, Is.True);
                return new Board(body, pendulum, scaledOffset, pivot.position);
            }

            private static void Set(Component component, string name, object value)
            { component.GetType().GetField(name, InstanceFields).SetValue(component, value); }

            public void Simulate()
            {
                Physics2D.SyncTransforms();
                Assert.That(_scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime), Is.True);
            }

            public void Dispose()
            {
                if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
                if (!_scene.IsValid() || !_scene.isLoaded) return;
                foreach (GameObject root in _scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(_scene);
            }
        }
    }
}
