using FirmReaper.Core.MStar;

namespace FirmReaper.Core.Analysis;

public enum CheckStatus
{
    Ok,
    Advertencia,
    Error,
}

/// <summary>Resultado de una comprobación individual del contenedor.</summary>
public sealed record Check(string Id, string Title, CheckStatus Status, string Detail);

/// <summary>
/// Resultado (solo lectura) del análisis de un .BIN. Nunca contiene datos modificados.
/// </summary>
public sealed class FirmwareAnalysis
{
    public required string FilePath { get; init; }
    public required long FileSize { get; init; }
    public required string Sha256 { get; init; }
    public AppHeader? Header { get; init; }
    public uint? ComputedCrc { get; init; }
    public TelTable? Tel { get; init; }
    public ImgInfo? ImgInfo { get; init; }
    /// <summary>Offset en flash donde termina el deflate (sin la cola).</summary>
    public int? DeflateEnd { get; init; }
    /// <summary>Cola de 6 bytes tras el deflate (u32 tamaño + 2 bytes desconocidos).</summary>
    public byte[]? Tail { get; init; }
    public int? AppSize { get; init; }
    /// <summary>Bytes libres entre el fin de la app y el primer recurso TEL.</summary>
    public long? FreeBytes { get; init; }
    public string? BoardId { get; init; }
    public string? SbootTag { get; init; }
    public required IReadOnlyList<Check> Checks { get; init; }

    /// <summary>App descomprimida (para etapas posteriores). Null si el contenedor no es válido.</summary>
    internal byte[]? App { get; init; }

    /// <summary>True si ninguna comprobación terminó en error.</summary>
    public bool ContainerValid => Checks.All(c => c.Status != CheckStatus.Error);
}
