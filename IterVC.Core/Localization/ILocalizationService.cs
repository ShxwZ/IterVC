namespace IterVC.Core.Localization;

/// <summary>
/// Idiomas soportados por la UI.
/// </summary>
public static class SupportedLanguages
{
    public const string Spanish = "Español";
    public const string English = "English";

    public static readonly IReadOnlyList<string> All = new[] { Spanish, English };
}

/// <summary>
/// Servicio de localización con cambio en tiempo real.
/// Expone un diccionario de cadenas por idioma y dispara <see cref="Changed"/>
/// cuando el idioma activo cambia — los consumidores (VM, code-behind) reevalúan
/// sus bindings en respuesta.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Idioma actualmente activo ("spanish" o "english").</summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Devuelve la traducción de <paramref name="key"/> en el idioma activo.
    /// Si no existe la clave en el idioma actual, busca en el idioma alternativo
    /// ("es" como fallback). Si sigue sin existir, devuelve la clave entre corchetes
    /// para detectar visualmente los huecos.
    /// </summary>
    string Get(string key);

    /// <summary>Cambia el idioma activo y dispara <see cref="Changed"/>.</summary>
    void SetLanguage(string lang);

    /// <summary>Se dispara tras <see cref="SetLanguage"/>. Los consumidores
    /// deben reevaluar todas sus cadenas expuestas.</summary>
    event EventHandler? Changed;
}
