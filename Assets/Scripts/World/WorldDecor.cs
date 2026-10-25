using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Everything on the planet that isn't ground or a building: a crystal deposit model on every ore tile (hidden
    /// while a building stands on it, gone once its patch is mined out), and a smouldering crater glow on every
    /// meteor landing site.
    /// </summary>
    [RequireComponent(typeof(IsoGrid))]
    public class WorldDecor : MonoBehaviour
    {
        IsoGrid map;
        GameObject[,] deposits;
        Transform root;
        SpriteRenderer[] craters = new SpriteRenderer[0];

        void Start()
        {
            map = GetComponent<IsoGrid>();
            root = new GameObject("Decor").transform;
            root.SetParent(transform, false);
            BuildDeposits();
            BuildCraters();
            map.SpawnsChanged += BuildCraters;
        }

        void OnDestroy()
        {
            if (map != null) map.SpawnsChanged -= BuildCraters;
        }

        void Update()
        {
            float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 2.5f);
            foreach (var c in craters) c.color = new Color(1f, 0.4f, 0.12f, pulse);
        }

        public void SetDepositVisible(Vector2Int c, bool visible)
        {
            if (deposits != null && map.InBounds(c) && deposits[c.x, c.y] != null) deposits[c.x, c.y].SetActive(visible && map.IsOre(c));
        }

        /// <summary>Crystals of each patch's ore type on its tiles; they vanish when the patch is mined out.</summary>
        void BuildDeposits()
        {
            deposits = new GameObject[map.Width, map.Height];
            var library = ArtLibrary.Instance;
            float scale = library != null ? library.DepositScale : 0.6f;
            foreach (var patch in map.Patches)
            {
                var models = IsoGrid.OreTypeOf(patch.Kind).Deposits;
                if (models.Count == 0) continue;
                foreach (var c in patch.Cells)
                {
                    var prefab = models[Random.Range(0, models.Count)];
                    if (prefab == null) continue;
                    var jitter = new Vector2(Random.Range(-0.12f, 0.12f), Random.Range(-0.12f, 0.12f));
                    var model = BuildingModels.Spawn(map, prefab, c, jitter, map.Heights[c.x, c.y], Vector3.one * scale, Random.Range(0f, 360f), root);
                    var holder = model.transform.parent.gameObject;
                    holder.SetActive(map.BuildingAt(c) == null);
                    deposits[c.x, c.y] = holder;
                }
            }
            map.PatchDepleted += patch =>
            {
                foreach (var c in patch.Cells)
                    if (deposits[c.x, c.y] != null) deposits[c.x, c.y].SetActive(false);
            };
        }

        void BuildCraters()
        {
            var material = GetComponent<MapView>().SpriteMaterial;
            foreach (var old in craters)
                if (old != null) Destroy(old.gameObject);
            craters = new SpriteRenderer[map.Spawns.Count];
            for (int i = 0; i < craters.Length; i++)
            {
                var c = map.Spawns[i].Landing;
                var sr = new GameObject("Crater").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(root, false);
                if (material != null) sr.sharedMaterial = material;
                sr.sprite = Fx.Soft;
                PlanetArt.LayFlat(sr.transform, map, c, map.Heights[c.x, c.y], 0.03f);
                sr.transform.localScale = Vector3.one * 2.2f;
                craters[i] = sr;
            }
        }
    }
}
