using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Shows the route enemies will take: a trail of arrows from each bridge landing to the Core, following the
    /// same flow field they walk. Redrawn whenever buildings change, so walls and towers visibly bend it.
    /// A pulse runs along the arrows toward the Core.
    /// </summary>
    [RequireComponent(typeof(IsoGrid), typeof(BuildController))]
    public class EnemyPathView : MonoBehaviour
    {
        public bool Show = true;
        public Color Color = new Color(1f, 0.35f, 0.25f, 1f);
        [Range(0f, 1f)] public float MinAlpha = 0.25f;
        [Range(0f, 1f)] public float MaxAlpha = 0.75f;
        [Tooltip("Cells per second the pulse travels toward the Core.")]
        public float PulseSpeed = 6f;
        [Tooltip("Cells between pulses.")]
        public float PulseSpacing = 8f;

        IsoGrid map;
        BuildController build;
        FlowField flow;
        Material material;
        Transform root;
        readonly List<(SpriteRenderer sr, int steps)> arrows = new List<(SpriteRenderer, int)>();
        readonly HashSet<Vector2Int> onPath = new HashSet<Vector2Int>();
        bool dirty = true;

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            var view = GetComponent<MapView>();
            if (view != null) material = view.SpriteMaterial;
            root = new GameObject("EnemyPath").transform;
            root.SetParent(transform, false);
            flow = new FlowField(map);
            build.BuildingsChanged += () => dirty = true;
        }

        void Update()
        {
            bool visible = Show && (build.Core == null || !build.Core.Destroyed);
            root.gameObject.SetActive(visible);
            if (!visible) return;

            if (dirty)
            {
                dirty = false;
                flow.Rebuild();
                Redraw();
            }

            float head = Time.time * PulseSpeed;
            foreach (var (sr, steps) in arrows)
            {
                // 1 at a pulse, fading behind it; steps shrink toward the Core, so pulses move that way.
                float t = Mathf.Repeat(steps + head, PulseSpacing) / PulseSpacing;
                var c = Color;
                c.a = Mathf.Lerp(MinAlpha, MaxAlpha, Mathf.Pow(1f - t, 3f));
                sr.color = c;
            }
        }

        void Redraw()
        {
            onPath.Clear();
            int used = 0;
            foreach (var bridge in map.Bridges)
            {
                var c = bridge.Landing;
                if (flow.Distance(c) == FlowField.Unreachable) continue;
                // Paths from different bridges merge; draw shared cells once.
                while (onPath.Add(c))
                {
                    bool more = flow.Next(c, out var next);
                    int dir = more ? Dir.Of(next - c) : IntoCore(c); // the last cell points into the Core
                    if (dir >= 0)
                    {
                        var sr = Arrow(used++, flow.Distance(c));
                        sr.sprite = BlockArt.PathArrow(dir);
                        sr.transform.position = map.SurfaceCenter(c);
                        sr.sortingOrder = map.SortOrder(c) + 2; // over belts and mines on the cell, under enemies
                    }
                    if (!more) break;
                    c = next;
                }
            }
            for (int i = used; i < arrows.Count; i++) arrows[i].sr.enabled = false;
        }

        int IntoCore(Vector2Int c)
        {
            for (int d = 0; d < 4; d++)
                if (map.IsBase(c + Dir.Offsets[d])) return d;
            return -1;
        }

        SpriteRenderer Arrow(int i, int steps)
        {
            while (arrows.Count <= i)
            {
                var sr = new GameObject("PathArrow").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(root, false);
                if (material != null) sr.sharedMaterial = material;
                arrows.Add((sr, 0));
            }
            var a = arrows[i];
            a.sr.enabled = true;
            arrows[i] = (a.sr, steps);
            return a.sr;
        }
    }
}
