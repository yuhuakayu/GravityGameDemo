using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    [DefaultExecutionOrder(-160)]
    public sealed class MazeMechanisms : MonoBehaviour
    {
        public string[] map;
        public WorldRotator world;
        public PlayerController player;
        public NearMissFeedback feedback;
        public Sprite[] sprites;
        public LaserEmitter[] emitters;
        [Serializable] public sealed class Mirror
        {
            public Vector2 center;
            public bool slash;
            public Transform plank;
        }
        [Serializable] public sealed class Screw
        {
            public Vector2 center;
            public int direction;
            public BoxCollider2D collider;
            public Transform bars, bolt, visual;
            public LineRenderer arc;
            [NonSerialized] public float turn;
            [NonSerialized] public bool open;
        }
        public Mirror[] mirrors = Array.Empty<Mirror>();
        public Screw[] screws = Array.Empty<Screw>();
        private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>();
        private readonly List<LineRenderer> beams = new List<LineRenderer>();
        private readonly List<Vector2[]> beamPoints = new List<Vector2[]>();
        private Rigidbody2D playerBody;
        private float previousAngle, iceAngle;
        private bool iceCarry;
        private int Width => map[0].Length * 2;
        private int Height => map.Length * 2;
        public Vector2 CellCenter(int x, int y) => new Vector2(x * 2 + 1 - Width * .5f, Height * .5f - y * 2 - 1);
        private Sprite FindSprite(string name) => Array.Find(sprites, s => s != null && s.name == name);

        public void BuildVisuals()
        {
            var mirrorList = new List<Mirror>(); var screwList = new List<Screw>();
            var jellyBodies = new List<Rigidbody2D>(world.attachedGeometryBodies);
            // One composite for every jelly block removes the seams between adjacent cells.
            Transform jellyRoot = null;
            for (int y = 0; y < map.Length; y++) for (int x = 0; x < map[y].Length; x++)
            {
                char cell = map[y][x]; Vector2 center = CellCenter(x, y);
                if (cell == 'J')
                {
                    if (jellyRoot == null)
                    {
                        jellyRoot = new GameObject("JellyBlocks").transform; jellyRoot.SetParent(transform, false);
                        jellyRoot.gameObject.layer = LayerMask.NameToLayer("Wall");
                        var body = jellyRoot.gameObject.AddComponent<Rigidbody2D>();
                        body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f;
                        body.interpolation = RigidbodyInterpolation2D.Interpolate;
                        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                        jellyBodies.Add(body);
                        jellyRoot.gameObject.AddComponent<CompositeCollider2D>().geometryType = CompositeCollider2D.GeometryType.Polygons;
                        var jelly = jellyRoot.gameObject.AddComponent<MazeJellyBlock>();
                        jelly.feedback = feedback; jelly.idle = FindSprite("jelly_block"); jelly.squash = FindSprite("jelly_squash");
                    }
                    var visual = Image("Jelly", "jelly_block", center);
                    visual.transform.SetParent(jellyRoot, true);
                    var box = visual.gameObject.AddComponent<BoxCollider2D>(); box.size = Vector2.one * 2f;
                    box.compositeOperation = Collider2D.CompositeOperation.Merge;
                }
                else if (cell == 'i')
                {
                    Image("Ice", "ice_block", center);
                    Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
                    for (int side = 0; side < 4; side++)
                    {
                        var d = directions[side]; char neighbour = CellAt(x + d.x, y - d.y);
                        if (neighbour == '#' || neighbour == 'i' || neighbour == 'X') continue;
                        var rim = Image("IceRim", "ice_rim", center + (Vector2)d);
                        rim.transform.localRotation = Quaternion.Euler(0f, 0f, -side * 90f);
                    }
                }
                else if (cell == 'm' || cell == 'M')
                {
                    var block = Image("Mirror", "mirror_block", center);
                    SolidBox(block.gameObject);
                    var plank = Image("MirrorPlank", "mirror_plank", center);
                    plank.sortingOrder = block.sortingOrder + 1;
                    plank.transform.localRotation = Quaternion.Euler(0f, 0f, cell == 'm' ? 45f : -45f);
                    mirrorList.Add(new Mirror { center = center, slash = cell == 'm', plank = plank.transform });
                }
                else if (cell == 'Z' || cell == 'z')
                {
                    var visual = new GameObject("ScrewGate").transform; visual.SetParent(transform, false); visual.localPosition = center;
                    bool horizontal = CellAt(x, y - 1) != '#' || CellAt(x, y + 1) != '#';
                    var bars = Image("ScrewBars", horizontal ? "screw_bars_h" : "screw_bars_v", center);
                    bars.transform.SetParent(visual, true);
                    var bolt = Image("ScrewBolt", "screw_bolt", center); bolt.transform.SetParent(visual, true);
                    Image("ScrewRing", "screw_ring", center).transform.SetParent(visual, true);
                    Image("ScrewArrow", cell == 'Z' ? "screw_arrow_cw" : "screw_arrow_ccw", center).transform.SetParent(visual, true);
                    var box = new GameObject("ScrewCollision"); box.transform.SetParent(transform, false); box.transform.localPosition = center;
                    SolidBox(box);
                    var arc = Line("ScrewProgress", .08f, cell == 'Z' ? new Color32(255,214,90,255) : new Color32(127,216,255,255));
                    arc.transform.SetParent(visual, false);
                    screwList.Add(new Screw { center = center, direction = cell == 'Z' ? 1 : -1,
                        collider = box.GetComponent<BoxCollider2D>(), visual = visual, bars = bars.transform, bolt = bolt.transform, arc = arc });
                }
            }
            mirrors = mirrorList.ToArray(); screws = screwList.ToArray();
            world.attachedGeometryBodies = jellyBodies.ToArray();
            if (jellyRoot != null) jellyRoot.GetComponent<CompositeCollider2D>().GenerateGeometry();
            if (mirrors.Length > 0) foreach (var emitter in emitters) emitter.UseExternalBeam();
        }

        private void Awake()
        {
            playerBody = player.GetComponent<Rigidbody2D>();
            previousAngle = iceAngle = world.ClockwiseAngleReadout;
            if (mirrors.Length > 0)
            {
                foreach (var emitter in emitters)
                {
                    var line = Line("ReflectedLaser", .0625f, DangerVisual.Red());
                    beams.Add(line); beamPoints.Add(Array.Empty<Vector2>());
                    emitter.UseExternalBeam();
                }
                TraceBeams();
            }
        }
        private void FixedUpdate()
        {
            float angle = world.ClockwiseAngleReadout, delta = angle - previousAngle; previousAngle = angle;
            if (!player.IsGameplayActive || Time.timeScale <= 0f) return;
            float gx = Mathf.Sin(angle * Mathf.Deg2Rad);
            bool changed = false;
            foreach (var mirror in mirrors)
            {
                bool want = gx > .17364818f ? false : gx < -.17364818f ? true : mirror.slash;
                if (want != mirror.slash) { mirror.slash = want; changed = true; }
            }
            if (changed) { feedback.Pop("pop.mirror_flip"); TraceBeams(); }
            NearMissFeedback.GetPlayerCircles(player, out Vector2 a, out Vector2 b, out float radius);
            for (int i = 0; i < beamPoints.Count; i++)
            {
                var points = beamPoints[i];
                for (int j = 1; j < points.Length; j++)
                {
                    Vector2 start = transform.TransformPoint(points[j-1]), end = transform.TransformPoint(points[j]);
                    float distance = Mathf.Min(SegmentDistance(a,start,end),SegmentDistance(b,start,end));
                    if (distance < radius + .03125f) { player.Die(); return; }
                    if (distance < radius + .5f) feedback.NearMiss("laser");
                }
            }
            foreach (var screw in screws)
            {
                if (screw.open) continue;
                int before = Mathf.FloorToInt(screw.turn/90f);
                screw.turn = Mathf.Clamp(screw.turn + screw.direction * delta,0f,360f);
                int after = Mathf.FloorToInt(screw.turn/90f);
                if (!screw.open) for (int q=before+1;q<=Mathf.Min(3,after);q++) { feedback.Pop("pop.screw_"+q); feedback.Shake(.05f); }
                if (!screw.open && screw.turn >= 350f)
                {
                    screw.open = true; screw.collider.enabled = false; screw.visual.gameObject.SetActive(false); feedback.Shake(.15f);
                    feedback.Pop(screws.Length == 1 ? "pop.screw_open" : screw.direction > 0 ? "pop.screw_open_cw" : "pop.screw_open_ccw");
                }
            }
        }
        private void LateUpdate()
        {
            foreach (var mirror in mirrors)
                mirror.plank.localRotation = Quaternion.RotateTowards(mirror.plank.localRotation,
                    Quaternion.Euler(0f,0f,mirror.slash ? 45f : -45f),900f * Time.deltaTime);
            foreach (var screw in screws)
            {
                screw.visual.gameObject.SetActive(!screw.open);
                float fraction = screw.turn/360f;
                screw.bars.localScale = new Vector3(1f-.35f*fraction,1f,1f);
                screw.bolt.localRotation = Quaternion.Euler(0f,0f,-screw.direction*screw.turn*2f);
                int count = Mathf.Max(2,Mathf.CeilToInt(fraction*48f)+1); screw.arc.positionCount=count;
                for (int i=0;i<count;i++)
                {
                    float a = Mathf.PI*.5f - screw.direction * fraction * Mathf.PI*2f*i/(count-1);
                    screw.arc.SetPosition(i,new Vector3(Mathf.Cos(a)*.72f,Mathf.Sin(a)*.72f));
                }
            }
        }
        public Vector2 ApplyIceVelocity(Vector2 previous, Vector2 normalVelocity, float dt)
        {
            float angle=world.ClockwiseAngleReadout, delta=angle-iceAngle; iceAngle=angle;
            bool ground=false, ice=false; contacts.Clear(); playerBody.GetContacts(contacts);
            foreach (var contact in contacts) if (contact.normal.y > .5f)
            {
                ground=true; Vector2 p=transform.InverseTransformPoint(contact.point-contact.normal*.04f);
                int x=Mathf.FloorToInt((p.x+Width*.5f)/2f), y=Mathf.FloorToInt((Height*.5f-p.y)/2f);
                if (CellAt(x,y)=='i') ice=true;
            }
            if (ice) iceCarry=true; else if (ground) iceCarry=false;
            if (!iceCarry || player.IsGoalLocked) return normalVelocity;
            Vector2 velocity=Quaternion.Euler(0f,0f,-delta)*previous;
            velocity += Physics2D.gravity*(player.SimulatedGravityScale*dt);
            return Vector2.ClampMagnitude(velocity,9f);
        }
        private void TraceBeams()
        {
            for (int i=0;i<emitters.Length;i++)
            {
                var points=new List<Vector2>();
                Vector2 p=transform.InverseTransformPoint(emitters[i].BeamStart);
                Vector2 direction=transform.InverseTransformVector(emitters[i].transform.right).normalized;
                direction = Mathf.Abs(direction.x)>.5f ? new Vector2(Mathf.Sign(direction.x),0f) : new Vector2(0f,Mathf.Sign(direction.y));
                points.Add(p); Mirror from=null;
                for (int bounce=0;bounce<10;bounce++)
                {
                    Mirror hit=null; Vector2 end=p;
                    for (int step=1;step<1024;step++)
                    {
                        Vector2 probe=p+direction*(step*.125f);
                        int x=Mathf.FloorToInt((probe.x+Width*.5f)/2f), y=Mathf.FloorToInt((Height*.5f-probe.y)/2f);
                        foreach (var mirror in mirrors) if (mirror!=from && (mirror.center-CellCenter(x,y)).sqrMagnitude<.01f) { hit=mirror; break; }
                        if (hit!=null) { end=hit.center; break; }
                        char cell=CellAt(x,y);
                        if (cell=='#' || cell=='i' || cell=='X') { end=probe; break; }
                        end=probe;
                    }
                    points.Add(end); if (hit==null) break;
                    direction=hit.slash ? new Vector2(direction.y,direction.x) : new Vector2(-direction.y,-direction.x);
                    p=end; from=hit;
                }
                beamPoints[i]=points.ToArray(); beams[i].positionCount=points.Count;
                for (int j=0;j<points.Count;j++) beams[i].SetPosition(j,points[j]);
            }
        }
        private char CellAt(int x,int y) => y<0 || x<0 || y>=map.Length || x>=map[y].Length ? '#' : map[y][x];
        private SpriteRenderer Image(string name,string sprite,Vector2 point)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);go.transform.localPosition=point;
            var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=FindSprite(sprite);renderer.sortingOrder=4;
            renderer.sharedMaterial=new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));return renderer;
        }
        private static void SolidBox(GameObject go) { go.layer=LayerMask.NameToLayer("Wall");go.AddComponent<BoxCollider2D>().size=Vector2.one*2f; }
        private LineRenderer Line(string name,float width,Color color)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();
            line.useWorldSpace=false;line.startWidth=line.endWidth=width;line.startColor=line.endColor=color;
            line.sharedMaterial=new Material(Shader.Find("Sprites/Default"));line.sortingOrder=6;return line;
        }
        private static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        { Vector2 d=b-a; return Vector2.Distance(p,a+d*(d.sqrMagnitude>0f ? Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude) : 0f)); }
    }
}
