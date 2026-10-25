using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// A placed building: which catalog entry, the cell its footprint is anchored on and the level it stands on. Its
    /// object stands upright on the middle of its footprint (see IsoGrid.FootprintFrame).
    /// </summary>
    public class Building : MonoBehaviour
    {
        public BuildingCatalog.Entry Def { get; private set; }
        public Vector2Int Origin { get; private set; } // footprint anchor (see IsoGrid.Footprint)
        public int Level { get; private set; }
        /// <summary>Every cell it stands on.</summary>
        public List<Vector2Int> Cells { get; private set; } = new List<Vector2Int>();
        /// <summary>Its sprite, for belts (drawn flat on the ground); null for buildings shown as models.</summary>
        public SpriteRenderer Renderer { get; private set; }
        /// <summary>The 3D model instance, if it has one.</summary>
        public GameObject Model { get; private set; }
        /// <summary>The model's particle effects, if its prefab has any (see BuildingFx).</summary>
        public BuildingFx Effects { get; private set; }

        public bool IsCore => Def != null && Def.Id == BuildingCatalog.CoreId;
        public Vector2Int Size => this is Conveyor || this is Wall ? Vector2Int.one : Def.Size;
        /// <summary>World position of the middle of the footprint, on the ground.</summary>
        public Vector3 Center => transform.position;
        /// <summary>Unit vector from the planet centre through the building (its "up").</summary>
        public Vector3 Up => transform.position.normalized;

        public void Init(BuildingCatalog.Entry def, Vector2Int origin, int level, SpriteRenderer renderer)
        {
            Def = def;
            Origin = origin;
            Level = level;
            Renderer = renderer;
            Cells = IsoGrid.Current != null ? IsoGrid.Current.Footprint(origin, Size) : new List<Vector2Int> { origin };
        }

        /// <summary>Spawns the 3D model and a collider around it for clicking (see BuildingModels).</summary>
        public void ShowModel(IsoGrid map)
        {
            if (Def.Model == null) return;
            Model = BuildingModels.Spawn(Def.Model, map.FootprintCenter(Origin, Size, Level), map.FootprintFrame(Origin, Size),
                Vector3.one * (Def.Scale * IsoGrid.FootprintScale(Size)), 0f, transform);
            BuildingModels.AddPickCollider(gameObject, Model);
            Effects = Model.GetComponent<BuildingFx>();
            if (Effects != null) Effects.Init(0);
        }

        /// <summary>Starts or stops the building's working effect, if it has one.</summary>
        public void SetWorking(bool working)
        {
            if (Effects != null) Effects.SetWorking(working);
        }

        /// <summary>Plays the building's one-off effect, if it has one.</summary>
        public void PlayEffect()
        {
            if (Effects != null) Effects.Fire();
        }

        public bool Covers(Vector2Int c) => Cells.Contains(c);

        /// <summary>Offered an item by a belt feeding into it; true if taken.</summary>
        public virtual bool TryAccept(BeltItem item) => false;
    }

    /// <summary>
    /// One belt cell. Items enter from side In and leave through side Out (see <see cref="Dir"/>, in this cell's
    /// frame). InLift/OutLift raise that end by a level to meet a higher connected belt, turning the tile into a ramp.
    /// </summary>
    public class Conveyor : Building
    {
        public int In, Out, InLift, OutLift;
        /// <summary>Items on this tile, front-most (highest progress) first. Their objects are children of this one.</summary>
        public readonly List<BeltItem> Items = new List<BeltItem>();

        public Vector2Int Cell => Origin;
        public Vector2Int OutCell => IsoGrid.Current.Neighbour(Origin, Out);
        public Vector2Int InCell => IsoGrid.Current.Neighbour(Origin, In);

        public void SetShape(int inSide, int outSide, int inLift, int outLift, int frame)
        {
            In = inSide;
            Out = outSide;
            InLift = inLift;
            OutLift = outLift;
            ShowFrame(frame);
        }

        public void ShowFrame(int frame) => Renderer.sprite = ConveyorArt.Get(In, Out, InLift, OutLift, frame);
    }

    /// <summary>A crystal riding a belt; Progress runs 0..1 across the current tile.</summary>
    public class BeltItem
    {
        public int Kind;       // index into IsoGrid.OreTypes (an alloy: the more valuable of its two ores)
        public bool Refined;
        /// <summary>Two different ores fused in a Refiner; worth much more (see ItemFlow).</summary>
        public bool Alloy;
        public int AlloyKind;  // the other ore of an alloy
        public float Progress;
        /// <summary>The crystal model riding the belt.</summary>
        public GameObject View;
    }

    /// <summary>Mining building: produces crystals from the ore under it and pushes them onto adjacent belts.</summary>
    public class Miner : Building
    {
        public readonly List<IsoGrid.OrePatch> Patches = new List<IsoGrid.OrePatch>();
        public readonly Queue<int> Stored = new Queue<int>(); // ore kinds waiting to go out
        public float Timer;
        public int NextOutput, NextPatch;
    }

    /// <summary>Takes items from belts feeding into it and hands them out in turn to the belts leading away.</summary>
    public class Splitter : Building
    {
        public const int Capacity = 2;
        public readonly Queue<BeltItem> Buffer = new Queue<BeltItem>();
        public int NextOutput;

        public override bool TryAccept(BeltItem item)
        {
            if (Buffer.Count >= Capacity) return false;
            Buffer.Enqueue(item);
            return true;
        }
    }

    /// <summary>Refines raw crystals one at a time; already refined ones pass straight through.</summary>
    public class Refiner : Building
    {
        public const int Capacity = 4;
        public readonly Queue<BeltItem> Input = new Queue<BeltItem>();
        public readonly Queue<BeltItem> Output = new Queue<BeltItem>();
        public float Progress;
        public int NextOutput;

        public override bool TryAccept(BeltItem item)
        {
            if (item.Refined)
            {
                if (Output.Count >= Capacity) return false;
                Output.Enqueue(item);
                return true;
            }
            if (Input.Count >= Capacity) return false;
            Input.Enqueue(item);
            return true;
        }
    }

    /// <summary>The base. Sells every crystal delivered to it; the game ends when its health runs out.</summary>
    public class CoreBuilding : Building
    {
        public float MaxHealth = 1000f;
        public float Health = 1000f;
        public bool Destroyed => Health <= 0f;
        public event System.Action OnDestroyed;

        /// <summary>Raised on every hit: the damage dealt and whether a boss dealt it.</summary>
        public event System.Action<float, bool> Damaged;

        public void TakeDamage(float amount, bool fromBoss = false)
        {
            if (Destroyed) return;
            Health = Mathf.Max(0f, Health - amount);
            Damaged?.Invoke(amount, fromBoss);
            if (Destroyed) OnDestroyed?.Invoke();
        }
    }

    /// <summary>
    /// A wall tile that snaps to its neighbours: a half-segment reaches toward every adjacent wall (any of the 6
    /// sides), so lines and corners join up, with a post where the wall turns or branches. A lone wall shows a full
    /// straight segment.
    /// </summary>
    public class Wall : Building
    {
        /// <summary>How many pieces it is drawn with: 2 for a straight run or a lone wall, more where it turns or branches.</summary>
        public int PieceCount { get; private set; }

        readonly List<GameObject> models = new List<GameObject>();

        /// <summary>Rebuilds the pieces; isWall says which cells hold walls.</summary>
        public void Shape(IsoGrid map, System.Func<Vector2Int, bool> isWall)
        {
            foreach (var m in models) Destroy(m.transform.parent.gameObject);
            models.Clear();
            PieceCount = 0;
            if (Def.Model == null) return;
            var linked = new List<int>();
            for (int d = 0; d < Dir.Count; d++)
                if (map.DirTo(Origin, map.Neighbour(Origin, d)) == d && isWall(map.Neighbour(Origin, d))) linked.Add(d);

            // A half segment from the centre toward side d (its model runs along +X).
            void Half(int d) => models.Add(BuildingModels.Spawn(map, Def.Model, Origin, Dir.Mid(d) * 0.5f, Level, GameArt.WallHalfScale, Dir.Yaw(d), transform));

            bool straight = linked.Count == 0 || linked.Count == 1 || (linked.Count == 2 && linked[1] == Dir.Opposite(linked[0]));
            if (straight)
            {
                // Lone walls, dead ends and straight runs: a full segment through the cell (along sides 0-3 if alone).
                int d = linked.Count > 0 ? linked[0] : 0;
                Half(d);
                Half(Dir.Opposite(d));
            }
            else
            {
                foreach (int d in linked) Half(d);
                models.Add(BuildingModels.Spawn(map, Def.Model, Origin, Vector2.zero, Level, GameArt.WallPostScale, 0f, transform));
            }
            PieceCount = models.Count;
            if (models.Count > 0) BuildingModels.AddPickCollider(gameObject, transform.gameObject);
        }
    }

    /// <summary>What a crystal fed to a tower boosts, by ore (see Tower.TryAccept).</summary>
    public enum OreBoost { FireRate, Range, Damage }

    /// <summary>
    /// A defensive building; Combat runs its attack (see <see cref="AttackKind"/>). Belts can feed it crystals, which
    /// it burns for a timed boost by ore: sapphire = attack speed, emerald = range, amethyst = damage, gold = speed and
    /// damage, ruby and alloys = all three. Refined crystals last twice as long.
    /// </summary>
    public class Tower : Building
    {
        public float Cooldown;
        public Enemy Target;
        public LineRenderer Beam;      // laser / tesla bolts
        public float BeamTimer;
        /// <summary>Damage dealt so far (for the map summary's most valuable tower).</summary>
        public float DamageDealt;
        /// <summary>Seconds of boost left, per OreBoost.</summary>
        public readonly float[] Boost = new float[3];
        /// <summary>Colour of the last crystal fed in (for its glow).</summary>
        public Color BoostColor = Color.white;
        public const float SecondsPerCrystal = 8f, MaxBoost = 40f;
        Transform headNode;            // the model's Head node, if it has one
        Quaternion headRest;

        public bool Boosted(OreBoost b) => Boost[(int)b] > 0f;
        public bool AnyBoost => Boost[0] > 0f || Boost[1] > 0f || Boost[2] > 0f;
        /// <summary>Towers that shoot take crystals (mines and slow fields don't).</summary>
        public bool TakesCrystals => Def.Attack != AttackKind.Mine && Def.Attack != AttackKind.Slow && Def.Attack != AttackKind.None;

        /// <summary>Which boosts an ore kind gives (by the ore's name; unknown ores boost attack speed).</summary>
        public static OreBoost[] BoostsOf(BeltItem item)
        {
            if (item.Alloy) return new[] { OreBoost.FireRate, OreBoost.Range, OreBoost.Damage };
            string name = IsoGrid.OreTypeOf(item.Kind).Name ?? "";
            switch (name)
            {
                case "Emerald": return new[] { OreBoost.Range };
                case "Amethyst": return new[] { OreBoost.Damage };
                case "Gold": return new[] { OreBoost.FireRate, OreBoost.Damage };
                case "Ruby": return new[] { OreBoost.FireRate, OreBoost.Range, OreBoost.Damage };
                default: return new[] { OreBoost.FireRate };
            }
        }

        public override bool TryAccept(BeltItem item)
        {
            if (!TakesCrystals) return false;
            var boosts = BoostsOf(item);
            foreach (var b in boosts)
                if (Boost[(int)b] >= MaxBoost) return false; // full: the belt backs up
            float seconds = SecondsPerCrystal * (item.Refined ? 2f : 1f);
            foreach (var b in boosts) Boost[(int)b] = Mathf.Min(MaxBoost, Boost[(int)b] + seconds);
            BoostColor = IsoGrid.OreTypeOf(item.Kind).Color;
            return true;
        }

        public void TickBoost(float dt)
        {
            for (int i = 0; i < Boost.Length; i++) Boost[i] = Mathf.Max(0f, Boost[i] - dt);
        }

        public bool CanAim => headNode != null;

        /// <summary>Lets the tower aim by turning its model's Head node (if it has one).</summary>
        public void SetupHead(bool aimingHeads)
        {
            if (!aimingHeads || Model == null) return;
            headNode = BuildingModels.FindChild(Model.transform, GameArt.HeadNode);
            if (headNode != null) headRest = headNode.localRotation;
        }

        /// <summary>
        /// Turns the head toward a world direction (only its part along the ground counts). The model's guns point
        /// along model -X (glTF flips X).
        /// </summary>
        public void Aim(Vector3 worldDir)
        {
            if (!CanAim) return;
            var local = Quaternion.Inverse(transform.rotation) * worldDir;
            if (local.x * local.x + local.z * local.z < 1e-6f) return;
            float yaw = Mathf.Atan2(local.z, -local.x) * Mathf.Rad2Deg;
            headNode.localRotation = Quaternion.Euler(0f, yaw, 0f) * headRest;
        }
    }
}
