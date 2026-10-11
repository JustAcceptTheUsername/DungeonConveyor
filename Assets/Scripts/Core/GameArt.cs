using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Every model-based sprite the game uses, rendered once on first use: building catalog entries and enemy
    /// catalog entries (stored on the entries themselves), crystals for each ore type, cliffs and rocks.
    /// </summary>
    public static class GameArt
    {
        public class OreArt
        {
            public readonly List<Sprite> Deposits = new List<Sprite>();
            public Sprite Item, Refined;
        }

        public static ArtLibrary Library { get; private set; }
        public static readonly List<OreArt> Ores = new List<OreArt>();
        public static readonly List<Sprite> CliffFacingY = new List<Sprite>();  // edge under a tile whose -y side is open
        public static readonly List<Sprite> CliffFacingX = new List<Sprite>();  // edge under a tile whose -x side is open
        public static Sprite CliffCorner;
        public static readonly List<Sprite> Rocks = new List<Sprite>();        // lowest first
        public static readonly List<Sprite> RockAccents = new List<Sprite>();
        /// <summary>Wall pieces that snap together: a half-tile segment along grid x, one along grid y, and a joint post.</summary>
        public static Sprite WallHalfX, WallHalfY, WallPost;

        static bool ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ready = false;
            Library = null;
            Ores.Clear();
            CliffFacingX.Clear();
            CliffFacingY.Clear();
            CliffCorner = null;
            Rocks.Clear();
            RockAccents.Clear();
            WallHalfX = WallHalfY = WallPost = null;
        }

        public static OreArt Ore(int kind) => kind >= 0 && kind < Ores.Count ? Ores[kind] : null;

        public static void EnsureRendered(Transform parent)
        {
            if (ready) return;
            ready = true;

            var catalog = BuildingCatalog.Instance;
            var enemies = EnemyCatalog.Instance;
            Library = ArtLibrary.Instance;
            using (var r = new ModelArt(parent))
            {
                if (catalog != null)
                {
                    foreach (var e in catalog.Entries)
                        e.Sprite = r.Render(e.Model, e.Scale, 0f, e.Id);

                    // The wall model is a straight segment along its X axis: squash it to half a tile long so two
                    // halves make a tile and pieces from neighbouring tiles meet edge to edge.
                    var wall = catalog.Find(BuildingCatalog.WallId);
                    if (wall != null && wall.Model != null)
                    {
                        var size = r.MeasureSize(wall.Model);
                        float half = 0.5f / Mathf.Max(0.01f, size.x);
                        WallHalfX = r.Render(wall.Model, new Vector3(half, wall.Scale, wall.Scale), 0f, "WallX");
                        WallHalfY = r.Render(wall.Model, new Vector3(half, wall.Scale, wall.Scale), 90f, "WallY");
                        WallPost = r.Render(wall.Model, new Vector3(0.36f / Mathf.Max(0.01f, size.x), wall.Scale * 1.12f, wall.Scale * 1.15f), 0f, "WallPost");
                    }
                }

                // Enemies face their direction of travel: one sprite per grid direction (Dir order: +x, -x, +y, -y).
                // Models face +Z; +Z lies along grid +y, so +x needs a 90 degree turn.
                if (enemies != null)
                    foreach (var e in enemies.Entries)
                    {
                        e.Sprites = new Sprite[4];
                        float[] yaw = { 90f, -90f, 0f, 180f };
                        for (int d = 0; d < 4; d++) e.Sprites[d] = r.Render(e.Model, e.Scale, yaw[d] + enemies.YawOffset, e.Id);
                    }

                if (Library == null)
                {
                    Debug.LogWarning("GameArt: Resources/ArtLibrary.asset not found (Dungeon > Sync Art Library).");
                    return;
                }

                foreach (var ore in Library.Ores)
                {
                    var art = new OreArt();
                    foreach (var m in ore.Deposits) Add(art.Deposits, r.Render(m, Library.DepositScale));
                    art.Item = r.Render(ore.Item, Library.ItemScale);
                    art.Refined = r.Render(ore.RefinedItem, Library.ItemScale) ?? art.Item;
                    Ores.Add(art);
                }
                foreach (var m in Library.CliffEdges)
                {
                    Add(CliffFacingY, r.Render(m, 1f, Library.CliffYaw));
                    Add(CliffFacingX, r.Render(m, 1f, Library.CliffYaw + 90f));
                }
                CliffCorner = r.Render(Library.CliffCorner, 1f, Library.CliffYaw);
                foreach (var m in Library.RockWalls) Add(Rocks, r.Render(m, 1f));
                foreach (var m in Library.RockAccents) Add(RockAccents, r.Render(m, 1f));
                // All rocks share a 1x1 base, so the sprite height orders them by how tall they stand.
                Rocks.Sort((a, b) => a.rect.height.CompareTo(b.rect.height));
            }
        }

        static void Add(List<Sprite> list, Sprite s)
        {
            if (s != null) list.Add(s);
        }
    }
}
