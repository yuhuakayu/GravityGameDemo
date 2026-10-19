using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Resource.Scripts.UI.Menu
{
    public sealed class MenuPresentation : MonoBehaviour
    {
        private static readonly string[] Worlds = { "maze", "mine", "lava", "sky" };
        private static readonly string[] English = { "THE MAZE", "THE MINE", "THE FORGE", "THE SKY" };
        private static readonly string[] Prefixes = { "Maze_", "Mine_", "Lava_", "Sky_" };
        private static readonly Color[] Rail = { C(92,90,160), C(128,88,52), C(150,56,44), Color.white };
        private static readonly Color[] Frame = { C(112,118,182), C(176,122,70), C(214,92,60), Color.white };
        private static readonly Color[] Hi = { C(150,150,220), C(214,160,96), C(255,140,90), Color.white };
        private static readonly Color[] Accent = { C(244,168,184), C(255,196,96), C(255,196,96), C(255,176,64) };
        private static readonly Color[] InkText = { C(232,228,255), C(252,236,214), C(255,232,216), C(44,64,110) };
        private static Color C(int r,int g,int b,float a=1f) => MenuDrawing.RGB(r,g,b,a);

        private sealed class Card
        {
            public int world, index, global;
            public LevelEntry level;
            public MenuDangle motion;
            public RectTransform root, body, rope;
            public Image anchor, anchorHi, image, shadow, halo1, halo2, diamond, check;
            public TextMeshProUGUI number, name;
            public float snapTime = -1f;
        }

        private sealed class Sign
        {
            public int world;
            public MenuDangle motion;
            public RectTransform root, body;
            public Image face, shadow, leftDoor, rightDoor;
            public readonly List<Image> chains = new List<Image>();
            public TextMeshProUGUI name, english;
        }

        private List<LevelEntry> _levels;
        private readonly List<Card> _cards = new List<Card>();
        private readonly List<Card> _oldCards = new List<Card>();
        private readonly Vector4[] _glows = new Vector4[4], _glowValues = new Vector4[4];
        private Action _openOptions;
        private Action<string> _loadScene;
        private Func<bool> _inputBlocked;
        private MenuArt _art;
        private MenuBlobBackground _background;
        private MenuTitleScene _title;
        private RectTransform _root, _selectionLayer, _railRoot;
        private CanvasGroup _railGroup;
        private Image[] _gems;
        private TextMeshProUGUI _left, _right, _hint, _version;
        private Sign _sign, _oldSign;
        private int _world, _selected, _menu;
        private bool _selectScreen, _stickX, _stickY;
        private float _time, _busy, _selectAge, _railAlpha, _flip = 1f, _shake;
        private int _flipWorld;
        private float _loadDelay = -1f;
        private string _pendingScene;
        private Vector2 _lastMouse = new Vector2(-10000f,-10000f);

        public void Build(Transform parent, List<LevelEntry> levels, Action openOptions, Action<string> loadScene, Func<bool> inputBlocked)
        {
            transform.SetParent(parent,false);
            _levels = levels;
            _openOptions = openOptions;
            _loadScene = loadScene;
            _inputBlocked = inputBlocked;
            _art = Resources.Load<MenuArt>("Menu/MenuArt");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2001;
            canvas.pixelPerfect = true;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f,1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _root = MenuDrawing.Node(transform,"MenuRoot",0,0,1920,1080,false);
            _background = new MenuBlobBackground(_root,_art.blobs,_art.blobShader);
            // Particles and lamps are behind the rails; the room and letters draw last.
            _title = new MenuTitleScene(_root,_art,Shake);
            _selectionLayer = MenuDrawing.Node(_root,"LevelSelection",0,0,0,0,false);
            _title.MenuItems[0].parent.parent.SetAsLastSibling();
            _hint = MenuDrawing.Label(_root,"Hint","",960,1040,24,false,C(170,166,220),1,true);
            _version = MenuDrawing.Label(_root,"Version","v0.1",32,1030,24,false,C(140,136,196),1,true,false);
            RefreshLocalizedTexts();
        }

        public void ShowTitle(bool first)
        {
            _selectScreen = false;
            _busy = first ? 1.8f : 1.2f;
            _menu = 0;
            _loadDelay = -1f;
            _title.Enter(first);
            _background.ToWorld(0);
            RefreshLocalizedTexts();
        }

        public void RefreshLocalizedTexts()
        {
            _title?.RefreshLocalizedTexts();
            if (_hint != null)
            {
                _hint.text = LocalizationManager.Instance.Get(_selectScreen ? "menu.hint.select" : "menu.hint.title");
                _hint.color = _selectScreen && _world == 3 ? C(60,84,130) : C(170,166,220);
                SetOutline(_hint,!(_selectScreen && _world == 3));
                MenuDrawing.Fit(_hint);
                _version.gameObject.SetActive(!_selectScreen);
            }
            foreach (var card in _cards) RefreshCard(card);
            foreach (var card in _oldCards) RefreshCard(card);
            if (_sign != null) RefreshSign(_sign);
            if (_oldSign != null) RefreshSign(_oldSign);
        }

        private static void SetOutline(TextMeshProUGUI text, bool enabled)
        {
            var outline = text.GetComponent<MenuTextOutline>();
            if (outline != null) outline.enabled = enabled;
        }

        private void Update()
        {
            if (_title == null) return;
            float dt = Time.unscaledDeltaTime;
            int count = Mathf.Max(1,Mathf.CeilToInt(dt/0.004f));
            float h = dt/count;
            for (int i=0;i<count;i++) Step(h);
            _title.GetGlows(_glows,_glowValues);
            _background.Tick(dt,_time,_glows,_glowValues);
            _title.Draw(_time,!_selectScreen,_menu);
            foreach (var card in _oldCards) DrawCard(card,false);
            foreach (var card in _cards) DrawCard(card,card.index==_selected);
            DrawSign(_oldSign);
            DrawSign(_sign);
            if (_railGroup != null) _railGroup.alpha = _railAlpha;
            if (_shake>0.3f) MenuDrawing.Position(_root,Mathf.Round(Random.Range(-_shake,_shake)),Mathf.Round(Random.Range(-_shake,_shake)));
            else MenuDrawing.Position(_root,0,0);
            if (_busy<=0f && !_inputBlocked()) HandleInput();
        }

        private void Step(float dt)
        {
            _time += dt;
            _busy = Mathf.Max(0f,_busy-dt);
            _title.Step(dt,_time,!_selectScreen,_menu,_world);
            foreach (var card in _cards)
            {
                card.motion.Step(dt);
                if (card.motion.state==MenuDangle.State.Hang || card.motion.state==MenuDangle.State.Drop)
                    card.motion.angularVelocity += Mathf.Sin(_time*0.9f+card.index*1.7f)*0.3f*dt;
            }
            for (int i=_oldCards.Count-1;i>=0;i--)
            {
                _oldCards[i].motion.Step(dt);
                if (_oldCards[i].motion.state==MenuDangle.State.Gone)
                {
                    Destroy(_oldCards[i].root.gameObject);
                    _oldCards.RemoveAt(i);
                }
            }
            _sign?.motion.Step(dt);
            _oldSign?.motion.Step(dt);
            if (_oldSign != null && _oldSign.motion.state==MenuDangle.State.Gone)
            { Destroy(_oldSign.root.gameObject); _oldSign=null; }
            _selectAge = _selectScreen ? _selectAge+dt : 0f;
            _railAlpha = _selectScreen ? Mathf.Clamp01((_selectAge-0.35f)/0.35f) : Mathf.Max(0f,_railAlpha-dt*5f);
            if (_flip<1f && _sign != null)
            {
                _flip=Mathf.Min(1f,_flip+dt/0.45f);
                if (_flip>=0.5f && _sign.world!=_flipWorld) { _sign.world=_flipWorld; RefreshSign(_sign); }
            }
            _shake*=Mathf.Exp(-10f*dt);
            if (_loadDelay>=0f)
            {
                _loadDelay=Mathf.Max(0f,_loadDelay-dt);
                if (_loadDelay<=0f && !_inputBlocked())
                {
                    _loadDelay=-1f;
                    _loadScene(_pendingScene);
                }
            }
        }

        private void Shake(float amount) => _shake = Mathf.Max(_shake,amount);
        private int FirstUncleared()
        {
            int index=_levels.FindIndex(l=>PlayerPrefs.GetInt("cleared."+l.sceneName,0)==0);
            return index<0 ? 0 : index;
        }

        private void ConfirmTitle()
        {
            SfxManager.Instance.PlayButtonClick();
            if (_menu==0)
            {
                _title.Exit();
                QueueLevel(_levels[FirstUncleared()].sceneName,0.5f);
            }
            else if (_menu==1) ToSelect();
            else if (_menu==2) _openOptions();
            else
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying=false;
#else
                Application.Quit();
#endif
            }
        }

        private void QueueLevel(string scene,float delay)
        {
            _busy=10f;
            _pendingScene=scene;
            _loadDelay=delay;
        }

        private void ToSelect()
        {
            int next=FirstUncleared();
            _world=WorldOf(_levels[next].sceneName);
            _selectScreen=true;
            _busy=1.1f;
            _selectAge=0f;
            _title.Exit();
            _background.ToWorld(_world);
            _title.LampsTo(_world<2 ? 1 : 2);
            BuildRails();
            MakeCards(0.45f);
            _sign=MakeSign(_world);
            _sign.motion.Drop(380f,0.3f);
            _sign.motion.onCatch=()=>Shake(2f);
            int index=_cards.FindIndex(c=>c.global==next);
            Select(Mathf.Max(0,index),true);
            RefreshLocalizedTexts();
        }

        private static int WorldOf(string scene)
        {
            for (int i=0;i<Prefixes.Length;i++) if (scene.StartsWith(Prefixes[i],StringComparison.Ordinal)) return i;
            return 0;
        }

        private void ToTitle()
        {
            SfxManager.Instance.PlayUIBack();
            ReleaseCards(0,true);
            if (_sign!=null)
            {
                _sign.motion.Release(0,-150f,Random.Range(-0.4f,0.4f));
                _oldSign=_sign;
                _sign=null;
            }
            ShowTitle(false);
        }

        private void SwitchWorld(int direction)
        {
            SfxManager.Instance.PlayUIMove();
            ReleaseCards(direction,false);
            _world=(_world+direction+4)%4;
            _busy=0.75f;
            _background.ToWorld(_world);
            _title.LampsTo(_world<2 ? 1 : 2);
            _flip=0f;
            _flipWorld=_world;
            if (_sign!=null) _sign.motion.angularVelocity+=1.4f*direction;
            BuildRails();
            MakeCards(0.28f);
            Select(direction>0 ? 0 : Mathf.Min(3,_cards.Count-1),true);
            RefreshLocalizedTexts();
        }

        private void ReleaseCards(int direction,bool returning)
        {
            foreach (var card in _cards)
            {
                card.motion.Release(Random.Range(returning ? -120f : -150f,returning ? 120f : 150f)-direction*120f,
                    -Random.Range(returning ? 80f : 60f,returning ? 280f : 220f),Random.Range(returning ? -1f : -1.2f,returning ? 1f : 1.2f));
                _oldCards.Add(card);
            }
            _cards.Clear();
        }

        private void EnterLevel()
        {
            if (_cards.Count==0) return;
            var card=_cards[_selected];
            SfxManager.Instance.PlayButtonClick();
            card.motion.Release(0,120f,Random.Range(-1.5f,1.5f));
            card.snapTime=_time;
            foreach (var other in _cards) if (other!=card) other.motion.angularVelocity+=Random.Range(-1.25f,1.25f);
            Shake(4f);
            QueueLevel(card.level.sceneName,0.45f);
        }

        private void Select(int index,bool silent=false)
        {
            index=Mathf.Clamp(index,0,_cards.Count-1);
            if (!silent && index==_selected) return;
            if (!silent && _selected<_cards.Count) _cards[_selected].motion.angularVelocity-=0.9f*Mathf.Sign(index-_selected);
            foreach (var card in _cards) card.motion.pull=0;
            _selected=index;
            _cards[index].motion.pull=18f;
            if (!silent)
            {
                _cards[index].motion.angularVelocity+=Random.Range(-0.8f,0.8f);
                SfxManager.Instance.PlayUIMove();
            }
        }

        private void Move(int dx,int dy)
        {
            if (!_selectScreen)
            {
                if (dy!=0) { _menu=(_menu+dy+4)%4; SfxManager.Instance.PlayUIMove(); }
                return;
            }
            if (dx!=0)
            {
                int next=_selected+dx;
                if (next<0) SwitchWorld(-1);
                else if (next>=_cards.Count) SwitchWorld(1);
                else Select(next);
            }
            else if (dy!=0)
            {
                int next=_selected+dy*4;
                if (next>=0 && next<_cards.Count) Select(next);
            }
        }

        private void HandleInput()
        {
            var kb=Keyboard.current;
            var pad=Gamepad.current;
            int x=0,y=0;
            if (kb!=null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) x=-1;
                else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) x=1;
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) y=-1;
                else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) y=1;
            }
            if (pad!=null)
            {
                var stick=pad.leftStick.ReadValue();
                if (pad.dpad.left.wasPressedThisFrame) x=-1;
                else if (pad.dpad.right.wasPressedThisFrame) x=1;
                if (pad.dpad.up.wasPressedThisFrame) y=-1;
                else if (pad.dpad.down.wasPressedThisFrame) y=1;
                if (!_stickX && Mathf.Abs(stick.x)>0.6f) x=stick.x<0 ? -1 : 1;
                if (!_stickY && Mathf.Abs(stick.y)>0.6f) y=stick.y>0 ? -1 : 1;
                _stickX=Mathf.Abs(stick.x)>0.6f;
                _stickY=Mathf.Abs(stick.y)>0.6f;
            }
            bool previous=(kb!=null && (kb.qKey.wasPressedThisFrame || kb.pageUpKey.wasPressedThisFrame)) || (pad!=null && pad.leftShoulder.wasPressedThisFrame);
            bool nextWorld=(kb!=null && (kb.eKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame)) || (pad!=null && pad.rightShoulder.wasPressedThisFrame);
            bool confirm=(kb!=null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.zKey.wasPressedThisFrame)) || (pad!=null && pad.buttonSouth.wasPressedThisFrame);
            bool back=(kb!=null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame || kb.xKey.wasPressedThisFrame)) || (pad!=null && pad.buttonEast.wasPressedThisFrame);
            if (_selectScreen && (previous || nextWorld)) { SwitchWorld(previous ? -1 : 1); return; }
            if (x!=0 || y!=0) Move(x,y);
            if (_busy>0) return;
            if (_selectScreen && back) { ToTitle(); return; }
            if (confirm) { if (_selectScreen) EnterLevel(); else ConfirmTitle(); return; }
            HandleMouse();
        }

        private static bool Hit(RectTransform rect,Vector2 point) => rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect,point,null);
        private void HandleMouse()
        {
            var mouse=Mouse.current;
            if (mouse==null) return;
            Vector2 point=mouse.position.ReadValue();
            bool clicked=mouse.leftButton.wasPressedThisFrame;
            bool moved=(point-_lastMouse).sqrMagnitude>0.1f;
            _lastMouse=point;
            if (!moved && !clicked) return;
            if (!_selectScreen)
            {
                for (int i=0;i<_title.MenuItems.Length;i++) if (Hit(_title.MenuItems[i],point))
                {
                    if (_menu!=i) { _menu=i; SfxManager.Instance.PlayButtonHover(); }
                    if (clicked) ConfirmTitle();
                    return;
                }
            }
            else
            {
                foreach (var card in _cards) if (Hit(card.image.rectTransform,point))
                { Select(card.index); if (clicked) EnterLevel(); return; }
                if (clicked && Hit(_left.rectTransform,point)) SwitchWorld(-1);
                else if (clicked && Hit(_right.rectTransform,point)) SwitchWorld(1);
                else if (clicked && Hit(_hint.rectTransform,point)) ToTitle();
            }
        }

        private void BuildRails()
        {
            if (_railRoot!=null) Destroy(_railRoot.gameObject);
            _railRoot=MenuDrawing.Node(_selectionLayer,"RailsAndWorlds",0,0,0,0,false);
            _railRoot.SetAsFirstSibling();
            _railGroup=_railRoot.gameObject.AddComponent<CanvasGroup>();
            int rows=_world==0 ? 2 : 1;
            Color dark=_world==3 ? C(120,150,200) : C(24,22,46);
            for (int row=0;row<rows;row++)
            {
                float y=rows==1 ? 430 : 300+350*row;
                MenuDrawing.Box(_railRoot,"RailShadow",_world==3 ? C(60,90,150,0.25f) : C(10,8,20,0.35f),218,y+9,1500,9,false);
                MenuDrawing.Box(_railRoot,"RailBorder",dark,207,y-9,1506,18,false);
                MenuDrawing.Box(_railRoot,"Rail",Rail[_world],210,y-6,1500,12,false);
                MenuDrawing.Box(_railRoot,"RailHighlight",Hi[_world],210,y-6,1500,3,false);
                foreach (float x in new[]{195f,1713f})
                {
                    MenuDrawing.Box(_railRoot,"RailEnd",dark,x,y-18,12,36,false);
                    MenuDrawing.Box(_railRoot,"RailEndFill",Rail[_world],x+3,y-15,6,30,false);
                }
            }
            _gems=new Image[4];
            for (int i=0;i<4;i++)
            {
                bool current=i==_world;
                string key="Select/gem_"+Worlds[i]+(current ? "_on" : _world==3 ? "_off_light" : "_off");
                float size=current ? 45 : 27;
                _gems[i]=MenuDrawing.Image(_railRoot,"WorldGem"+i,_art.Get(key),960+(i-1.5f)*56,214,size,size);
                _gems[i].color=new Color(1,1,1,current ? 1 : 0.6f);
            }
            Color text=_world==3 ? C(60,84,130) : C(200,196,240);
            // The supplied GB2312 fallback contains the pointer variants of these triangles.
            _left=MenuDrawing.Label(_railRoot,"PreviousWorld","Q ◄",790,218,36,true,text,2,_world!=3);
            _right=MenuDrawing.Label(_railRoot,"NextWorld","► E",1130,218,36,true,text,2,_world!=3);
        }

        private void MakeCards(float start)
        {
            for (int global=0;global<_levels.Count;global++)
            {
                if (WorldOf(_levels[global].sceneName)!=_world) continue;
                int index=_cards.Count;
                float anchorY=_world==0 ? 300+350*(index/4) : 430;
                var card=new Card {world=_world,index=index,global=global,level=_levels[global],motion=new MenuDangle(420+360*(index%4),anchorY,40,264)};
                card.root=MenuDrawing.Node(_selectionLayer,"Card_"+card.level.sceneName,0,0,0,0,false);
                card.rope=MenuDrawing.Box(card.root,"Rope",Rail[_world],0,0,6,40).rectTransform;
                card.anchor=MenuDrawing.Box(card.root,"Anchor",_world==3 ? C(120,150,200) : C(24,22,46),card.motion.anchor.x,anchorY,18,18);
                card.anchorHi=MenuDrawing.Box(card.root,"AnchorHighlight",Hi[_world],card.motion.anchor.x,anchorY,12,12);
                card.body=MenuDrawing.Node(card.root,"Body",0,0,0,0);
                card.shadow=MenuDrawing.Box(card.body,"Shadow",_world==3 ? C(60,90,150,0.22f) : C(10,8,20,0.32f),9,15,288,264);
                card.halo1=MenuDrawing.Box(card.body,"OuterAccent",new Color(Accent[_world].r,Accent[_world].g,Accent[_world].b,0.16f),0,0,318,294);
                card.halo2=MenuDrawing.Box(card.body,"InnerAccent",card.halo1.color,0,0,302,278);
                card.image=MenuDrawing.Image(card.body,"Card",_art.Get("Select/card_"+Worlds[_world]),0,0,288,264);
                MenuDrawing.Image(card.body,"Thumbnail",_art.Get("Thumbs/thumb_"+card.level.sceneName),-126,-114,252,168,false);
                card.number=MenuDrawing.Label(card.body,"Number",(global+1).ToString("00"),-123,66,45,true,InkText[_world],1,false,false);
                card.name=MenuDrawing.Label(card.body,"LevelName","",-50,75,36,false,InkText[_world],1,false,false);
                card.check=MenuDrawing.Image(card.body,"Cleared",_art.Get(_world==3 ? "Select/check_sky" : "Select/check"),96,81,21,18,false);
                card.diamond=MenuDrawing.Image(card.root,"SelectionDiamond",_art.Get("Select/diamond_"+(_world==0 ? "pink" : _world==3 ? "orange" : "gold")),card.motion.anchor.x,anchorY-30,20,20);
                int rows=_world==0 ? 2 : 1;
                card.motion.Drop(anchorY+40+300+Random.Range(0,120),start+(rows-1-index/4)*0.2f+(index%4)*0.06f);
                card.motion.onCatch=()=> { Shake(1.2f); _title.KickLamps(0.025f); };
                _cards.Add(card);
                RefreshCard(card);
            }
        }

        private void RefreshCard(Card card)
        {
            card.name.text=LocalizationManager.Instance.Get("level."+card.level.sceneName);
            card.name.fontSize=36;
            float x=-123+card.number.preferredWidth+15;
            MenuDrawing.Position(card.name.rectTransform,x,75);
            float available=123-x;
            if (card.name.preferredWidth>available)
                card.name.fontSize=Mathf.Max(18,Mathf.Floor(36f*available/card.name.preferredWidth/3f)*3f);
            MenuDrawing.Fit(card.name);
            card.check.gameObject.SetActive(PlayerPrefs.GetInt("cleared."+card.level.sceneName,0)!=0);
        }

        private void DrawCard(Card card,bool selected)
        {
            var m=card.motion;
            card.root.gameObject.SetActive(m.Visible);
            if (!m.Visible) return;
            var p=m.Position;
            float a=m.Angle;
            MenuDrawing.Position(card.body,p.x,p.y);
            MenuDrawing.Rotation(card.body,a);
            bool hanging=m.state!=MenuDangle.State.Fall;
            card.anchor.enabled=card.anchorHi.enabled=hanging;
            Vector2 hook=p+new Vector2(Mathf.Sin(a),-Mathf.Cos(a))*132f;
            card.rope.gameObject.SetActive(hanging && hook.y>m.anchor.y+4 || !hanging && _time-card.snapTime<0.4f && card.snapTime>=0f);
            if (!hanging) hook=m.anchor+new Vector2(10,30)*Mathf.Clamp01(1-(_time-card.snapTime)/0.4f);
            MenuDrawing.Line(card.rope,m.anchor,hook,6);
            card.rope.GetComponent<Image>().color=selected && hanging ? Accent[card.world] : card.world==3 ? C(120,150,200) : Rail[card.world];
            card.image.sprite=_art.Get("Select/card_"+Worlds[card.world]+(selected ? "_sel" : ""));
            card.halo1.enabled=card.halo2.enabled=selected;
            card.number.color=selected ? card.world==3 ? C(176,110,20) : Accent[card.world] : InkText[card.world];
            card.diamond.enabled=selected && hanging;
            MenuDrawing.Position(card.diamond.rectTransform,m.anchor.x,m.anchor.y-30-Mathf.Abs(Mathf.Sin(_time*5.2f))*12);
        }

        private Sign MakeSign(int world)
        {
            var sign=new Sign { world=world, motion=new MenuDangle(960,-20,64,102) };
            sign.root=MenuDrawing.Node(_selectionLayer,"WorldSign",0,0,0,0,false);
            for (int i=0;i<14;i++)
            {
                var chain=MenuDrawing.Box(sign.root,"Chain",Color.white,0,0,12,14);
                MenuDrawing.Box(chain.transform,"ChainInner",Color.white,6,7,6,8);
                sign.chains.Add(chain);
            }
            sign.body=MenuDrawing.Node(sign.root,"SignBody",0,0,0,0);
            sign.shadow=MenuDrawing.Box(sign.body,"Shadow",C(10,8,20,0.32f),9,14,480,102);
            sign.face=MenuDrawing.Image(sign.body,"Sign",_art.Get("Select/sign_"+Worlds[world]),0,0,480,102);
            sign.name=MenuDrawing.Label(sign.body,"WorldName","",0,-5,48,true,InkText[world],3);
            sign.english=MenuDrawing.Label(sign.body,"EnglishName",English[world],0,30,24,true,Hi[world],2);
            sign.leftDoor=MenuDrawing.Image(sign.body,"DoorLeft",_art.Get("Door/"+Worlds[world]+"_idle"),-219,-34,64,64,false);
            sign.rightDoor=MenuDrawing.Image(sign.body,"DoorRight",sign.leftDoor.sprite,155,-34,64,64,false);
            RefreshSign(sign);
            return sign;
        }

        private void RefreshSign(Sign sign)
        {
            sign.face.sprite=_art.Get("Select/sign_"+Worlds[sign.world]);
            sign.name.text=LocalizationManager.Instance.Get("world."+Worlds[sign.world]);
            bool english=LocalizationManager.Instance.CurrentLanguage==GameLanguage.English;
            sign.english.gameObject.SetActive(!english);
            MenuDrawing.Position(sign.name.rectTransform,0,english ? 0 : -16);
            sign.name.color=InkText[sign.world];
            sign.english.text=English[sign.world];
            sign.english.color=sign.world==3 ? C(90,120,170) : Hi[sign.world];
            sign.shadow.color=sign.world==3 ? C(60,90,150,0.22f) : C(10,8,20,0.32f);
            MenuDrawing.Fit(sign.name);
            MenuDrawing.Fit(sign.english);
        }

        private void DrawSign(Sign sign)
        {
            if (sign==null) return;
            sign.root.gameObject.SetActive(sign.motion.Visible);
            if (!sign.motion.Visible) return;
            var p=sign.motion.Position;
            MenuDrawing.Position(sign.body,p.x,p.y);
            MenuDrawing.Rotation(sign.body,sign.motion.Angle);
            float flip=sign==_sign && _flip<1 ? Mathf.Cos(_flip*Mathf.PI) : 1f;
            if (sign==_sign && _flip>=0.5f) flip=Mathf.Abs(flip);
            sign.body.localScale=new Vector3(1,flip,1);
            sign.leftDoor.sprite=sign.rightDoor.sprite=_art.Get("Door/"+Worlds[sign.world]+"_idle",Mathf.FloorToInt(_time*7));
            for (int i=0;i<14;i++)
            {
                var chain=sign.chains[i];
                chain.gameObject.SetActive(sign.motion.state!=MenuDangle.State.Fall);
                float offset=i<7 ? -170f : 170f;
                float k=(i%7+0.5f)/7f;
                float x=Mathf.Lerp(sign.motion.anchor.x+offset*0.9f,p.x+offset,k);
                float y=Mathf.Lerp(-10,p.y-51,k);
                MenuDrawing.Position(chain.rectTransform,Mathf.Round(x/3)*3,Mathf.Round(y/3)*3);
                chain.color=sign.world==3 ? C(120,150,200) : C(24,22,46);
                chain.transform.GetChild(0).GetComponent<Image>().color=Frame[sign.world];
            }
        }

        private void OnDestroy() => _background?.Dispose();
    }
}
