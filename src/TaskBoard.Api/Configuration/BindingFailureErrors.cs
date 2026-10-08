using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Primitives;

namespace TaskBoard.Api.Configuration;

/// <summary>
/// Works out which request values the framework failed to bind, so a
/// rejection can name the parameter that caused it.
/// </summary>
/// <remarks>
/// A minimal-API binding failure arrives as a bare 400 with no
/// <c>ModelState</c>, so the offending key is not handed to us the way MVC
/// hands one over. What is available on the failure path is the request's own
/// query string and the matched endpoint's declared parameters, and those two
/// are enough: a query value that does not parse as its declared type is the
/// value the framework rejected.
///
/// This is deliberately not a validation layer. Handler validation already
/// reports bad values that <em>did</em> bind — a <c>pageSize</c> of 101
/// binds successfully and is rejected by the handler, producing a proper
/// <c>HttpValidationProblemDetails</c> this never sees. This only classifies
/// failures that arrive with no field name at all.
/// </remarks>
public static class BindingFailureErrors
{
    /// <summary>
    /// Produces the errors map for a request the framework could not bind.
    /// </summary>
    /// <param name="context">The request being rejected.</param>
    /// <returns>
    /// One key per query parameter that failed to bind to its declared type,
    /// or a single <c>request</c> key when the body could not be read.
    /// </returns>
    public static Dictionary<string, string[]> From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var unparseable = UnparseableQueryValues(context);

        if (unparseable.Count > 0)
        {
            return unparseable;
        }

        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["request"] =
            [
                "The request body could not be read. Send a JSON object " +
                "matching the documented schema."
            ]
        };
    }

    /// <summary>
    /// Returns the query parameters whose raw text does not parse as the type
    /// the endpoint declared for them.
    /// </summary>
    private static Dictionary<string, string[]> UnparseableQueryValues(
        HttpContext context)
    {
        var failures = new Dictionary<string, string[]>(
            StringComparer.Ordinal);

        var query = context.Request.Query;

        if (query.Count == 0)
        {
            return failures;
        }

        var declared = DeclaredQueryParameters(context);

        if (declared.Count == 0)
        {
            return failures;
        }

        foreach (var (key, values) in query)
        {
            if (values.Count == 0
                || !declared.TryGetValue(key, out var type)
                || Parses(values, type))
            {
                continue;
            }

            // Repeated and empty are the two ways a scalar fails without the
            // text being malformed, so say which one it was.
            failures[key] = [Message(values, type)];
        }

        return failures;
    }

    /// <summary>
    /// Maps the query-bound parameter names to their declared types. The
    /// endpoint is absent when routing itself failed, and then there is
    /// nothing to bind against, so the body branch is the honest answer.
    /// </summary>
    private static Dictionary<string, Type> DeclaredQueryParameters(
        HttpContext context)
    {
        var declared = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        var method = context.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>();

        if (method is null)
        {
            return declared;
        }

        // Only simple query-bound types are of interest here. Anything bound
        // from a service, the body, or the request itself is not something the
        // query string could have caused, so it is left out rather than
        // guessed at.
        foreach (var name in context.Request.Query.Keys)
        {
            var parameter = Array.Find(
                method.GetParameters(),
                candidate => string.Equals(
                    candidate.Name, name, StringComparison.OrdinalIgnoreCase));

            if (parameter is not null && Bindable(parameter.ParameterType))
            {
                declared[name] = parameter.ParameterType;
            }
        }

        return declared;
    }

    /// <summary>
    /// Whether the binder would accept these values for the declared type.
    /// </summary>
    /// <remarks>
    /// Only scalar types are judged here, and a scalar has three ways to fail
    /// that are all measured rather than assumed: an empty value binds to
    /// nothing, a repeated parameter has no single value to bind, and text
    /// that is not the right type does not parse. A string parameter is left
    /// alone — every value binds to one, so the handler judges it instead.
    /// </remarks>
    private static bool Parses(StringValues values, Type declaredType)
    {
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (type != typeof(int))
        {
            return true;
        }

        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
        {
            return false;
        }

        return int.TryParse(
            values[0], CultureInfo.InvariantCulture, out _);
    }

    private static bool Bindable(Type declaredType)
    {
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        return type == typeof(string) || type == typeof(int);
    }

    private static string Name(Type declaredType)
    {
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        return type == typeof(int) ? "whole number" : "text";
    }

    /// <summary>
    /// Explains why the binder would reject these values, distinguishing the
    /// two failures that are not about malformed text.
    /// </summary>
    private static string Message(StringValues values, Type declaredType)
    {
        if (values.Count == 0)
        {
            return "A value is required.";
        }

        if (values.Count > 1)
        {
            return "It must be given once; this parameter takes a single "
                + "value.";
        }

        if (string.IsNullOrEmpty(values[0]))
        {
            return "A value is required.";
        }

        return $"The value '{values[0]}' is not a valid {Name(declaredType)}.";
    }
}