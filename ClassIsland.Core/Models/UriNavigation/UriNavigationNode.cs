namespace ClassIsland.Core.Models.UriNavigation;

internal class UriNavigationNode(string name)
{
    public string Name { get; } = name;

    public Dictionary<string, UriNavigationNode> Children { get; } = new();

    public Action<UriNavigationEventArgs>? NavigatedAction { get; set; }

    public bool Contains(string path)
    {
        var paths = path.Split('/');
        return Contains(paths);
    }

    public bool Contains(string[] paths)
    {
        if (paths.Length <= 0)
            return false;
        if (!Children.TryGetValue(paths[0], out var node))
            return false;

        return paths.Length <= 1 || node.Contains(Slice(paths, 1));
    }

    public UriNavigationNode AddNode(string path, Action<UriNavigationEventArgs> onNavigated)
    {
        var paths = path.Split('/');
        return AddNode(paths, onNavigated);
    }

    public UriNavigationNode AddNode(string[] paths, Action<UriNavigationEventArgs> onNavigated)
    {
        switch (paths.Length)
        {
            case <= 0:
                throw new ArgumentException($"无效的路径：{paths}");
            case 1:
            {
                var node = new UriNavigationNode(paths[0])
                {
                    NavigatedAction = onNavigated
                };
                return Children[paths[0]] = node;
            }
        }

        if (!Children.ContainsKey(paths[0]))
        {
            Children[paths[0]] = new UriNavigationNode(paths[0]);
        }

        return Children[paths[0]].AddNode(Slice(paths, 1), onNavigated);
    }

    public UriNavigationNode GetNode(string path, out string[] children)
    {
        var paths = path.Split('/');
        return GetNode(paths, out children);
    }

    public UriNavigationNode GetNode(string[] paths, out string[] children)
    {
        children = [];
        if (paths.Length <= 0)
            throw new ArgumentException("给定的节点不存在。");
        children = (string[])paths.Clone();
        if (!Children.TryGetValue(paths[0], out var node))
            return this;

        return paths.Length <= 1 ? node : node.GetNode(Slice(paths, 1), out children);
    }

    private static string[] Slice(string[] paths, int start)
    {
        var length = paths.Length - start;
        if (length <= 0)
        {
            return Array.Empty<string>();
        }

        var rest = new string[length];
        Array.Copy(paths, start, rest, 0, length);
        return rest;
    }
}