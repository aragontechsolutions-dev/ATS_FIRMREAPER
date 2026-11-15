namespace FirmReaper.Core.Profiles;

// Modelo del archivo JSON de perfil (formato 1). Ver docs/PERFILES.md.
// Todas las direcciones son de RAM (la app descomprimida empieza en app.baseRam) y van en texto "0x…".

public sealed class ProfileFile
{
    public required int Formato { get; init; }
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string Nombre { get; init; }
    public required string Descripcion { get; init; }
    /// <summary>"verificado" (probado contra el binario) o "sin-verificar".</summary>
    public required string Estado { get; init; }
    public required ProfileOriginal Original { get; init; }
    public required ProfileApp App { get; init; }
    public required ProfileCompression Compresion { get; init; }
    public required List<ProfileZone> ZonasPermitidas { get; init; }
    public required List<ProfilePatch> ParchesFijos { get; init; }
    public required List<ProfileOption> Opciones { get; init; }
    public required List<ProfileKnownOutput> SalidasConocidas { get; init; }
    /// <summary>Opcional: recurso del logo de arranque (necesario si algún valor actúa sobre el logo).</summary>
    public ProfileLogo? Logo { get; init; }
}

public sealed class ProfileLogo
{
    /// <summary>Id del recurso en la tabla TEL ("0x065E").</summary>
    public required string IdTel { get; init; }
    /// <summary>Longitud del JPEG original (hasta FFD9). Lo que sigue en el recurso se conserva.</summary>
    public required int LongitudJpeg { get; init; }
    public required int Ancho { get; init; }
    public required int Alto { get; init; }
}

public sealed class ProfileOriginal
{
    public required string Sha256 { get; init; }
}

public sealed class ProfileApp
{
    /// <summary>Dirección de RAM del primer byte de la app descomprimida (destino RAM + 0x3800).</summary>
    public required string BaseRam { get; init; }
}

public sealed class ProfileCompression
{
    /// <summary>Solo "zopfli" en el formato 1.</summary>
    public required string Metodo { get; init; }
    public required int Iteraciones { get; init; }
}

public sealed class ProfileZone
{
    public required string Desde { get; init; }
    /// <summary>Exclusivo.</summary>
    public required string Hasta { get; init; }
    public required string Motivo { get; init; }
}

public sealed class ProfilePatch
{
    public required string Direccion { get; init; }
    public required string Original { get; init; }
    public required string Nuevo { get; init; }
    public required string Descripcion { get; init; }
}

public sealed class ProfileOption
{
    public required string Id { get; init; }
    public required string Etiqueta { get; init; }
    /// <summary>Solo "lista" en el formato 1 (una opción = elegir uno de sus valores).</summary>
    public required string Tipo { get; init; }
    public required string PorDefecto { get; init; }
    public required List<ProfileOptionValue> Valores { get; init; }
}

public sealed class ProfileOptionValue
{
    public required string Id { get; init; }
    public required string Etiqueta { get; init; }
    public required List<ProfilePatch> Parches { get; init; }
    /// <summary>Opcional: "girar" (180°) o "propio" (JPEG del usuario). Sin el campo: el logo no cambia.</summary>
    public string? Logo { get; init; }
}

public sealed class ProfileKnownOutput
{
    public required Dictionary<string, string> Opciones { get; init; }
    public required string Sha256 { get; init; }
    public required string Archivo { get; init; }
}
