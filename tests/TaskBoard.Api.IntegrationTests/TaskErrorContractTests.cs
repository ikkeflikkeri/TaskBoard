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

/// <summary>
/// The shape of every error the API returns, observed over HTTP.
/// </summary>
/// <remarks>
/// The point of these tests is that a client can parse an error the same way
/// regardless of which layer rejected the request. Handler validation and a
/// malformed request envelope are the two paths most likely to drift apart,
/// so they are asserted against each other rather than against literals.
/// Existing success, validation, and concurrency tests in
/// <see cref="TaskEndpointsTests"/> are not duplicated here.
/// </remarks>
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

        // A body a client can act on: this is what the ticket calls for,
        // replacing the bare Results.NotFound() empty payload.
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Update_returns_a_documented_body_when_the_task_is_missing()
    {
        using var client = _fixture.Factory.CreateClient();

        // Valid input so the request reaches the missing-task branch
        // rather than failing validation first.
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

        // The envelope itself could not be read, so no field name is
        // truthful here. The key is documented as generic rather than
        // guessing at 'title' or 'version'.
        var errors = root.GetProperty("errors");
        var messages = errors.GetProperty("request");

        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.True(messages.GetArrayLength() > 0);
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

        // A client can read either response with one code path: the same
        // media type, the same members, and an errors map in both. Only the
        // keys inside errors differ, which the README explains.
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

    [Fact]
    public async Task An_unhandled_exception_does_not_expose_internals()
    {
        using var factory = ThrowingEndpointFactory.Start();

        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/throw-sensitive-detail");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        // What a developer would write into a bug report, and anything that
        // could carry a credential, must never reach the wire.
        //
        // Verified non-vacuous: re-pointing Program.cs at the
        // argument-taking UseExceptionHandler(ExceptionHandlerOptions)
        // overload makes all five assertions below fail, because that
        // overload writes the message, stack trace, and handler path into
        // the body. The no-argument overload does not.
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

        // Take the task to version 2 so the next update is genuinely stale.
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

        // A conflict is not a field error, so it carries no errors map.
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task An_unsupported_method_is_a_documented_problem_body()
    {
        using var client = _fixture.Factory.CreateClient();

        // The path exists, but not for DELETE.
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