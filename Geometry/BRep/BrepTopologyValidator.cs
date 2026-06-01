namespace CNCSS.Geometry.BRep;

public static class BrepTopologyValidator
{
    public static bool ValidateClosedShell(BrepSolid solid, out string error)
    {
        foreach (BrepFace face in solid.Faces)
        {
            if (face.Outer.First == null)
            {
                error = $"Face {face.Id} has empty loop.";
                return false;
            }

            var visited = new HashSet<int>();
            BrepHalfEdge start = face.Outer.First;
            BrepHalfEdge e = start;
            int safety = 0;
            while (true)
            {
                if (e.Start == null || e.Next == null || e.Face == null)
                {
                    error = $"Half-edge {e.Id} is not fully linked.";
                    return false;
                }

                if (!ReferenceEquals(e.Face, face))
                {
                    error = $"Half-edge {e.Id} has invalid face ref.";
                    return false;
                }

                if (e.Twin == null || e.Twin.Twin != e)
                {
                    error = $"Half-edge {e.Id} twin relation broken.";
                    return false;
                }

                if (!visited.Add(e.Id))
                {
                    error = $"Loop on face {face.Id} self-intersects by edge id.";
                    return false;
                }

                e = e.Next;
                safety++;
                if (ReferenceEquals(e, start))
                {
                    break;
                }

                if (safety > 100000)
                {
                    error = $"Face {face.Id} loop does not close.";
                    return false;
                }
            }

            if (visited.Count < 3)
            {
                error = $"Face {face.Id} has less than 3 edges.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
