using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Resource.Scripts.Editor
{
    /// <summary>
    /// 给出口门装上重力锁的表盘和指针（MazeSceneBuilder 放好门之后调用）。
    /// 在 DoorVisual 下建 "GravityLockOverlay"（盖在门上的表盘）和 "GravityNeedle"（指针，位置 = 表盘中心），
    /// 并把图片和引用填进 GoalDoor。解锁角度用 GoalDoor 里的默认值（20 度），不在这里改。
    /// </summary>
    public static class GravityLockDoorSetup
    {
        private const string Art = "Assets/Resource/Mode/Cave Tileset/Door/";
        // 表盘中心在门图（32x32，中心枢轴）里往上 5 像素 = 5/16 单位
        private static readonly Vector3 GaugeCenter = new Vector3(0f, 5f / 16f, 0f);

        public static void Apply(GoalDoor door)
        {
            Transform visual = door.transform.Find("DoorVisual");
            if (visual == null) throw new System.InvalidOperationException(door.name + " 没有 DoorVisual，先运行 Replace Exit Doors。");
            var doorRenderer = visual.GetComponent<SpriteRenderer>();
            // 门、表盘、指针作为一组排序：和玩家等其他物体的前后关系跟原来的门一样
            var group = visual.GetComponent<SortingGroup>();
            if (group == null) group = visual.gameObject.AddComponent<SortingGroup>();
            if (doorRenderer != null)
            {
                group.sortingLayerID = doorRenderer.sortingLayerID;
                group.sortingOrder = doorRenderer.sortingOrder;
            }

            Sprite open = Load("door_lock_open.png");
            Sprite locked = Load("door_lock_locked.png");
            Sprite hint = Load("door_lock_hint.png");
            Sprite needleSprite = Load("door_lock_needle.png");

            SpriteRenderer overlay = Child(visual, "GravityLockOverlay", Vector3.zero, doorRenderer, 1);
            overlay.sprite = open;
            SpriteRenderer needle = Child(visual, "GravityNeedle", GaugeCenter, doorRenderer, 2);
            needle.sprite = needleSprite;

            var settings = new SerializedObject(door);
            settings.FindProperty("gravityLock").boolValue = true;
            settings.FindProperty("lockOverlay").objectReferenceValue = overlay;
            settings.FindProperty("overlayOpen").objectReferenceValue = open;
            settings.FindProperty("overlayLocked").objectReferenceValue = locked;
            settings.FindProperty("overlayHint").objectReferenceValue = hint;
            settings.FindProperty("needle").objectReferenceValue = needle.transform;
            settings.ApplyModifiedProperties();
        }

        private static Sprite Load(string file)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + file);
            if (sprite == null) throw new System.IO.FileNotFoundException("缺少重力锁图片（单张 Sprite，PPU 16）：" + Art + file);
            return sprite;
        }

        private static SpriteRenderer Child(Transform parent, string name, Vector3 localPosition, SpriteRenderer reference, int orderOffset)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                child = new GameObject(name).transform;
                child.SetParent(parent, false);
            }
            child.localPosition = localPosition;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            var renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = child.gameObject.AddComponent<SpriteRenderer>();
            if (reference != null)
            {
                renderer.sharedMaterial = reference.sharedMaterial;
                renderer.sortingLayerID = reference.sortingLayerID;
                renderer.sortingOrder = reference.sortingOrder + orderOffset;
            }
            return renderer;
        }
    }
}
