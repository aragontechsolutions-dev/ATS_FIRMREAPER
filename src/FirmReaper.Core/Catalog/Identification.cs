using FirmReaper.Core.Analysis;

namespace FirmReaper.Core.Catalog;

public enum IdentificationKind
{
    /// <summary>Huella en el catálogo (original, generado o referencia).</summary>
    Conocido,
    /// <summary>Huella desconocida pero contenedor MStar válido: modificado por otro o firmware no analizado.</summary>
    DesconocidoValido,
    /// <summary>Huella desconocida y contenedor inválido o dañado.</summary>
    DesconocidoInvalido,
}

public sealed record Identification(IdentificationKind Kind, KnownImage? Image, string Message);

public static class Identifier
{
    public static Identification Identify(FirmwareAnalysis analysis, FirmwareCatalog catalog)
    {
        var img = catalog.Find(analysis.Sha256);
        if (img is not null)
        {
            string msg = img.Tipo switch
            {
                ImageKind.Original => $"ORIGINAL reconocido: {img.Nombre}",
                ImageKind.Generado => $"VERSIÓN NUESTRA: {img.Nombre}" + StateText(img.EstadoPrueba),
                _ => $"REFERENCIA: {img.Nombre}",
            };
            if (!analysis.ContainerValid)
                msg += " — ATENCIÓN: la huella coincide pero el contenedor no supera las comprobaciones.";
            return new Identification(IdentificationKind.Conocido, img, msg);
        }

        if (analysis.ContainerValid)
        {
            return new Identification(IdentificationKind.DesconocidoValido, null,
                "NO RECONOCIDO: el contenedor MStar es válido, pero la huella no está en el catálogo " +
                "(modificado por otra persona o firmware aún no analizado). No se puede parchear.");
        }
        return new Identification(IdentificationKind.DesconocidoInvalido, null,
            "NO RECONOCIDO y contenedor NO válido: el archivo está dañado, no es de este formato o fue mal leído.");
    }

    private static string StateText(TestState? s) => s switch
    {
        TestState.Funciono => " (probado en el TV: funcionó)",
        TestState.NoFunciono => " (probado en el TV: NO funcionó)",
        TestState.Pendiente => " (pendiente de probar en el TV)",
        _ => "",
    };
}
