using System.Globalization;
using System.Reflection;

namespace TaskBoard.Api.Configuration;

public static class BindingFailureErrors
{
    public static Dictionary<string, string[]> From(HttpContext context)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var parameters = context.GetEndpoint()?.Metadata
            .GetMetadata<MethodInfo>()?.GetParameters() ?? [];

        foreach (var parameter in parameters)
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType)
                ?? parameter.ParameterType;
            if (type != typeof(int)
                || parameter.Name is not { } name
                || !context.Request.Query.TryGetValue(name, out var values))
            {
                continue;
            }

            if (values.Count > 1)
            {
                errors[name] = ["It must be given once; this parameter takes a single value."];
            }
            else if (values.Count == 0 || string.IsNullOrEmpty(values[0]))
            {
                errors[name] = ["A value is required."];
            }
            else if (!int.TryParse(values[0], CultureInfo.InvariantCulture, out _))
            {
                errors[name] = [$"The value '{values[0]}' is not a valid whole number."];
            }
        }

        if (errors.Count == 0)
        {
            errors["request"] =
            [
                "The request body could not be read. Send a JSON object " +
                "matching the documented schema."
            ];
        }

        return errors;
    }
}
