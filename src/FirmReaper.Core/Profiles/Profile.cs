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
    /// <summary>Opcional: tabla código → función del mando, editable por el usuario.</summary>
    public ProfileKeyTable? TablaMando { get; init; }
    /// <summary>Opcional: código de cliente del mando, editable por el usuario.</summary>
    public ProfileClientCode? ClienteMando { get; init; }
}

public sealed class ProfileKeyTable
{
    /// <summary>"lista8" (LG, por defecto), "lista4" (Konka) o "directa256" (ATEC). Ver docs/PERFILES.md.</summary>
    public string? Formato { get; init; }
    /// <summary>Dirección (RAM) de la tabla.</summary>
    public required string Direccion { get; init; }
    /// <summary>lista8/lista4: número de entradas (incluido el terminador). No se usa en directa256.</summary>
    public int? Entradas { get; init; }
    /// <summary>Solo lista8: valor que se suma al código del botón en cada entrada (u32), p. ej. "0x0100".</summary>
    public string? PrefijoCodigo { get; init; }
    /// <summary>
    /// Tabla ligada a un parche: opción y valor cuyo parche (misma dirección y longitud) es la tabla.
    /// Sin ellos, la tabla es la del firmware original ("original") y se edita en su sitio.
    /// </summary>
    public string? Opcion { get; init; }
    public string? Valor { get; init; }
    /// <summary>Tabla en su sitio: bytes originales en hexadecimal (se comprueban al generar).</summary>
    public string? Original { get; init; }
    /// <summary>Texto para el usuario en el editor (p. ej. cómo actúa la cruceta).</summary>
    public string? Nota { get; init; }
    public required List<ProfileKeyFunction> Funciones { get; init; }
    public required List<ProfileKeyButton> Botones { get; init; }
}

public sealed class ProfileClientCode
{
    /// <summary>Dirección (RAM) donde está el código de cliente.</summary>
    public required string Direccion { get; init; }
    /// <summary>"bytes-invertidos" o "aeon-sfeqi" (ver docs/PERFILES.md).</summary>
    public required string Formato { get; init; }
    /// <summary>Solo aeon-sfeqi: registro de la instrucción (0–31).</summary>
    public int? Registro { get; init; }
    /// <summary>Código del original, en el orden de envío: "00 BF".</summary>
    public required string Original { get; init; }
}

public sealed class ProfileKeyFunction
{
    public required string Indice { get; init; }
    public required string Nombre { get; init; }
}

public sealed class ProfileKeyButton
{
    public required string Codigo { get; init; }
    public required string Nombre { get; init; }
}

public sealed class ProfileLogo
{
    /// <summary>Logo en un recurso de la tabla TEL: su id ("0x065E"). Excluyente con <see cref="ZonaFlash"/>.</summary>
    public string? IdTel { get; init; }
    /// <summary>
    /// Solo con <see cref="IdTel"/>: true si el recurso termina en un CRC-16 que hay que mantener (el JPEG puede
    /// ocupar todo el recurso menos esos 2 bytes). False: se rellena con ceros hasta <see cref="LongitudJpeg"/>.
    /// </summary>
    public bool Crc16 { get; init; }
    /// <summary>Logo en una zona fija de la flash (fuera de la tabla TEL). Excluyente con <see cref="IdTel"/>.</summary>
    public ProfileFlashZone? ZonaFlash { get; init; }
    /// <summary>Longitud del JPEG original (hasta FFD9 incluido).</summary>
    public required int LongitudJpeg { get; init; }
    public required int Ancho { get; init; }
    public required int Alto { get; init; }
}

public sealed class ProfileFlashZone
{
    /// <summary>Dirección de FLASH (posición en el .BIN, no de RAM) donde empieza el JPEG.</summary>
    public required string Desde { get; init; }
    /// <summary>Fin de la zona (exclusivo). El JPEG nuevo puede ocupar hasta aquí.</summary>
    public required string Hasta { get; init; }
    /// <summary>Byte con el que está (y se deja) relleno el resto de la zona, normalmente "0xFF" (flash borrada).</summary>
    public required string Relleno { get; init; }
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
    /// <summary>"zopfli" (con "iteraciones") o "zlib" (con "nivel" 4–9; idéntico a zlib 1.3).</summary>
    public required string Metodo { get; init; }
    public int? Iteraciones { get; init; }
    public int? Nivel { get; init; }
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
    /// <summary>Opcional: "girar" (180°) o "propio" (imagen del usuario). Sin el campo: el logo no cambia.</summary>
    public string? Logo { get; init; }
}

public sealed class ProfileKnownOutput
{
    public required Dictionary<string, string> Opciones { get; init; }
    public required string Sha256 { get; init; }
    public required string Archivo { get; init; }
}
