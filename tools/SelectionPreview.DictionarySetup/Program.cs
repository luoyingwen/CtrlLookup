using SelectionPreview.Dictionary;

if (args.Length != 2 || args[1] is not ("--install" or "--replace"))
{
    Console.Error.WriteLine("Usage: SelectionPreview.DictionarySetup <ecdict.csv> --install|--replace");
    return 2;
}

try
{
    var path = OfflineDictionary.DefaultPath;
    var count = DictionaryBuilder.Build(args[0], path, overwrite: args[1] == "--replace");
    Console.WriteLine($"Installed {count:N0} entries ({new FileInfo(path).Length / (1024.0 * 1024):F1} MiB): {path}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Dictionary setup failed: {ex.Message}");
    return 1;
}
