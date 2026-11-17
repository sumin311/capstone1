using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Chef24
{
    /// <summary>
    /// Sends customers to the window while the truck is open. Up to one customer per slot.
    /// Staff (교직원) come more often at lunch time (개발계획서 핵심기능 3).
    /// </summary>
    public class CustomerSpawner : MonoBehaviour
    {
        [Tooltip("Customer model (the VRM avatar). A capsule is used if empty.")]
        public GameObject customerPrefab;
        public RuntimeAnimatorController animatorController;

        public Transform[] slots;
        public Transform spawnPoint;
        public Transform exitPoint;
        [Tooltip("Direction customers face while waiting (towards the truck).")]
        public Vector3 faceDirection = Vector3.forward;

        public Vector2 spawnIntervalRange = new Vector2(9f, 15f);
        public Vector2 lunchIntervalRange = new Vector2(5f, 9f);

        public List<CustomerTypeData> types = new List<CustomerTypeData>
        {
            new CustomerTypeData { name = "학생", color = new Color(0.45f, 0.8f, 1f), orderPatience = 25f, foodPatience = 70f, payMultiplier = 0.9f, spawnWeight = 5f, lunchWeightMultiplier = 1f, maxExclusions = 1 },
            new CustomerTypeData { name = "교수", color = new Color(1f, 0.65f, 0.35f), orderPatience = 14f, foodPatience = 45f, payMultiplier = 1.5f, picky = true, spawnWeight = 2f, lunchWeightMultiplier = 1f, maxExclusions = 2 },
            new CustomerTypeData { name = "교직원", color = new Color(0.6f, 1f, 0.55f), orderPatience = 20f, foodPatience = 55f, payMultiplier = 1.1f, spawnWeight = 1.5f, lunchWeightMultiplier = 4f, maxExclusions = 1 },
        };

        public int ActiveCount => active.Count;

        Customer[] occupied;
        readonly List<Customer> active = new List<Customer>();
        bool spawning = true;
        float nextSpawn = 2f;

        void Awake() => occupied = new Customer[slots != null ? slots.Length : 0];

        public void StopSpawning() => spawning = false;

        void Update()
        {
            active.RemoveAll(c => c == null);
            var gm = GameManager.Instance;
            if (!spawning || gm == null || gm.State != GameState.Playing || gm.clock == null || !gm.clock.IsOpen) return;

            nextSpawn -= Time.deltaTime;
            if (nextSpawn > 0f) return;

            int free = System.Array.FindIndex(occupied, c => c == null);
            if (free < 0)
            {
                nextSpawn = 1f;
                return;
            }
            Spawn(free, gm.clock.IsLunchTime);
            Vector2 range = gm.clock.IsLunchTime ? lunchIntervalRange : spawnIntervalRange;
            nextSpawn = Random.Range(range.x, range.y);
        }

        void Spawn(int slot, bool lunch)
        {
            CustomerTypeData type = PickType(lunch);
            GameObject go;
            if (customerPrefab != null)
            {
                go = Instantiate(customerPrefab, spawnPoint.position, Quaternion.identity);
                var anim = go.GetComponentInChildren<Animator>();
                if (anim != null && animatorController != null)
                {
                    anim.runtimeAnimatorController = animatorController;
                    anim.applyRootMotion = false;
                }
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.position = spawnPoint.position + Vector3.up;
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().material.color = type.color;
            }
            go.name = "Customer (" + type.name + ")";

            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.85f, 0f);
            col.height = 1.7f;
            col.radius = 0.3f;

            var c = go.AddComponent<Customer>();
            c.Init(this, slot, type, slots[slot].position, faceDirection, exitPoint.position);
            occupied[slot] = c;
            active.Add(c);
        }

        CustomerTypeData PickType(bool lunch)
        {
            float total = types.Sum(t => t.spawnWeight * (lunch ? t.lunchWeightMultiplier : 1f));
            float r = Random.value * total;
            foreach (var t in types)
            {
                r -= t.spawnWeight * (lunch ? t.lunchWeightMultiplier : 1f);
                if (r <= 0f) return t;
            }
            return types[0];
        }

        public void FreeSlot(int slot, Customer c)
        {
            if (slot >= 0 && slot < occupied.Length && occupied[slot] == c) occupied[slot] = null;
        }
    }
}
