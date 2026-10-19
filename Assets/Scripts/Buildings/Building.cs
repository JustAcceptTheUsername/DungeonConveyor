using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>A placed building: which catalog entry, where its footprint starts and the level it stands on.</summary>
    public class Building : MonoBehaviour
    {
        public BuildingCatalog.Entry Def { get; private set; }
        public Vector2Int Origin { get; private set; } // footprint cell with the smallest x and y
        public int Level { get; private set; }
        /// <summary>Its sprite. With a live model (see ShowModel) it is no longer drawn but still used for clicking.</summary>
        public SpriteRenderer Renderer { get; private set; }
        /// <summary>The live 3D model instance, if it has one.</summary>
        public GameObject Model { get; private set; }
        /// <summary>The live model's particle effects, if its prefab has any (see BuildingFx).</summary>
        public BuildingFx Effects { get; private set; }

        public bool IsCore => Def != null && Def.Id == BuildingCatalog.CoreId;
        public Vector2Int Size => this is Conveyor ? Vector2Int.one : Def.Size;
        /// <summary>Centre of the footprint in grid coordinates (cell (x, y) has its centre at (x, y)).</summary>
        public Vector2 GridCenter => (Vector2)Origin + (Vector2)(Size - Vector2Int.one) * 0.5f;

        public void Init(BuildingCatalog.Entry def, Vector2Int origin, int level, SpriteRenderer renderer)
        {
            Def = def;
            Origin = origin;
            Level = level;
            Renderer = renderer;
        }

        /// <summary>Stands the live 3D model in for the sprite (see BuildingModels).</summary>
        public void ShowModel(IsoGrid map)
        {
            if (Def.Model == null) return;
            Model = BuildingModels.Spawn(map, Def.Model, GridCenter, Level, Vector3.one * Def.Scale, 0f, Renderer.sortingOrder, transform);
            Renderer.forceRenderingOff = true; // keeps its bounds and pixels for clicking and rock fading
            Effects = Model.GetComponent<BuildingFx>();
            if (Effects != null) Effects.Init(Renderer.sortingOrder);
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

        public bool Covers(Vector2Int c) => c.x >= Origin.x && c.y >= Origin.y && c.x < Origin.x + Size.x && c.y < Origin.y + Size.y;

        /// <summary>Offered an item by a belt feeding into it; true if taken.</summary>
        public virtual bool TryAccept(BeltItem item) => false;
    }

    /// <summary>
    /// One belt cell. Items enter from side In and leave through side Out (see <see cref="Dir"/>). InLift/OutLift
    /// raise that end by a level to meet a higher connected belt, turning the tile into a ramp.
    /// </summary>
    public class Conveyor : Building
    {
        public int In, Out, InLift, OutLift;
        /// <summary>Items on this tile, front-most (highest progress) first. Their objects are children of this one.</summary>
        public readonly List<BeltItem> Items = new List<BeltItem>();

        public Vector2Int Cell => Origin;
        public Vector2Int OutCell => Origin + Dir.Offsets[Out];
        public Vector2Int InCell => Origin + Dir.Offsets[In];

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
        public int Kind;       // index into IsoGrid.OreTypes
        public bool Refined;
        public float Progress;
        public SpriteRenderer Renderer;
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
    /// A wall tile that snaps to its neighbours: a half-segment reaches toward every adjacent wall, so lines and
    /// corners join up, with a post where the wall turns or branches. A lone wall shows a full straight segment.
    /// </summary>
    public class Wall : Building
    {
        readonly List<SpriteRenderer> parts = new List<SpriteRenderer>();

        /// <summary>How many pieces it is drawn with: 2 for a straight run or a lone wall, more where it turns or branches.</summary>
        public int PieceCount { get; private set; }

        readonly List<GameObject> models = new List<GameObject>();

        /// <summary>Rebuilds the pieces; isWall says which neighbouring cells hold walls.</summary>
        public void Shape(IsoGrid map, System.Func<Vector2Int, bool> isWall, Material material, bool liveModels)
        {
            if (liveModels && Def.Model != null)
            {
                PieceCount = BuildModels(map, isWall);
                return;
            }
            PieceCount = Build(map, Origin, Level, isWall, material, transform, parts, 0, map.SortOrder(Origin) + 1, Color.white);
            for (int i = PieceCount; i < parts.Count; i++) parts[i].enabled = false;
        }

        /// <summary>The same pieces as Build lays out, as live models (see BuildingModels).</summary>
        int BuildModels(IsoGrid map, System.Func<Vector2Int, bool> isWall)
        {
            foreach (var m in models) Destroy(m.transform.parent.gameObject);
            models.Clear();
            bool px = isWall(Origin + Vector2Int.right), nx = isWall(Origin + Vector2Int.left);
            bool py = isWall(Origin + Vector2Int.up), ny = isWall(Origin + Vector2Int.down);
            bool alongX = px || nx, alongY = py || ny;
            int order = map.SortOrder(Origin) + 1;

            void Piece(bool y, Vector2 offset, bool post = false) =>
                models.Add(BuildingModels.Spawn(map, Def.Model, (Vector2)Origin + offset, Level,
                    post ? GameArt.WallPostScale : GameArt.WallHalfScale, y && !post ? 90f : 0f, order, transform));

            if (!(alongX && alongY))
            {
                // Lone walls and straight runs: a full segment along the run (x if alone).
                var h = alongY ? new Vector2(0f, 0.25f) : new Vector2(0.25f, 0f);
                Piece(alongY, h);
                Piece(alongY, -h);
                return models.Count;
            }
            if (px) Piece(false, new Vector2(0.25f, 0f));
            if (py) Piece(true, new Vector2(0f, 0.25f));
            Piece(false, Vector2.zero, post: true);
            if (nx) Piece(false, new Vector2(-0.25f, 0f));
            if (ny) Piece(true, new Vector2(0f, -0.25f));
            return models.Count;
        }

        /// <summary>
        /// Lays out the wall pieces for a cell into the renderer pool from index `start` on (creating renderers under
        /// parent as needed) and returns the next free index. Also used for the placement preview.
        /// </summary>
        public static int Build(IsoGrid map, Vector2Int cell, int level, System.Func<Vector2Int, bool> isWall, Material material,
            Transform parent, List<SpriteRenderer> pool, int start, int order, Color color)
        {
            bool px = isWall(cell + Vector2Int.right), nx = isWall(cell + Vector2Int.left);
            bool py = isWall(cell + Vector2Int.up), ny = isWall(cell + Vector2Int.down);
            bool alongX = px || nx, alongY = py || ny;
            bool straight = !(alongX && alongY);

            int used = start;
            var center = map.GridToWorld(cell, level);
            var xHalf = (map.GridToWorld(cell + new Vector2(0.25f, 0f), level) - center);
            var yHalf = (map.GridToWorld(cell + new Vector2(0f, 0.25f), level) - center);

            // Back pieces first (further from the camera), then the post, then the front pieces.
            void Piece(Sprite sprite, Vector3 offset, float depth)
            {
                if (sprite == null) return;
                while (pool.Count <= used)
                {
                    var sr = new GameObject("WallPiece").AddComponent<SpriteRenderer>();
                    sr.transform.SetParent(parent, false);
                    if (material != null) sr.sharedMaterial = material;
                    pool.Add(sr);
                }
                var p = pool[used++];
                p.enabled = true;
                p.sprite = sprite;
                p.color = color;
                p.sortingOrder = order;
                p.transform.position = center + offset + new Vector3(0f, 0f, depth);
            }

            if (straight)
            {
                // Lone walls and straight runs: a full segment along the run (x if alone).
                bool y = alongY;
                var s = y ? GameArt.WallHalfY : GameArt.WallHalfX;
                var h = y ? yHalf : xHalf;
                Piece(s, h, 0.002f);
                Piece(s, -h, 0f);
                return used;
            }
            if (px) Piece(GameArt.WallHalfX, xHalf, 0.002f);
            if (py) Piece(GameArt.WallHalfY, yHalf, 0.002f);
            Piece(GameArt.WallPost, Vector3.zero, 0.001f);
            if (nx) Piece(GameArt.WallHalfX, -xHalf, 0f);
            if (ny) Piece(GameArt.WallHalfY, -yHalf, 0f);
            return used;
        }
    }

    /// <summary>A defensive building; Combat runs its attack (see <see cref="AttackKind"/>).</summary>
    public class Tower : Building
    {
        public float Cooldown;
        public Enemy Target;
        public LineRenderer Beam;      // laser / tesla bolts
        public float BeamTimer;
        public SpriteRenderer Head;    // turning head sprite, for models that have one (see GameArt.RenderHead)
        Transform headNode;            // or, with a live model, its Head node
        Quaternion headRest;

        public bool CanAim => Head != null || headNode != null;

        /// <summary>
        /// Lets the tower aim: with a live model, by turning its Head node (if it has one); otherwise by splitting the
        /// sprite into body + head sprites.
        /// </summary>
        public void SetupHead(bool aimingHeads)
        {
            if (!aimingHeads) return;
            if (Model != null)
            {
                headNode = BuildingModels.FindChild(Model.transform, GameArt.HeadNode);
                if (headNode != null) headRest = headNode.localRotation;
                return;
            }
            if (Def.BaseSprite == null) return;
            if (Def.BaseSprite == null) return;
            Renderer.sprite = Def.BaseSprite;
            Head = new GameObject("Head").AddComponent<SpriteRenderer>();
            Head.transform.SetParent(transform, false);
            Head.transform.localPosition = new Vector3(0f, 0f, -0.001f); // just in front of the body
            Head.sharedMaterial = Renderer.sharedMaterial;
            Head.sortingOrder = Renderer.sortingOrder;
            Head.sprite = Def.HeadSprites[0];
        }

        /// <summary>
        /// Turns the head toward a grid direction. The model's guns point along model -X (glTF flips X), which lies
        /// along grid -x; head sprite i is the head turned by i steps of 360/HeadDirections about model Y.
        /// </summary>
        public void Aim(Vector2 gridDir)
        {
            if (!CanAim || gridDir.sqrMagnitude < 1e-6f) return;
            float yaw = Mathf.Atan2(gridDir.y, -gridDir.x) * Mathf.Rad2Deg;
            if (headNode != null)
            {
                headNode.localRotation = Quaternion.Euler(0f, yaw, 0f) * headRest;
                return;
            }
            int n = Def.HeadSprites.Length;
            int i = ((Mathf.RoundToInt(yaw / (360f / n)) % n) + n) % n;
            Head.sprite = Def.HeadSprites[i];
        }
    }
}
