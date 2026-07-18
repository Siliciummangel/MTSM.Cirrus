using Microsoft.AspNetCore.Mvc;
using MTSM.Cirrus.Core.Exceptions;

namespace MTSM.Cirrus.API.Middleware;

public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Request {Method} {Path} was cancelled by the client.",
                context.Request.Method,
                context.Request.Path);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(
                context,
                exception);
        }
    }

    private async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception)
    {
        ProblemDetails problemDetails =
            CreateProblemDetails(
                context,
                exception);

        if (problemDetails.Status >= 500)
        {
            _logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Request {Method} {Path} failed with status code {StatusCode}.",
                context.Request.Method,
                context.Request.Path,
                problemDetails.Status);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "The response has already started. " +
                "ProblemDetails cannot be written.");

            throw exception;
        }

        context.Response.Clear();

        context.Response.StatusCode =
            problemDetails.Status
            ?? StatusCodes.Status500InternalServerError;

        context.Response.ContentType =
            "application/problem+json";

        await context.Response.WriteAsJsonAsync(
            problemDetails,
            cancellationToken: context.RequestAborted);
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext context,
        Exception exception)
    {
        int statusCode;
        string title;
        string detail;

        switch (exception)
        {
            case ArchiveObjectNotFoundException:
                statusCode =
                    StatusCodes.Status404NotFound;

                title = "Archive object not found";
                detail = exception.Message;
                break;

            case ArchiveObjectUnavailableException:
                statusCode =
                    StatusCodes.Status409Conflict;

                title = "Archive object unavailable";
                detail = exception.Message;
                break;

            case ArgumentException:
                statusCode =
                    StatusCodes.Status400BadRequest;

                title = "Invalid request";
                detail = exception.Message;
                break;

            case InvalidOperationException:
                statusCode =
                    StatusCodes.Status409Conflict;

                title = "Operation could not be completed";
                detail = exception.Message;
                break;

            case ArchiveException:
                statusCode =
                    StatusCodes.Status500InternalServerError;

                title = "Archive operation failed";

                detail =
                    "The archive operation could not be completed.";
                break;

            default:
                statusCode =
                    StatusCodes.Status500InternalServerError;

                title = "Internal server error";

                detail =
                    "An unexpected error occurred.";
                break;
        }

        return new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = context.Request.Path,
            Extensions =
            {
                ["traceId"] = context.TraceIdentifier
            }
        };
    }
}