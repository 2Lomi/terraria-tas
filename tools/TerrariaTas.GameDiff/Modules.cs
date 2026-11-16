using System.IO;
using Mono.Cecil;

namespace TerrariaTas.GameDiff;

internal static class Modules
{
    /// <summary>Reads an assembly as data, resolving its references from the folder it sits in.</summary>
    public static ModuleDefinition Read(string path)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        return ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
    }
}
