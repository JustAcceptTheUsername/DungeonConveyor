using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Each of the 12 pentagon rock spires holds a relic, shown as a glowing beacon on its tip. Building anything on a
    /// cell next to a spire claims it: the beacon bursts and the run gains a random relic (Run.Relic).
    /// </summary>
    [RequireComponent(typeof(IsoGrid), typeof(BuildController))]
    public class SpireRelics : MonoBehaviour
    {
        public Color BeaconColor = new Color(1f, 0.8f, 0.3f, 0.9f);

        /// <summary>Raised when a spire is claimed: where, and the relic it gave (null if every relic is owned).</summary>
        public event System.Action<Vector3, Run.Card> Claimed;

        /// <summary>Spires still holding their relic.</summary>
        public int Unclaimed => spires.Count;

        IsoGrid map;
        BuildController build;
        readonly List<(Vector2Int cell, SpriteRenderer beacon)> spires = new List<(Vector2Int, SpriteRenderer)>();

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            var material = GetComponent<MapView>().SpriteMaterial;
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Vector2Int(x, 0);
                if (!map.IsPentagon(c)) continue;
                var sr = new GameObject("SpireBeacon").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(transform, false);
                if (material != null) sr.sharedMaterial = material;
                sr.sprite = Fx.Soft;
                sr.color = BeaconColor;
                sr.transform.position = map.SurfaceCenter(c) + map.Up(c) * 0.6f;
                spires.Add((c, sr));
            }
            build.BuildingsChanged += Check;
        }

        void OnDestroy()
        {
            if (build != null) build.BuildingsChanged -= Check;
        }

        void LateUpdate()
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 3f);
            foreach (var (_, sr) in spires)
            {
                PlanetArt.Billboard(sr.transform);
                sr.transform.localScale = Vector3.one * 1.4f * pulse;
            }
        }

        void Check()
        {
            for (int i = spires.Count - 1; i >= 0; i--)
            {
                var (cell, beacon) = spires[i];
                bool touched = false;
                for (int d = 0; d < map.Sides(cell) && !touched; d++)
                    if (map.BuildingAt(map.Neighbour(cell, d)) != null) touched = true;
                if (!touched) continue;

                var at = beacon.transform.position;
                Destroy(beacon.gameObject);
                spires.RemoveAt(i);
                var relic = Run.RandomRelic();
                if (relic != null) Run.Apply(relic);
                Fx.Explosion(at, 1.4f, new Color(1f, 0.85f, 0.35f, 0.9f));
                Sfx.Play(Sfx.Lib.SpireClaimed, at);
                if (relic != null) Sfx.Play(Sfx.Lib.RelicGained);
                Claimed?.Invoke(at, relic);
            }
        }
    }
}
