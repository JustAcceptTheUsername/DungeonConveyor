using UnityEngine;

namespace DungeonConveyor
{
    public enum ResourceType { Gold = 0, Crystal = 1, Amethyst = 2, Iron = 3 }

    public enum Dir { N = 0, E = 1, S = 2, W = 3 }

    public static class DirUtil
    {
        static readonly Vector2Int[] Offsets = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

        public static Vector2Int Offset(this Dir d) => Offsets[(int)d];
        public static Vector3 World(this Dir d) { var o = Offsets[(int)d]; return new Vector3(o.x, 0, o.y); }
        public static Dir Right(this Dir d) => (Dir)(((int)d + 1) % 4);
        public static Dir Left(this Dir d) => (Dir)(((int)d + 3) % 4);
        public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) % 4);
        public static float Yaw(this Dir d) => (int)d * 90f;
        public static Quaternion Rotation(this Dir d) => Quaternion.Euler(0, d.Yaw(), 0);

        /// <summary>Direction from cell a to adjacent cell b, or null if not adjacent.</summary>
        public static Dir? Between(Vector2Int a, Vector2Int b)
        {
            var d = b - a;
            for (int i = 0; i < 4; i++)
                if (Offsets[i] == d) return (Dir)i;
            return null;
        }
    }

    public static class ResourceInfo
    {
        public static readonly string[] Names = { "Gold", "Crystal", "Amethyst", "Iron" };
        public static readonly Color[] Colors =
        {
            new Color(1f, 0.72f, 0.12f),
            new Color(0.2f, 0.62f, 1f),
            new Color(0.62f, 0.32f, 1f),
            new Color(0.72f, 0.74f, 0.8f),
        };
        public const int Count = 4;
    }

    [System.Serializable]
    public struct Cost
    {
        public int Gold, Crystal, Amethyst, Iron;

        public Cost(int gold, int crystal = 0, int amethyst = 0, int iron = 0)
        {
            Gold = gold; Crystal = crystal; Amethyst = amethyst; Iron = iron;
        }

        public int this[int i] => i switch { 0 => Gold, 1 => Crystal, 2 => Amethyst, _ => Iron };

        public static Cost operator *(Cost c, float f) =>
            new Cost(Mathf.RoundToInt(c.Gold * f), Mathf.RoundToInt(c.Crystal * f), Mathf.RoundToInt(c.Amethyst * f), Mathf.RoundToInt(c.Iron * f));

        public static Cost operator +(Cost a, Cost b) =>
            new Cost(a.Gold + b.Gold, a.Crystal + b.Crystal, a.Amethyst + b.Amethyst, a.Iron + b.Iron);
    }

    public static class Fmt
    {
        public static string Short(float v)
        {
            if (v >= 1_000_000) return (v / 1_000_000f).ToString("0.##") + "M";
            if (v >= 10_000) return (v / 1000f).ToString("0.#") + "K";
            if (v >= 1000) return (v / 1000f).ToString("0.##") + "K";
            return Mathf.FloorToInt(v).ToString();
        }

        public static string Time(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}
