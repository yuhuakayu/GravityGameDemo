using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Resource.Scripts.Editor
{
    /// <summary>Additional continuous runs alongside the original plank-top cases.</summary>
    public static class PendulumScenarioValidation
    {
        [MenuItem("Tools/Gravity Game/Physics/Validate Stage1 Side And Far Wall")]
        public static void RunMenu() { Debug.Log(RunAndSave("additional", 10)); }

        [Serializable] public sealed class ScenarioResult
        {
            public string scenario, contactCollider, pivotMode;
            public Vector2 initialPlayerCenter, initialSurfacePoint, initialSurfaceNormal;
            public Vector2 initialPivotPosition;
            public float initialPivotDistanceToSurface;
            public bool initialTileOverlap;
            public int targetContactFrames, surfaceCrossings;
            public PendulumStageValidation.Result physics;
        }
        [Serializable] public sealed class Report
        {
            public string utc;
            public string method = "Additional unchanged Stage1 collider fixtures: each plank free-end side, the farthest clear playable vertical wall edge with real player-history pivot, and the same edge rotating about an explicitly fixed WorldRoot-origin test anchor. Initial player bounds must be clear of actual collidable Tilemap cells as well as collider outlines. This corrects the invalid solid-tile interior placement retained in additional-quick.json. Player is initialized once. Synchronous 60/240 Hz render scheduling; not measured rendered FPS.";
            public bool passed;
            public List<ScenarioResult> results = new List<ScenarioResult>();
        }

        public static string RunAndSave(string label = "additional", int revolutions = 10)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode with Stage1 loaded.");
            var source = SceneManager.GetSceneByName("Stage1");
            if (!source.IsValid() || !source.isLoaded) throw new InvalidOperationException("Stage1 is not loaded.");
            var roots = source.GetRootGameObjects();
            var world = roots.SelectMany(r => r.GetComponentsInChildren<WorldRotator>(true)).Single();
            var player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Single();
            var pendulums = roots.SelectMany(r => r.GetComponentsInChildren<PivotPendulum>(true)).OrderBy(p => p.name).ToArray();
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            float savedScale = Time.timeScale;
            try
            {
                Time.timeScale = 1f;
                foreach (int hz in new[] { 60, 240 })
                    for (int scenario = 0; scenario < 4; ++scenario)
                    {
                        var result = new ScenarioResult
                        {
                            scenario = scenario < 2 ? pendulums[scenario].name + " free-end side" : scenario == 2 ? "playable far wall / automatic pivot" : "playable far wall / fixed root-origin pivot",
                            physics = new PendulumStageValidation.Result
                            {
                                name = (scenario < 2 ? pendulums[scenario].name + " side" : scenario == 2 ? "far wall / automatic pivot" : "far wall / fixed root-origin pivot") + " / simulated " + hz + " Hz",
                                simulatedRenderHz = hz, revolutions = revolutions
                            }
                        };
                        report.results.Add(result);
                        try
                        {
                            using (var fixture = new PendulumStageValidation.Fixture(world, player, pendulums, Mathf.Min(scenario, 1)))
                            {
                                var session = new PendulumStageValidation.Session(fixture, result.physics, 1, revolutions);
                                var placement = Place(fixture, scenario, result);
                                result.physics.clonedWorldColliders = fixture.Geometry.Count;
                                Action afterStep = () => Measure(placement, fixture, result);
                                while (!session.Finished) session.AdvanceFrame(1.0 / hz, afterStep);
                                session.Finish();
                                FinalizeResult(result);
                            }
                        }
                        catch (Exception ex) { result.physics.passed = false; result.physics.error = ex.ToString(); }
                    }
            }
            finally { Time.timeScale = savedScale; }
            report.passed = report.results.All(r => r.physics.passed);
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(PendulumStageValidation.Folder);
            string safeLabel = new string(label.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            File.WriteAllText(PendulumStageValidation.Folder + "/Pendulum-" + safeLabel + ".json", json);
            return json;
        }

        internal sealed class Placement
        {
            internal Collider2D Collider;
            internal Transform Transform;
            internal Vector2 LocalA, LocalB, LocalNormal;
            internal bool Tracking = true, HasPrevious, Counted;
            internal float Side = 1f, PreviousNormal, PreviousTangent;
        }

        internal static Placement Place(PendulumStageValidation.Fixture f, int scenario, ScenarioResult result)
        {
            Placement placement;
            if (scenario < 2)
            {
                var box = f.Pendulums[scenario].GetComponent<BoxCollider2D>();
                Vector2 axis = Direction(box.transform, Vector2.right);
                Vector2 tangent = Direction(box.transform, Vector2.up);
                Vector2 center = Point(box.transform, box.offset);
                Vector2 scale = box.transform.lossyScale;
                Vector2 point = center + axis * box.size.x * Mathf.Abs(scale.x) * .5f;
                float halfHeight = box.size.y * Mathf.Abs(scale.y) * .5f;
                SetPlayer(f, point, axis);
                if (HasSolidOverlap(f)) throw new InvalidOperationException("The actual free-end side placement overlaps another Stage1 solid. This fixture cannot silently move the scene geometry.");
                placement = MakePlacement(box, point - tangent * halfHeight, point + tangent * halfHeight, axis);
                result.initialSurfacePoint = point;
                result.initialSurfaceNormal = axis;
            }
            else placement = PlaceAtFarWall(f, result);
            if (scenario == 2)
            {
                f.World.pivot = null;
                typeof(WorldRotator).GetField("_autoPlayer", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(f.World, f.Player);
                result.pivotMode = "Stage1 automatic airborne-history branch with configured render-frame delay; ground probes absent, grounded branch not covered";
            }
            else if (scenario == 3)
            {
                var anchor = new GameObject("Fixed WorldRoot Origin (Validation Only)");
                SceneManager.MoveGameObjectToScene(anchor, f.Scene);
                anchor.transform.position = f.World.GetComponent<Rigidbody2D>().position;
                f.World.pivot = anchor.transform;
                result.pivotMode = "Explicit fixed initial WorldRoot origin; far-radius stress override confined to this private fixture";
            }
            else result.pivotMode = f.World.pivot == null ? "Stage1 automatic player-history pivot" : "Explicit current player pivot";
            f.Player.SetIntendedVelocity(Vector2.zero);
            f.Guard.ResetState();
            f.TrackOriginalSide = false;
            f.HasPreviousCenter = false;
            result.contactCollider = placement.Collider.name + " / " + placement.Collider.GetType().Name;
            result.initialPlayerCenter = f.PlayerCollider.bounds.center;
            result.initialPivotPosition = f.World.pivot == null || f.World.pivot == f.Player.transform ? f.PlayerBody.position : (Vector2)f.World.pivot.position;
            result.initialPivotDistanceToSurface = Vector2.Distance(result.initialPivotPosition, result.initialSurfacePoint);
            result.initialTileOverlap = OccupiesCollidableTile(f);
            if (result.initialTileOverlap) throw new InvalidOperationException("Initial player bounds occupy collidable Tilemap cells; an Outline collider's Distance alone cannot validate a playable starting position.");
            return placement;
        }

        private struct WallCandidate
        {
            internal CompositeCollider2D Collider;
            internal Vector2 A, B;
            internal float Score;
        }
        private static Placement PlaceAtFarWall(PendulumStageValidation.Fixture f, ScenarioResult result)
        {
            var candidates = new List<WallCandidate>();
            Vector2 rootPosition = f.World.GetComponent<Rigidbody2D>().position;
            foreach (var wall in f.Geometry.OfType<CompositeCollider2D>())
                for (int path = 0; path < wall.pathCount; ++path)
                {
                    var vertices = new Vector2[wall.GetPathPointCount(path)];
                    wall.GetPath(path, vertices);
                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        Vector2 a = Point(wall.transform, vertices[i]);
                        Vector2 b = Point(wall.transform, vertices[(i + 1) % vertices.Length]);
                        Vector2 edge = b - a;
                        if (Mathf.Abs(edge.y) < f.PlayerCollider.bounds.size.y + .1f || Mathf.Abs(edge.x) > .02f) continue;
                        candidates.Add(new WallCandidate { Collider = wall, A = a, B = b, Score = ((a + b) * .5f - rootPosition).sqrMagnitude });
                    }
                }
            foreach (var candidate in candidates.OrderByDescending(c => c.Score))
            {
                Vector2 edge = (candidate.B - candidate.A).normalized;
                Vector2 point = (candidate.A + candidate.B) * .5f;
                Vector2 normal = new Vector2(edge.y, -edge.x);
                if (Vector2.Dot(normal, (Vector2)candidate.Collider.bounds.center - point) < 0f) normal = -normal;
                // Stage1's bounds center lies in its playable room. Only the side facing
                // that interior is eligible: the opposite side of an outer outline is
                // outside the level and is also empty of tiles, but is not a valid start.
                SetPlayer(f, point, normal);
                if (HasSolidOverlap(f) || OccupiesCollidableTile(f)) continue;
                result.initialSurfacePoint = point;
                result.initialSurfaceNormal = normal;
                return MakePlacement(candidate.Collider, candidate.A, candidate.B, normal);
            }
            throw new InvalidOperationException("No clear player-sized vertical wall edge exists in the cloned Stage1 CompositeCollider2D geometry.");
        }

        private static void SetPlayer(PendulumStageValidation.Fixture f, Vector2 surfacePoint, Vector2 normal)
        {
            Vector2 extents = f.PlayerCollider.bounds.extents;
            float support = Mathf.Abs(normal.x) * extents.x + Mathf.Abs(normal.y) * extents.y;
            Vector2 offset = (Vector2)f.PlayerCollider.bounds.center - f.PlayerBody.position;
            f.PlayerBody.position = surfacePoint + normal * (support + .025f) - offset;
            // Initial fixture teleport only: interpolation can leave Transform at the
            // source spawn until a rendered frame, so align the pivot-visible pose once.
            f.PlayerBody.transform.position = f.PlayerBody.position;
            Physics2D.SyncTransforms();
        }

        private static bool HasSolidOverlap(PendulumStageValidation.Fixture f)
        {
            foreach (var c in f.Geometry)
            {
                if (!c.enabled || c.isTrigger || c.shapeCount == 0 ||
                    (!(c is CompositeCollider2D) && c.compositeOperation != Collider2D.CompositeOperation.None) ||
                    Physics2D.GetIgnoreCollision(f.PlayerCollider, c)) continue;
                var distance = f.PlayerCollider.Distance(c);
                if (distance.isValid && distance.isOverlapped && distance.distance < -.0001f) return true;
            }
            return false;
        }

        private static bool OccupiesCollidableTile(PendulumStageValidation.Fixture f)
        {
            Bounds bounds = f.PlayerCollider.bounds;
            const float skin = .0001f;
            var corners = new[]
            {
                new Vector3(bounds.min.x + skin, bounds.min.y + skin, 0f),
                new Vector3(bounds.max.x - skin, bounds.min.y + skin, 0f),
                new Vector3(bounds.max.x - skin, bounds.max.y - skin, 0f),
                new Vector3(bounds.min.x + skin, bounds.max.y - skin, 0f)
            };
            foreach (var tileCollider in f.Geometry.OfType<TilemapCollider2D>())
            {
                var tilemap = tileCollider.GetComponent<Tilemap>();
                if (tilemap == null || !tileCollider.enabled || tileCollider.isTrigger) continue;
                var cells = corners.Select(tilemap.WorldToCell).ToArray();
                int minX = cells.Min(c => c.x), maxX = cells.Max(c => c.x);
                int minY = cells.Min(c => c.y), maxY = cells.Max(c => c.y);
                for (int x = minX; x <= maxX; ++x)
                    for (int y = minY; y <= maxY; ++y)
                    {
                        var cell = new Vector3Int(x, y, cells[0].z);
                        if (tilemap.HasTile(cell) && tilemap.GetColliderType(cell) != Tile.ColliderType.None) return true;
                    }
            }
            return false;
        }

        private static Placement MakePlacement(Collider2D collider, Vector2 a, Vector2 b, Vector2 normal)
        {
            Transform t = collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform;
            return new Placement
            {
                Collider = collider, Transform = t,
                LocalA = t.InverseTransformPoint(a), LocalB = t.InverseTransformPoint(b),
                LocalNormal = t.InverseTransformDirection(normal)
            };
        }

        internal static void Measure(Placement p, PendulumStageValidation.Fixture f, ScenarioResult result)
        {
            var distance = f.PlayerCollider.Distance(p.Collider);
            if (distance.isValid && distance.distance <= .01f) result.targetContactFrames++;
            Vector2 a = Point(p.Transform, p.LocalA), b = Point(p.Transform, p.LocalB);
            Vector2 tangent = (b - a).normalized, normal = Direction(p.Transform, p.LocalNormal).normalized;
            Vector2 center = f.PlayerCollider.bounds.center;
            float along = Vector2.Dot(center - a, tangent), signed = Vector2.Dot(center - a, normal);
            Vector2 extent = f.PlayerCollider.bounds.extents;
            float playerNormal = Mathf.Abs(normal.x) * extent.x + Mathf.Abs(normal.y) * extent.y;
            float playerTangent = Mathf.Abs(tangent.x) * extent.x + Mathf.Abs(tangent.y) * extent.y;
            float length = Vector2.Distance(a, b);
            bool withinEnds = along >= -playerTangent && along <= length + playerTangent;
            if (!p.Tracking && withinEnds && Mathf.Abs(signed) - playerNormal <= .05f && Mathf.Abs(signed) > .001f)
            { p.Tracking = true; p.Side = Mathf.Sign(signed); p.HasPrevious = false; p.Counted = false; }
            float previous = p.PreviousNormal * p.Side, current = signed * p.Side;
            if (p.Tracking && p.HasPrevious && previous >= 0f && current < 0f && !p.Counted)
            {
                float crossing = Mathf.Lerp(p.PreviousTangent, along, previous / Mathf.Max(.000001f, previous - current));
                if (crossing > .001f && crossing < length - .001f) { result.surfaceCrossings++; p.Counted = true; }
            }
            if (!withinEnds || Mathf.Abs(signed) - playerNormal > .25f) p.Tracking = false;
            p.PreviousNormal = signed; p.PreviousTangent = along; p.HasPrevious = true;
        }

        internal static void FinalizeResult(ScenarioResult r)
        {
            var p = r.physics;
            p.passed = p.completedHalfTurns >= p.revolutions * 2 && p.accumulatedClockwiseDegrees >= p.revolutions * 360f - 1f &&
                p.accumulatedCounterClockwiseDegrees >= p.revolutions * 360f - 1f && p.maxHingeGap < .01f && p.maxBoardAxisErrorDegrees < .1f &&
                p.maxRemainingOverlap == 0 && p.overlapFrames == 0 && p.originalSideCrossings == 0 && r.surfaceCrossings == 0 && r.targetContactFrames > 0 && !r.initialTileOverlap &&
                p.maxCorrection <= p.maxDepenetrationPerStep + .00001f;
        }

        private static Vector2 Point(Transform t, Vector2 local)
        {
            Vector2 point = t.TransformPoint(local);
            var body = t.GetComponentInParent<Rigidbody2D>();
            if (body == null) return point;
            Vector2 unrotated = Quaternion.Inverse(body.transform.rotation) * (Vector3)(point - (Vector2)body.transform.position);
            return body.position + (Vector2)(Quaternion.Euler(0f, 0f, body.rotation) * unrotated);
        }
        private static Vector2 Direction(Transform t, Vector2 local)
        {
            Vector2 direction = t.TransformDirection(local);
            var body = t.GetComponentInParent<Rigidbody2D>();
            if (body == null) return direction;
            return Quaternion.Euler(0f, 0f, body.rotation) * (Quaternion.Inverse(body.transform.rotation) * (Vector3)direction);
        }
    }
}
