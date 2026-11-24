using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// Original material -> toon material pairs, filled by the scene builder. Used at runtime to give
    /// objects spawned from model prefabs (the held takoyaki, dishes on plate spots) the same toon look.
    /// </summary>
    [CreateAssetMenu(menuName = "Chef24/Toon Material Map")]
    public class ToonMaterialMap : ScriptableObject
    {
        [Serializable]
        public class Pair
        {
            public Material from;
            public Material to;
        }

        public List<Pair> pairs = new List<Pair>();

        Dictionary<Material, Material> lookup;

        public Material Find(Material from)
        {
            if (from == null) return null;
            if (lookup == null)
            {
                lookup = new Dictionary<Material, Material>();
                foreach (var p in pairs)
                    if (p.from != null && p.to != null) lookup[p.from] = p.to;
            }
            return lookup.TryGetValue(from, out Material to) ? to : null;
        }

        public void Set(Material from, Material to)
        {
            foreach (var p in pairs)
                if (p.from == from) { p.to = to; lookup = null; return; }
            pairs.Add(new Pair { from = from, to = to });
            lookup = null;
        }

        /// <summary>Swaps every mapped material under <paramref name="root"/> for its toon version.</summary>
        public void Apply(GameObject root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var to = Find(mats[i]);
                    if (to != null) { mats[i] = to; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
