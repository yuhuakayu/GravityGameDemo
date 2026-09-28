using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>Limits actual geometry travel before simulation, without changing global physics.</summary>
    public static class KinematicMotionSafety
    {
        private const float PredictionTolerance = 0.0001f;
        // A merely non-overlapping capsule can exactly fill a narrow corridor. Keep a real
        // positive gap for tangent escape and native-query rounding; this is prediction-only,
        // not a relaxed penetration tolerance or an increase to the player's recovery budget.
        private const float PredictionClearance = 0.002f;
        private const int PredictionIterations = 6;
        private const int AngleSearchIterations = 10;
        // Calls run sequentially on the physics thread; reuse the small query buffers rather
        // than allocating them for every attempted angle during a pinch.
        private static readonly List<PredictedGeometry> PredictionGeometry = new List<PredictedGeometry>(16);
        private static readonly List<RecoveryPlane> RecoveryPlanes = new List<RecoveryPlane>(16);

        private struct PredictedGeometry
        {
            public Collider2D Collider;
            public Vector2 Position;
            public float Angle;
        }

        private struct RecoveryPlane
        {
            public Vector2 Normal;
            public float Depth;
        }

        public static float ClampRotationStep(Collider2D[] geometry, Vector2 center, float requestedDegrees,
            PlayerController player, float thicknessFraction = 0.45f, float repairBudgetFraction = 0.4f,
            Rigidbody2D transportedBody = null, Vector2 transportedPosition = default, float transportedAngle = 0f)
        {
            if (player == null || geometry == null || Mathf.Abs(requestedDegrees) < 0.000001f)
                return requestedDegrees;
            var guard = player.GetComponent<CrushGuard>();
            var playerCollider = guard != null ? guard.playerCollider : player.GetComponent<Collider2D>();
            if (playerCollider == null || !playerCollider.enabled) return requestedDegrees;
            Bounds playerBounds = playerCollider.bounds;
            float thickness = Mathf.Min(playerBounds.size.x, playerBounds.size.y);
            float maxTravel = Mathf.Max(0.001f, thickness * Mathf.Clamp(thicknessFraction, 0.01f, 0.49f));
            float radians = Mathf.Abs(requestedDegrees) * Mathf.Deg2Rad;
            float farthest = 0f;
            float transportTravel = 0f;
            foreach (var collider in geometry)
            {
                if (!Usable(collider, playerCollider)) continue;
                farthest = Mathf.Max(farthest, FarthestRadius(collider.bounds, center,
                    transportedBody, transportedPosition, transportedAngle));
                if (transportedBody != null)
                    for (int x = 0; x < 2; ++x)
                        for (int y = 0; y < 2; ++y)
                        {
                            Vector2 corner = new Vector2(x == 0 ? collider.bounds.min.x : collider.bounds.max.x,
                                y == 0 ? collider.bounds.min.y : collider.bounds.max.y);
                            transportTravel = Mathf.Max(transportTravel, Vector2.Distance(corner,
                                TransportPoint(corner, transportedBody, transportedPosition, transportedAngle)));
                        }
            }
            radians = Mathf.Min(radians, AngleForTravel(Mathf.Max(0f, maxTravel - transportTravel), farthest));

            // A step small enough for CCD can still be larger than the player's bounded recovery.
            // Near a real surface, also reserve a fraction of that recovery budget. Use the
            // intersection of the possible swept region with the player's vicinity: a long
            // Composite must not use only its nearest point, or a distant edge can sweep inward.
            if (guard != null && player.antiPushEnabled)
            {
                float recovery = Mathf.Max(0f, guard.maxDepenetrationPerStep * repairBudgetFraction - 0.002f);
                float possibleTravel = 2f * farthest * Mathf.Sin(radians * 0.5f);
                float ownTravel = player.IntendedVelocity.magnitude * Time.fixedDeltaTime;
                Bounds vicinity = playerBounds;
                vicinity.Expand(new Vector3(2f * (possibleTravel + ownTravel + recovery),
                    2f * (possibleTravel + ownTravel + recovery), 0f));
                foreach (var collider in geometry)
                {
                    if (!Usable(collider, playerCollider)) continue;
                    var distance = playerCollider.Distance(collider);
                    if (!distance.isValid || distance.distance > possibleTravel + ownTravel + recovery) continue;
                    Bounds region = Intersect(collider.bounds, vicinity);
                    float nearRadius = FarthestRadius(region, center, transportedBody, transportedPosition, transportedAngle);
                    float transportedDistance = transportedBody == null ? 0f : Vector2.Distance(distance.pointB,
                        TransportPoint(distance.pointB, transportedBody, transportedPosition, transportedAngle));
                    float budget = Mathf.Max(0f, distance.distance) + recovery - transportedDistance;
                    radians = Mathf.Min(radians, AngleForTravel(Mathf.Max(0f, budget), nearRadius));
                }
            }
            float safeDegrees = Mathf.Sign(requestedDegrees) * radians * Mathf.Rad2Deg;
            if (guard != null && player.antiPushEnabled && transportedBody == null)
                safeDegrees = ClampToRecoverablePose(geometry, center, safeDegrees, player,
                    playerCollider, guard, repairBudgetFraction);
            return safeDegrees;
        }

        private static float ClampToRecoverablePose(Collider2D[] geometry, Vector2 center,
            float requestedDegrees, PlayerController player, Collider2D playerCollider,
            CrushGuard guard, float repairBudgetFraction)
        {
            Rigidbody2D playerBody = playerCollider.attachedRigidbody;
            if (playerBody == null || Mathf.Abs(requestedDegrees) < 0.000001f) return requestedDegrees;

            PredictionGeometry.Clear();
            foreach (Collider2D collider in geometry)
            {
                if (!Usable(collider, playerCollider) || collider.shapeCount == 0) continue;
                Rigidbody2D body = collider.attachedRigidbody;
                PredictionGeometry.Add(new PredictedGeometry
                {
                    Collider = collider,
                    // The native Distance pose overload takes the attached BODY's physical
                    // pose, including for offset child colliders and CompositeCollider2D.
                    Position = body != null ? body.position : (Vector2)collider.transform.position,
                    Angle = body != null ? body.rotation : collider.transform.eulerAngles.z
                });
            }
            if (PredictionGeometry.Count == 0) return requestedDegrees;

            float dt = Time.fixedDeltaTime;
            Vector2 playerPosition = playerBody.position + player.IntendedVelocity * dt;
            // Root executes before PlayerController adds this step's gravity. Keep that
            // additional travel inside the existing root share, not the board's reserved share.
            float gravityMargin = Physics2D.gravity.magnitude * player.SimulatedGravityScale * dt * dt;
            float budget = Mathf.Max(0f, guard.maxDepenetrationPerStep * repairBudgetFraction -
                0.002f - gravityMargin);
            if (HasRecoverablePlayerPose(center, requestedDegrees, playerCollider,
                playerPosition, playerBody.rotation, budget)) return requestedDegrees;

            // Capture the original failed candidate before zero-angle/bisection queries replace
            // the scratch planes. One blocking wall or an exhausted travel budget is not a pinch.
            bool opposedFaces = TryGetOpposedRecoveryNormals(out Vector2 normalA, out Vector2 normalB);
            Quaternion backToCurrentPose = Quaternion.Euler(0f, 0f, -requestedDegrees);
            normalA = backToCurrentPose * normalA;
            normalB = backToCurrentPose * normalB;

            // No feasible angle is deliberately allowed to wait: actual player gravity and
            // its tangent motion still run, so leaving the narrow channel can unlock rotation.
            // Never move the player here, ignore a collision, or enlarge its repair budget.
            float lower = 0f, upper = 1f;
            if (HasRecoverablePlayerPose(center, 0f, playerCollider,
                playerPosition, playerBody.rotation, budget))
            {
                for (int iteration = 0; iteration < AngleSearchIterations; ++iteration)
                {
                    float fraction = (lower + upper) * 0.5f;
                    if (HasRecoverablePlayerPose(center, requestedDegrees * fraction, playerCollider,
                        playerPosition, playerBody.rotation, budget)) lower = fraction;
                    else upper = fraction;
                }
            }
            float safeDegrees = requestedDegrees * lower;
            if (opposedFaces && Mathf.Abs(safeDegrees) <= 0.001f && lower <= 0.001f)
            {
                // Exactly one notification for this real root step, never one per prediction.
                // These normals have been rotated back from the failed virtual root pose into
                // the current physical pose. Guard owns the threshold, cast and shared budget.
                guard.NotifyPredictedCrushBlocked(normalA, normalB);
            }
            return safeDegrees;
        }

        private static bool TryGetOpposedRecoveryNormals(out Vector2 normalA, out Vector2 normalB)
        {
            normalA = normalB = Vector2.zero;
            float closestToOpposed = -0.9f;
            for (int first = 0; first < RecoveryPlanes.Count; ++first)
                for (int second = first + 1; second < RecoveryPlanes.Count; ++second)
                {
                    float alignment = Vector2.Dot(RecoveryPlanes[first].Normal, RecoveryPlanes[second].Normal);
                    if (alignment >= closestToOpposed) continue;
                    closestToOpposed = alignment;
                    normalA = RecoveryPlanes[first].Normal;
                    normalB = RecoveryPlanes[second].Normal;
                }
            return closestToOpposed < -0.9f;
        }

        private static bool HasRecoverablePlayerPose(Vector2 center, float degrees,
            Collider2D playerCollider, Vector2 playerPosition, float playerAngle, float budget)
        {
            RecoveryPlanes.Clear();
            Vector2 correction = Vector2.zero;
            Quaternion rotation = Quaternion.Euler(0f, 0f, degrees);
            for (int iteration = 0; iteration <= PredictionIterations; ++iteration)
            {
                bool needsRecovery = false;
                foreach (PredictedGeometry geometry in PredictionGeometry)
                {
                    // A root turn transports independent boards rigidly as well; their own
                    // subsequent swing retains the existing transported-body travel limit.
                    Vector2 position = center + (Vector2)(rotation * (Vector3)(geometry.Position - center));
                    ColliderDistance2D distance = Physics2D.Distance(playerCollider,
                        playerPosition + correction, playerAngle, geometry.Collider,
                        position, geometry.Angle + degrees);
                    if (!distance.isValid) continue;
                    if (distance.distance >= PredictionClearance) continue;
                    needsRecovery = true;
                    Vector2 normal = distance.isOverlapped
                        ? -distance.normal : distance.pointA - distance.pointB;
                    if (normal.sqrMagnitude < 0.0000000001f)
                        normal = distance.isOverlapped ? -distance.normal : distance.normal;
                    if (normal.sqrMagnitude < 0.000000000001f) return false;
                    normal.Normalize();
                    AddRecoveryPlane(normal, PredictionClearance - distance.distance + PredictionTolerance +
                        Vector2.Dot(normal, correction));
                }
                if (!needsRecovery) return true;
                if (iteration == PredictionIterations || !TryFindRecovery(budget, out correction)) return false;
                // Keep previously found faces: a Composite query can expose a different
                // feature after correction. Clearing one face must not undo an earlier one.
            }
            return false;
        }

        private static void AddRecoveryPlane(Vector2 normal, float depth)
        {
            for (int index = 0; index < RecoveryPlanes.Count; ++index)
            {
                RecoveryPlane existing = RecoveryPlanes[index];
                if ((existing.Normal - normal).sqrMagnitude > 0.0000000001f) continue;
                existing.Depth = Mathf.Max(existing.Depth, depth);
                RecoveryPlanes[index] = existing;
                return;
            }
            RecoveryPlanes.Add(new RecoveryPlane { Normal = normal, Depth = depth });
        }

        private static bool TryFindRecovery(float budget, out Vector2 correction)
        {
            correction = Vector2.zero;
            float bestSquared = float.PositiveInfinity;
            float budgetSquared = budget * budget;
            for (int first = 0; first < RecoveryPlanes.Count; ++first)
            {
                RecoveryPlane a = RecoveryPlanes[first];
                ConsiderRecovery(a.Normal * a.Depth, budgetSquared, ref correction, ref bestSquared);
                for (int second = first + 1; second < RecoveryPlanes.Count; ++second)
                {
                    RecoveryPlane b = RecoveryPlanes[second];
                    float determinant = a.Normal.x * b.Normal.y - a.Normal.y * b.Normal.x;
                    if (Mathf.Abs(determinant) < 0.000001f) continue;
                    Vector2 candidate = new Vector2(
                        (a.Depth * b.Normal.y - a.Normal.y * b.Depth) / determinant,
                        (a.Normal.x * b.Depth - a.Depth * b.Normal.x) / determinant);
                    ConsiderRecovery(candidate, budgetSquared, ref correction, ref bestSquared);
                }
            }
            return !float.IsPositiveInfinity(bestSquared);
        }

        private static void ConsiderRecovery(Vector2 candidate, float budgetSquared,
            ref Vector2 correction, ref float bestSquared)
        {
            float squared = candidate.sqrMagnitude;
            if (float.IsNaN(squared) || float.IsInfinity(squared) || squared > budgetSquared || squared >= bestSquared) return;
            foreach (RecoveryPlane plane in RecoveryPlanes)
                if (Vector2.Dot(plane.Normal, candidate) < plane.Depth - 0.000001f) return;
            bestSquared = squared;
            correction = candidate;
        }

        private static bool Usable(Collider2D collider, Collider2D player)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
                !collider.isTrigger && collider != player && collider.attachedRigidbody != player.attachedRigidbody &&
                !Physics2D.GetIgnoreLayerCollision(player.gameObject.layer, collider.gameObject.layer) &&
                !Physics2D.GetIgnoreCollision(player, collider);
        }

        private static float AngleForTravel(float distance, float radius)
        {
            if (radius < 0.000001f) return Mathf.PI;
            return 2f * Mathf.Asin(Mathf.Clamp01(distance / (2f * radius)));
        }

        private static Vector2 TransportPoint(Vector2 point, Rigidbody2D body, Vector2 position, float angle)
        {
            if (body == null) return point;
            return position + (Vector2)(Quaternion.Euler(0f, 0f, angle - body.rotation) * (Vector3)(point - body.position));
        }

        private static float FarthestRadius(Bounds bounds, Vector2 center, Rigidbody2D body, Vector2 position, float angle)
        {
            float radius = 0f;
            for (int x = 0; x < 2; ++x)
                for (int y = 0; y < 2; ++y)
                {
                    Vector2 corner = new Vector2(x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y);
                    radius = Mathf.Max(radius, Vector2.Distance(center, TransportPoint(corner, body, position, angle)));
                }
            return radius;
        }

        private static Bounds Intersect(Bounds a, Bounds b)
        {
            Vector3 minimum = Vector3.Max(a.min, b.min);
            Vector3 maximum = Vector3.Min(a.max, b.max);
            if (minimum.x > maximum.x || minimum.y > maximum.y) return new Bounds(a.ClosestPoint(b.center), Vector3.zero);
            minimum.z = maximum.z = 0f;
            var result = new Bounds();
            result.SetMinMax(minimum, maximum);
            return result;
        }
    }
}
