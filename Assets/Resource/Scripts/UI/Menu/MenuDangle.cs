using System;
using UnityEngine;

namespace Resource.Scripts.UI.Menu
{
    // The prototype's one-sided rope spring and pendulum, in top-left canvas coordinates.
    public sealed class MenuDangle
    {
        public enum State { Drop, Hang, Fall, Gone }
        public State state = State.Hang;
        public readonly Vector2 anchor;
        public readonly float rope, height;
        public float offset, velocity, angle, angularVelocity, pull, delay;
        public Action onCatch;
        private bool _caught = true;
        private Vector2 _fallPosition, _fallVelocity;
        private float _fallAngle, _fallSpin;

        public MenuDangle(float x, float y, float ropeLength, float bodyHeight)
        {
            anchor = new Vector2(x, y);
            rope = ropeLength;
            height = bodyHeight;
        }

        public void Drop(float distance, float wait)
        {
            state = State.Drop;
            offset = -distance;
            velocity = 0f;
            delay = wait;
            _caught = false;
            angle = UnityEngine.Random.Range(-0.15f, 0.15f);
            angularVelocity = 0f;
        }

        public void Release(float vx, float vy, float spin)
        {
            _fallPosition = Position;
            _fallAngle = Angle;
            state = State.Fall;
            _fallVelocity = new Vector2(vx, vy);
            _fallSpin = spin;
        }

        public bool Visible => state != State.Gone && !(state == State.Drop && delay > 0f);
        public Vector2 Position => state == State.Fall || state == State.Gone ? _fallPosition
            : anchor + new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * (rope + offset + height / 2f);
        public float Angle => state == State.Fall || state == State.Gone ? _fallAngle : angle;

        public void Step(float dt)
        {
            if (state == State.Gone) return;
            if (state == State.Fall)
            {
                _fallVelocity.y += 3600f * dt;
                _fallPosition += _fallVelocity * dt;
                _fallAngle += _fallSpin * dt;
                if (_fallPosition.y > 1580f) state = State.Gone;
                return;
            }
            if (delay > 0f) { delay -= dt; return; }
            float acceleration = 3600f;
            if (offset > pull)
            {
                acceleration -= 1500f * (offset - pull) + 26f * velocity;
                if (!_caught)
                {
                    _caught = true;
                    angularVelocity += UnityEngine.Random.Range(-0.75f, 0.75f);
                    onCatch?.Invoke();
                }
            }
            velocity += acceleration * dt;
            if (state == State.Drop && !_caught) velocity = Mathf.Min(velocity, 1700f);
            offset += velocity * dt;
            if (_caught && Mathf.Abs(offset - pull) < 0.5f && Mathf.Abs(velocity) < 8f) state = State.Hang;
            angularVelocity += (-(3600f / (rope + height / 2f)) * Mathf.Sin(angle) - 3f * angularVelocity) * dt;
            angle += angularVelocity * dt;
        }
    }
}
