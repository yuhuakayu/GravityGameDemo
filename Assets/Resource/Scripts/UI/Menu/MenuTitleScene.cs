using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Resource.Scripts.UI.Menu
{
    public sealed class MenuTitleScene
    {
        private enum FallMode { Hidden, In, Rest, Out, Gone }

        private sealed class Faller
        {
            public readonly float targetX, targetY;
            public float x, y, velocityX, velocityY, angle, angularVelocity, delay;
            public FallMode mode;
            public Action onLand;
            private int bounces;
            private bool hopping;

            public Faller(float x, float y)
            {
                targetX = this.x = x;
                targetY = this.y = y;
            }

            public bool Visible => mode != FallMode.Hidden && mode != FallMode.Gone &&
                                   !(mode == FallMode.In && delay > 0 && y < targetY - 1);

            public void Drop(float height, float wait)
            {
                hopping = false;
                mode = FallMode.In;
                x = targetX;
                y = targetY - height;
                velocityX = velocityY = angularVelocity = 0;
                angle = Random.Range(-0.125f, 0.125f);
                delay = wait;
                bounces = 0;
            }

            public void Hop(float wait)
            {
                if (mode != FallMode.Rest) return;
                mode = FallMode.In;
                velocityY = -330;
                delay = wait;
                bounces = 0;
                hopping = true;
            }

            public void FallAway(float wait)
            {
                mode = FallMode.Out;
                velocityY = -Random.Range(120f, 380f);
                velocityX = Random.Range(-110f, 110f);
                angularVelocity = Random.Range(-0.8f, 0.8f);
                delay = wait;
            }

            public void Step(float dt)
            {
                if (mode == FallMode.Hidden || mode == FallMode.Gone) return;
                if (delay > 0) { delay -= dt; return; }
                if (mode == FallMode.In)
                {
                    velocityY += 3600 * dt;
                    y += velocityY * dt;
                    angle *= Mathf.Exp(-6 * dt);
                    if (y < targetY) return;
                    y = targetY;
                    if (velocityY > 260 && bounces < 3)
                    {
                        velocityY *= -0.28f;
                        bounces++;
                        if (bounces == 1 && !hopping) onLand?.Invoke();
                    }
                    else
                    {
                        velocityY = angle = 0;
                        mode = FallMode.Rest;
                        hopping = false;
                    }
                }
                else if (mode == FallMode.Out)
                {
                    velocityY += 3600 * dt;
                    x += velocityX * dt;
                    y += velocityY * dt;
                    angle += angularVelocity * dt;
                    if (y > 1780) mode = FallMode.Gone;
                }
            }
        }

        private sealed class Lamp
        {
            public float x, titleLength, selectLength, length = -160, target = -160;
            public float velocity, angle, angularVelocity, phase, flick = 1, wait, next;
            public bool pending;
            public int frame;
            public Image body;
            public Image[] chain;
            public float Visibility => Mathf.Clamp01((length + 40) / 60);
            public Vector2 Center => new Vector2(x + Mathf.Sin(angle) * (length + 28),
                                                 -6 + Mathf.Cos(angle) * (length + 28));

            public void Step(float dt, float time)
            {
                if (pending)
                {
                    wait -= dt;
                    if (wait <= 0)
                    {
                        target = next;
                        pending = false;
                        angularVelocity += Random.Range(-0.2f, 0.2f);
                    }
                }
                velocity += (70 * (target - length) - 9 * velocity) * dt;
                length += velocity * dt;
                float w2 = 1000 / Mathf.Max(80, length + 60);
                angularVelocity += (-w2 * Mathf.Sin(angle) - 0.55f * angularVelocity +
                                    Mathf.Sin(time * 0.6f + phase) * 0.06f) * dt;
                angle += angularVelocity * dt;
                float n = Mathf.Sin(time * 13 + phase) * Mathf.Sin(time * 7.3f + phase * 2);
                flick = 1 + 0.08f * n;
                frame = n > 0.35f ? 1 : n < -0.35f ? 2 : 0;
            }
        }

        private sealed class Particle
        {
            public Image image;
            public bool active;
            public float x, y, vx, vy, age, life;
            public Color color;
        }

        [Serializable]
        private sealed class LogoLayout { public LogoLetter[] letters; }

        [Serializable]
        private sealed class LogoLetter { public string file; public float x, y; }

        private readonly MenuArt art;
        private readonly Action<float> shake;
        private readonly Faller room = new Faller(960, 236);
        private readonly Faller[] letters = new Faller[8];
        private readonly Faller[] items = new Faller[4];
        private readonly RectTransform[] letterRoots = new RectTransform[8];
        private readonly Vector2[] letterSizes = new Vector2[8];
        private readonly RectTransform[] itemRoots = new RectTransform[4];
        private readonly TextMeshProUGUI[] itemLabels = new TextMeshProUGUI[4];
        private readonly Image[,] diamonds = new Image[4, 2];
        private readonly Lamp[] lamps = new Lamp[4];
        private readonly Particle[] particles = new Particle[40];
        private readonly Sprite[] lampFrames = new Sprite[3];
        private readonly RectTransform roomRoot;
        private readonly Image door, outerLight, innerLight;
        private float roomAngle, roomFrom, roomTo, roomTimer = 2.2f, doorProgress = 1, hopTimer = 6;
        private bool roomTurning;
        private static readonly string[] MenuKeys = { "menu.start", "menu.levels", "menu.options", "menu.quit" };
        private static readonly Color Warm = Rgb(255, 186, 104);

        public RectTransform[] MenuItems { get; } = new RectTransform[4];

        public MenuTitleScene(Transform parent, MenuArt art, Action<float> shake)
        {
            this.art = art;
            this.shake = shake;
            RectTransform particleRoot = MenuDrawing.Node(parent, "Particles", 0, 0, 0, 0);
            RectTransform lampRoot = MenuDrawing.Node(parent, "Lamps", 0, 0, 0, 0);
            RectTransform titleRoot = MenuDrawing.Node(parent, "Title", 0, 0, 0, 0);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i] = new Particle { image = MenuDrawing.Box(particleRoot, "Particle" + i, Color.clear, 0, 0, 4, 4, false) };
                particles[i].image.gameObject.SetActive(false);
            }

            for (int i = 0; i < lampFrames.Length; i++) lampFrames[i] = art.Get("Lamp/lamp_" + i);
            float[] xs = { 176, 330, 1622, 1772 };
            float[] titleLengths = { 190, 330, 270, 150 };
            float[] selectLengths = { 96, 150, 120, 74 };
            for (int i = 0; i < lamps.Length; i++)
            {
                var lamp = new Lamp { x = xs[i], titleLength = titleLengths[i], selectLength = selectLengths[i], phase = Random.value * 10, chain = new Image[48] };
                RectTransform node = MenuDrawing.Node(lampRoot, "Lamp" + i, 0, 0, 0, 0);
                for (int j = 0; j < lamp.chain.Length; j++)
                    lamp.chain[j] = MenuDrawing.Image(node, "Chain" + j, art.Get(j % 2 == 0 ? "Lamp/chain_dark" : "Lamp/chain_light"), 0, 0, j % 2 == 0 ? 4 : 8, 12, false);
                lamp.body = MenuDrawing.Image(node, "Body", lampFrames[0], 0, 0, 44, 56);
                lamps[i] = lamp;
            }

            roomRoot = MenuDrawing.Node(titleRoot, "Room", 960, 236, 0, 0);
            MenuDrawing.Box(roomRoot, "Shadow", Rgb(16, 14, 34, 0.35f), -134, -130, 288, 288, false);
            MenuDrawing.Image(roomRoot, "RoomArt", art.Get("Title/room"), -144, -144, 288, 288, false);
            door = MenuDrawing.Image(roomRoot, "Door", art.Get("Door/maze_open", 3), -8, -8, 128, 128, false);
            outerLight = MenuDrawing.Box(roomRoot, "OuterDoorLight", Color.clear, -32, -48, 176, 168, false);
            innerLight = MenuDrawing.Box(roomRoot, "InnerDoorLight", Color.clear, -8, -24, 128, 144, false);
            room.onLand = () => { this.shake?.Invoke(3); KickLamps(0.15f); };

            LogoLetter[] logo = JsonUtility.FromJson<LogoLayout>(art.logoLetters.text).letters;
            for (int i = 0; i < letters.Length; i++)
            {
                Sprite sprite = art.Get("Title/" + System.IO.Path.GetFileNameWithoutExtension(logo[i].file));
                Vector2 size = sprite.rect.size * 3;
                letterSizes[i] = size;
                letters[i] = new Faller(513 + logo[i].x * 3, 474 + logo[i].y * 3);
                letters[i].onLand = () => this.shake?.Invoke(1.5f);
                letterRoots[i] = MenuDrawing.Node(titleRoot, "LogoLetter" + i, 0, 0, 0, 0);
                MenuDrawing.Image(letterRoots[i], "Art", sprite, -size.x / 2, -size.y / 2, size.x, size.y, false);
            }

            for (int i = 0; i < items.Length; i++)
            {
                items[i] = new Faller(960, 700 + i * 66);
                itemRoots[i] = MenuDrawing.Node(titleRoot, "MenuItem" + i, 960, 700 + i * 66, 0, 0);
                itemLabels[i] = MenuDrawing.Label(itemRoots[i], "Label", "", 0, 0, 39, true, Rgb(176, 172, 226), 2, true);
                MenuItems[i] = itemLabels[i].rectTransform;
                for (int side = 0; side < 2; side++)
                    diamonds[i, side] = MenuDrawing.Image(itemRoots[i], "Diamond" + side, art.Get("Select/diamond_pink"), 0, 0, 20, 20);
            }
            RefreshLocalizedTexts();
        }

        public void Enter(bool first)
        {
            float delay = first ? 0.2f : 0.1f;
            room.Drop(700, delay);
            for (int i = 0; i < letters.Length; i++) letters[i].Drop(Random.Range(900f, 1100f), delay + 0.35f + i * 0.06f);
            for (int i = 0; i < items.Length; i++) items[i].Drop(520, delay + 0.85f + i * 0.07f);
            roomTurning = false;
            roomTimer = 2.2f;
            hopTimer = 6;
            if (first)
                foreach (Lamp lamp in lamps)
                {
                    lamp.length = lamp.target = -160;
                    lamp.velocity = lamp.angle = lamp.angularVelocity = 0;
                    lamp.pending = false;
                }
            LampsTo(0, first ? 1 : 0.3f);
        }

        public void Exit()
        {
            foreach (Faller letter in letters) letter.FallAway(Random.Range(0f, 0.12f));
            foreach (Faller item in items) item.FallAway(Random.Range(0f, 0.12f));
            room.FallAway(0.05f);
        }

        public void LampsTo(int mode, float delay = 0)
        {
            int[] order = { 0, 2, 3, 1 };
            for (int i = 0; i < lamps.Length; i++)
            {
                Lamp lamp = lamps[i];
                float target = mode == 0 ? lamp.titleLength : mode == 1 ? lamp.selectLength : -170;
                if (Mathf.Abs(target - (lamp.pending ? lamp.next : lamp.target)) <= 1) continue;
                lamp.next = target;
                lamp.wait = delay + order[i] * 0.12f;
                lamp.pending = true;
            }
        }

        public void KickLamps(float strength)
        {
            foreach (Lamp lamp in lamps) lamp.angularVelocity += Random.Range(-strength, strength);
        }

        public void Step(float dt, float time, bool titleScreen, int selectedMenu, int world)
        {
            foreach (Faller letter in letters) letter.Step(dt);
            foreach (Faller item in items) item.Step(dt);
            room.Step(dt);
            roomTimer -= dt;
            if (!roomTurning && roomTimer <= 0 && room.mode == FallMode.Rest)
            {
                roomTurning = true;
                roomTimer = 1;
                roomFrom = roomAngle;
                roomTo = roomAngle + Mathf.PI / 2;
                for (int i = 0; i < lamps.Length; i++) lamps[i].angularVelocity += i % 2 == 0 ? -0.22f : 0.22f;
            }
            else if (roomTurning)
            {
                float k = Mathf.Clamp01(1 - roomTimer);
                k = k < 0.5f ? 4 * k * k * k : 1 - Mathf.Pow(-2 * k + 2, 3) / 2;
                roomAngle = Mathf.Lerp(roomFrom, roomTo, k);
                if (roomTimer <= 0) { roomAngle = roomTo; roomTurning = false; roomTimer = 2.4f; }
            }
            doorProgress = Mathf.Clamp01(doorProgress + (Mathf.Cos(roomAngle + room.angle) > 0.94f ? dt : -dt) / 0.45f);
            foreach (Lamp lamp in lamps) lamp.Step(dt, time);
            bool settled = true;
            foreach (Faller letter in letters) settled &= letter.mode == FallMode.Rest;
            if (titleScreen && settled)
            {
                hopTimer -= dt;
                if (hopTimer <= 0)
                {
                    for (int i = 0; i < letters.Length; i++) letters[i].Hop(i * 0.055f);
                    hopTimer = 7;
                }
            }
            StepParticles(dt, titleScreen, world);
        }

        private void StepParticles(float dt, bool titleScreen, int world)
        {
            if (Random.value < dt * 2.2f)
                foreach (Lamp lamp in lamps)
                    if (lamp.Visibility > 0.5f && Random.value < 0.4f)
                    {
                        Vector2 center = lamp.Center;
                        Spawn(center.x + Random.Range(-8f, 8f), center.y - 10, Random.Range(-6f, 6f), Random.Range(-30f, -14f), Random.Range(1.2f, 2.2f), Warm);
                    }
            if ((titleScreen || world == 0) && Random.value < dt * 3)
                Spawn(Random.value * 1920, Random.value * 1080, Random.Range(-4f, 4f), Random.Range(-10f, -3f), Random.Range(3f, 5f), Rgb(170, 166, 236));
            if (!titleScreen && world == 1 && Random.value < dt * 4)
                Spawn(Random.value * 1920, Random.value * 1080, Random.Range(-8f, 8f), Random.Range(-3f, 3f), Random.Range(3f, 5f), Rgb(220, 180, 130));
            if (!titleScreen && world == 2 && Random.value < dt * 7)
                Spawn(Random.value * 1920, 1080 - Random.value * 120, Random.Range(-10f, 10f), Random.Range(-90f, -40f), Random.Range(2f, 4f), Random.value < 0.5f ? Rgb(255, 190, 90) : Rgb(255, 120, 50));
            foreach (Particle particle in particles)
            {
                if (!particle.active) continue;
                particle.age += dt;
                particle.x += particle.vx * dt;
                particle.y += particle.vy * dt;
                if (particle.age > particle.life) particle.active = false;
            }
        }

        private void Spawn(float x, float y, float vx, float vy, float life, Color color)
        {
            foreach (Particle particle in particles)
            {
                if (particle.active) continue;
                particle.active = true;
                particle.x = x;
                particle.y = y;
                particle.vx = vx;
                particle.vy = vy;
                particle.age = 0;
                particle.life = life;
                particle.color = color;
                return;
            }
        }

        public void Draw(float time, bool titleScreen, int selectedMenu)
        {
            foreach (Particle particle in particles)
            {
                particle.image.gameObject.SetActive(particle.active);
                if (!particle.active) continue;
                Color color = particle.color;
                color.a = 0.8f * Mathf.Clamp01(Mathf.Min(particle.age / 0.4f, (particle.life - particle.age) / 0.6f));
                particle.image.color = color;
                MenuDrawing.Position(particle.image.rectTransform, Snap(particle.x), Snap(particle.y));
            }
            foreach (Lamp lamp in lamps)
            {
                bool visible = lamp.Visibility > 0;
                for (int j = 0; j < lamp.chain.Length; j++)
                {
                    bool show = visible && j * 12 < lamp.length;
                    lamp.chain[j].gameObject.SetActive(show);
                    if (show) MenuDrawing.Position(lamp.chain[j].rectTransform,
                        Snap(lamp.x + Mathf.Sin(lamp.angle) * j * 12) - (j % 2 == 0 ? 2 : 4),
                        Snap(-6 + Mathf.Cos(lamp.angle) * j * 12));
                }
                lamp.body.gameObject.SetActive(visible);
                if (!visible) continue;
                Vector2 center = lamp.Center;
                MenuDrawing.Position(lamp.body.rectTransform, center.x, center.y);
                MenuDrawing.Rotation(lamp.body.rectTransform, -lamp.angle);
                lamp.body.sprite = lampFrames[lamp.frame];
            }
            roomRoot.gameObject.SetActive(room.Visible);
            if (room.Visible)
            {
                MenuDrawing.Position(roomRoot, room.x, room.y);
                MenuDrawing.Rotation(roomRoot, roomAngle + room.angle);
                door.sprite = doorProgress > 0 ? art.Get("Door/maze_open", Mathf.Min(3, Mathf.FloorToInt(doorProgress * 3.999f))) : art.Get("Door/maze_idle", Mathf.FloorToInt(time * 8) % 6);
                Color light = Rgb(63, 214, 224, doorProgress > 0.5f ? 0.1f * doorProgress : 0);
                outerLight.color = innerLight.color = light;
            }
            for (int i = 0; i < letters.Length; i++)
            {
                Faller letter = letters[i];
                letterRoots[i].gameObject.SetActive(letter.Visible);
                if (!letter.Visible) continue;
                MenuDrawing.Position(letterRoots[i], letter.x + letterSizes[i].x / 2, letter.y + letterSizes[i].y / 2);
                MenuDrawing.Rotation(letterRoots[i], letter.angle);
            }
            for (int i = 0; i < items.Length; i++)
            {
                Faller item = items[i];
                itemRoots[i].gameObject.SetActive(item.Visible);
                if (!item.Visible) continue;
                MenuDrawing.Position(itemRoots[i], item.x, item.y);
                MenuDrawing.Rotation(itemRoots[i], item.angle);
                bool selected = titleScreen && selectedMenu == i;
                itemLabels[i].color = selected ? Rgb(242, 239, 255) : Rgb(176, 172, 226);
                float bounce = Mathf.Abs(Mathf.Sin(time * 5.2f));
                bool squash = bounce < 0.15f;
                for (int side = 0; side < 2; side++)
                {
                    Image diamond = diamonds[i, side];
                    diamond.gameObject.SetActive(selected);
                    if (!selected) continue;
                    diamond.rectTransform.sizeDelta = squash ? new Vector2(24, 16) : new Vector2(20, 20);
                    MenuDrawing.Position(diamond.rectTransform, (side == 0 ? -1 : 1) * (MenuItems[i].rect.width / 2 + 36), -6 - bounce * 18);
                }
            }
        }

        public void RefreshLocalizedTexts()
        {
            for (int i = 0; i < itemLabels.Length; i++)
            {
                TextMeshProUGUI label = itemLabels[i];
                label.text = LocalizationManager.Instance.Get(MenuKeys[i]);
                Vector2 preferred = label.GetPreferredValues(label.text);
                label.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(preferred.x), Mathf.Ceil(preferred.y));
            }
        }

        public void GetGlows(Vector4[] centers, Vector4[] values)
        {
            for (int i = 0; i < lamps.Length; i++)
            {
                Vector2 center = lamps[i].Center / 4;
                centers[i] = new Vector4(center.x, center.y, 0, 0);
                values[i] = new Vector4(lamps[i].flick, lamps[i].Visibility, 0, 0);
            }
        }

        private static float Snap(float value) => Mathf.Round(value / 4) * 4;
        private static Color Rgb(int r, int g, int b, float a = 1) => new Color(r / 255f, g / 255f, b / 255f, a);
    }
}
