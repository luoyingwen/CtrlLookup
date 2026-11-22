using CtrlLookup.Dictionary;

if (args.Length != 3 || args[0] is not ("english" or "chinese") || args[2] is not ("--install" or "--replace"))
{
    Console.Error.WriteLine("Usage: CtrlLookup.DictionarySetup english|chinese <source-file> --install|--replace");
    return 2;
}

try
{
    var replace = args[2] == "--replace";
    var chinese = args[0] == "chinese";
    var path = chinese ? OfflineDictionary.ChinesePath : OfflineDictionary.DefaultPath;
    var count = chinese ? ChineseDictionaryBuilder.Build(args[1], path, replace) : DictionaryBuilder.Build(args[1], path, replace);
    Console.WriteLine($"Installed {count:N0} entries ({new FileInfo(path).Length / (1024.0 * 1024):F1} MiB): {path}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Dictionary setup failed: {ex.Message}");
    return 1;
}
