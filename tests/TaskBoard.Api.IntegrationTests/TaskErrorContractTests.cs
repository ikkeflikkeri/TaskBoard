using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using TaskBoard.Api.Configuration;
using TaskBoard.Api.Features.Tasks;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskErrorContractTests
    : IClassFixture<TaskBoardFixture>
{
    private const string ProblemJson = "application/problem+json";

    private readonly TaskBoardFixture _fixture;

    public TaskErrorContractTests(TaskBoardFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Get_returns_a_documented_body_when_the_task_is_missing()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/tasks/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Update_returns_a_documented_body_when_the_task_is_missing()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/tasks/{Guid.CreateVersion7()}",
            new UpdateTaskRequest("Valid title", true, 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
    }

    public static TheoryData<string, string> UnreadableEnvelopes => new()
    {
        { "malformed-json", "this is not json" },
        { "json-array", "[]" },
        { "empty-body", "" },
        { "wrong-json-type", """{"title":123,"version":1}""" },
        { "non-numeric-version", """{"title":"Ok","version":"one"}""" },
    };

    [Theory]
    [MemberData(nameof(UnreadableEnvelopes))]
    public async Task An_unreadable_envelope_uses_the_same_shape_as_validation(
        string label,
        string body)
    {
        using var client = _fixture.Factory.CreateClient();

        using var content = new StringContent(
            body,
            Encoding.UTF8,
            "application/json");

        using var response = await client.PutAsync(
            $"/api/tasks/{Guid.CreateVersion7()}",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadProblemAsync(response);
        var root = problem.RootElement;

        Assert.Equal(400, root.GetProperty("status").GetInt32());

        var errors = root.GetProperty("errors");
        var messages = errors.GetProperty("request");

        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.True(messages.GetArrayLength() > 0);
    }

    [Fact]
    public async Task An_unreadable_envelope_keeps_its_trace_id()
    {
        using var client = _fixture.Factory.CreateClient();

        using var content = new StringContent(
            "not json",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PutAsync(
            $"/api/tasks/{Guid.CreateVersion7()}",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var root = (await ReadProblemAsync(response)).RootElement;

        Assert.True(
            root.TryGetProperty("traceId", out var traceId),
            "A binding failure lost its traceId, so it cannot be correlated "
            + "with the log.");

        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task A_bad_query_parameter_is_keyed_by_its_field_name()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync("/api/tasks/?pageSize=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var errors = (await ReadProblemAsync(response))
            .RootElement.GetProperty("errors");

        Assert.True(
            errors.TryGetProperty("pageSize", out var messages),
            "A bad query parameter is not keyed by its field name, so the "
            + "documented distinction between an invalid field and an "
            + "unreadable envelope does not hold.");

        Assert.Equal(JsonValueKind.Array, messages.ValueKind);

        Assert.DoesNotContain(
            "JSON",
            string.Join(" ", messages.EnumerateArray().Select(m => m.GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string, string> UnbindableQueryValues => new()
    {
        { "abc", "not a number at all" },
        { "99999999999999", "a number too large to bind" },
        { "", "an empty value" },
        { "%20", "a whitespace value" },
    };

    [Theory]
    [MemberData(nameof(UnbindableQueryValues))]
    public async Task A_query_value_the_binder_rejects_is_keyed_by_field(
        string value,
        string why)
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/tasks/?pageSize={value}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadProblemAsync(response))
            .RootElement.GetProperty("errors");

        Assert.True(
            errors.TryGetProperty("pageSize", out var messages),
            $"pageSize={value} ({why}) was not keyed by its field name.");
    }

    [Fact]
    public async Task A_repeated_query_parameter_is_keyed_by_field()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/tasks/?pageSize=2&pageSize=3");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await ReadProblemAsync(response))
            .RootElement.GetProperty("errors");

        Assert.True(
            errors.TryGetProperty("pageSize", out var messages),
            "A repeated scalar parameter is a field error, not an unreadable "
            + "body.");

        Assert.Contains(
            "once",
            string.Join(
                " ", messages.EnumerateArray().Select(m => m.GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_query_value_the_handler_rejects_keeps_its_own_message()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/tasks/?pageSize=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var messages = (await ReadProblemAsync(response))
            .RootElement.GetProperty("errors").GetProperty("pageSize");

        Assert.Contains(
            "between 1 and 100",
            string.Join(
                " ", messages.EnumerateArray().Select(m => m.GetString())),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("*/*;q=0.8, text/html")]
    [InlineData("application/xml")]
    public async Task An_error_is_problem_json_whatever_the_client_accepts(
        string accept)
    {
        using var client = _fixture.Factory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/tasks/this-path-does-not-exist");

        request.Headers.TryAddWithoutValidation("Accept", accept);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadProblemAsync(response);

        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Both_rejection_layers_produce_the_same_envelope()
    {
        using var client = _fixture.Factory.CreateClient();

        using var validation = await client.PostAsJsonAsync(
            "/api/tasks/",
            new CreateTaskRequest("   "));

        using var binding = new StringContent(
            "not json",
            Encoding.UTF8,
            "application/json");

        using var malformed = await client.PostAsync(
            "/api/tasks/",
            binding);

        Assert.Equal(
            validation.StatusCode,
            malformed.StatusCode);

        var fromValidation = await ReadProblemAsync(validation);
        var fromBinding = await ReadProblemAsync(malformed);

        foreach (var property in new[] { "type", "title", "status", "errors" })
        {
            Assert.True(
                fromValidation.RootElement.TryGetProperty(
                    property, out var expected),
                $"Handler validation is missing '{property}'.");

            Assert.True(
                fromBinding.RootElement.TryGetProperty(property, out _),
                $"Binding failure is missing '{property}'.");
        }

        Assert.Equal(
            fromValidation.RootElement.GetProperty("title").GetString(),
            fromBinding.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public async Task An_unhandled_exception_does_not_expose_internals(string accept)
    {
        using var factory = ThrowingEndpointFactory.Start();

        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", accept);

        using var response = await client.GetAsync("/throw-sensitive-detail");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(ThrowingEndpointFactory.Secret, body);
        Assert.DoesNotContain("InvalidOperationException: ", body);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("exceptionHandlerPath", body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);

        var problem = await ReadProblemAsync(response);

        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_version_conflict_is_a_documented_problem_body()
    {
        using var client = _fixture.Factory.CreateClient();

        using var create = await client.PostAsJsonAsync(
            "/api/tasks/",
            new CreateTaskRequest("Original"));

        create.EnsureSuccessStatusCode();

        var created = await create.Content.ReadFromJsonAsync<TaskResponse>();

        Assert.NotNull(created);

        var uri = $"/api/tasks/{created.Id}";

        using var first = await client.PutAsJsonAsync(
            uri,
            new UpdateTaskRequest("Accepted edit", true, created.Version));

        first.EnsureSuccessStatusCode();

        using var conflict = await client.PutAsJsonAsync(
            uri,
            new UpdateTaskRequest("Stale edit", false, created.Version));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(ProblemJson, conflict.Content.Headers.ContentType?.MediaType);

        var problem = await ReadProblemAsync(conflict);

        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("detail").GetString()));

        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task An_unsupported_method_is_a_documented_problem_body()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.DeleteAsync(
            $"/api/tasks/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadProblemAsync(response);

        Assert.Equal(405, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task An_unsupported_media_type_is_a_documented_problem_body()
    {
        using var client = _fixture.Factory.CreateClient();

        using var content = new StringContent(
            "title=not-json",
            Encoding.UTF8,
            "text/plain");

        using var response = await client.PostAsync("/api/tasks/", content);

        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);

        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadProblemAsync(response);

        Assert.Equal(415, problem.RootElement.GetProperty("status").GetInt32());
    }

    private static async Task<JsonDocument> ReadProblemAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.False(
            string.IsNullOrWhiteSpace(body),
            "Expected an error body, but the response was empty.");

        return JsonDocument.Parse(body);
    }
}
