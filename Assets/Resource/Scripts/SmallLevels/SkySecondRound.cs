using System;
using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    public partial class SmallLevelData
    {
        public WindSeed[] winds = Array.Empty<WindSeed>();
        public Vector2[] seeds = Array.Empty<Vector2>();
        public PortalSeed[] portals = Array.Empty<PortalSeed>();
    }

    [Serializable] public class WindSeed { public Vector4 rect; public Vector2 force; }
    [Serializable] public class PortalSeed { public Vector3 a, b; public float r = 0.75f; }

    public sealed partial class SkyMechanisms
    {
        private void BuildDangerMarks()
        {
            var root = new GameObject("DangerMarkers").transform;
            root.SetParent(level, false);
            Transform artTransform = level.Find("LevelArt");
            SpriteRenderer art = artTransform != null ? artTransform.GetComponent<SpriteRenderer>() : null;
            if (art != null && art.sprite != null)
            {
                Sprite source = art.sprite; float ppu = source.pixelsPerUnit;
                for (int i = 0; i < data.spikes.Length; i++)
                {
                    Vector4 box = data.spikes[i];
                    int left = Mathf.Clamp(Mathf.RoundToInt(box.x * ppu), 0, source.texture.width);
                    int right = Mathf.Clamp(Mathf.RoundToInt(box.z * ppu), 0, source.texture.width);
                    int bottom = Mathf.Clamp(Mathf.RoundToInt((data.h - box.w) * ppu), 0, source.texture.height);
                    int top = Mathf.Clamp(Mathf.RoundToInt((data.h - box.y) * ppu), 0, source.texture.height);
                    if (right <= left || top <= bottom) continue;
                    var sprite = Sprite.Create(source.texture, new Rect(left, bottom, right-left, top-bottom), Vector2.zero, ppu, 0, SpriteMeshType.FullRect);
                    sprite.name = "DangerSpike_" + i; dangerSprites.Add(sprite);
                    var go = new GameObject("DangerSpike_" + i); go.transform.SetParent(root, false);
                    go.transform.localPosition = new Vector3((left-source.pivot.x)/ppu, (bottom-source.pivot.y)/ppu);
                    var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
                    renderer.sharedMaterial = art.sharedMaterial; renderer.sortingLayerID = art.sortingLayerID; renderer.sortingOrder = art.sortingOrder + 1;
                    renderer.color = DangerVisual.Red();
                }
            }
            var edge = new GameObject("VoidBoundary").AddComponent<LineRenderer>();
            edge.transform.SetParent(root, false); edge.useWorldSpace = false; edge.loop = true; edge.positionCount = 4;
            edge.sharedMaterial = particleMaterial; edge.startWidth = edge.endWidth = .0625f;
            edge.startColor = edge.endColor = DangerVisual.Red(.8f); edge.sortingOrder = 2;
            Vector4 bound = data.bound;
            edge.SetPosition(0, new Vector3(bound.x, -bound.y)); edge.SetPosition(1, new Vector3(bound.z, -bound.y));
            edge.SetPosition(2, new Vector3(bound.z, -bound.w)); edge.SetPosition(3, new Vector3(bound.x, -bound.w));
        }

        [Serializable]
        private sealed class WindColumn
        {
            public LineRenderer band;
            public LineRenderer[] edges, streaks;
            public SpriteRenderer[] arrows;
        }

        [SerializeField] private WindColumn[] windColumns = Array.Empty<WindColumn>();
        [SerializeField] private SpriteRenderer[] grabBalloonVisuals = Array.Empty<SpriteRenderer>(), portalVisuals = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer heldBalloon;
        [SerializeField] private LineRenderer balloonProgress;
        [SerializeField] private Material secondRoundLineMaterial;
        private bool[] balloonsGrabbed;
        private float balloonTime;
        private int portalOut = -1;

        private void BuildSecondRoundVisuals(Transform visualRoot)
        {
            windColumns = new WindColumn[data.winds.Length];
            for (int i = 0; i < data.winds.Length; i++)
            {
                WindSeed seed = data.winds[i];
                Vector4 r = seed.rect;
                bool vertical = Mathf.Abs(seed.force.y) >= Mathf.Abs(seed.force.x);
                float length = vertical ? r.w - r.y : r.z - r.x;
                float width = vertical ? r.z - r.x : r.w - r.y;
                var column = new WindColumn();
                windColumns[i] = column;
                column.band = SecondRoundLine(visualRoot, "WindBand_" + i, r.z - r.x,
                    new Color32(90, 180, 255, 71), new Vector2((r.x + r.z) * 0.5f, r.y), new Vector2((r.x + r.z) * 0.5f, r.w), 18);
                int edgeCount = Mathf.CeilToInt(length / 0.5f);
                column.edges = new LineRenderer[edgeCount * 2];
                for (int j = 0; j < edgeCount; j++)
                for (int side = 0; side < 2; side++)
                {
                    float a = j * 0.5f, b = Mathf.Min(length, a + 0.3f);
                    Vector2 start = vertical ? new Vector2(side == 0 ? r.x + 0.05f : r.z - 0.05f, r.y + a) : new Vector2(r.x + a, side == 0 ? r.y + 0.05f : r.w - 0.05f);
                    Vector2 end = vertical ? new Vector2(start.x, r.y + b) : new Vector2(r.x + b, start.y);
                    column.edges[j * 2 + side] = SecondRoundLine(visualRoot, "WindEdge_" + i + "_" + j + "_" + side,
                        0.1f, new Color32(30, 120, 230, 166), start, end, 19);
                }
                int columns = width > 3f ? 2 : 1;
                column.arrows = new SpriteRenderer[Mathf.CeilToInt(length / 1.6f) * columns];
                for (int j = 0; j < column.arrows.Length; j++)
                {
                    var arrow = MakeVisual(visualRoot, "WindArrow_" + i + "_" + j, "wind_chevron", Vector2.zero, 20);
                    arrow.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-seed.force.y, seed.force.x) * Mathf.Rad2Deg - 90f);
                    arrow.transform.localScale = Vector3.one * Mathf.Min(1f, width * 0.6f / 1.8f);
                    column.arrows[j] = arrow;
                }
                int n = Mathf.Max(3, Mathf.RoundToInt(width * 1.8f));
                column.streaks = new LineRenderer[n * 2];
                for (int j = 0; j < column.streaks.Length; j++)
                    column.streaks[j] = SecondRoundLine(visualRoot, "WindStreak_" + i + "_" + j, 0.06f,
                        new Color(1f, 1f, 1f, 0.7f), Vector2.zero, Vector2.zero, 21);
            }

            grabBalloonVisuals = new SpriteRenderer[data.seeds.Length];
            for (int i = 0; i < data.seeds.Length; i++)
                grabBalloonVisuals[i] = MakeVisual(visualRoot, "GrabBalloon_" + i, "grab_balloon", data.seeds[i], 26);
            if (data.seeds.Length > 0)
            {
                heldBalloon = MakeVisual(visualRoot, "HeldBalloon", "grab_balloon", Vector2.zero, 27);
                heldBalloon.enabled = false;
                balloonProgress = SecondRoundLine(visualRoot, "BalloonTime", 0.04f, Color.white, Vector2.zero, Vector2.zero, 28);
                balloonProgress.useWorldSpace = true;
                balloonProgress.enabled = false;
            }
            portalVisuals = new SpriteRenderer[data.portals.Length * 2];
            string[] colors = { "orange", "blue", "green", "purple" };
            for (int i = 0; i < data.portals.Length; i++)
            for (int end = 0; end < 2; end++)
            {
                PortalSeed seed = data.portals[i];
                Vector3 point = end == 0 ? seed.a : seed.b;
                var portal = MakeVisual(visualRoot, "Portal_" + i + "_" + end, "portal_" + colors[(i * 2 + end) % colors.Length], point, 24);
                portal.transform.localRotation = Quaternion.Euler(0f, 0f, -point.z);
                portal.transform.localScale = Vector3.one * (seed.r / 0.75f);
                portalVisuals[i * 2 + end] = portal;
            }
        }

        private LineRenderer SecondRoundLine(Transform parent, string name, float width, Color color, Vector2 start, Vector2 end, int order)
        {
            if (secondRoundLineMaterial == null) secondRoundLineMaterial = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(start.x, -start.y));
            line.SetPosition(1, new Vector3(end.x, -end.y));
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.sharedMaterial = secondRoundLineMaterial;
            line.sortingOrder = order;
            return line;
        }

        private void InitializeSecondRound()
        {
            balloonsGrabbed = new bool[data.seeds.Length];
            balloonTime = 0f;
            portalOut = -1;
        }

        private void StepSecondRound(float dt)
        {
            if (data.winds.Length == 0 && data.seeds.Length == 0 && data.portals.Length == 0) return;
            Vector2 velocity = player.IntendedVelocity;
            float gravityStep = Physics2D.gravity.y * player.SimulatedGravityScale * dt;
            if (balloonTime > 0f)
            {
                velocity.y = Mathf.Max(-3f, velocity.y + gravityStep * 0.25f);
                balloonTime = Mathf.Max(0f, balloonTime - dt);
                if (balloonTime == 0f) feedback.Pop("pop.balloon_out");
            }
            else
            {
                velocity.y += gravityStep;
                if (player.maxFallSpeed > 0f) velocity.y = Mathf.Max(-player.maxFallSpeed, velocity.y);
            }
            bool inWind = false;
            foreach (WindSeed wind in data.winds)
            {
                Vector4 r = wind.rect;
                Vector2 p = circles[0];
                if (p.x < r.x || p.x > r.z || p.y < r.y || p.y > r.w) continue;
                velocity.y += level.TransformVector(new Vector3(wind.force.x, -wind.force.y)).y * dt;
                inWind = true;
            }
            if (inWind) velocity.y = Mathf.Clamp(velocity.y, -11f, 7f);
            StepPortals(ref velocity);
            for (int i = 0; i < data.seeds.Length; i++)
            {
                if (balloonsGrabbed[i]) continue;
                Vector2 point = data.seeds[i];
                if (Vector2.Distance(circles[0], point) >= radius + 0.55f && Vector2.Distance(circles[1], point) >= radius + 0.55f) continue;
                balloonsGrabbed[i] = true;
                grabBalloonVisuals[i].enabled = false;
                balloonTime = 7f;
                velocity.y = Mathf.Max(velocity.y, -3f);
                feedback.Pop("pop.balloon_grab");
                EmitBurst(point, 12, Color.white, 1.5f, 0.8f, 0.12f);
            }
            // PlayerController adds its ordinary gravity after this component in the same physics step.
            velocity.y -= gravityStep;
            player.SetIntendedVelocity(velocity);
        }

        private void StepPortals(ref Vector2 velocity)
        {
            int touched = -1;
            for (int i = 0; i < data.portals.Length && touched < 0; i++)
            for (int end = 0; end < 2; end++)
            {
                PortalSeed pair = data.portals[i];
                Vector2 point = end == 0 ? pair.a : pair.b;
                if (Vector2.Distance(circles[0], point) < pair.r + 0.2f || Vector2.Distance(circles[1], point) < pair.r + 0.2f)
                { touched = i * 2 + end; break; }
            }
            if (touched < 0) { portalOut = -1; return; }
            if (touched == portalOut) return;
            PortalSeed portal = data.portals[touched / 2];
            Vector3 destination = (touched & 1) == 0 ? portal.b : portal.a;
            float angle = destination.z * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 exit = (Vector2)destination + direction * 0.2f;
            Vector2 displacement = ToWorld(exit) - ToWorld(circles[0]);
            player.ApplyPlatformDisplacement(displacement);
            Vector2 localDelta = VectorToLevel(displacement);
            circles[0] += localDelta;
            circles[1] += localDelta;
            float speed = Mathf.Max(Mathf.Abs(velocity.y), 3f);
            velocity = new Vector2(0f, level.TransformVector(new Vector3(direction.x * speed, -direction.y * speed)).y);
            portalOut = touched ^ 1;
            feedback.Pop("pop.warp");
            feedback.Shake(0.15f);
            EmitBurst(destination, 14, Color.white, 2.5f, 0.5f, 0.1f);
        }

        private void UpdateSecondRoundVisuals()
        {
            float time = clock + (Alive ? Alpha * Time.fixedDeltaTime : 0f);
            for (int i = 0; i < windColumns.Length; i++)
            {
                WindColumn column = windColumns[i];
                WindSeed wind = data.winds[i];
                Vector4 r = wind.rect;
                bool vertical = Mathf.Abs(wind.force.y) >= Mathf.Abs(wind.force.x);
                float sign = Mathf.Sign(vertical ? wind.force.y : wind.force.x);
                float length = vertical ? r.w - r.y : r.z - r.x;
                float width = vertical ? r.z - r.x : r.w - r.y;
                float lift = Mathf.Clamp01(level.TransformVector(new Vector3(wind.force.x, -wind.force.y).normalized).y);
                column.band.startColor = column.band.endColor = new Color(90f / 255f, 180f / 255f, 1f, 0.12f + 0.16f * lift);
                Color edgeColor = new Color(30f / 255f, 120f / 255f, 230f / 255f, 0.25f + 0.4f * lift);
                foreach (LineRenderer edge in column.edges) edge.startColor = edge.endColor = edgeColor;
                int columns = width > 3f ? 2 : 1;
                float phase = Mathf.Repeat(time * 3.2f, 1.6f);
                for (int j = 0; j < column.arrows.Length; j++)
                {
                    SpriteRenderer arrow = column.arrows[j];
                    float along = phase + (j / columns) * 1.6f;
                    arrow.enabled = along < length;
                    if (!arrow.enabled) continue;
                    if (sign < 0f) along = length - along;
                    float across = width * (columns == 1 ? 0.5f : j % columns == 0 ? 0.3f : 0.7f);
                    Vector2 point = vertical ? new Vector2(r.x + across, r.y + along) : new Vector2(r.x + along, r.y + across);
                    arrow.transform.localPosition = new Vector3(point.x, -point.y);
                    arrow.color = new Color(1f, 1f, 1f, 0.3f + 0.6f * lift);
                }
                int n = column.streaks.Length / 2;
                for (int j = 0; j < column.streaks.Length; j++)
                {
                    float hash = Mathf.Repeat(Mathf.Sin((j + 1) * 12.9898f) * 43758.5453f, 1f);
                    float across = (j % n + 0.5f) / n * width + (hash - 0.5f) * 0.3f;
                    float along = Mathf.Repeat(time * (6f + hash * 4f) + hash * length, length);
                    if (sign < 0f) along = length - along;
                    float start = Mathf.Clamp(along - (sign < 0f ? 0f : 0.6f + hash * 0.6f), 0f, length);
                    float end = Mathf.Clamp(start + 0.6f + hash * 0.6f, 0f, length);
                    Vector2 a = vertical ? new Vector2(r.x + across, r.y + start) : new Vector2(r.x + start, r.y + across);
                    Vector2 b = vertical ? new Vector2(a.x, r.y + end) : new Vector2(r.x + end, a.y);
                    column.streaks[j].SetPosition(0, new Vector3(a.x, -a.y));
                    column.streaks[j].SetPosition(1, new Vector3(b.x, -b.y));
                }
            }
            for (int i = 0; i < grabBalloonVisuals.Length; i++)
            {
                if (balloonsGrabbed[i]) continue;
                grabBalloonVisuals[i].transform.position = ToWorld(data.seeds[i]);
                grabBalloonVisuals[i].transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2f + i) * 0.1f * Mathf.Rad2Deg);
            }
            if (heldBalloon == null) return;
            bool floating = Alive && balloonTime > 0f;
            heldBalloon.enabled = balloonProgress.enabled = floating;
            if (!floating) return;
            float fraction = balloonTime / 7f;
            Vector2 center = Feet + Vector2.up * 2.2f;
            heldBalloon.transform.position = center;
            heldBalloon.transform.rotation = Quaternion.identity;
            heldBalloon.transform.localScale = Vector3.one * ((0.3f + 0.12f * fraction) / 0.48f);
            int segments = Mathf.Max(1, Mathf.CeilToInt(48f * fraction));
            balloonProgress.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.PI * 0.5f - Mathf.PI * 2f * fraction * i / segments;
                balloonProgress.SetPosition(i, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.75f);
            }
        }
    }
}
