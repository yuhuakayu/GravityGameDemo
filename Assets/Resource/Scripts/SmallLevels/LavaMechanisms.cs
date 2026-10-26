using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(-200)]
    public sealed partial class LavaMechanisms : SmallLevelRuntime
    {
        private const int Capacity = 1500, PixelsPerUnit = 8;
        private const float Spacing = .3f, H = .62f, Pressure = 150f, NearPressure = 250f;
        private const float Cohesion = .3f, Damping = .3f, Sigma = .3f, Beta = .05f;
        private const float Friction = .1f, WallRadius = .12f, VelocityCap = 13f;
        private static readonly float RestDensity = CalculateRestDensity();

        [SerializeField] private SpriteRenderer lavaVisual, glowQuarterVisual, glowSixteenthVisual;
        private readonly Vector2[] positions = new Vector2[Capacity], velocities = new Vector2[Capacity], oldPositions = new Vector2[Capacity];
        private readonly int[] groups = new int[Capacity], particleCells = new int[Capacity], sorted = new int[Capacity], neighbours = new int[Capacity];
        private readonly bool[] frozen = new bool[Capacity], crust = new bool[Capacity];
        private readonly float[] densities = new float[Capacity], isolated = new float[Capacity];
        private readonly HashSet<int> woken = new HashSet<int>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private int count, made, ventIndex, vented, gridWidth, gridHeight, imageWidth, imageHeight;
        private int[] cellStarts, cellFill;
        private float[] field, crustField, frozenField;
        private Color32[] pixels, quarterPixels, sixteenthPixels;
        private Color32[] previousPixels, previousQuarterPixels, previousSixteenthPixels;
        private Texture2D lavaTexture, quarterTexture, sixteenthTexture, previousTexture, previousQuarterTexture, previousSixteenthTexture;
        private Material lavaMaterial, quarterMaterial, sixteenthMaterial;
        private ParticleSystem embers, smoke;
        private float ventAccumulator, lastSizzle;

        public override void BuildVisuals()
        {
            Transform old = level.Find("LavaVisuals");
            if (old != null) DestroyImmediate(old.gameObject);
            var root = new GameObject("LavaVisuals").transform;
            root.SetParent(level, false);
            InitializeState();
            BuildLavaPixels();
            Sprite body = SaveInitialSprite("body", pixels, imageWidth, imageHeight, FilterMode.Point);
            Sprite quarter = SaveInitialSprite("glow4", quarterPixels, imageWidth >> 2, imageHeight >> 2, FilterMode.Bilinear);
            Sprite sixteenth = SaveInitialSprite("glow16", sixteenthPixels, imageWidth >> 4, imageHeight >> 4, FilterMode.Bilinear);
            lavaVisual = MakeSurface("Lava", body, root, 13, 1f, false);
            glowQuarterVisual = MakeSurface("LavaGlowQuarter", quarter, root, 12, .35f, true);
            glowSixteenthVisual = MakeSurface("LavaGlowSixteenth", sixteenth, root, 11, .5f, true);
            BuildSecondRoundVisuals(root);
        }

        private SpriteRenderer MakeSurface(string name, Sprite sprite, Transform root, int order, float opacity, bool additive)
        {
            var surface = new GameObject(name);
            surface.transform.SetParent(root, false);
            var renderer = surface.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.sortingOrder = order;
            renderer.transform.localScale = new Vector3(data.w / sprite.bounds.size.x, data.h / sprite.bounds.size.y, 1f);
            renderer.color = Color.white;
            renderer.sharedMaterial = MakeMaterial(additive, opacity);
            return renderer;
        }

        private static Material MakeMaterial(bool additive, float opacity = 1f)
        {
            var material = new Material(Shader.Find("Gravity Game/Small Levels/Lava"));
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_Interpolation", 1f);
            material.SetColor("_Color", new Color(1f, 1f, 1f, opacity));
            return material;
        }

        private Sprite SaveInitialSprite(string suffix, Color32[] colors, int width, int height, FilterMode filter)
        {
            var texture = NewTexture(width, height, filter);
            texture.SetPixels32(colors); texture.Apply(false, false);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                string path = "Assets/Resource/Art/SmallLevels/Lava/lava_initial_" + data.scene + "_" + suffix + ".png";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2(0f, 1f);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.filterMode = filter;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
#endif
            owned.Add(texture);
            Sprite sprite = NewSprite(texture); owned.Add(sprite);
            return sprite;
        }

        private void InitializeState()
        {
            count = Mathf.Min(data.lavaStart.n, Capacity); made = count;
            ventIndex = vented = 0; ventAccumulator = 0f;
            Array.Clear(velocities, 0, velocities.Length);
            Array.Clear(isolated, 0, isolated.Length);
            for (int i = 0; i < count; i++)
            {
                positions[i] = oldPositions[i] = new Vector2(data.lavaStart.x[i], data.lavaStart.y[i]);
                groups[i] = data.lavaStart.g[i]; frozen[i] = data.lavaStart.frozen[i]; crust[i] = data.lavaStart.crust[i];
                water[i] = data.lavaStart.water != null && i < data.lavaStart.water.Length && data.lavaStart.water[i];
                if (water[i]) { groups[i] = 9; frozen[i] = false; }
            }
            gridWidth = Mathf.CeilToInt(data.w / H) + 2; gridHeight = Mathf.CeilToInt(data.h / H) + 2;
            cellStarts = new int[gridWidth * gridHeight + 1]; cellFill = new int[cellStarts.Length];
            imageWidth = data.w * PixelsPerUnit; imageHeight = data.h * PixelsPerUnit;
            field = new float[imageWidth * imageHeight]; crustField = new float[field.Length]; frozenField = new float[field.Length];
            pixels = new Color32[field.Length]; previousPixels = new Color32[field.Length];
            waterField = new float[field.Length]; glowSourcePixels = new Color32[field.Length];
            quarterPixels = new Color32[(imageWidth >> 2) * (imageHeight >> 2)]; previousQuarterPixels = new Color32[quarterPixels.Length];
            sixteenthPixels = new Color32[(imageWidth >> 4) * (imageHeight >> 4)]; previousSixteenthPixels = new Color32[sixteenthPixels.Length];
            InitializeSecondRoundState();
            BuildNeighbourGrid();
        }

        protected override void Awake()
        {
            base.Awake();
            if (lavaVisual == null || glowQuarterVisual == null || glowSixteenthVisual == null) BuildVisuals();
            InitializeState();
            lavaTexture = OwnTexture(imageWidth, imageHeight, FilterMode.Point);
            quarterTexture = OwnTexture(imageWidth >> 2, imageHeight >> 2, FilterMode.Bilinear);
            sixteenthTexture = OwnTexture(imageWidth >> 4, imageHeight >> 4, FilterMode.Bilinear);
            previousTexture = OwnTexture(imageWidth, imageHeight, FilterMode.Point);
            previousQuarterTexture = OwnTexture(imageWidth >> 2, imageHeight >> 2, FilterMode.Bilinear);
            previousSixteenthTexture = OwnTexture(imageWidth >> 4, imageHeight >> 4, FilterMode.Bilinear);
            lavaMaterial = BindSurface(lavaVisual, lavaTexture, previousTexture, false);
            quarterMaterial = BindSurface(glowQuarterVisual, quarterTexture, previousQuarterTexture, true);
            sixteenthMaterial = BindSurface(glowSixteenthVisual, sixteenthTexture, previousSixteenthTexture, true);
            BuildLavaPixels();
            CopyCurrentToPrevious(); UploadTextures();
            embers = MakeParticles("LavaEmbers", true);
            smoke = MakeParticles("LavaSmoke", false);
        }

        private Material BindSurface(SpriteRenderer renderer, Texture2D current, Texture2D previous, bool additive)
        {
            Sprite sprite = NewSprite(current); owned.Add(sprite); renderer.sprite = sprite;
            float opacity = renderer.sharedMaterial.GetColor("_Color").a;
            Material material = MakeMaterial(additive, opacity); owned.Add(material); renderer.sharedMaterial = material;
            material.SetTexture("_PreviousTex", previous);
            return material;
        }

        private static Texture2D NewTexture(int width, int height, FilterMode filter) => new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = TextureWrapMode.Clamp };
        private Texture2D OwnTexture(int width, int height, FilterMode filter) { var texture = NewTexture(width, height, filter); owned.Add(texture); return texture; }
        private static Sprite NewSprite(Texture2D texture) => Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0f, 1f), PixelsPerUnit, 0, SpriteMeshType.FullRect);

        private static float CalculateRestDensity()
        {
            float rho = 0f;
            for (int row = -4; row <= 4; row++)
            for (int column = -4; column <= 4; column++)
            {
                float x = column * Spacing + (row % 2 != 0 ? Spacing * .5f : 0f), y = row * Spacing * .866f;
                float distance = Mathf.Sqrt(x * x + y * y);
                if (distance == 0f || distance >= H) continue;
                float q = 1f - distance / H; rho += q * q;
            }
            return rho;
        }

        private int GridX(float x) => Mathf.Clamp(Mathf.FloorToInt(x / H) + 1, 0, gridWidth - 1);
        private int GridY(float y) => Mathf.Clamp(Mathf.FloorToInt(y / H) + 1, 0, gridHeight - 1);
        private void BuildNeighbourGrid()
        {
            Array.Clear(cellStarts, 0, cellStarts.Length);
            for (int i = 0; i < count; i++) { int cell = GridY(positions[i].y) * gridWidth + GridX(positions[i].x); particleCells[i] = cell; cellStarts[cell + 1]++; }
            for (int cell = 0; cell < cellStarts.Length - 1; cell++) cellStarts[cell + 1] += cellStarts[cell];
            Array.Copy(cellStarts, cellFill, cellStarts.Length);
            for (int i = 0; i < count; i++) sorted[cellFill[particleCells[i]]++] = i;
        }

        private int GatherNeighbours(int particle)
        {
            int cx = particleCells[particle] % gridWidth, cy = particleCells[particle] / gridWidth, length = 0;
            for (int y = Mathf.Max(0, cy - 1); y <= Mathf.Min(gridHeight - 1, cy + 1); y++)
            for (int x = Mathf.Max(0, cx - 1); x <= Mathf.Min(gridWidth - 1, cx + 1); x++)
            {
                int cell = y * gridWidth + x;
                for (int k = cellStarts[cell]; k < cellStarts[cell + 1]; k++) neighbours[length++] = sorted[k];
            }
            return length;
        }

        private void FixedUpdate()
        {
            if (!BeginStep()) return;
            float dt = Time.fixedDeltaTime;
            CopyCurrentToPrevious();
            if (freezeRemaining > 0f) StepFreeze(dt);
            else
            {
                LavaStep(dt);
                StepWater();
                if (Alive) StepVent(dt);
                if (ventSealed) CoolVentLava(dt);
            }
            BuildNeighbourGrid();
            StepKegs(dt);
            if (Alive) CheckHazards(dt);
            if (Alive) CheckWakes();
            EmitEmbers();
            BuildLavaPixels(); UploadTextures();
        }

        private void LavaStep(float dt)
        {
            Vector2 gravity = Gravity * 9.81f;
            float damp = 1f / (1f + Damping * dt), drag = .25f * data.dragMul;
            for (int i = 0; i < count; i++)
            {
                if (frozen[i]) { velocities[i] = Vector2.zero; continue; }
                Vector2 velocity = (velocities[i] + gravity * dt) * damp;
                velocities[i] = velocity / (1f + drag * (water[i] ? .5f : 1f) * velocity.magnitude * dt);
            }
            BuildNeighbourGrid();
            for (int i = 0; i < count; i++)
            {
                int length = GatherNeighbours(i);
                for (int k = 0; k < length; k++)
                {
                    int j = neighbours[k];
                    if (j <= i || (frozen[i] && frozen[j])) continue;
                    Vector2 delta = positions[j] - positions[i]; float squared = delta.sqrMagnitude;
                    if (squared >= H * H || squared < 1e-12f) continue;
                    float distance = Mathf.Sqrt(squared); Vector2 normal = delta / distance;
                    float approach = Vector2.Dot(velocities[i] - velocities[j], normal);
                    if (approach <= 0f) continue;
                    float impulse = dt * (1f - distance / H) * (Sigma * approach + Beta * approach * approach) * .5f;
                    if (water[i] && water[j]) impulse *= .5f;
                    if (!frozen[i]) velocities[i] -= normal * impulse;
                    if (!frozen[j]) velocities[j] += normal * impulse;
                }
            }
            for (int i = 0; i < count; i++) { oldPositions[i] = positions[i]; if (!frozen[i]) positions[i] += velocities[i] * dt; }
            BuildNeighbourGrid();
            for (int i = 0; i < count; i++)
            {
                if (frozen[i]) { densities[i] = RestDensity; continue; }
                int length = GatherNeighbours(i); float rho = 0f, nearRho = 0f;
                for (int k = 0; k < length; k++)
                {
                    int j = neighbours[k]; if (j == i) continue;
                    float squared = (positions[j] - positions[i]).sqrMagnitude; if (squared >= H * H) continue;
                    float q = 1f - Mathf.Sqrt(squared) / H; rho += q * q; nearRho += q * q * q;
                }
                densities[i] = rho;
                float pressure = Pressure * (rho - RestDensity); if (pressure < 0f) pressure *= Cohesion;
                float nearPressure = NearPressure * nearRho; Vector2 displacement = Vector2.zero;
                for (int k = 0; k < length; k++)
                {
                    int j = neighbours[k]; if (j == i) continue;
                    Vector2 delta = positions[j] - positions[i]; float squared = delta.sqrMagnitude;
                    if (squared >= H * H || squared < 1e-12f) continue;
                    float distance = Mathf.Sqrt(squared), q = 1f - distance / H;
                    Vector2 movement = delta / distance * (dt * dt * (pressure * q + nearPressure * q * q) * .5f);
                    if (frozen[j]) displacement -= movement * 2f;
                    else { positions[j] += movement; displacement -= movement; }
                }
                positions[i] += displacement;
            }
            for (int i = 0; i < count; i++)
            {
                if (frozen[i]) continue;
                Vector2 point = positions[i];
                ResolveCircle(ref point, WallRadius, null, 3, oldPositions[i], Friction * (water[i] ? .2f : 1f));
                positions[i] = new Vector2(Mathf.Clamp(point.x, .2f, data.w - .2f), Mathf.Clamp(point.y, .2f, data.h - .2f));
                velocities[i] = Vector2.ClampMagnitude((positions[i] - oldPositions[i]) / dt, VelocityCap);
            }
            for (int i = count - 1; i >= 0; i--)
            {
                isolated[i] = frozen[i] ? 0f : densities[i] < .3f ? isolated[i] + dt : 0f;
                if (isolated[i] <= .6f) continue;
                EmitSmoke(positions[i], 1, .26f, .7f);
                RemoveParticle(i);
            }
        }

        private int CountNear(Vector2 point, float range, bool onlyLive)
        {
            int total = 0; float squared = range * range;
            for (int y = GridY(point.y - range); y <= GridY(point.y + range); y++)
            for (int x = GridX(point.x - range); x <= GridX(point.x + range); x++)
            {
                int cell = y * gridWidth + x;
                for (int k = cellStarts[cell]; k < cellStarts[cell + 1]; k++)
                {
                    int i = sorted[k];
                    if (!water[i] && (!onlyLive || !frozen[i]) && (positions[i] - point).sqrMagnitude < squared) total++;
                }
            }
            return total;
        }

        private void StepVent(float dt)
        {
            if (data.vent == null || data.vent.rate <= 0f || data.vent.max <= 0 || ventSealed) return;
            if (TrySealBlockedVent()) return;
            BuildNeighbourGrid();
            ventAccumulator += data.vent.rate * dt;
            while (ventAccumulator >= 1f)
            {
                ventAccumulator -= 1f;
                int slot = ventIndex++ % 3;
                Vector2 point = new Vector2(data.vent.x + (slot - 1) * .24f, data.vent.y + .18f);
                if (SolidAt(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y))) { SealVent(); return; }
                if (vented >= data.vent.max || count >= Capacity || CountNear(point, .2f, false) != 0) continue;
                int i = count++;
                positions[i] = oldPositions[i] = point; velocities[i] = new Vector2(data.vent.dx, data.vent.dy) * 2.6f;
                crust[i] = Hash(made * .731f, 3.3f) > .7f; groups[i] = 8; frozen[i] = water[i] = false; isolated[i] = densities[i] = cooling[i] = 0f;
                made++; vented++;
                BuildNeighbourGrid();
            }
        }

        private void CheckWakes()
        {
            foreach (WakeSeed wake in data.wakes)
            {
                if (woken.Contains(wake.group)) continue;
                float coordinate = wake.axis == "y" ? circles[0].y : circles[0].x;
                if (!(wake.op == "<" ? coordinate < wake.value : coordinate > wake.value)) continue;
                woken.Add(wake.group); bool changed = false;
                for (int i = 0; i < count; i++) if (!water[i] && !(ventSealed && groups[i] == 8) && groups[i] == wake.group && frozen[i]) { frozen[i] = false; changed = true; }
                if (changed) { feedback.Pop("pop.lava_wake"); feedback.Shake(.25f); }
            }
        }

        private void CheckHazards(float dt)
        {
            CheckCrystals();
            int touch = 0, near = 0;
            if (freezeRemaining <= 0f) foreach (Vector2 circle in circles)
            {
                touch = Mathf.Max(touch, CountNear(circle, radius + .08f, true));
                near = Mathf.Max(near, CountNear(circle, radius + .75f, true));
            }
            heat = touch > 0 ? heat + dt * touch : Mathf.Max(0f, heat - dt * 3f);
            if (touch >= 3 || heat > .09f) { player.Die(); return; }
            if (touch > 0 && clock - lastSizzle >= .15f)
            {
                lastSizzle = clock;
                for (int i = 0; i < 4; i++) EmitSmoke(circles[0] + new Vector2(UnityEngine.Random.Range(-.3f, .3f), UnityEngine.Random.Range(-.3f, .3f)), 1, .18f, .6f);
            }
            if (near >= 2) feedback.NearMiss("lava");
        }

        private void BuildLavaPixels()
        {
            Array.Clear(field, 0, field.Length); Array.Clear(crustField, 0, crustField.Length); Array.Clear(frozenField, 0, frozenField.Length);
            Array.Clear(waterField, 0, waterField.Length);
            const float r = .4f, squaredRadius = r * r, pixelRadius = r * PixelsPerUnit;
            for (int i = 0; i < count; i++)
            {
                float px = positions[i].x * PixelsPerUnit, py = positions[i].y * PixelsPerUnit;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(px - pixelRadius)), x1 = Mathf.Min(imageWidth - 1, Mathf.FloorToInt(px + pixelRadius));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(py - pixelRadius)), y1 = Mathf.Min(imageHeight - 1, Mathf.FloorToInt(py + pixelRadius));
                for (int y = y0; y <= y1; y++)
                {
                    float dy = (y + .5f - py) / PixelsPerUnit; int row = (imageHeight - y - 1) * imageWidth;
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + .5f - px) / PixelsPerUnit, distance = dx * dx + dy * dy;
                        if (distance >= squaredRadius) continue;
                        float q = 1f - distance / squaredRadius, weight = q * q; int index = row + x;
                        field[index] += weight; if (crust[i]) crustField[index] += weight; if (frozen[i]) frozenField[index] += weight;
                        if (water[i]) waterField[index] += weight;
                    }
                }
            }
            int flicker = Mathf.FloorToInt(clock * 6f);
            bool iceNow = freezeRemaining > 0f && !(freezeRemaining < 1.2f && Mathf.FloorToInt(freezeRemaining * 7f) % 2 != 0);
            for (int k = 0; k < pixels.Length; k++)
            {
                float f = field[k];
                if (f < .42f) { pixels[k] = glowSourcePixels[k] = new Color32(0, 0, 0, 0); continue; }
                int x = k % imageWidth, y = imageHeight - 1 - k / imageWidth;
                float edge = f - .42f, hard = crustField[k] / f; Color32 color;
                bool isWater = waterField[k] > f * .5f;
                if (isWater)
                    color = edge < .12f ? new Color32(214, 240, 255, 225) : Hash(x >> 2, (y >> 1) + flicker) > .95f ? new Color32(168, 214, 250, 225) : hard > .42f ? new Color32(58, 120, 204, 225) : new Color32(86, 160, 228, 225);
                else if (freezeRemaining > 0f)
                    color = edge < .14f || Hash((x >> 1) * .37f + (y >> 1) * 1.31f, (y >> 1) * .53f - (x >> 1) * .21f) > .92f ? new Color32(228, 246, 255, 255) : hard > .42f ? new Color32(104, 164, 218, 255) : new Color32(150, 206, 240, 255);
                else if (frozenField[k] > f * .5f)
                {
                    int hx = x >> 1, hy = y >> 1;
                    float seam = Hash(hx * .37f + hy * 1.31f, hy * .53f - hx * .21f);
                    color = edge < .14f ? new Color32(150, 64, 34, 255) : seam > .9f ? new Color32(196, 76, 26, 255) : hard > .42f ? new Color32(52, 20, 14, 255) : new Color32(74, 28, 18, 255);
                }
                else if (edge < .14f) color = new Color32(255, 228, 150, 255);
                else if (hard > .42f) color = edge < .6f ? new Color32(150, 46, 22, 255) : new Color32(92, 26, 14, 255);
                else if (edge < .45f) color = new Color32(255, 156, 48, 255);
                else color = Hash(x >> 1, y + flicker * 3) > .93f ? new Color32(255, 128, 60, 255) : new Color32(238, 98, 30, 255);
                if (freezeRemaining > 0f && !iceNow && !isWater)
                    color = new Color32((byte)(color.r * .7f), (byte)(color.g * .7f), (byte)(color.b * .7f), color.a);
                pixels[k] = color;
                glowSourcePixels[k] = isWater ? new Color32(0, 0, 0, 0) : color;
            }
            Downsample(glowSourcePixels, imageWidth, imageHeight, quarterPixels, imageWidth >> 2, imageHeight >> 2);
            Downsample(quarterPixels, imageWidth >> 2, imageHeight >> 2, sixteenthPixels, imageWidth >> 4, imageHeight >> 4);
        }

        private static void Downsample(Color32[] source, int width, int height, Color32[] target, int targetWidth, int targetHeight)
        {
            for (int y = 0; y < targetHeight; y++)
            for (int x = 0; x < targetWidth; x++)
            {
                float sx = (x + .5f) * width / targetWidth - .5f, sy = (y + .5f) * height / targetHeight - .5f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(sx), 0, width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(sy), 0, height - 1);
                int x1 = Mathf.Min(x0 + 1, width - 1), y1 = Mathf.Min(y0 + 1, height - 1);
                Color a = Color.Lerp(source[y0 * width + x0], source[y0 * width + x1], sx - Mathf.Floor(sx));
                Color b = Color.Lerp(source[y1 * width + x0], source[y1 * width + x1], sx - Mathf.Floor(sx));
                target[y * targetWidth + x] = Color.Lerp(a, b, sy - Mathf.Floor(sy));
            }
        }

        private void CopyCurrentToPrevious()
        {
            Array.Copy(pixels, previousPixels, pixels.Length);
            Array.Copy(quarterPixels, previousQuarterPixels, quarterPixels.Length);
            Array.Copy(sixteenthPixels, previousSixteenthPixels, sixteenthPixels.Length);
        }

        private void UploadTextures()
        {
            Upload(lavaTexture, pixels); Upload(quarterTexture, quarterPixels); Upload(sixteenthTexture, sixteenthPixels);
            Upload(previousTexture, previousPixels); Upload(previousQuarterTexture, previousQuarterPixels); Upload(previousSixteenthTexture, previousSixteenthPixels);
        }
        private static void Upload(Texture2D texture, Color32[] colors) { texture.SetPixels32(colors); texture.Apply(false, false); }
        private static float Hash(float x, float y) { double value = Math.Sin(x * 127.1 + y * 311.7) * 43758.5453; return (float)(value - Math.Floor(value)); }

        private ParticleSystem MakeParticles(string name, bool sparks)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main; main.loop = true; main.playOnAwake = false; main.maxParticles = 1500; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = sparks ? .25f : 0f;
            var emission = particles.emission; emission.enabled = false; var shape = particles.shape; shape.enabled = false;
            var overLife = particles.colorOverLifetime; overLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(sparks ? new[] { new GradientColorKey(new Color32(255, 210, 122, 255), 0f), new GradientColorKey(new Color32(255, 210, 122, 255), .5f), new GradientColorKey(new Color32(255, 138, 58, 255), .501f) } : new[] { new GradientColorKey(new Color32(58, 48, 48, 255), 0f) }, new[] { new GradientAlphaKey(sparks ? 1f : .5f, 0f), new GradientAlphaKey(0f, 1f) });
            overLife.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.sortingOrder = 16;
            var material = new Material(Shader.Find("Sprites/Default")); owned.Add(material); renderer.sharedMaterial = material;
            particles.Play();
            return particles;
        }

        private void EmitEmbers()
        {
            if (freezeRemaining > 0f || count == 0) return;
            int sparks = Mathf.FloorToInt(count / 70f + UnityEngine.Random.value);
            for (int k = 0; k < sparks; k++)
            {
                int i = UnityEngine.Random.Range(0, count); if (frozen[i] || water[i]) continue;
                var emit = new ParticleSystem.EmitParams { position = ToWorld(positions[i]), velocity = Vector3.up * UnityEngine.Random.Range(1f, 3f) + new Vector3(UnityEngine.Random.Range(-.5f, .5f), UnityEngine.Random.Range(-.5f, .5f)), startLifetime = UnityEngine.Random.Range(.5f, 1f), startSize = UnityEngine.Random.Range(.07f, .12f), startColor = Color.white };
                embers.Emit(emit, 1);
            }
        }

        private void EmitSmoke(Vector2 point, int amount, float size, float life)
        {
            if (smoke == null) return;
            var emit = new ParticleSystem.EmitParams { position = ToWorld(point), velocity = Vector3.up * 1.6f, startLifetime = life, startSize = size, startColor = Color.white };
            smoke.Emit(emit, amount);
        }

        private void Update()
        {
            if (lavaMaterial == null) return;
            float alpha = started ? Alpha : 1f;
            lavaMaterial.SetFloat("_Interpolation", alpha); quarterMaterial.SetFloat("_Interpolation", alpha); sixteenthMaterial.SetFloat("_Interpolation", alpha);
            UpdateSecondRoundVisuals();
        }

        private void OnDestroy()
        {
            foreach (UnityEngine.Object asset in owned) if (asset != null) Destroy(asset);
        }
    }
}
