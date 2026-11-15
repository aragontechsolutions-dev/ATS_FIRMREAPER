namespace FirmReaper.Core.Profiles;

/// <summary>
/// Todos los perfiles disponibles: los integrados y, opcionalmente, los de una carpeta externa
/// ("perfiles" junto al programa). Permite añadir modelos sin recompilar.
/// </summary>
public sealed class ProfileRegistry
{
    public const string ExternalFolderName = "perfiles";

    public IReadOnlyList<Profile> Profiles { get; }
    /// <summary>Perfiles externos rechazados y el motivo.</summary>
    public IReadOnlyList<string> Errors { get; }

    private ProfileRegistry(List<Profile> profiles, List<string> errors)
    {
        Profiles = profiles;
        Errors = errors;
    }

    /// <param name="externalFolder">Carpeta de perfiles externos, o null para usar solo los integrados.</param>
    public static ProfileRegistry Load(string? externalFolder)
    {
        var builtin = ProfileLoader.LoadBuiltin();
        var errors = new List<string>();
        var all = new List<Profile>(builtin);
        if (externalFolder is not null)
            all.AddRange(ProfileLoader.LoadFolder(externalFolder, builtin.Select(p => p.Id).ToHashSet(), errors));
        return new ProfileRegistry(all, errors);
    }

    /// <summary>Carpeta "perfiles" junto al ejecutable.</summary>
    public static string DefaultExternalFolder() => Path.Combine(AppContext.BaseDirectory, ExternalFolderName);

    /// <summary>Perfiles aplicables a un original (por su SHA-256).</summary>
    public IReadOnlyList<Profile> ForOriginal(string sha256) =>
        Profiles.Where(p => p.OriginalSha256 == sha256).ToList();

    public Profile? ById(string id) => Profiles.FirstOrDefault(p => p.Id == id);
}
