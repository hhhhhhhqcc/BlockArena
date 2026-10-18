using System;
using System.Collections.Generic;
using BlockArena.Core;

namespace BlockArena.Runtime
{
    // A released creature keeps its predicted position until this exact command is acknowledged.
    // Coordinates alone cannot acknowledge a rapid A -> B -> A drag: an older A snapshot may arrive first.
    public sealed class PlacementPreview
    {
        sealed class Pending
        {
            public string Id;
            public Point2 Position;
            public float Expires;
        }
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        readonly List<string> completed = new List<string>();
        public int Count { get { return pending.Count; } }

        public GameCommand Submit(string unitId, Point2 position, float now)
        {
            string id = Guid.NewGuid().ToString("N");
            pending[unitId] = new Pending { Id = id, Position = position, Expires = now + 8f };
            return new GameCommand { type = "place", unitId = unitId, x = position.x, y = position.y, placementId = id };
        }

        public bool TryGet(string unitId, out Point2 position)
        {
            Pending item;
            if (unitId != null && pending.TryGetValue(unitId, out item)) { position = item.Position; return true; }
            position = new Point2(); return false;
        }

        public bool Reconcile(PlayerState owner, float now)
        {
            bool expired = false;
            completed.Clear();
            foreach (var pair in pending)
            {
                UnitState unit = owner == null ? null : owner.units.Find(u => u.id == pair.Key);
                if (unit == null || unit.placementId == pair.Value.Id) completed.Add(pair.Key);
                else if (now >= pair.Value.Expires) { completed.Add(pair.Key); expired = true; }
            }
            foreach (string unitId in completed) pending.Remove(unitId);
            return expired;
        }

        public bool Reject(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return false;
            string rejected = null;
            foreach (var pair in pending) if (pair.Value.Id == placementId) { rejected = pair.Key; break; }
            return rejected != null && pending.Remove(rejected);
        }

        public void Clear() { pending.Clear(); completed.Clear(); }
    }
}
