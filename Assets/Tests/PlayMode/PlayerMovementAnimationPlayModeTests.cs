using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GravityGame.Tests.PlayMode
{
    public sealed class PlayerMovementAnimationPlayModeTests
    {
        private const string Art = "Assets/Resource/Art/Character/";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private Component _player, _movement;
        private Rigidbody2D _body;
        private SpriteRenderer _renderer;
        private Animator _animator;
        private float _timeScale;

        [SetUp]
        public void SetUp()
        {
            _timeScale = Time.timeScale;
            Time.timeScale = 1f;
            _root = new GameObject("Astronaut animation test");
            _root.SetActive(false);
            _body = _root.AddComponent<Rigidbody2D>();
            _body.simulated = false;
            _root.AddComponent<CapsuleCollider2D>();
            _player = _root.AddComponent(FindType("Resource.Scripts.PlayerController"));
            var art = new GameObject("PlayerIM");
            art.transform.SetParent(_root.transform, false);
            _renderer = art.AddComponent<SpriteRenderer>();
            _animator = art.AddComponent<Animator>();
#if UNITY_EDITOR
            _animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                Art + "Animations/Astronaut.controller");
#endif
            Assert.That(_animator.runtimeAnimatorController, Is.Not.Null,
                "Install the astronaut player assets before running these Editor PlayMode tests.");
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _movement = _root.AddComponent(FindType("Resource.Scripts.PlayerMovementAnimation"));
            Set(_movement, "player", _player);
            Set(_movement, "animator", _animator);
            Set(_player, "spriteFacesRight", true);
            Set(_player, "_spriteRenderer", _renderer);
            _root.SetActive(true);
            Set(_player, "_gameplayStarted", true);
            Sample(Vector2.zero, Time.fixedTimeAsDouble);
            _animator.Update(0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            Time.timeScale = _timeScale;
        }

        [TestCase(0.2f, 0f)]
        [TestCase(-0.2f, 0f)]
        [TestCase(0f, -0.2f)]
        public void ActualRigidbodyDisplacementWithoutInput_StartsRun(float x, float y)
        {
            _body.linearVelocity = Vector2.zero;
            _body.position = new Vector2(x, y);
            _movement.GetType().GetMethod("LateUpdate", Fields).Invoke(_movement, null);
            AssertMoving(true);
        }

        [Test]
        public void VelocityWithoutDisplacement_DoesNotStartRun()
        {
            _body.linearVelocity = new Vector2(10f, -5f);
            Sample(Vector2.zero, 10d);
            AssertMoving(false);
            Sample(new Vector2(0.000001f, 0f), 10.02d);
            AssertMoving(false);
        }

        [Test]
        public void RenderFramesWithoutNewPhysics_KeepRun_ThenStationaryStepStops()
        {
            Sample(Vector2.right, 10d);
            AssertMoving(true);
            Sample(Vector2.right, 10d);
            AssertMoving(true);
            Sample(Vector2.right, 10.02d);
            AssertMoving(false);
        }

        [TestCase("preview")]
        [TestCase("paused")]
        [TestCase("dead")]
        public void InactiveGameplay_StopsRun_AndResetsPositionSample(string reason)
        {
            Sample(Vector2.right, 10d);
            AssertMoving(true);
            if (reason == "preview") Set(_player, "_gameplayStarted", false);
            if (reason == "paused") Time.timeScale = 0f;
            if (reason == "dead") Set(_player, "_isDead", true);
            Sample(Vector2.right * 2f, 10.02d);
            AssertMoving(false);
            Set(_player, "_gameplayStarted", true);
            Set(_player, "_isDead", false);
            Time.timeScale = 1f;
            Sample(Vector2.right * 2f, 10.04d);
            AssertMoving(false);
        }

        [TestCase(true, 1f, false)]
        [TestCase(true, -1f, true)]
        [TestCase(false, 1f, true)]
        [TestCase(false, -1f, false)]
        public void FacingAndJetpackFallback_MatchActualHorizontalMovement(bool facesRight, float direction, bool flip)
        {
            Set(_player, "spriteFacesRight", facesRight);
            Sample(Vector2.right * direction, 10d);
            Assert.That(_renderer.flipX, Is.EqualTo(flip));
            Assert.That((float)_player.GetType().GetProperty("FacingSign").GetValue(_player), Is.EqualTo(direction));
            Sample(new Vector2(direction, -1f), 10.02d);
            Assert.That(_renderer.flipX, Is.EqualTo(flip), "Falling must preserve the horizontal facing.");
        }

#if UNITY_EDITOR
        [Test]
        public void Animator_SwitchesIdleRunAndAllEightSprites_WithoutChangingMaterialOrTransform()
        {
            var idle = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Design/astronaut_reference_pose_1x.png");
            var run = AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + "Animations/Run/Astronaut_Run.anim");
            Assert.That(_renderer.sprite, Is.SameAs(idle));
            Assert.That(run.isLooping, Is.True);
            Assert.That(run.frameRate, Is.EqualTo(12f));
            Assert.That(run.length, Is.EqualTo(8f / 12f).Within(0.0001f));
            Material material = _renderer.sharedMaterial;
            Vector3 position = _renderer.transform.localPosition;
            Vector3 scale = _renderer.transform.localScale;
            Sample(Vector2.left, 10d);
            _animator.Update(0.001f);
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Run"), Is.True);
            for (int i = 0; i < 8; i++)
            {
                _animator.Play("Base Layer.Run", 0, (i + 0.25f) / 8f);
                _animator.Update(0f);
                Sprite expected = AssetDatabase.LoadAssetAtPath<Sprite>(
                    Art + $"Animations/Run/Frames/astronaut_run_{i:00}.png");
                Assert.That(_renderer.sprite, Is.SameAs(expected), "Run frame " + i);
                Assert.That(_renderer.sharedMaterial, Is.SameAs(material));
                Assert.That(_renderer.flipX, Is.True);
                Assert.That(_renderer.transform.localPosition, Is.EqualTo(position));
                Assert.That(_renderer.transform.localScale, Is.EqualTo(scale));
            }
            Sample(Vector2.left, 10.02d);
            _animator.Update(0.001f);
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"), Is.True);
            Assert.That(_renderer.sprite, Is.SameAs(idle));
        }
#endif

        private static Type FindType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        private static void Set(Component component, string name, object value)
        {
            component.GetType().GetField(name, Fields).SetValue(component, value);
        }

        private void Sample(Vector2 position, double physicsTime)
        {
            _movement.GetType().GetMethod("SamplePosition").Invoke(_movement, new object[] { position, physicsTime });
        }

        private void AssertMoving(bool expected)
        {
            Assert.That((bool)_movement.GetType().GetProperty("IsMoving").GetValue(_movement), Is.EqualTo(expected));
            Assert.That(_animator.GetBool("IsMoving"), Is.EqualTo(expected));
        }
    }
}
