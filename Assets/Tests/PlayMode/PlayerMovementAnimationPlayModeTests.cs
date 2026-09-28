using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace GravityGame.Tests.PlayMode
{
    public sealed class PlayerMovementAnimationPlayModeTests
    {
        private const string Art = "Assets/Resource/Art/Character/";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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
            Set(_player, "_spriteRenderer", _renderer);
            _root.SetActive(true);
            Set(_player, "_gameplayStarted", true);
            Sample(true, 0f);
            _animator.Update(0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            Time.timeScale = _timeScale;
        }

        [Test]
        public void Grounded_DisplacementKeepsBreathingWithoutRestartingOrFlipping()
        {
            Set(_player, "isGrounded", true);
            _animator.Update(0.3f);
            float breathingTime = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            _body.position = new Vector2(-3f, -1f);
            _movement.GetType().GetMethod("LateUpdate", Fields).Invoke(_movement, null);
            AssertFloating(false);
            Assert.That(_renderer.flipX, Is.False);
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
                Is.EqualTo(breathingTime).Within(0.0001f), "Standing samples must not restart the breathing loop.");
        }

        [Test]
        public void Airborne_OnlyStartsFloatAfterContinuousDelay()
        {
            Sample(false, 0.04f);
            AssertFloating(false);
            Sample(false, 0.039f);
            AssertFloating(false);
            Sample(false, 0.002f);
            AssertFloating(true);
            _animator.Update(0.001f);
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Float"), Is.True);
        }

        [Test]
        public void BriefBumps_ResetDelayEachTimeGroundReturns()
        {
            for (int i = 0; i < 4; i++)
            {
                Sample(false, 0.06f);
                AssertFloating(false);
                Sample(true, 0.02f);
                AssertFloating(false);
            }
            Sample(false, 0.081f);
            AssertFloating(true);
        }

        [Test]
        public void Landing_ImmediatelyReturnsToFirstIdleFrame_ThenNextFallWaitsAgain()
        {
            Sprite firstIdleFrame = _renderer.sprite;
            Sample(false, 0.1f);
            _animator.Update(0.4f);
            AssertFloating(true);
            Sample(true, 0.02f);
            AssertFloating(false);
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"), Is.True);
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(_renderer.sprite, Is.SameAs(firstIdleFrame));
            Sample(false, 0.04f);
            AssertFloating(false);
        }

        [TestCase("preview")]
        [TestCase("paused")]
        [TestCase("dead")]
        public void InactiveGameplay_ReturnsToIdle_AndResetsAirborneDelay(string reason)
        {
            Sample(false, 0.1f);
            AssertFloating(true);
            if (reason == "preview") Set(_player, "_gameplayStarted", false);
            if (reason == "paused") Time.timeScale = 0f;
            if (reason == "dead") Set(_player, "_isDead", true);
            Sample(false, 0.1f);
            AssertFloating(false);
            Set(_player, "_gameplayStarted", true);
            Set(_player, "_isDead", false);
            Time.timeScale = 1f;
            Sample(false, 0.04f);
            AssertFloating(false);
        }

#if UNITY_EDITOR
        [TestCase("Idle", 4, 0.2f, "player_idle.png")]
        [TestCase("Float", 6, 0.12f, "player_float.png")]
        public void Animator_LoopsAllSprites_WithoutChangingMaterialOrTransform(
            string stateName, int count, float secondsPerFrame, string sheetName)
        {
            var controller = (AnimatorController)_animator.runtimeAnimatorController;
            var states = controller.layers[0].stateMachine.states;
            CollectionAssert.AreEquivalent(new[] { "Idle", "Float" }, states.Select(s => s.state.name));
            AnimationClip clip = (AnimationClip)states.Single(s => s.state.name == stateName).state.motion;
            Assert.That(clip.isLooping, Is.True);
            Assert.That(clip.frameRate, Is.EqualTo(1f / secondsPerFrame).Within(0.0001f));
            Assert.That(clip.length, Is.EqualTo(count * secondsPerFrame).Within(0.0001f));
            Assert.That(AnimationUtility.GetCurveBindings(clip), Is.Empty);
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            Assert.That(binding.type, Is.EqualTo(typeof(SpriteRenderer)));
            Assert.That(binding.propertyName, Is.EqualTo("m_Sprite"));
            var frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            var sprites = frames.Select(f => f.value).Distinct().ToArray();
            Assert.That(sprites.Length, Is.EqualTo(count));
            Assert.That(sprites.All(s => AssetDatabase.GetAssetPath(s) == Art + "Player/" + sheetName), Is.True);

            Material material = _renderer.sharedMaterial;
            Vector3 position = _renderer.transform.localPosition;
            Vector3 scale = _renderer.transform.localScale;
            Quaternion rotation = _renderer.transform.rotation;
            Sample(stateName == "Idle", 0.1f);
            for (int i = 0; i < count; i++)
            {
                _animator.Play("Base Layer." + stateName, 0, (i + 0.25f) / count);
                _animator.Update(0f);
                Assert.That(_renderer.sprite, Is.SameAs(frames[i].value), stateName + " frame " + i);
                Assert.That(_renderer.sharedMaterial, Is.SameAs(material));
                Assert.That(_renderer.flipX, Is.False);
                Assert.That(_renderer.transform.localPosition, Is.EqualTo(position));
                Assert.That(_renderer.transform.localScale, Is.EqualTo(scale));
                Assert.That(_renderer.transform.rotation, Is.EqualTo(rotation));
            }
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

        private void Sample(bool grounded, float deltaTime)
        {
            _movement.GetType().GetMethod("SampleGrounded").Invoke(_movement, new object[] { grounded, deltaTime });
        }

        private void AssertFloating(bool expected)
        {
            Assert.That((bool)_movement.GetType().GetProperty("IsFloating").GetValue(_movement), Is.EqualTo(expected));
            Assert.That(_animator.GetBool("IsFloating"), Is.EqualTo(expected));
        }
    }
}
