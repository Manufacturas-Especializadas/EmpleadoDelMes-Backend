namespace EmpleadoDelMes.Domain;

internal static class DomainGuard
{
    internal static Guid RequiredId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("El identificador no puede estar vacío.", parameterName);
        }

        return value;
    }

    internal static string RequiredText(string value, string parameterName, int? maximumLength = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var text = value.Trim();
        if (maximumLength.HasValue && text.Length > maximumLength.Value)
        {
            throw new ArgumentException($"El texto no puede superar {maximumLength.Value} caracteres.", parameterName);
        }

        return text;
    }
}
