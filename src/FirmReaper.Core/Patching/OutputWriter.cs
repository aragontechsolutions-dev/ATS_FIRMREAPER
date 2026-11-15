using System.Security.Cryptography;
using System.Text;
using FirmReaper.Core.Reporting;

namespace FirmReaper.Core.Patching;

/// <summary>Rutas de lo que se escribió al guardar un resultado.</summary>
public sealed record SavedFiles(string Firmware, string ReportText, string ReportJson, string Backup, bool BackupCreated);

/// <summary>
/// Guarda el resultado sin poner nunca en riesgo el original:
/// - nunca sobrescribe ningún archivo existente (si el nombre existe, añade _2, _3…);
/// - escribe en un temporal, fuerza el volcado a disco, relee y compara la huella, y solo entonces renombra;
/// - guarda una copia de seguridad del original (solo lectura) en "respaldo_originales".
/// </summary>
public static class OutputWriter
{
    public const string BackupFolderName = "respaldo_originales";

    public static SavedFiles Save(BuildResult r, string inputPath, string outputDir, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(r);
        string dir = Path.GetFullPath(outputDir);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException("No existe la carpeta de salida: " + dir);
        string input = Path.GetFullPath(inputPath);

        // 1) Copia de seguridad del original (antes que nada).
        byte[] original = File.ReadAllBytes(input);
        if (Convert.ToHexStringLower(SHA256.HashData(original)) != r.InputSha256)
            throw new IOException("El archivo original cambió en disco después de analizarlo. No se guarda nada.");
        string backupDir = Path.Combine(dir, BackupFolderName);
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(input)}_{r.InputSha256[..12]}.BIN");
        bool backupCreated = false;
        if (File.Exists(backup))
        {
            if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(backup))) != r.InputSha256)
                throw new IOException("Ya existe una copia de seguridad con ese nombre y distinto contenido: " + backup);
        }
        else
        {
            WriteAtomic(backup, original);
            File.SetAttributes(backup, File.GetAttributes(backup) | FileAttributes.ReadOnly);
            backupCreated = true;
        }

        // 2) Firmware generado (nombre único; nunca el del original).
        string baseName = r.Known?.FileName ?? DefaultName(r);
        string target = UniquePath(dir, baseName);
        if (string.Equals(target, input, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La salida coincidiría con el archivo original.");
        WriteAtomic(target, r.Output);

        // 3) Informes
        string txt = UniquePath(dir, Path.GetFileName(target) + ".informe.txt");
        string json = UniquePath(dir, Path.GetFileName(target) + ".informe.json");
        var saved = new SavedFiles(target, txt, json, backup, backupCreated);
        WriteAtomic(txt, new UTF8Encoding(true).GetBytes(BuildReport.ToText(r, input, saved, now)));
        WriteAtomic(json, new UTF8Encoding(false).GetBytes(BuildReport.ToJson(r, input, saved, now)));
        return saved;
    }

    private static string DefaultName(BuildResult r)
    {
        var parts = r.Profile.Options.Select(o => $"{o.Id}-{r.Selection[o.Id]}");
        return $"{r.Profile.Id}_{string.Join("_", parts)}.BIN".Replace("__", "_", StringComparison.Ordinal);
    }

    private static string UniquePath(string dir, string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        string p = Path.Combine(dir, fileName);
        for (int i = 2; File.Exists(p); i++)
            p = Path.Combine(dir, $"{name}_{i}{ext}");
        return p;
    }

    /// <summary>Escribe en un temporal del mismo directorio, vuelca a disco, verifica y renombra (sin sobrescribir).</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        string dir = Path.GetDirectoryName(path)!;
        string tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data);
                fs.Flush(flushToDisk: true);
            }
            var back = File.ReadAllBytes(tmp);
            if (!back.AsSpan().SequenceEqual(data))
                throw new IOException("La verificación tras escribir falló: " + path);
            File.Move(tmp, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
