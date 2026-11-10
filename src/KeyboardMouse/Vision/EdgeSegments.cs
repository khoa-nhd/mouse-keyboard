namespace KeyboardMouse.Vision;

/// <summary>
/// A thin horizontal or vertical run of edge pixels, collapsed across adjacent
/// rows so one border becomes one segment instead of three.
/// </summary>
public readonly record struct EdgeSegment(int Start, int End, int Position)
{
    public int Length => End - Start + 1;

    public int Center => Start + (Length / 2);
}

/// <summary>
/// Line extraction from an edge map. Rectangles, underlined links and tab
/// strips are all built from these segments, so the work happens once per
/// frame instead of once per detector.
/// </summary>
public static class EdgeSegments
{
    public static List<EdgeSegment> Horizontal(GrayImage edges, int minLength = 10, int threshold = 128) =>
        Extract(edges, minLength, threshold, horizontal: true);

    public static List<EdgeSegment> Vertical(GrayImage edges, int minLength = 10, int threshold = 128) =>
        Extract(edges, minLength, threshold, horizontal: false);

    private static List<EdgeSegment> Extract(GrayImage edges, int minLength, int threshold, bool horizontal)
    {
        int width = horizontal ? edges.Width : edges.Height;
        int height = horizontal ? edges.Height : edges.Width;
        var runs = new List<Run>(256);
        var byRow = new List<int>[height];

        for (int row = 0; row < height; row++)
        {
            byRow[row] = [];
            int start = -1;
            int end = -1;

            for (int i = 0; i < width; i++)
            {
                bool on = horizontal ? edges[i, row] >= threshold : edges[row, i] >= threshold;

                if (on)
                {
                    if (start < 0)
                    {
                        start = i;
                    }

                    end = i;
                }
                else if (start >= 0)
                {
                    // A single antialiased pixel can break a solid border; treat
                    // a one pixel gap as part of the same run.
                    bool bridge = i + 1 < width &&
                                  (horizontal ? edges[i + 1, row] : edges[row, i + 1]) >= threshold;

                    if (!bridge)
                    {
                        if (end - start + 1 >= minLength)
                        {
                            byRow[row].Add(runs.Count);
                            runs.Add(new Run(start, end, row));
                        }

                        start = -1;
                        end = -1;
                    }
                }
            }

            if (start >= 0 && end - start + 1 >= minLength)
            {
                byRow[row].Add(runs.Count);
                runs.Add(new Run(start, end, row));
            }
        }

        var parent = new int[runs.Count];

        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        // Runs inside a row are produced left to right, so an adjacent-row join
        // can be a two-pointer sweep instead of a cross product. Text-heavy rows
        // have hundreds of runs each; the quadratic version spent seconds here.
        for (int row = 0; row + 1 < height; row++)
        {
            var upper = byRow[row];
            var lower = byRow[row + 1];
            int i = 0, j = 0;

            while (i < upper.Count && j < lower.Count)
            {
                Run a = runs[upper[i]];
                Run b = runs[lower[j]];
                int overlap = Math.Min(a.End, b.End) - Math.Max(a.Start, b.Start) + 1;

                if (overlap > 0 && overlap >= 0.6 * Math.Min(a.Length, b.Length))
                {
                    Union(parent, upper[i], lower[j]);
                }

                if (a.End < b.End)
                {
                    i++;
                }
                else
                {
                    j++;
                }
            }
        }

        var groups = new Dictionary<int, (int Start, int End, int Top, int Bottom)>();

        for (int i = 0; i < runs.Count; i++)
        {
            int root = Find(parent, i);
            var run = runs[i];

            if (groups.TryGetValue(root, out var group))
            {
                groups[root] = (
                    Math.Min(group.Start, run.Start),
                    Math.Max(group.End, run.End),
                    Math.Min(group.Top, run.Row),
                    Math.Max(group.Bottom, run.Row));
            }
            else
            {
                groups[root] = (run.Start, run.End, run.Row, run.Row);
            }
        }

        var result = new List<EdgeSegment>(groups.Count);

        foreach (var group in groups.Values)
        {
            int thickness = group.Bottom - group.Top + 1;

            if (thickness > 4)
            {
                continue;
            }

            int length = group.End - group.Start + 1;
            if (length < minLength)
            {
                continue;
            }

            result.Add(new EdgeSegment(group.Start, group.End, (group.Top + group.Bottom) / 2));
        }

        result.Sort((a, b) => b.Length.CompareTo(a.Length));
        return result;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);

        if (rootA != rootB)
        {
            parent[rootB] = rootA;
        }
    }

    private readonly record struct Run(int Start, int End, int Row)
    {
        public int Length => End - Start + 1;
    }
}
