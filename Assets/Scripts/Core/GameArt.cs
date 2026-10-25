using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Model-based art prepared once on first use: a sprite of every building catalog entry (the build menu and card
    /// icons), and the scales the snapping wall pieces are built with.
    /// </summary>
    public static class GameArt
    {
        public static ArtLibrary Library { get; private set; }
        /// <summary>Model scales of the wall pieces: a half-tile segment along model X, and the joint post.</summary>
        public static Vector3 WallHalfScale = Vector3.one, WallPostScale = Vector3.one;

        static bool ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ready = false;
            Library = null;
            WallHalfScale = WallPostScale = Vector3.one;
        }

        public static void EnsureRendered(Transform parent)
        {
            if (ready) return;
            ready = true;

            var catalog = BuildingCatalog.Instance;
            Library = ArtLibrary.Instance;
            if (Library == null) Debug.LogWarning("GameArt: Resources/ArtLibrary.asset not found (Dungeon > Sync Art Library).");
            if (catalog == null) return;
            using (var r = new ModelArt(parent))
            {
                Shader.SetGlobalFloat("_DitherScreenOn", catalog.ScreenDither ? 1f : 0f); // style test D2
                r.Dither = catalog.DitherLook; // style test D: buildings only
                foreach (var e in catalog.Entries)
                    e.Sprite = r.Render(e.Model, e.Scale, 0f, e.Id);

                // The wall model is a straight segment along its X axis: squash it to half a tile long so two
                // halves make a tile and pieces from neighbouring tiles meet edge to edge.
                var wall = catalog.Find(BuildingCatalog.WallId);
                if (wall != null && wall.Model != null)
                {
                    var size = r.MeasureSize(wall.Model);
                    float half = 0.5f / Mathf.Max(0.01f, size.x);
                    WallHalfScale = new Vector3(half, wall.Scale, wall.Scale);
                    WallPostScale = new Vector3(0.36f / Mathf.Max(0.01f, size.x), wall.Scale * 1.12f, wall.Scale * 1.15f);
                }
            }
        }

        public const string HeadNode = "Head";
    }
}
