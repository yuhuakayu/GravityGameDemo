using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Resource.Scripts.UI.Menu
{
    public sealed class MenuBlobBackground : IDisposable
    {
        const int Width = 480;
        const int Height = 270;
        const int MaxBlobs = 256;
        const float MorphDuration = 0.7f;

        static readonly int BlobCountId = Shader.PropertyToID("_BlobCount");
        static readonly int BlobsId = Shader.PropertyToID("_Blobs");
        static readonly int LayersId = Shader.PropertyToID("_BlobLayers");
        static readonly int BaseId = Shader.PropertyToID("_BaseColor");
        static readonly int BackId = Shader.PropertyToID("_BackColor");
        static readonly int FrontId = Shader.PropertyToID("_FrontColor");
        static readonly int GlowsId = Shader.PropertyToID("_Glows");
        static readonly int GlowParamsId = Shader.PropertyToID("_GlowParams");
        static readonly int ThresholdId = Shader.PropertyToID("_Threshold");

        static readonly Vector4[] BaseColors = { Rgb(52, 46, 108), Rgb(56, 40, 30), Rgb(54, 18, 22), Rgb(112, 172, 230) };
        static readonly Vector4[] BackColors = { Rgb(68, 62, 138), Rgb(74, 52, 36), Rgb(86, 28, 30), Rgb(146, 196, 240) };
        static readonly Vector4[] FrontColors = { Rgb(100, 94, 190), Rgb(112, 80, 50), Rgb(222, 92, 44), Rgb(246, 250, 255) };

        readonly RawImage image;
        readonly RenderTexture texture;
        readonly Material material;
        readonly BlobScene[] scenes;
        readonly Vector4[] blobs = new Vector4[MaxBlobs];
        readonly float[] layers = new float[MaxBlobs];
        readonly Vector4[] lampGlows = new Vector4[4];
        readonly Vector4[] lampParams = new Vector4[4];
        int from;
        int to;
        float progress = 1f;
        bool disposed;

        public MenuBlobBackground(Transform parent, TextAsset data, Shader shader)
        {
            BlobData parsed = JsonUtility.FromJson<BlobData>(data.text);
            scenes = new[]
            {
                new BlobScene(0, parsed.scenes.maze, 11),
                new BlobScene(1, parsed.scenes.mine, 23),
                new BlobScene(2, parsed.scenes.lava, 37),
                new BlobScene(3, parsed.scenes.sky, 51)
            };
            material = new Material(shader) { name = "Menu Blobs", hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat(ThresholdId, parsed.threshold);
            texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = "Menu Blobs 480x270",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();

            var gameObject = new GameObject("Menu Blob Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.SetAsFirstSibling();
            image = gameObject.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.texture = texture;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Tick(0f, 0f, null, null);
        }

        public void ToWorld(int index)
        {
            if (index == to) return;
            from = progress < 0.5f ? from : to;
            to = index;
            progress = 0f;
        }

        public void Tick(float dt, float time, Vector4[] glows, Vector4[] glowParams)
        {
            if (disposed) return;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                progress = Mathf.Min(1f, progress + step / MorphDuration);
                if (progress < 1f && from != to) scenes[from].Update(step);
                scenes[to].Update(step);
            }
            float k = progress < 0.5f ? 4f * progress * progress * progress : 1f - Mathf.Pow(-2f * progress + 2f, 3f) * 0.5f;
            int count = 0;
            if (k < 1f) AppendScene(scenes[from], 1f - k, time, ref count);
            AppendScene(scenes[to], k, time, ref count);

            material.SetInt(BlobCountId, count);
            material.SetVectorArray(BlobsId, blobs);
            material.SetFloatArray(LayersId, layers);
            material.SetVector(BaseId, Vector4.Lerp(BaseColors[from], BaseColors[to], k));
            material.SetVector(BackId, Vector4.Lerp(BackColors[from], BackColors[to], k));
            material.SetVector(FrontId, Vector4.Lerp(FrontColors[from], FrontColors[to], k));
            for (int i = 0; i < lampGlows.Length; i++)
            {
                lampGlows[i] = glows != null && i < glows.Length ? glows[i] : Vector4.zero;
                lampParams[i] = glowParams != null && i < glowParams.Length ? glowParams[i] : Vector4.zero;
            }
            material.SetVectorArray(GlowsId, lampGlows);
            material.SetVectorArray(GlowParamsId, lampParams);
            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(null, texture, material);
            RenderTexture.active = previousTarget;
            GL.sRGBWrite = previousSrgbWrite;
        }

        void AppendScene(BlobScene scene, float weight, float time, ref int count)
        {
            foreach (Mass mass in scene.masses)
            {
                AddBlob(mass.x0 + Mathf.Sin(time * mass.fx + mass.ph) * mass.ax,
                    mass.y0 + Mathf.Sin(time * mass.fy + mass.ph * 1.3f) * mass.ay,
                    mass.r0 + Mathf.Sin(time * mass.fr + mass.ph * 0.7f) * mass.ar,
                    weight, mass.L, ref count);
            }
            foreach (Mover mover in scene.movers)
            {
                AddBlob(mover.x, mover.y, mover.radius, weight, mover.layer, ref count);
                if (mover.state != MoverState.Cloud) continue;
                float r = mover.radius;
                AddBlob(mover.x + r * 0.9f, mover.y + 2f, r * 0.8f, weight, mover.layer, ref count);
                AddBlob(mover.x - r * 0.9f, mover.y + 3f, r * 0.7f, weight, mover.layer, ref count);
                AddBlob(mover.x + r * 0.3f, mover.y - r * 0.5f, r * 0.75f, weight, mover.layer, ref count);
            }
        }

        void AddBlob(float x, float y, float radius, float weight, int layer, ref int count)
        {
            if (count >= MaxBlobs || radius <= 0.5f || weight <= 0.001f) return;
            blobs[count] = new Vector4(x, y, radius, weight);
            layers[count] = layer;
            count++;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (image != null)
            {
                image.texture = null;
                UnityEngine.Object.Destroy(image.gameObject);
            }
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(material);
        }

        static Vector4 Rgb(int r, int g, int b) => new Vector4(r / 255f, g / 255f, b / 255f, 1f);

        [Serializable]
        sealed class BlobData
        {
            public float threshold;
            public SceneData scenes;
        }

        [Serializable]
        sealed class SceneData
        {
            public Mass[] maze;
            public Mass[] mine;
            public Mass[] lava;
            public Mass[] sky;
        }

        [Serializable]
        sealed class Mass
        {
            public int L;
            public float x0, y0, r0, ax, ay, fx, fy, ph, ar, fr;
        }

        enum MoverState { Grow, Go, Pop, Cloud }

        sealed class Mover
        {
            public float x, y, radius, maxRadius, velocityX, velocityY, age, wobble;
            public int layer = 1;
            public MoverState state;
        }

        sealed class BlobScene
        {
            public readonly Mass[] masses;
            public readonly List<Mover> movers = new List<Mover>();
            readonly int kind;
            readonly System.Random random;
            float spawn = 0.4f;

            public BlobScene(int kind, Mass[] masses, int seed)
            {
                this.kind = kind;
                this.masses = masses;
                random = new System.Random(seed);
            }

            public void Update(float dt)
            {
                spawn -= dt;
                if (spawn <= 0f) Spawn();
                for (int i = movers.Count - 1; i >= 0; i--)
                {
                    Mover mover = movers[i];
                    mover.age += dt;
                    if (mover.state == MoverState.Grow)
                    {
                        mover.radius = mover.maxRadius * Mathf.Clamp01(mover.age / 0.9f);
                        if (mover.age > 0.9f) mover.state = MoverState.Go;
                        if (kind == 2) mover.y = Height - 14f - mover.age * 6f;
                    }
                    else if (mover.state == MoverState.Go)
                    {
                        if (kind == 2)
                        {
                            mover.velocityY = Mathf.Max(mover.velocityY - 40f * dt, -34f);
                            mover.y += mover.velocityY * dt;
                            mover.x += Mathf.Sin(mover.age * 2f + mover.wobble) * 6f * dt;
                            if (mover.y < 70f) mover.state = MoverState.Pop;
                        }
                        else
                        {
                            mover.velocityY = Mathf.Min(mover.velocityY + (kind == 1 ? 160f : 60f) * dt, kind == 1 ? 120f : 70f);
                            mover.y += mover.velocityY * dt;
                            if (mover.y > Height - 20f) mover.state = MoverState.Pop;
                        }
                    }
                    else if (mover.state == MoverState.Pop)
                    {
                        mover.radius -= mover.maxRadius * dt / 0.3f;
                        if (kind != 2) mover.y += mover.velocityY * dt * 0.5f;
                    }
                    else
                    {
                        mover.x += mover.velocityX * dt;
                    }
                    if ((mover.state == MoverState.Pop && mover.radius <= 0.3f) || mover.x >= Width + 60f)
                        movers.RemoveAt(i);
                }
            }

            void Spawn()
            {
                if (kind == 3)
                {
                    float y = Range(40f, 210f);
                    float radius = Range(9f, 16f);
                    movers.Add(new Mover
                    {
                        x = -40f, y = y, radius = radius, maxRadius = radius,
                        velocityX = Range(6f, 14f), layer = random.NextDouble() < 0.5 ? 0 : 1,
                        state = MoverState.Cloud
                    });
                    spawn = Range(3f, 6f);
                    return;
                }
                float x = random.NextDouble() < 0.5 ? Range(14f, 130f) : Range(350f, 466f);
                movers.Add(new Mover
                {
                    x = x, y = kind == 2 ? Height - 14f : kind == 1 ? 14f : 19f,
                    maxRadius = kind == 2 ? Range(5f, 10f) : kind == 1 ? Range(3f, 4.5f) : Range(4f, 7f),
                    wobble = kind == 2 ? Range(0f, 6f) : 0f
                });
                spawn = kind == 2 ? Range(0.7f, 1.3f) : kind == 1 ? Range(1.6f, 2.8f) : Range(0.9f, 1.7f);
            }

            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        }
    }
}
