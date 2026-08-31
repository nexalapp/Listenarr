/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Listenarr.Tests.Features.Api.Common;

public sealed class ServerErrorProblemDetailsFilterTests
{
    [Fact]
    public async Task Production_ReplacesLegacy500PayloadAndRedactsMessage()
    {
        var filter = new ServerErrorProblemDetailsFilter(
            new TestHostEnvironment(Environments.Production));
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-500" };
        httpContext.Request.Path = "/api/test";
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        var context = new ResultExecutingContext(
            actionContext,
            [],
            new ObjectResult(new { error = "failed", message = "secret detail" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            },
            controller: new object());

        await filter.OnResultExecutionAsync(
            context,
            () => Task.FromResult(new ResultExecutedContext(
                actionContext,
                [],
                context.Result,
                controller: new object())));

        var result = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Null(problem.Detail);
        Assert.Equal("internal_error", problem.Extensions["code"]);
        Assert.Equal("trace-500", problem.Extensions["traceId"]);
        Assert.Contains("application/problem+json", result.ContentTypes);
    }

    [Fact]
    public async Task Production_ReplacesBareServerErrorStatus()
    {
        var filter = new ServerErrorProblemDetailsFilter(
            new TestHostEnvironment(Environments.Production));
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-bare-500" };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        var context = new ResultExecutingContext(
            actionContext,
            [],
            new StatusCodeResult(StatusCodes.Status503ServiceUnavailable),
            controller: new object());

        await filter.OnResultExecutionAsync(
            context,
            () => Task.FromResult(new ResultExecutedContext(
                actionContext,
                [],
                context.Result,
                controller: new object())));

        var result = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.Equal("internal_error", problem.Extensions["code"]);
        Assert.Equal("trace-bare-500", problem.Extensions["traceId"]);
    }

    [Fact]
    public async Task Production_LeavesAnExplicitProblemDetailUntouched()
    {
        // The filter blanks the detail of any other 5xx body outside Development, so a
        // handler with a reason worth showing states it as ProblemDetails itself. Download
        // submission does exactly that: its failures name the stage and the remedy, and
        // are meant for the person who asked for the grab.
        var filter = new ServerErrorProblemDetailsFilter(
            new TestHostEnvironment(Environments.Production));
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-502" };
        httpContext.Request.Path = "/api/v1/download/send";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title = "Failed to send usenet to download client",
            Detail = "Read payload: The post was thanked but no search string could be read from it.",
            Instance = "/api/v1/download/send"
        };
        problem.Extensions["code"] = "download_submission_failed";

        var context = new ResultExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            new ObjectResult(problem) { StatusCode = StatusCodes.Status502BadGateway },
            controller: null!);

        await filter.OnResultExecutionAsync(context, () =>
            Task.FromResult(new ResultExecutedContext(
                context, [], context.Result, controller: null!)));

        var result = Assert.IsType<ObjectResult>(context.Result);
        var preserved = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(
            "Read payload: The post was thanked but no search string could be read from it.",
            preserved.Detail);
        Assert.Equal("Failed to send usenet to download client", preserved.Title);
        Assert.Equal("download_submission_failed", preserved.Extensions["code"]);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Listenarr.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
