using System.ComponentModel.DataAnnotations;

namespace M2Manager.Api.Endpoints;

public static class EndpointHelpers
{
    /// <summary>
    /// Walidacja atrybutami DataAnnotations. Ten sam model waliduje się po stronie Blazora
    /// (EditForm), więc komunikaty są spójne w UI i w API.
    /// </summary>
    public static bool TryValidate<T>(T model, out Dictionary<string, string[]> errors) where T : notnull
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(model, context, results, validateAllProperties: true))
        {
            errors = [];
            return true;
        }

        errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(string.Empty).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ErrorMessage ?? "Nieprawidłowa wartość.").ToArray());

        return false;
    }

    /// <summary>Skrót: waliduj albo zwróć 400 z listą błędów.</summary>
    public static IResult? ValidationProblemOrNull<T>(T model) where T : notnull =>
        TryValidate(model, out var errors) ? null : Results.ValidationProblem(errors);
}
