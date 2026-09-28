using UnityEngine;
using UnityEngine.Tilemaps;

namespace Resource.Scripts.Editor
{
    public static class ExteriorTilePadding
    {
        public static void Apply(Tilemap source, TileBase fillTile, Camera camera, Vector2 playableSize, float maxOrthoSize)
        {
            Transform existing = source.transform.Find("Outer Fill");
            var go = existing != null ? existing.gameObject : new GameObject("Outer Fill", typeof(Tilemap), typeof(TilemapRenderer));
            go.transform.SetParent(source.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var map = go.GetComponent<Tilemap>();
            var renderer = go.GetComponent<TilemapRenderer>();
            var sourceRenderer = source.GetComponent<TilemapRenderer>();
            map.tileAnchor = source.tileAnchor;
            map.orientation = source.orientation;
            map.orientationMatrix = source.orientationMatrix;
            map.color = source.color;
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder = sourceRenderer.sortingOrder - 1;
            renderer.mode = TilemapRenderer.Mode.Chunk;

            // 地图对角线覆盖镜头从预览远端移回玩家时的滞后，镜头半对角线覆盖任意旋转。
            float worldMargin = Mathf.Ceil(playableSize.magnitude + Mathf.Max(camera.orthographicSize, maxOrthoSize)
                * Mathf.Sqrt(1f + camera.aspect * camera.aspect) + 4f);
            Vector3 origin = source.CellToWorld(Vector3Int.zero);
            float cellWidth = (source.CellToWorld(Vector3Int.right) - origin).magnitude;
            float cellHeight = (source.CellToWorld(Vector3Int.up) - origin).magnitude;
            int marginX = Mathf.CeilToInt(worldMargin / cellWidth);
            int marginY = Mathf.CeilToInt(worldMargin / cellHeight);
            BoundsInt inner = source.cellBounds;
            var outer = new BoundsInt(inner.xMin - marginX, inner.yMin - marginY, inner.zMin,
                inner.size.x + marginX * 2, inner.size.y + marginY * 2, 1);
            var tiles = new TileBase[outer.size.x * outer.size.y];
            for (int y = 0; y < outer.size.y; y++)
            for (int x = 0; x < outer.size.x; x++)
            {
                int cellX = outer.xMin + x, cellY = outer.yMin + y;
                if (cellX < inner.xMin || cellX >= inner.xMax || cellY < inner.yMin || cellY >= inner.yMax)
                    tiles[y * outer.size.x + x] = fillTile;
            }
            map.ClearAllTiles();
            map.SetTilesBlock(outer, tiles);
        }
    }
}
