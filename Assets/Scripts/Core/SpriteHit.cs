using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Pixel-accurate tests against sprites (needs readable textures, as ModelArt produces).</summary>
    public static class SpriteHit
    {
        /// <summary>Is the sprite drawn (not transparent) at this world point?</summary>
        public static bool OpaqueAt(SpriteRenderer sr, Vector3 world, float minAlpha = 0.3f)
        {
            var sprite = sr.sprite;
            if (sprite == null || !sprite.texture.isReadable) return false;
            Vector2 local = sr.transform.InverseTransformPoint(world);
            if (sr.flipX) local.x = -local.x;
            var px = sprite.pivot + local * sprite.pixelsPerUnit + sprite.rect.position;
            int x = (int)px.x, y = (int)px.y;
            if (x < 0 || y < 0 || x >= sprite.texture.width || y >= sprite.texture.height) return false;
            return sprite.texture.GetPixel(x, y).a > minAlpha;
        }
    }
}
