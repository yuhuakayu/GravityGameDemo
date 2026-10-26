using UnityEngine;

namespace Resource.Scripts.SmallLevels
{
    public sealed class MazeJellyBlock : MonoBehaviour
    {
        public NearMissFeedback feedback;
        public Sprite idle, squash;
        private SpriteRenderer[] visuals;
        private float squashUntil;
        private void Awake() => visuals=GetComponentsInChildren<SpriteRenderer>();
        public static bool TryBounce(PlayerController player, Collision2D collision,float speed)
        {
            var jelly=collision.collider.GetComponentInParent<MazeJellyBlock>();
            if (jelly==null || speed<=3f || !player.IsGameplayActive) return false;
            bool top=false;
            foreach (var contact in collision.contacts) if (contact.normal.y>.5f) top=true;
            if (!top) return false;
            player.BounceFromJelly(speed*.92f); jelly.squashUntil=Time.time+.1f;
            jelly.feedback.Pop(speed>9f ? "pop.jelly_big" : "pop.jelly_small");
            jelly.feedback.Shake(Mathf.Min(.3f,speed*.02f));
            var go=new GameObject("Jelly particles");go.transform.position=player.transform.position;
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=false;main.startLifetime=.5f;main.startSpeed=3f;main.startSize=.14f;main.startColor=(Color)new Color32(244,168,184,255);
            var emission=ps.emission;emission.enabled=false;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=new Material(Shader.Find("Sprites/Default"));
            ps.Play();ps.Emit(8);Destroy(go,.7f);return true;
        }
        private void LateUpdate()
        {
            foreach (var visual in visuals) visual.sprite=Time.time<squashUntil ? squash : idle;
        }
    }
}
