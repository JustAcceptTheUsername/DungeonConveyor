using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Owns every MaterialPropertyBlock on one enemy model's own renderers (EnemyLook draws on its own "Look" meshes
    /// and material, so it never meets these): the palette colours per role (EnemyPalette, an element variant) and the
    /// glow multipliers the rig drives (the spitter's Core swell, the boss's seams, the crumble cooling). Both are
    /// merged into one block per material slot, so they never overwrite each other. A slot still showing the colour
    /// its glb was exported with has no palette paint: with element None and the authored palette, an enemy carries no
    /// blocks at all and looks (and batches) exactly as imported. Shared materials are never touched or copied.
    /// No allocation after construction.
    /// </summary>
    public sealed class EnemyPaint
    {
        static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor"), BaseColor = Shader.PropertyToID("_BaseColor"),
            EmissiveFactor = Shader.PropertyToID("emissiveFactor"), EmissionColor = Shader.PropertyToID("_EmissionColor");
        static MaterialPropertyBlock block;

        /// <summary>Debug: paint every role slot even when it shows its exported colour (to check the port is exact).</summary>
        public static bool AlwaysPaint;

        readonly Renderer[] renderer;
        readonly int[] slot, role, baseId, emissionId;
        readonly Color[] materialEmission;   // the shared material's emissive factor (as GetColor returns it)
        readonly bool[] painted;
        readonly Color[] paintBase, paintEmission; // linear
        readonly float[] glow;               // rig multiplier on the emission (1 = as is)
        float cool = 1f;                     // crumble cooling on every glow slot (1 = hot)

        public int Count => renderer.Length;
        /// <summary>The element last applied.</summary>
        public EnemyElement Element { get; private set; }
        /// <summary>Slots painted by the palette right now (0 = the imported look).</summary>
        public int PaintedSlots { get; private set; }

        /// <summary>Every material slot of every MeshRenderer under model (call before EnemyLook adds its meshes).</summary>
        public EnemyPaint(GameObject model)
        {
            var list = new List<(Renderer r, int s, Material m)>();
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int s = 0; s < mats.Length; s++)
                    if (mats[s] != null) list.Add((r, s, mats[s]));
            }
            int n = list.Count;
            renderer = new Renderer[n];
            slot = new int[n];
            role = new int[n];
            baseId = new int[n];
            emissionId = new int[n];
            materialEmission = new Color[n];
            painted = new bool[n];
            paintBase = new Color[n];
            paintEmission = new Color[n];
            glow = new float[n];
            for (int i = 0; i < n; i++)
            {
                var (r, s, m) = list[i];
                renderer[i] = r;
                slot[i] = s;
                role[i] = EnemyPalette.RoleOf(m.name);
                baseId[i] = m.HasProperty(BaseColorFactor) ? BaseColorFactor : m.HasProperty(BaseColor) ? BaseColor : -1;
                emissionId[i] = m.HasProperty(EmissiveFactor) ? EmissiveFactor : m.HasProperty(EmissionColor) ? EmissionColor : -1;
                materialEmission[i] = emissionId[i] >= 0 ? m.GetColor(emissionId[i]) : Color.black;
                glow[i] = 1f;
            }
        }

        /// <summary>The paint index of a renderer's material slot, or -1.</summary>
        public int Index(Renderer r, int materialSlot)
        {
            for (int i = 0; i < renderer.Length; i++)
                if (renderer[i] == r && slot[i] == materialSlot) return i;
            return -1;
        }

        /// <summary>
        /// Paints the palette for an element (None = the palette's own colours). palette null: back to the imported
        /// look. compensate: EnemyCatalog.Palette.Compensate; shared: the catalog's element table.
        /// </summary>
        public void Apply(EnemyPalette palette, EnemyElement element, bool compensate, ElementColors[] shared)
        {
            Element = element;
            int count = 0;
            for (int i = 0; i < renderer.Length; i++)
            {
                bool was = painted[i];
                painted[i] = false;
                var s = palette != null && role[i] >= 0 ? palette.Find((PaletteRole)role[i]) : null;
                if (s != null)
                {
                    var c = palette.Resolve(s, element, shared);
                    bool asExported = EnemyPalette.HexOf(c) == EnemyPalette.HexOf(s.Exported) && compensate == s.ExportedCompensated;
                    if (!asExported || AlwaysPaint)
                    {
                        EnemyPalette.ExportColor(c, s.Role, s.BandLift, compensate, out paintBase[i], out paintEmission[i]);
                        painted[i] = true;
                        count++;
                    }
                }
                if (painted[i] || was) Write(i);
            }
            PaintedSlots = count;
        }

        /// <summary>The rig's glow on slot i: k times its (painted or material) emission. 1 = as is.</summary>
        public void SetGlow(int i, float k)
        {
            if (i < 0 || glow[i] == k) return;
            glow[i] = k;
            Write(i);
        }

        /// <summary>The crumble's cooling (1 = hot, 0 = cold) on every slot with a block (the rest cool by material swap).</summary>
        public void Cool(float heat)
        {
            if (cool == heat) return;
            cool = heat;
            for (int i = 0; i < renderer.Length; i++)
                if (emissionId[i] >= 0 && (painted[i] || glow[i] != 1f)) Write(i);
        }

        void Write(int i)
        {
            var r = renderer[i];
            if (r == null) return;
            float k = glow[i] * cool;
            bool hasGlow = emissionId[i] >= 0 && glow[i] != 1f;
            if (!painted[i] && !hasGlow)
            {
                r.SetPropertyBlock(null, slot[i]);
                return;
            }
            if (block == null) block = new MaterialPropertyBlock();
            block.Clear();
            // Measured in game (2026-10-26): a material uploads the glTF base factor (stored as sRGB) converted to
            // linear, but the HDR emissive factor raw. A block's SetColor converts sRGB to linear and SetVector
            // doesn't, so: base -> SetColor(sRGB), emission -> SetVector(raw). (SetColor on the emission darkened
            // it: 0.63 went to 0.35.)
            if (painted[i])
            {
                if (baseId[i] >= 0) block.SetColor(baseId[i], paintBase[i].gamma);
                if (emissionId[i] >= 0) block.SetVector(emissionId[i], paintEmission[i] * k);
            }
            else // a rig glow on the shared material's colour
            {
                var e = materialEmission[i];
                block.SetVector(emissionId[i], new Vector4(e.r * k, e.g * k, e.b * k, e.a));
            }
            r.SetPropertyBlock(block, slot[i]);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            block = null;
            AlwaysPaint = false;
        }
    }
}
