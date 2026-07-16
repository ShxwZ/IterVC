namespace RadioOSC.Core.Helpers;

/// <summary>
/// Resultado simple de operación, evita usar excepciones para flujo de control
/// en operaciones que pueden fallar de forma esperable (p.ej. URL inválida).
/// </summary>
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(string error) => new(false, default, error);
}
