using System.Collections.Generic;
using System.Linq;

// Online player ids: the host is P1, everyone else by the order they connected (Netcode client ids
// only grow). Teams stay id % 2, so the left team is Vietnamese and the right team Korean.
public static class OnlineSeats
{
    public static Dictionary<ulong, int> Assign(ulong hostClientId, IEnumerable<ulong> connectedClientIds)
    {
        var seats = new Dictionary<ulong, int> { { hostClientId, 0 } };
        foreach (var client in connectedClientIds.Where(id => id != hostClientId).OrderBy(id => id))
            seats[client] = seats.Count;
        return seats;
    }
}
