using System.Globalization;

namespace FirmReaper.Core;

/// <summary>Formato de números fijo en español (es-ES) para que los informes no dependan de la configuración de Windows.</summary>
internal static class Fmt
{
    public static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Entero con separador de miles español: 4194304 → "4.194.304".</summary>
    public static string N(long v) => v.ToString("N0", Es);
}
