﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

// This is under the NodeServices namespace because post 2.1 it will be moved to that package
namespace ElectronJsRevitAddin.Application.Utils.NodeUtils;

/// <summary>
/// Executes the <c>script</c> entries defined in a <c>package.json</c> file,
/// capturing any output written to stdio.
/// </summary>
internal class NodeScriptRunner : IDisposable
{
    private Process? _npmProcess;
    public EventedStreamReader StdOut { get; }
    public EventedStreamReader StdErr { get; }

    private static readonly Regex AnsiColorRegex = new Regex("\x001b\\[[0-9;]*m", RegexOptions.None, TimeSpan.FromSeconds(1));

    public NodeScriptRunner(string workingDirectory, string scriptName, string? arguments, IDictionary<string, string>? envVars, string pkgManagerCommand, DiagnosticSource diagnosticSource, CancellationToken applicationStoppingToken)
    {
        if (string.IsNullOrEmpty(workingDirectory))
        {
            throw new ArgumentException("Cannot be null or empty.", nameof(workingDirectory));
        }

        if (string.IsNullOrEmpty(scriptName))
        {
            throw new ArgumentException("Cannot be null or empty.", nameof(scriptName));
        }

        if (string.IsNullOrEmpty(pkgManagerCommand))
        {
            throw new ArgumentException("Cannot be null or empty.", nameof(pkgManagerCommand));
        }
         
        var completeArguments = $"run {scriptName} -- {arguments ?? string.Empty}";

        // On Windows, the node executable is a .cmd file, so it can't be executed
        // directly (except with UseShellExecute=true, but that's no good, because
        // it prevents capturing stdio). So we need to invoke it via "cmd /c".
        var exeToRun = "cmd";
        completeArguments = $"/c {pkgManagerCommand} {completeArguments}";

        var processStartInfo = new ProcessStartInfo(exeToRun)
        {
            Arguments = completeArguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };

        if (envVars != null)
        {
            foreach (var keyValuePair in envVars)
            {
                processStartInfo.Environment[keyValuePair.Key] = keyValuePair.Value;
            }
        }

        _npmProcess = LaunchNodeProcess(processStartInfo, pkgManagerCommand);
        StdOut = new EventedStreamReader(_npmProcess.StandardOutput);
        StdErr = new EventedStreamReader(_npmProcess.StandardError);

        applicationStoppingToken.Register(((IDisposable)this).Dispose);

        if (diagnosticSource.IsEnabled("Microsoft.AspNetCore.NodeServices.Npm.NpmStarted"))
        {
            diagnosticSource.Write(
                "Microsoft.AspNetCore.NodeServices.Npm.NpmStarted",
                new
                {
                    processStartInfo = processStartInfo,
                    process = _npmProcess
                });
        }
    }

    public void AttachToLogger(ILogger logger)
    {
        // When the node task emits complete lines, pass them through to the real logger
        StdOut.OnReceivedLine += line =>
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                // Node tasks commonly emit ANSI colors, but it wouldn't make sense to forward
                // those to loggers (because a logger isn't necessarily any kind of terminal)
                logger.LogInformation(StripAnsiColors(line));
            }
        };

        StdErr.OnReceivedLine += line =>
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                logger.LogError(StripAnsiColors(line));
            }
        };

        // But when it emits incomplete lines, assume this is progress information and
        // hence just pass it through to StdOut regardless of logger config.
        StdErr.OnReceivedChunk += chunk =>
        {
            Debug.Assert(chunk.Array != null);

            var containsNewline = Array.IndexOf(
                chunk.Array, '\n', chunk.Offset, chunk.Count) >= 0;
            if (!containsNewline)
            {
                Console.Write(chunk.Array, chunk.Offset, chunk.Count);
            }
        };
    }

    private static string StripAnsiColors(string line)
        => AnsiColorRegex.Replace(line, string.Empty);

    private static Process LaunchNodeProcess(ProcessStartInfo startInfo, string commandName)
    {
        try
        {
            var process = Process.Start(startInfo)!;

            // See equivalent comment in OutOfProcessNodeInstance.cs for why
            process.EnableRaisingEvents = true;

            return process;
        }
        catch (Exception ex)
        {
            var message = $"Failed to start '{commandName}'. To resolve this:.\n\n"
                        + $"[1] Ensure that '{commandName}' is installed and can be found in one of the PATH directories.\n"
                        + $"    Current PATH enviroment variable is: {Environment.GetEnvironmentVariable("PATH")}\n"
                        + "    Make sure the executable is in one of those directories, or update your PATH.\n\n"
                        + "[2] See the InnerException for further details of the cause.";
            throw new InvalidOperationException(message, ex);
        }
    }

    void IDisposable.Dispose()
    {
        if (_npmProcess is { HasExited: false })
        {
            _npmProcess.Kill();
            _npmProcess = null;
        }
    }
}