using System.Text.Json;

namespace AssetIQ.Api.Infrastructure;

/// <summary>An error that maps directly to an HTTP status (thrown by services, rendered as <c>{ "error": message }</c>).</summary>
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Body of every error response — the dashboard reads <c>error</c>.</summary>
public sealed record ErrorResponse(string Error);

/// <summary>Message returned (with a 500) when the action fails unexpectedly, e.g. "Failed to list assets".</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FailureMessageAttribute(string message) : Attribute
{
    public string Message { get; } = message;
}

/// <summary>
/// Renders <see cref="ApiException"/> with its status and message; anything else is logged and becomes
/// a 500 carrying the endpoint's <see cref="FailureMessageAttribute"/> text, never the exception details.
/// </summary>
public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex) when (!context.Response.HasStarted)
        {
            await WriteErrorAsync(context, ex.Status, ex.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var message = context.GetEndpoint()?.Metadata.GetMetadata<FailureMessageAttribute>()?.Message ?? "Internal server error";
            logger.LogError(ex, "{Method} {Path} failed", context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, message);
        }
    }

    public static Task WriteErrorAsync(HttpContext context, int status, string message)
    {
        context.Response.Clear();
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(message), JsonDefaults.Options);
    }
}

/// <summary>Serializer settings shared by MVC and the code that writes responses directly.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
        // Plain UTF-8 like the old Express API ("→", "•", quotes): responses are JSON, never HTML.
        options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        if (!options.Converters.OfType<UtcDateTimeJsonConverter>().Any())
        {
            options.Converters.Add(new UtcDateTimeJsonConverter());
        }
        return options;
    }
}
