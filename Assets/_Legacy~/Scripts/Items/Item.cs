using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>A resource chunk travelling through the logistics network.</summary>
    public class Item
    {
        public ResourceType Type;
        public Transform T;
    }

    public static class ItemPool
    {
        static readonly Stack<Item>[] free = new Stack<Item>[ResourceInfo.Count];
        static GameObject[] templates;
        static Transform root;

        public static void Init(Transform parent)
        {
            root = new GameObject("Items").transform;
            root.SetParent(parent, false);
            templates = new GameObject[ResourceInfo.Count];
            for (int i = 0; i < ResourceInfo.Count; i++)
            {
                free[i] = new Stack<Item>();
                templates[i] = MakeTemplate((ResourceType)i);
                templates[i].transform.SetParent(root, false);
                templates[i].SetActive(false);
            }
        }

        public static GameObject MakeTemplate(ResourceType type)
        {
            var mat = Art.Resource[(int)type];
            var m = new VoxelModel("Item_" + type);
            switch (type)
            {
                case ResourceType.Gold:
                    m.Box(new Vector3(0, 0.08f, 0), new Vector3(0.24f, 0.16f, 0.2f), mat, "", 20f);
                    m.Box(new Vector3(0.04f, 0.18f, 0), new Vector3(0.13f, 0.1f, 0.12f), mat, "", 50f);
                    break;
                case ResourceType.Crystal:
                case ResourceType.Amethyst:
                    m.Box(new Vector3(0, 0.15f, 0), new Vector3(0.14f, 0.3f, 0.14f), mat, Quaternion.Euler(0, 45, 12));
                    m.Box(new Vector3(0.08f, 0.1f, 0.04f), new Vector3(0.09f, 0.2f, 0.09f), mat, Quaternion.Euler(0, 20, -25));
                    break;
                default:
                    m.Box(new Vector3(0, 0.09f, 0), new Vector3(0.22f, 0.18f, 0.22f), mat, "", 15f);
                    break;
            }
            return m.Finish(shadows: false);
        }

        public static Item Get(ResourceType type)
        {
            var stack = free[(int)type];
            Item item = stack.Count > 0 ? stack.Pop() : new Item
            {
                Type = type,
                T = Object.Instantiate(templates[(int)type], root).transform,
            };
            item.T.gameObject.SetActive(true);
            return item;
        }

        public static void Release(Item item)
        {
            if (item == null) return;
            item.T.gameObject.SetActive(false);
            free[(int)item.Type].Push(item);
        }
    }
}
