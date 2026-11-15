/// <summary>
/// Logaritmo natural de un ENTERO positivo, idéntico bit a bit al log() de glibc (el que usó Python
/// para generar los firmwares) y DETERMINISTA en cualquier plataforma.
///
/// Motivo: Zopfli usa log() en su modelo de costes. Math.Log de .NET llama a la biblioteca matemática
/// del sistema, y la de Windows (ucrt) difiere en el último bit de la de Linux (glibc) en algunos enteros
/// (comprobado en GitHub Actions); con eso la salida comprimida podría cambiar.
///
/// Cálculo: aritmética "doble-doble" (~106 bits) con solo sumas, restas, productos y divisiones IEEE
/// (deterministas, sin FMA) y redondeo al double más cercano (= valor correctamente redondeado).
/// glibc NO redondea correctamente en 57 enteros de 1..2 100 000 (error ≤ 0,52 ULP): esos valores se
/// toman de <see cref="GlibcExceptions"/>. Resultado: igual a glibc en todo ese rango (ver pruebas).
/// Fuera de él (no ocurre con bloques de 1 MB) devuelve el valor correctamente redondeado.
/// </summary>
internal static class ExactLog
{
    /// <summary>Límite del rango comprobado contra glibc.</summary>
    public const ulong GlibcCheckedLimit = 2_100_000;

    /// <summary>Enteros donde glibc 2.x no da el valor correctamente redondeado → bits de glibc.</summary>
    private static readonly Dictionary<ulong, ulong> GlibcExceptions = new()
    {
        [9170] = 0x40223F54A1C504C2UL, [136837] = 0x4027A7310000E6DCUL, [141614] = 0x4027B8C2AF4020B4UL,
        [147674] = 0x4027CE36E0406E44UL, [277862] = 0x402911DBC61C360AUL, [278555] = 0x402913224425EBD0UL,
        [330034] = 0x402969F579DCBCE5UL, [351497] = 0x40298A37BB861BC9UL, [372772] = 0x4029A84E4870D692UL,
        [394915] = 0x4029C5D99B43B024UL, [416542] = 0x4029E125F05BCAF8UL, [504274] = 0x402A43020DF84723UL,
        [507328] = 0x402A4619767DEC97UL, [557003] = 0x402A75ED46E9DC16UL, [622817] = 0x402AAF1BACABCC2BUL,
        [658711] = 0x402ACBCBEBF411E8UL, [664679] = 0x402AD06A1A308867UL, [672295] = 0x402AD63F6855B26EUL,
        [699095] = 0x402AEA42EF418599UL, [721368] = 0x402AFA51B71E01D4UL, [822630] = 0x402B3D92F484EEAAUL,
        [832164] = 0x402B43794CA3A015UL, [843741] = 0x402B4A8C31F83980UL, [900741] = 0x402B6C04A86B888AUL,
        [1006553] = 0x402BA4E2B6718129UL, [1070557] = 0x402BC472F7A848A9UL, [1071347] = 0x402BC4D3A77822D0UL,
        [1083589] = 0x402BCAA4E310DEA1UL, [1096588] = 0x402BD0BFE702747FUL, [1106346] = 0x402BD54916D4960CUL,
        [1147879] = 0x402BE82782F86E32UL, [1154500] = 0x402BEB195DE1E4A6UL, [1203295] = 0x402C004B42751F1DUL,
        [1225733] = 0x402C09C0DE2F2CFBUL, [1229685] = 0x402C0B66CA1F976DUL, [1243783] = 0x402C113CF1F6D447UL,
        [1303354] = 0x402C2930F0625B92UL, [1313338] = 0x402C2D1927CBF88AUL, [1358636] = 0x402C3E75B6009BE7UL,
        [1383131] = 0x402C479BC5DBA700UL, [1392511] = 0x402C4B11AA4E455DUL, [1393959] = 0x402C4B99E3B114A7UL,
        [1441959] = 0x402C5CEF4B1BB039UL, [1512573] = 0x402C7569CA05EE18UL, [1513253] = 0x402C75A4B388C300UL,
        [1574769] = 0x402C8A0B85877653UL, [1670159] = 0x402CA827E59D64E4UL, [1713756] = 0x402CB5597112A5E4UL,
        [1735226] = 0x402CBBB950D75DEBUL, [1782331] = 0x402CC96FFFF6CD0AUL, [1878149] = 0x402CE43F8A5C5817UL,
        [1908407] = 0x402CEC6E5A2AFC01UL, [1959253] = 0x402CF9E4D05844FDUL, [1965771] = 0x402CFB9823397C19UL,
        [2048974] = 0x402D10D1AE4CFD52UL, [2054466] = 0x402D123088230497UL, [2066629] = 0x402D153639D10EF5UL,
    };

    // ln(2) en doble-doble: hi + lo.
    private const double Ln2Hi = 6.93147180559945286227e-01;   // 0x3FE62E42FEFA39EF
    private const double Ln2Lo = 2.31904681384629955842e-17;   // 0x3C7ABC9E3B39803F
    private const double Sqrt2 = 1.4142135623730951;

    private readonly struct DD(double hi, double lo)
    {
        public readonly double Hi = hi;
        public readonly double Lo = lo;
    }

    // ---- Operaciones exactas sin FMA (Knuth / Dekker) ----

    private static DD TwoSum(double a, double b)
    {
        double s = a + b;
        double bb = s - a;
        double e = (a - (s - bb)) + (b - bb);
        return new DD(s, e);
    }

    private static DD QuickTwoSum(double a, double b)
    {
        double s = a + b;
        double e = b - (s - a);
        return new DD(s, e);
    }

    private static DD Split(double a)
    {
        const double Splitter = 134217729.0; // 2^27 + 1
        double t = Splitter * a;
        double hi = t - (t - a);
        return new DD(hi, a - hi);
    }

    private static DD TwoProd(double a, double b)
    {
        double p = a * b;
        var sa = Split(a);
        var sb = Split(b);
        double e = ((sa.Hi * sb.Hi - p) + sa.Hi * sb.Lo + sa.Lo * sb.Hi) + sa.Lo * sb.Lo;
        return new DD(p, e);
    }

    private static DD Add(DD a, DD b)
    {
        var s = TwoSum(a.Hi, b.Hi);
        var t = TwoSum(a.Lo, b.Lo);
        double e = s.Lo + t.Hi;
        var u = QuickTwoSum(s.Hi, e);
        e = t.Lo + u.Lo;
        return QuickTwoSum(u.Hi, e);
    }

    private static DD Mul(DD a, DD b)
    {
        var p = TwoProd(a.Hi, b.Hi);
        double e = p.Lo + (a.Hi * b.Lo + a.Lo * b.Hi);
        return QuickTwoSum(p.Hi, e);
    }

    private static DD Mul(DD a, double b)
    {
        var p = TwoProd(a.Hi, b);
        double e = p.Lo + a.Lo * b;
        return QuickTwoSum(p.Hi, e);
    }

    private static DD Div(DD a, DD b)
    {
        double q1 = a.Hi / b.Hi;
        var r = Add(a, Neg(Mul(b, q1)));
        double q2 = r.Hi / b.Hi;
        r = Add(r, Neg(Mul(b, q2)));
        double q3 = r.Hi / b.Hi;
        var q = QuickTwoSum(q1, q2);
        return Add(q, new DD(q3, 0));
    }

    private static DD Neg(DD a) => new(-a.Hi, -a.Lo);

    /// <summary>ln(n) para n ≥ 1, igual al log() de glibc.</summary>
    public static double Log(ulong n) =>
        GlibcExceptions.TryGetValue(n, out ulong bits) ? BitConverter.Int64BitsToDouble((long)bits) : CorrectlyRounded(n);

    /// <summary>ln(n) para n ≥ 1, correctamente redondeado.</summary>
    public static double CorrectlyRounded(ulong n)
    {
        if (n == 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 1) return 0.0;

        // n = 2^k · m con m en [√½, √2): reduce el argumento de la serie.
        int k = 63 - System.Numerics.BitOperations.LeadingZeroCount(n);
        double m = (double)n / Math.ScaleB(1.0, k); // exacto: n < 2^53 en todos los usos
        if (m > Sqrt2)
        {
            m /= 2;
            k++;
        }

        // ln(m) = 2·atanh(s), s = (m−1)/(m+1), |s| < 0,172 → la serie converge rápido.
        var s = Div(new DD(m - 1.0, 0), new DD(m + 1.0, 0));
        var s2 = Mul(s, s);
        var term = s;
        var sum = s;
        for (int j = 1; j <= 40; j++)
        {
            term = Mul(term, s2);
            var add = Div(term, new DD(2 * j + 1, 0));
            if (Math.Abs(add.Hi) < 1e-40) break;
            sum = Add(sum, add);
        }
        var result = Add(Mul(sum, 2.0), Mul(new DD(Ln2Hi, Ln2Lo), k));
        // Normalizado: Hi es el double más cercano a Hi + Lo.
        return result.Hi + result.Lo;
    }
}
