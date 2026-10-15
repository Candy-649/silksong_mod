using AssetsTools.NET;
using AssetsTools.NET.Extra;

internal static class Fields {
    public static void Run(string bundlePath, AssetClassID type, int maxDepth) {
        var manager = new AssetsManager();
        var bundle = manager.LoadBundleFile(bundlePath, true);
        var instance = manager.LoadAssetsFileFromBundle(bundle, 0, false);
        foreach (var info in instance.file.GetAssetsOfType(type)) {
            var f = manager.GetBaseField(instance, info);
            Print(f, 0, maxDepth);
            break;
        }
    }

    private static void Print(AssetTypeValueField f, int depth, int maxDepth) {
        if (depth > maxDepth) return;
        var value = f.Value == null ? "" : f.Value.ValueType switch {
            AssetValueType.String => " = " + f.AsString,
            AssetValueType.ByteArray => $" = bytes[{f.AsByteArray.Length}]",
            AssetValueType.Array => "",
            _ => " = " + f.Value.AsObject
        };
        Console.WriteLine($"{new string(' ', depth * 2)}{f.FieldName} : {f.TypeName}{value}" +
                          (f.TypeName == "Array" || f.Children.Count > 0 && f.TypeName == "vector" ? $" ({f.Children.Count})" : ""));
        var shown = 0;
        foreach (var c in f.Children) {
            Print(c, depth + 1, maxDepth);
            if (f.TypeName == "Array" && ++shown >= 2) break;
        }
    }
}
