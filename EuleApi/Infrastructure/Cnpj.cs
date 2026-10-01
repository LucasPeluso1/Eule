namespace EuleApi.Infrastructure;

public static class Cnpj
{
    /// <summary>Remove máscara e valida (14 dígitos + dígitos verificadores). Lança <see cref="CnpjInvalidoException"/>.</summary>
    public static string Normalizar(string? entrada)
    {
        // Aceita a barra da máscara codificada (%2F) que alguns clientes enviam no caminho da URL.
        var digitos = new string(Uri.UnescapeDataString(entrada ?? "").Where(char.IsDigit).ToArray());

        if (digitos.Length != 14)
            throw new CnpjInvalidoException("O CNPJ deve conter 14 dígitos.");
        if (digitos.Distinct().Count() == 1 || !DigitosVerificadoresOk(digitos))
            throw new CnpjInvalidoException("CNPJ inválido: dígitos verificadores não conferem.");

        return digitos;
    }

    public static string Formatar(string cnpj14) =>
        $"{cnpj14[..2]}.{cnpj14[2..5]}.{cnpj14[5..8]}/{cnpj14[8..12]}-{cnpj14[12..]}";

    private static bool DigitosVerificadoresOk(string c)
    {
        int[] p1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] p2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        return Digito(c, p1) == c[12] - '0' && Digito(c, p2) == c[13] - '0';
    }

    private static int Digito(string c, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++) soma += (c[i] - '0') * pesos[i];
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
