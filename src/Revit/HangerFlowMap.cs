using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace HangerLayout.Revit
{
    /// <summary>
    /// Maps each connected fabrication part to the WORLD-POSITION origin of
    /// the connector that's closer to a user-chosen "start" element. Built
    /// once per Apply via a BFS over the fab network. Lets the placer decide
    /// which side of each fitting is "before" (far from start) vs "after"
    /// (near to start).
    ///
    /// Stores XYZ instead of an index because Revit's ConnectorManager.Connectors
    /// enumeration order is unstable across calls — a near-connector index
    /// captured during BFS may refer to a different connector when the placer
    /// re-enumerates the part. Origin position is stable.
    /// </summary>
    internal class HangerFlowMap
    {
        // ElementId.Value → world position of the connector that's CLOSER to start
        private readonly Dictionary<long, XYZ> _nearEndOrigin = new();

        // ~1.2" tolerance — well below typical pipe/duct lengths but generous
        // enough to absorb floating-point noise on connector origins.
        // NOTE: We use explicit DistanceTo, NOT XYZ.IsAlmostEqualTo(tol). The
        // latter has a non-obvious tolerance semantic (NOT plain Euclidean
        // distance) — empirically it returns true for connectors ~5 ft apart
        // when tol=0.1, which is wildly wrong for our use case. DistanceTo is
        // unambiguous.
        private const double OriginTolFt = 0.1;

        public bool IsKnown(ElementId id) =>
            _nearEndOrigin.ContainsKey(id.ToIdValue());

        public int Count => _nearEndOrigin.Count;

        /// <summary>Returns every (partId, nearConnectorOrigin) entry in the map.</summary>
        public IEnumerable<KeyValuePair<long, XYZ>> Entries => _nearEndOrigin;

        public bool IsNearEnd(ElementId id, Connector c)
        {
            if (!_nearEndOrigin.TryGetValue(id.ToIdValue(), out var near)) return false;
            return c.Origin.DistanceTo(near) <= OriginTolFt;
        }

        public bool IsFarEnd(ElementId id, Connector c)
        {
            if (!_nearEndOrigin.TryGetValue(id.ToIdValue(), out var near)) return false;
            return c.Origin.DistanceTo(near) > OriginTolFt;
        }

        /// <summary>
        /// BFS the fabrication network from <paramref name="startId"/>. Each
        /// visited part records the origin of the connector through which the
        /// BFS first reached it — i.e. the "near" end relative to the start.
        /// Takes the start connector's ORIGIN (not an index) to avoid the
        /// unstable-enumeration-order trap.
        /// </summary>
        public static HangerFlowMap Build(Document doc, ElementId startId, XYZ startConnectorOrigin)
        {
            var map = new HangerFlowMap();
            if (startId == null || startId == ElementId.InvalidElementId) return map;
            if (doc.GetElement(startId) is not FabricationPart startPart) return map;
            if (startConnectorOrigin == null) return map;

            var startConns = ConnectorHelper.GetPhysicalConnectors(startPart);
            if (startConns.Count == 0) return map;

            // Find the start connector by ORIGIN match (this enumeration may
            // differ from earlier ones — that's exactly why we don't trust
            // indices).
            int startNear = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < startConns.Count; i++)
            {
                double d = startConns[i].Origin.DistanceTo(startConnectorOrigin);
                if (d < bestD) { bestD = d; startNear = i; }
            }
            if (startNear < 0 || bestD > OriginTolFt) startNear = 0;
            // bestD here is already DistanceTo (computed above) — fine.
            map._nearEndOrigin[startPart.Id.ToIdValue()] = startConns[startNear].Origin;

            var queue = new Queue<FabricationPart>();
            queue.Enqueue(startPart);

            while (queue.Count > 0)
            {
                var part = queue.Dequeue();
                var conns = ConnectorHelper.GetPhysicalConnectors(part);

                for (int i = 0; i < conns.Count; i++)
                {
                    var c = conns[i];
                    bool connected = false;
                    try { connected = c.IsConnected; } catch { }
                    if (!connected) continue;

                    foreach (Connector other in c.AllRefs)
                    {
                        if (other == null) continue;
                        if (other.Owner is not FabricationPart neighbor) continue;
                        if (neighbor.Id == part.Id) continue;
                        if (map._nearEndOrigin.ContainsKey(neighbor.Id.ToIdValue())) continue;
                        // Skip hangers — they're branches off the run, not part
                        // of the flow path between pipes/fittings, and they
                        // would otherwise eat into the "covered" count and
                        // possibly route the BFS into a dead-end.
                        bool isHanger = false;
                        try { isHanger = neighbor.IsAHanger(); } catch { }
                        if (isHanger) continue;

                        // The neighbor's near connector is the one whose origin
                        // matches the connection point. Store the ORIGIN, not
                        // an index — index lookups suffer from unstable
                        // ConnectorManager enumeration order.
                        map._nearEndOrigin[neighbor.Id.ToIdValue()] = other.Origin;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return map;
        }
    }
}
