using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts
{
    public enum CrushResponse { PushAlongGravity, IgnoreCollision, Kill }

    /// <summary>
    /// Position-only recovery for moving level geometry. PlayerController calls this after
    /// the physics solver and after restoring its own velocity; this component never drives velocity.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class CrushGuard : MonoBehaviour
    {
        [SerializeField] public Collider2D playerCollider;
        [SerializeField] public LayerMask worldGeometryMask;
        [Tooltip("全部穿透迭代与夹死抬出共用的单个物理步位置修正预算。")]
        [SerializeField, Min(0f)] public float maxDepenetrationPerStep = 0.15f;
        [SerializeField, Min(1)] public int overlapIterations = 3;
        [SerializeField, Min(1)] public int crushFrameThreshold = 10;
        [SerializeField] public CrushResponse crushResponse = CrushResponse.PushAlongGravity;
        [Tooltip("IgnoreCollision 策略临时忽略夹住的碰撞对，分离或达到此时限后恢复。")]
        [SerializeField, Min(0.02f)] public float ignoreCollisionTimeout = 0.5f;
        [SerializeField] public bool drawDepenetrationGizmo;
        [SerializeField, Min(0f)] public float preventiveCastSkin = 0.001f;
        [Tooltip("预防抬出改沿备用表面方向后，保持该方向的物理步数；每步仍重新验证路径。")]
        [SerializeField, Min(1)] public int preventiveDirectionHoldSteps = 12;

        public Vector2 LastCorrection { get; private set; }
        public float LastCorrectionDistance { get; private set; }
        public int RemainingOverlapCount { get; private set; }
        public int CrushFrames { get; private set; }
        public int EscapeFrames { get; private set; }
        public bool IsCrushed { get; private set; }
        public int IgnoredCollisionCount => _ignoredPairs.Count;
        public int CapturedSideCount => _sideAnchors.Count;
        public int LastSideCorrectionCount { get; private set; }
        public Collider2D LastConstraintCollider { get; private set; }
        public Vector2 LastEntryNormal { get; private set; }
        public Vector2 LastMtvNormal { get; private set; }
        public float LastMtvAlignment { get; private set; }
        public float LastEntryPlaneDepth { get; private set; }
        public float LastMtvDepth { get; private set; }
        public Vector2 LastRequestedCorrection { get; private set; }
        public int LastConstraintCount { get; private set; }
        public bool LastConstraintSolutionFound { get; private set; }
        public int PredictedCrushFrames { get; private set; }
        public int PreventiveEscapeFrames { get; private set; }
        public bool IsPreventiveEscapeActive { get; private set; }
        public Vector2 LastPreventiveLift { get; private set; }
        public Vector2 LastPreventiveDirection { get; private set; }
        public float LastPreventiveMinimumDistance { get; private set; }
        public bool LastPreventiveUsedFallback { get; private set; }
        public int PreventiveDirectionHoldRemaining => _heldEscapeSteps;

        private const float PenetrationTolerance = 0.0001f;
        private Rigidbody2D _body;
        private PlayerController _player;
        // The List overload grows as necessary; dense tilemap contacts are never truncated.
        private readonly List<Collider2D> _overlaps = new List<Collider2D>(16);
        private readonly List<Penetration> _penetrations = new List<Penetration>(16);
        private readonly List<HalfPlane> _constraints = new List<HalfPlane>(32);
        private readonly List<IgnoredPair> _ignoredPairs = new List<IgnoredPair>(4);
        private readonly Dictionary<Collider2D, SideAnchor> _sideAnchors = new Dictionary<Collider2D, SideAnchor>();
        private readonly List<Collider2D> _nearbyGeometry = new List<Collider2D>(16);
        private readonly HashSet<Collider2D> _seenGeometry = new HashSet<Collider2D>();
        private readonly List<Collider2D> _expiredAnchors = new List<Collider2D>(16);
        private readonly PhysicsShapeGroup2D _playerShapes = new PhysicsShapeGroup2D(4, 16);
        private readonly List<Collider2D> _escapeGeometry = new List<Collider2D>(16);
        private readonly List<Vector2> _escapeNormals = new List<Vector2>(8);
        private readonly List<RaycastHit2D> _escapeHits = new List<RaycastHit2D>(16);
        private readonly List<Vector2> _escapeBlockingNormals = new List<Vector2>(8);
        private readonly List<Vector2> _escapeCandidates = new List<Vector2>(16);
        private Vector2 _heldEscapeDirection;
        private int _heldEscapeSteps;
        private bool _pendingPredictedBlock;
        private Vector2 _pendingBlockNormalA, _pendingBlockNormalB;
        private Vector2 _activeBlockNormalA, _activeBlockNormalB;
        private Collider2D _capturedPlayerCollider;
        private Vector2 _previousCorrectionDirection;
        private int _directionReversals;
        private CrushResponse _activeResponse;

        private struct Penetration
        {
            public Collider2D Collider;
            public Vector2 Correction;
            public Vector2 EntryNormal;
            public float EntryDepth;
            public Vector2 CurrentNormal;
            public float CurrentDepth;
            public bool HasEntry;
            public bool IncludeCurrent;
        }

        private struct HalfPlane
        {
            public Vector2 Normal;
            public float Depth;
        }

        private struct SideAnchor
        {
            public Vector3 LocalPoint;
            public Vector3 LocalNormal;
            public Rigidbody2D Body;
            public bool UsesBodySpace;
            public float DistanceBeforeStep;
        }

        private struct IgnoredPair
        {
            public Collider2D Player;
            public Collider2D Other;
            public float Age;
        }

        private void Awake() => ResolveReferences();

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            maxDepenetrationPerStep = Mathf.Max(0f, maxDepenetrationPerStep);
            overlapIterations = Mathf.Max(1, overlapIterations);
            crushFrameThreshold = Mathf.Max(1, crushFrameThreshold);
            ignoreCollisionTimeout = Mathf.Max(0.02f, ignoreCollisionTimeout);
            preventiveCastSkin = Mathf.Max(0f, preventiveCastSkin);
            preventiveDirectionHoldSteps = Mathf.Max(1, preventiveDirectionHoldSteps);
        }

        private void ResolveReferences()
        {
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            if (_player == null) _player = GetComponent<PlayerController>();
            if (playerCollider == null)
            {
                foreach (Collider2D candidate in GetComponentsInChildren<Collider2D>())
                {
                    if (candidate.isTrigger || candidate.attachedRigidbody != _body) continue;
                    playerCollider = candidate;
                    break;
                }
            }
            if (worldGeometryMask.value == 0 && _player != null)
                worldGeometryMask = _player.groundLayer.value | _player.wallLayer.value;
        }

        private void LateUpdate()
        {
            // A paused preview has no fixed steps, but must still release any owned ignores.
            if (_player == null || !_player.antiPushEnabled || !_player.IsGameplayActive)
                ResetState();
        }

        private void OnDisable() => ResetState();

        public void ResetState()
        {
            RestoreIgnoredPairs();
            LastCorrection = Vector2.zero;
            LastCorrectionDistance = 0f;
            LastSideCorrectionCount = 0;
            ResetConstraintDiagnostics();
            RemainingOverlapCount = 0;
            _sideAnchors.Clear();
            _constraints.Clear();
            _capturedPlayerCollider = null;
            ResetPredictedCrush();
            ResetCrushTracking();
        }

        /// <summary>Called once per attempted geometry step, only for a predicted opposing-face pinch.</summary>
        public void NotifyPredictedCrushBlocked(Vector2 normalA, Vector2 normalB)
        {
            if (!isActiveAndEnabled || _player == null || !_player.antiPushEnabled || !_player.IsGameplayActive ||
                crushResponse != CrushResponse.PushAlongGravity || normalA.sqrMagnitude < 0.000001f ||
                normalB.sqrMagnitude < 0.000001f) return;
            normalA.Normalize();
            normalB.Normalize();
            float opposition = Vector2.Dot(normalA, normalB);
            if (opposition > -0.5f) return;
            if (!_pendingPredictedBlock || opposition < Vector2.Dot(_pendingBlockNormalA, _pendingBlockNormalB))
            {
                _pendingBlockNormalA = normalA;
                _pendingBlockNormalB = normalB;
            }
            _pendingPredictedBlock = true;
        }

        private void ConsumePredictedCrushRequest()
        {
            bool requested = _pendingPredictedBlock;
            _activeBlockNormalA = _pendingBlockNormalA;
            _activeBlockNormalB = _pendingBlockNormalB;
            _pendingPredictedBlock = false;
            _pendingBlockNormalA = _pendingBlockNormalB = Vector2.zero;
            LastPreventiveLift = LastPreventiveDirection = Vector2.zero;
            LastPreventiveMinimumDistance = 0f;
            LastPreventiveUsedFallback = false;
            if (!requested || crushResponse != CrushResponse.PushAlongGravity)
            {
                PredictedCrushFrames = PreventiveEscapeFrames = 0;
                IsPreventiveEscapeActive = false;
                _heldEscapeDirection = Vector2.zero;
                _heldEscapeSteps = 0;
                return;
            }
            ++PredictedCrushFrames;
            IsPreventiveEscapeActive = PredictedCrushFrames >= Mathf.Max(1, crushFrameThreshold);
            if (IsPreventiveEscapeActive) ++PreventiveEscapeFrames;
        }

        private void ResetPredictedCrush()
        {
            _pendingPredictedBlock = false;
            _pendingBlockNormalA = _pendingBlockNormalB = Vector2.zero;
            _activeBlockNormalA = _activeBlockNormalB = Vector2.zero;
            PredictedCrushFrames = PreventiveEscapeFrames = 0;
            IsPreventiveEscapeActive = false;
            LastPreventiveLift = LastPreventiveDirection = Vector2.zero;
            LastPreventiveMinimumDistance = 0f;
            LastPreventiveUsedFallback = false;
            _heldEscapeDirection = Vector2.zero;
            _heldEscapeSteps = 0;
        }

        /// <summary>
        /// Capture the player's original side before any geometry is simulated. A shortest
        /// distance measured after a thin wall crosses the player's centre selects the far side;
        /// keep the entry face in geometry-local space instead, including while overlap persists.
        /// </summary>
        public void CaptureBeforePhysics(float dt = 0f)
        {
            ResolveReferences();
            if (!isActiveAndEnabled || _body == null || !_body.simulated || playerCollider == null ||
                !playerCollider.enabled || _player == null || !_player.antiPushEnabled || !_player.IsGameplayActive)
            {
                ResetState();
                return;
            }
            if (_capturedPlayerCollider != playerCollider) _sideAnchors.Clear();
            _capturedPlayerCollider = playerCollider;
            _nearbyGeometry.Clear();
            _seenGeometry.Clear();
            Bounds bounds = playerCollider.bounds;
            Vector2 ownStep = _player.IntendedVelocity * (dt > 0f ? dt : Time.fixedDeltaTime);
            float padding = Mathf.Max(0.01f, maxDepenetrationPerStep * 2f);
            Vector2 querySize = (Vector2)bounds.size + new Vector2(
                (Mathf.Abs(ownStep.x) + padding) * 2f, (Mathf.Abs(ownStep.y) + padding) * 2f);
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(worldGeometryMask);
            filter.useTriggers = false;
            gameObject.scene.GetPhysicsScene2D().OverlapBox(bounds.center, querySize, 0f, filter, _nearbyGeometry);
            foreach (Collider2D other in _nearbyGeometry)
            {
                if (!IsWorldGeometry(other) || Physics2D.GetIgnoreCollision(playerCollider, other)) continue;
                ColliderDistance2D distance = playerCollider.Distance(other);
                if (!distance.isValid) continue;
                if (distance.normal.sqrMagnitude < 0.000001f)
                {
                    // A coincident/degenerate query must not discard a previously reliable side.
                    if (_sideAnchors.ContainsKey(other)) _seenGeometry.Add(other);
                    continue;
                }
                _seenGeometry.Add(other);
                SideAnchor anchor;
                Rigidbody2D geometryBody = other.attachedRigidbody;
                if (!_sideAnchors.TryGetValue(other, out anchor) || distance.distance > PenetrationTolerance ||
                    anchor.Body != geometryBody || anchor.UsesBodySpace != (geometryBody != null))
                {
                    // The reported normal follows the witness-point direction. For separated
                    // shapes pointA - pointB points out toward the player; in overlap the witness
                    // points cross and the signed-distance MTV reverses it. Do not negate a
                    // separated normal: that would capture the far side before first contact.
                    Vector2 outwardNormal = distance.isOverlapped
                        ? -distance.normal : distance.pointA - distance.pointB;
                    if (outwardNormal.sqrMagnitude < 0.000001f)
                        outwardNormal = distance.isOverlapped ? -distance.normal : distance.normal;
                    outwardNormal.Normalize();
                    anchor.Body = geometryBody;
                    anchor.UsesBodySpace = geometryBody != null;
                    if (anchor.UsesBodySpace)
                    {
                        // Distance points use the physics pose, while an interpolated Transform
                        // may still render the previous pose. Body-local data includes the collider's
                        // baked scale/offset without mixing these two coordinate systems.
                        anchor.LocalPoint = Rotate(distance.pointB - geometryBody.position, -geometryBody.rotation);
                        anchor.LocalNormal = Rotate(outwardNormal, -geometryBody.rotation).normalized;
                    }
                    else
                    {
                        Vector3 point = new Vector3(distance.pointB.x, distance.pointB.y, other.transform.position.z);
                        anchor.LocalPoint = other.transform.InverseTransformPoint(point);
                        // Normals are covectors: include any non-uniform transform scale.
                        anchor.LocalNormal = other.transform.localToWorldMatrix.transpose.MultiplyVector(outwardNormal).normalized;
                    }
                }
                anchor.DistanceBeforeStep = distance.distance;
                _sideAnchors[other] = anchor;
            }
            _expiredAnchors.Clear();
            foreach (var entry in _sideAnchors)
                if (entry.Key == null || !_seenGeometry.Contains(entry.Key)) _expiredAnchors.Add(entry.Key);
            foreach (Collider2D other in _expiredAnchors) _sideAnchors.Remove(other);
        }

        public bool TryGetCapturedSide(Collider2D other, out Vector2 point, out Vector2 normal, out float distanceBeforeStep)
        {
            SideAnchor anchor;
            if (other != null && _sideAnchors.TryGetValue(other, out anchor))
            {
                if (anchor.UsesBodySpace)
                {
                    Rigidbody2D body = anchor.Body;
                    if (body == null || other.attachedRigidbody != body)
                    {
                        point = normal = Vector2.zero;
                        distanceBeforeStep = 0f;
                        return false;
                    }
                    point = body.position + Rotate(anchor.LocalPoint, body.rotation);
                    normal = Rotate(anchor.LocalNormal, body.rotation).normalized;
                }
                else
                {
                    point = other.transform.TransformPoint(anchor.LocalPoint);
                    normal = ((Vector2)other.transform.worldToLocalMatrix.transpose.MultiplyVector(anchor.LocalNormal)).normalized;
                }
                distanceBeforeStep = anchor.DistanceBeforeStep;
                return normal.sqrMagnitude > 0.000001f;
            }
            point = normal = Vector2.zero;
            distanceBeforeStep = 0f;
            return false;
        }

        public void ResolveAfterPhysics(float dt)
        {
            ResolveReferences();
            if (!isActiveAndEnabled || _body == null || !_body.simulated || playerCollider == null ||
                !playerCollider.enabled || _player == null || !_player.antiPushEnabled ||
                !_player.IsGameplayActive)
            {
                ResetState();
                return;
            }

            MaintainIgnoredPairs(Mathf.Max(0f, dt));
            ConsumePredictedCrushRequest();
            LastCorrection = Vector2.zero;
            LastCorrectionDistance = 0f;
            LastSideCorrectionCount = 0;
            ResetConstraintDiagnostics();
            Vector2 stepStart = _body.position;
            float remainingBudget = Mathf.Max(0f, maxDepenetrationPerStep);
            _constraints.Clear();

            CollectPenetrations();
            if (_penetrations.Count == 0)
            {
                RemainingOverlapCount = 0;
                ResetCrushTracking();
                TryPreventiveEscape(ref remainingBudget);
                DrawCorrection(stepStart, dt);
                return;
            }

            // Reserve the whole shared budget for the escape direction once pinned. Ordinary
            // separation must not consume it first and prevent the requested upward escape.
            if (IsCrushed && _activeResponse == CrushResponse.PushAlongGravity)
            {
                Vector2 upward = Physics2D.gravity.sqrMagnitude > 0.000001f
                    ? -Physics2D.gravity.normalized : Vector2.up;
                // A ceiling is not an escape route. Slide the upward recovery along entry
                // faces rather than lift through a thin platform to its opposite side.
                foreach (Penetration penetration in _penetrations)
                {
                    Vector2 point, normal;
                    float beforeDistance;
                    if (!TryGetCapturedSide(penetration.Collider, out point, out normal, out beforeDistance)) continue;
                    float intoSurface = Vector2.Dot(upward, normal);
                    if (intoSurface < 0f) upward -= normal * intoSurface;
                }
                ApplyCorrection(upward * remainingBudget, ref remainingBudget);
            }
            else
            {
                for (int iteration = 0; iteration < Mathf.Max(1, overlapIterations); ++iteration)
                {
                    if (iteration > 0) CollectPenetrations();
                    if (_penetrations.Count == 0 || remainingBudget <= PenetrationTolerance) break;
                    Vector2 deepest = Vector2.zero;
                    foreach (Penetration penetration in _penetrations)
                    {
                        if (OwnsIgnoredPair(penetration.Collider)) continue;
                        if (penetration.Correction.sqrMagnitude > deepest.sqrMagnitude)
                            deepest = penetration.Correction;
                        if (penetration.HasEntry)
                            AddConstraint(penetration.EntryNormal, penetration.EntryDepth);
                        if (penetration.IncludeCurrent)
                            AddConstraint(penetration.CurrentNormal, penetration.CurrentDepth);
                    }
                    Vector2 solution;
                    LastConstraintCount = _constraints.Count;
                    LastConstraintSolutionFound = TrySolveConstraints(out solution);
                    // The solution is measured from this step's original position. Keeping
                    // earlier feature planes prevents a later CompositeCollider feature from
                    // moving the player back into a face repaired by a previous iteration.
                    Vector2 correction = LastConstraintSolutionFound ? solution - LastCorrection : deepest;
                    // Incompatible opposed half-planes mean real pinching; retain the deepest
                    // bounded recovery so the existing direction-reversal crush detector works.
                    if (correction.sqrMagnitude <= PenetrationTolerance * PenetrationTolerance) break;
                    ApplyCorrection(correction, ref remainingBudget);
                }
            }

            CollectPenetrations();
            RemainingOverlapCount = _penetrations.Count;
            UpdateCrushTracking();
            if (RemainingOverlapCount == 0) TryPreventiveEscape(ref remainingBudget);

            DrawCorrection(stepStart, dt);
        }

        private void DrawCorrection(Vector2 stepStart, float dt)
        {
            if (drawDepenetrationGizmo && LastCorrection.sqrMagnitude > 0f)
                Debug.DrawRay(new Vector3(stepStart.x, stepStart.y, transform.position.z),
                    LastCorrection, IsCrushed || IsPreventiveEscapeActive ? Color.magenta : Color.cyan,
                    Mathf.Max(dt, 0.02f), false);
        }

        private void TryPreventiveEscape(ref float remainingBudget)
        {
            if (!IsPreventiveEscapeActive || _player.IsDead || remainingBudget <= PenetrationTolerance ||
                crushResponse != CrushResponse.PushAlongGravity) return;
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(worldGeometryMask);
            filter.useTriggers = false;
            Bounds bounds = playerCollider.bounds;
            float skin = Mathf.Max(PenetrationTolerance, preventiveCastSkin);
            Vector2 size = (Vector2)bounds.size + Vector2.one * (2f * (remainingBudget + skin));
            _escapeGeometry.Clear();
            gameObject.scene.GetPhysicsScene2D().OverlapBox(bounds.center, size, 0f, filter, _escapeGeometry);
            _escapeNormals.Clear();
            float nearDistance = Mathf.Max(skin, Physics2D.defaultContactOffset * 2f);
            foreach (Collider2D other in _escapeGeometry)
            {
                if (!IsWorldGeometry(other) || Physics2D.GetIgnoreCollision(playerCollider, other)) continue;
                ColliderDistance2D distance = playerCollider.Distance(other);
                if (!distance.isValid || distance.distance < -PenetrationTolerance) return;
                if (distance.distance > nearDistance) continue;
                Vector2 normal = OutwardNormal(other, distance);
                if (normal.sqrMagnitude > 0.000001f) _escapeNormals.Add(normal);
            }
            // Actual close faces take precedence over candidate-pose normals at curved features.
            // Before both faces are touching, use the predictor's opposing pair as the corridor.
            if (_escapeNormals.Count < 2)
            {
                _escapeNormals.Add(_activeBlockNormalA);
                _escapeNormals.Add(_activeBlockNormalB);
            }
            Vector2 upward = Physics2D.gravity.sqrMagnitude > 0.000001f ? -Physics2D.gravity.normalized : Vector2.up;
            Vector2 projected = ProjectEscapeDirection(upward);
            LastPreventiveDirection = projected;
            float magnitude = projected.magnitude;
            Vector2 direction = magnitude > 0.000001f ? projected / magnitude : Vector2.zero;
            float allowed = 0f;
            _escapeBlockingNormals.Clear();
            // A chosen fallback is held briefly so a corner cannot alternate between its two
            // tangents every step. It still has to pass the complete cast/endpoint checks anew.
            if (_heldEscapeSteps > 0)
            {
                allowed = FindSafeEscapeDistance(_heldEscapeDirection, remainingBudget, filter, skin);
                if (allowed > PenetrationTolerance)
                {
                    direction = _heldEscapeDirection;
                    --_heldEscapeSteps;
                    LastPreventiveUsedFallback = true;
                }
                else _heldEscapeSteps = 0;
            }
            if (allowed <= PenetrationTolerance && magnitude > 0.000001f)
                allowed = FindSafeEscapeDistance(direction, remainingBudget * Mathf.Min(1f, magnitude), filter, skin);
            if (allowed <= PenetrationTolerance)
            {
                // The upward tangent can point into a second feature of the same Composite.
                // Try the reverse tangent and tangents of the actual blocking cast faces. Do not
                // reject them using zero-clearance half-planes: the measured finite gap may admit
                // a short safe move. Cast and signed-distance checks decide how much is possible.
                _escapeCandidates.Clear();
                AddEscapeCandidate(-projected);
                foreach (Vector2 normal in _escapeNormals) AddEscapeTangents(normal);
                foreach (Vector2 normal in _escapeBlockingNormals) AddEscapeTangents(normal);
                float bestScore = 0f;
                foreach (Vector2 candidate in _escapeCandidates)
                {
                    float distance = FindSafeEscapeDistance(candidate, remainingBudget, filter, skin);
                    // Prefer useful travel, with upward progress breaking near ties. Downward
                    // surface travel is allowed only when it is a verified route out of a corner.
                    float score = distance * (1f + 0.25f * Vector2.Dot(candidate, upward));
                    if (distance <= PenetrationTolerance || score <= bestScore) continue;
                    bestScore = score;
                    allowed = distance;
                    direction = candidate;
                }
                if (allowed > PenetrationTolerance)
                {
                    _heldEscapeDirection = direction;
                    _heldEscapeSteps = Mathf.Max(1, preventiveDirectionHoldSteps) - 1;
                    LastPreventiveUsedFallback = true;
                }
            }
            if (allowed <= PenetrationTolerance) return;
            if (LastPreventiveUsedFallback) LastPreventiveDirection = direction;
            Vector2 before = _body.position;
            ApplyCorrection(direction * allowed, ref remainingBudget);
            LastPreventiveLift = _body.position - before;
            // Verify the actual physical endpoint as well. Previously the no-overlap early path
            // left RemainingOverlapCount at zero even if the virtual/actual GJK queries differed.
            LastPreventiveMinimumDistance = float.PositiveInfinity;
            foreach (Collider2D other in _escapeGeometry)
            {
                if (!IsWorldGeometry(other) || Physics2D.GetIgnoreCollision(playerCollider, other)) continue;
                ColliderDistance2D distance = playerCollider.Distance(other);
                if (distance.isValid) LastPreventiveMinimumDistance = Mathf.Min(LastPreventiveMinimumDistance, distance.distance);
            }
            if (float.IsPositiveInfinity(LastPreventiveMinimumDistance)) LastPreventiveMinimumDistance = 0f;
            CollectPenetrations();
            RemainingOverlapCount = _penetrations.Count;
            if (RemainingOverlapCount > 0) UpdateCrushTracking();
        }

        private float FindSafeEscapeDistance(Vector2 direction, float requestedDistance,
            ContactFilter2D filter, float skin)
        {
            if (direction.sqrMagnitude < 0.000001f || requestedDistance <= PenetrationTolerance) return 0f;
            float allowed = requestedDistance;
            _escapeHits.Clear();
            playerCollider.Cast(direction, filter, _escapeHits, allowed + skin, true);
            foreach (RaycastHit2D hit in _escapeHits)
            {
                Collider2D other = hit.collider;
                if (!IsWorldGeometry(other) || Physics2D.GetIgnoreCollision(playerCollider, other)) continue;
                if (hit.distance <= skin)
                {
                    ColliderDistance2D startDistance = playerCollider.Distance(other);
                    // Cast reports initial tangencies as fraction zero. A tangent/away move may
                    // leave that face; all nearby geometry is checked again at the virtual endpoint.
                    if (startDistance.isValid && startDistance.distance >= -PenetrationTolerance &&
                        Vector2.Dot(direction, OutwardNormal(other, startDistance)) >= -0.000001f) continue;
                }
                if (hit.normal.sqrMagnitude > 0.000001f) _escapeBlockingNormals.Add(hit.normal.normalized);
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - skin));
            }
            if (allowed <= PenetrationTolerance) return 0f;
            if (!EscapePoseIsClear(_body.position + direction * allowed))
            {
                // A Composite can report an initial tangent before a later corner feature.
                // Bound travel against that feature without moving any real body to test it.
                float lower = 0f, upper = allowed;
                for (int iteration = 0; iteration < 10; ++iteration)
                {
                    float middle = (lower + upper) * 0.5f;
                    if (EscapePoseIsClear(_body.position + direction * middle)) lower = middle;
                    else upper = middle;
                }
                allowed = Mathf.Max(0f, lower - skin);
            }
            // Recheck after skin subtraction as the start may contain a diagnostic-sized overlap.
            // Shortening a move away from such a face must not put the endpoint back inside it.
            if (allowed <= PenetrationTolerance || !EscapePoseIsClear(_body.position + direction * allowed)) return 0f;
            return allowed;
        }

        private void AddEscapeTangents(Vector2 normal)
        {
            AddEscapeCandidate(new Vector2(-normal.y, normal.x));
            AddEscapeCandidate(new Vector2(normal.y, -normal.x));
        }

        private void AddEscapeCandidate(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.000001f) return;
            direction.Normalize();
            foreach (Vector2 existing in _escapeCandidates)
                if (Vector2.Dot(existing, direction) > 0.9999f) return;
            _escapeCandidates.Add(direction);
        }

        private Vector2 ProjectEscapeDirection(Vector2 desired)
        {
            if (EscapeDirectionAllowed(desired)) return desired;
            Vector2 best = Vector2.zero;
            float bestError = desired.sqrMagnitude;
            foreach (Vector2 normal in _escapeNormals)
            {
                Vector2 candidate = desired - normal * Vector2.Dot(desired, normal);
                float error = (candidate - desired).sqrMagnitude;
                if (error < bestError && EscapeDirectionAllowed(candidate))
                {
                    best = candidate;
                    bestError = error;
                }
            }
            // For 2D homogeneous half-planes, an intersection of nonparallel faces is zero.
            return best;
        }

        private bool EscapeDirectionAllowed(Vector2 direction)
        {
            foreach (Vector2 normal in _escapeNormals)
                if (Vector2.Dot(normal, direction) < -0.000001f) return false;
            return true;
        }

        private bool EscapePoseIsClear(Vector2 position)
        {
            // Do not accept penetration at an escape endpoint. Safety reserves positive corridor
            // clearance before freezing rotation, but ordinary depenetration may leave one face
            // closer than preventiveCastSkin; requiring that entire skin here would forbid a
            // valid tangent escape from that face.
            foreach (Collider2D other in _escapeGeometry)
            {
                if (!IsWorldGeometry(other) || Physics2D.GetIgnoreCollision(playerCollider, other)) continue;
                Rigidbody2D body = other.attachedRigidbody;
                ColliderDistance2D distance = Physics2D.Distance(playerCollider, position, _body.rotation, other,
                    body != null ? body.position : (Vector2)other.transform.position,
                    body != null ? body.rotation : other.transform.eulerAngles.z);
                if (!distance.isValid || distance.distance < 0f) return false;
            }
            return true;
        }

        private Vector2 OutwardNormal(Collider2D other, ColliderDistance2D distance)
        {
            // Native surface normals remain stable at short separations. Normalizing pointA -
            // pointB magnifies witness-point rounding enough to close a narrow tangent corridor.
            Vector2 normal = distance.isOverlapped ? -distance.normal : distance.normal;
            if (distance.distance == 0f)
            {
                // At exact contact the native query can switch its normal sign without setting
                // isOverlapped. The captured entry face disambiguates that sign at the same pose.
                Vector2 point, entryNormal;
                float beforeDistance;
                if (TryGetCapturedSide(other, out point, out entryNormal, out beforeDistance) &&
                    Vector2.Dot(normal, entryNormal) < 0f) normal = -normal;
            }
            return normal.normalized;
        }

        private void ApplyCorrection(Vector2 requested, ref float remainingBudget)
        {
            Vector2 correction = Vector2.ClampMagnitude(requested, remainingBudget);
            if (correction.sqrMagnitude <= 0f) return;
            _body.position += correction;
            float distance = correction.magnitude;
            remainingBudget = Mathf.Max(0f, remainingBudget - distance);
            LastCorrection += correction;
            LastCorrectionDistance += distance;
            if (!IsCrushed)
            {
                Vector2 direction = correction / distance;
                if (_previousCorrectionDirection.sqrMagnitude > 0f &&
                    Vector2.Dot(direction, _previousCorrectionDirection) < -0.5f)
                    ++_directionReversals;
                _previousCorrectionDirection = direction;
            }
        }

        private void CollectPenetrations()
        {
            _penetrations.Clear();
            _overlaps.Clear();
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(worldGeometryMask);
            filter.useTriggers = false;
            playerCollider.Overlap(filter, _overlaps);
            _playerShapes.Clear();
            playerCollider.GetShapes(_playerShapes);
            foreach (Collider2D other in _overlaps)
            {
                if (!IsWorldGeometry(other)) continue;
                if (Physics2D.GetIgnoreCollision(playerCollider, other) && !OwnsIgnoredPair(other)) continue;
                ColliderDistance2D distance = playerCollider.Distance(other);
                if (!distance.isValid || !distance.isOverlapped || distance.distance >= -PenetrationTolerance)
                    continue;
                Vector2 correction = distance.normal * distance.distance;
                var penetrationInfo = new Penetration
                {
                    Collider = other,
                    CurrentNormal = -distance.normal,
                    CurrentDepth = -distance.distance + PenetrationTolerance,
                    IncludeCurrent = true
                };
                Vector2 point, normal;
                float beforeDistance;
                if (TryGetCapturedSide(other, out point, out normal, out beforeDistance))
                {
                    float penetration = Vector2.Dot(point, normal) - PlayerMinimumProjection(normal);
                    Vector2 currentOutward = -distance.normal;
                    float currentDepth = -distance.distance;
                    correction = OriginalSideCorrection(normal, penetration, currentOutward, currentDepth);
                    penetrationInfo.HasEntry = true;
                    penetrationInfo.EntryNormal = normal;
                    penetrationInfo.EntryDepth = Mathf.Max(0f, penetration) + PenetrationTolerance;
                    penetrationInfo.IncludeCurrent = !IsFarFace(Vector2.Dot(normal, currentOutward), penetration);
                    LastConstraintCollider = other;
                    LastEntryNormal = normal;
                    LastMtvNormal = currentOutward;
                    LastMtvAlignment = Vector2.Dot(normal, currentOutward);
                    LastEntryPlaneDepth = penetration;
                    LastMtvDepth = currentDepth;
                    LastRequestedCorrection = correction;
                    ++LastSideCorrectionCount;
                }
                penetrationInfo.Correction = correction;
                _penetrations.Add(penetrationInfo);
            }
        }

        private void AddConstraint(Vector2 normal, float depthFromCurrentPosition)
        {
            // Convert every newly observed face back to the common step-start coordinate frame.
            // Collider.Distance exposes one feature at a time on a composite; older feature
            // constraints must survive the next overlap query even when that feature is clear.
            float depth = depthFromCurrentPosition + Vector2.Dot(normal, LastCorrection);
            for (int index = 0; index < _constraints.Count; ++index)
            {
                HalfPlane plane = _constraints[index];
                if ((plane.Normal - normal).sqrMagnitude > 0.0000000001f) continue;
                if (depth > plane.Depth)
                {
                    plane.Depth = depth;
                    _constraints[index] = plane;
                }
                return;
            }
            _constraints.Add(new HalfPlane { Normal = normal, Depth = depth });
        }

        private bool TrySolveConstraints(out Vector2 solution)
        {
            solution = Vector2.zero;
            if (SatisfiesConstraints(solution)) return true;
            float bestSquared = float.PositiveInfinity;
            // A 2D minimum-norm feasible point has one active face or lies at an intersection
            // of two active faces. Enumerating those candidates solves the complete local set;
            // summing independently computed pushes does not satisfy this property.
            for (int first = 0; first < _constraints.Count; ++first)
            {
                HalfPlane a = _constraints[first];
                ConsiderConstraintCandidate(a.Normal * (a.Depth / a.Normal.sqrMagnitude), ref solution, ref bestSquared);
                for (int second = first + 1; second < _constraints.Count; ++second)
                {
                    HalfPlane b = _constraints[second];
                    float determinant = a.Normal.x * b.Normal.y - a.Normal.y * b.Normal.x;
                    if (Mathf.Abs(determinant) < 0.000001f) continue;
                    Vector2 intersection = new Vector2(
                        (a.Depth * b.Normal.y - a.Normal.y * b.Depth) / determinant,
                        (a.Normal.x * b.Depth - a.Depth * b.Normal.x) / determinant);
                    ConsiderConstraintCandidate(intersection, ref solution, ref bestSquared);
                }
            }
            return !float.IsPositiveInfinity(bestSquared);
        }

        private void ConsiderConstraintCandidate(Vector2 candidate, ref Vector2 solution, ref float bestSquared)
        {
            float squared = candidate.sqrMagnitude;
            if (float.IsNaN(squared) || float.IsInfinity(squared) || squared >= bestSquared || !SatisfiesConstraints(candidate)) return;
            bestSquared = squared;
            solution = candidate;
        }

        private bool SatisfiesConstraints(Vector2 correction)
        {
            foreach (HalfPlane plane in _constraints)
                if (Vector2.Dot(plane.Normal, correction) < plane.Depth - 0.000001f) return false;
            return true;
        }

        private static Vector2 OriginalSideCorrection(Vector2 entryNormal, float planeDepth,
            Vector2 currentNormal, float currentDepth)
        {
            float entryDepth = Mathf.Max(0f, planeDepth) + PenetrationTolerance;
            float depth = currentDepth + PenetrationTolerance;
            float alignment = Mathf.Clamp(Vector2.Dot(entryNormal, currentNormal), -1f, 1f);
            Vector2 entryOnly = entryNormal * entryDepth;
            // Only a nearly opposite MTV while still inside the entry plane denotes a thin
            // obstacle's far-face exit. A cleared entry plane plus another CompositeCollider
            // feature is a legitimate corner and must keep both constraints.
            if (IsFarFace(alignment, planeDepth)) return entryOnly;

            // Minimum-length solution of two half-plane constraints:
            // entryNormal . correction >= entryDepth; currentNormal . correction >= depth.
            // Try either single active face first; otherwise both planes are active. The second
            // term is tangent to the entry face, so clearing a new corner cannot undo its repair.
            if (alignment * entryDepth >= depth) return entryOnly;
            Vector2 currentOnly = currentNormal * depth;
            if (alignment * depth >= entryDepth) return currentOnly;
            Vector2 tangent = currentNormal - entryNormal * alignment;
            float tangentSquared = tangent.sqrMagnitude;
            if (tangentSquared > 0.000001f)
                return entryOnly + tangent * ((depth - alignment * entryDepth) / tangentSquared);
            if (alignment < 0f) return depth > entryDepth ? currentOnly : entryOnly;
            return entryNormal * Mathf.Max(entryDepth, depth / Mathf.Max(alignment, 0.000001f));
        }

        private static bool IsFarFace(float alignment, float entryPlaneDepth)
        {
            return alignment < -0.95f && entryPlaneDepth > PenetrationTolerance;
        }

        private void ResetConstraintDiagnostics()
        {
            LastConstraintCollider = null;
            LastEntryNormal = LastMtvNormal = LastRequestedCorrection = Vector2.zero;
            LastMtvAlignment = LastEntryPlaneDepth = LastMtvDepth = 0f;
            LastConstraintCount = 0;
            LastConstraintSolutionFound = false;
        }

        private bool IsWorldGeometry(Collider2D other)
        {
            return other != null && other != playerCollider && other.enabled && !other.isTrigger &&
                other.attachedRigidbody != _body && other.gameObject.activeInHierarchy &&
                (worldGeometryMask.value & (1 << other.gameObject.layer)) != 0;
        }

        private float PlayerMinimumProjection(Vector2 normal)
        {
            // Work on native collision shapes, including capsule radii. An AABB would
            // overestimate capsule support on slopes and introduce an artificial hover gap.
            // GetShapes vertices are in attached-body local space (scale and collider offset
            // already baked in). Explicitly use the physical pose, never the interpolated Transform.
            Rigidbody2D shapeBody = playerCollider.attachedRigidbody;
            Matrix4x4 matrix = shapeBody != null
                ? Matrix4x4.TRS(shapeBody.position, Quaternion.Euler(0f, 0f, shapeBody.rotation), Vector3.one)
                : _playerShapes.localToWorldMatrix;
            Vector2 localNormal = matrix.transpose.MultiplyVector(normal);
            float translation = Vector2.Dot(normal, matrix.MultiplyPoint3x4(Vector3.zero));
            float radiusScale = localNormal.magnitude;
            float minimum = float.PositiveInfinity;
            for (int shapeIndex = 0; shapeIndex < _playerShapes.shapeCount; ++shapeIndex)
            {
                PhysicsShape2D shape = _playerShapes.GetShape(shapeIndex);
                for (int vertex = 0; vertex < shape.vertexCount; ++vertex)
                {
                    float projection = Vector2.Dot(localNormal, _playerShapes.GetShapeVertex(shapeIndex, vertex)) +
                        translation - shape.radius * radiusScale;
                    minimum = Mathf.Min(minimum, projection);
                }
            }
            return minimum;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
            return new Vector2(value.x * cosine - value.y * sine, value.x * sine + value.y * cosine);
        }

        private void UpdateCrushTracking()
        {
            if (RemainingOverlapCount == 0)
            {
                ResetCrushTracking();
                return;
            }

            ++CrushFrames;
            if (IsCrushed)
            {
                ++EscapeFrames;
                if (_activeResponse == CrushResponse.PushAlongGravity &&
                    EscapeFrames >= Mathf.Max(1, crushFrameThreshold))
                    KillPlayer();
                else if (_activeResponse == CrushResponse.IgnoreCollision && _ignoredPairs.Count == 0)
                    ResetCrushTracking();
                return;
            }

            if (CrushFrames < Mathf.Max(1, crushFrameThreshold) || _directionReversals < 2) return;
            IsCrushed = true;
            EscapeFrames = 0;
            _activeResponse = crushResponse;
            if (_activeResponse == CrushResponse.Kill) KillPlayer();
            else if (_activeResponse == CrushResponse.IgnoreCollision) IgnoreCurrentPairs();
        }

        private void ResetCrushTracking()
        {
            CrushFrames = 0;
            EscapeFrames = 0;
            IsCrushed = false;
            _directionReversals = 0;
            _previousCorrectionDirection = Vector2.zero;
        }

        private void KillPlayer()
        {
            RestoreIgnoredPairs();
            if (_player != null && !_player.IsDead) _player.Die();
        }

        private bool OwnsIgnoredPair(Collider2D other)
        {
            foreach (IgnoredPair pair in _ignoredPairs)
                if (pair.Player == playerCollider && pair.Other == other) return true;
            return false;
        }

        private void IgnoreCurrentPairs()
        {
            foreach (Penetration penetration in _penetrations)
            {
                Collider2D other = penetration.Collider;
                if (Physics2D.GetIgnoreCollision(playerCollider, other)) continue;
                Physics2D.IgnoreCollision(playerCollider, other, true);
                _ignoredPairs.Add(new IgnoredPair { Player = playerCollider, Other = other, Age = 0f });
            }
        }

        private void MaintainIgnoredPairs(float dt)
        {
            for (int index = _ignoredPairs.Count - 1; index >= 0; --index)
            {
                IgnoredPair pair = _ignoredPairs[index];
                pair.Age += dt;
                bool restore = pair.Player == null || pair.Other == null ||
                    pair.Player != playerCollider || !pair.Player.enabled || !pair.Other.enabled ||
                    !pair.Player.gameObject.activeInHierarchy || !pair.Other.gameObject.activeInHierarchy ||
                    pair.Age >= Mathf.Max(0.02f, ignoreCollisionTimeout);
                if (!restore)
                {
                    ColliderDistance2D distance = pair.Player.Distance(pair.Other);
                    restore = !distance.isValid || !distance.isOverlapped;
                }
                if (restore)
                {
                    RestorePair(pair);
                    _ignoredPairs.RemoveAt(index);
                }
                else _ignoredPairs[index] = pair;
            }
        }

        private void RestoreIgnoredPairs()
        {
            foreach (IgnoredPair pair in _ignoredPairs) RestorePair(pair);
            _ignoredPairs.Clear();
        }

        private static void RestorePair(IgnoredPair pair)
        {
            if (pair.Player != null && pair.Other != null)
                Physics2D.IgnoreCollision(pair.Player, pair.Other, false);
        }
    }
}
