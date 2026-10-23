namespace ModFolderCopier.WinUI;

public sealed class FirstLevelFolderItem
{
    public FirstLevelFolderItem(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    public string Name { get; }

    public string Path { get; }

    public List<SecondLevelFolderItem> Children { get; } = [];

    public int ChildrenCount => Children.Count;
}
