﻿// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel.Experimental.Agents;

namespace SkPluginLibrary.Examples;

// ReSharper disable once InconsistentNaming
/// <summary>
/// Showcase usage of code_interpreter and retrieval tools.
/// </summary>
public static class Example75_AgentTools
{
    private static void WriteLine(object? target = null)
    {
        Console.WriteLine(target);
    }
    /// <summary>
    /// Specific model is required that supports agents and parallel function calling.
    /// Currently this is limited to Open AI hosted services.
    /// </summary>
    private const string OpenAIFunctionEnabledModel = "gpt-4-turbo";

    // Track agents for clean-up
    private static readonly List<IAgent> _agents = [];

    /// <summary>
    /// Show how to utilize code_interpreter and retrieval tools.
    /// </summary>
    
    public static async Task RunAsync()
    {
        WriteLine("======== Example75_AgentTools ========");

        if (TestConfiguration.OpenAI.ApiKey == null)
        {
            WriteLine("OpenAI apiKey not found. Skipping example.");
            return;
        }

        // Run agent with 'code_interpreter' tool
        await RunCodeInterpreterToolAsync();

        // Run agent with 'retrieval' tool
        await RunRetrievalToolAsync();
    }

    private static async Task RunCodeInterpreterToolAsync()
    {
        WriteLine("======== Run:CodeInterpreterTool ========");

        var builder =
            new AgentBuilder()
                .WithOpenAIChatCompletion(OpenAIFunctionEnabledModel, TestConfiguration.OpenAI.ApiKey)
                .WithInstructions("Write only code to solve the given problem without comment.");

        try
        {
            var defaultAgent =
                Track(
                    await builder.BuildAsync());

            var codeInterpreterAgent =
                Track(
                    await builder.WithCodeInterpreter().BuildAsync());

            await ChatAsync(
                defaultAgent,
                codeInterpreterAgent,
                "What is the solution to `3x + 2 = 14`?",
                "What is the fibinacci sequence until 101?");
        }
        finally
        {
            await Task.WhenAll(_agents.Select(a => a.DeleteAsync()));
        }
    }

    private static async Task RunRetrievalToolAsync()
    {
        WriteLine("======== Run:RunRetrievalTool ========");

        // REQUIRED:
        //
        // Use `curl` to upload document prior to running example and assign the
        // identifier to `fileId`.
        //
        // Powershell:
        // curl https://api.openai.com/v1/files `
        // -H "Authorization: Bearer $Env:OPENAI_APIKEY" `
        // -F purpose="assistants" `
        // -F file="@Resources/travelinfo.txt"

        var fileId = "file-pMRf5623THPy39uooHD5TpEh";

        var defaultAgent =
            await new AgentBuilder()
                .WithOpenAIChatCompletion(OpenAIFunctionEnabledModel, TestConfiguration.OpenAI.ApiKey)
                .BuildAsync();

        var retrievalAgent =
            await new AgentBuilder()
                .WithOpenAIChatCompletion(OpenAIFunctionEnabledModel, TestConfiguration.OpenAI.ApiKey)
                .WithRetrieval(fileId)
                .BuildAsync();

        try
        {
            await ChatAsync(
                defaultAgent,
                retrievalAgent,
                "Where did sam go?",
                "When does the flight leave Seattle?",
                "What is the hotel contact info at the destination?");
        }
        finally
        {
            await Task.WhenAll(_agents.Select(a => a.DeleteAsync()));
        }
    }

    /// <summary>
    /// Common chat loop used for: RunCodeInterpreterToolAsync and RunRetrievalToolAsync.
    /// Processes each question for both "default" and "enabled" agents.
    /// </summary>
    private static async Task ChatAsync(
        IAgent defaultAgent,
        IAgent enabledAgent,
        params string[] questions)
    {
        foreach (var question in questions)
        {
            WriteLine("\nDEFAULT AGENT:");
            await InvokeAgentAsync(defaultAgent, question);

            WriteLine("\nTOOL ENABLED AGENT:");
            await InvokeAgentAsync(enabledAgent, question);
        }

        static async Task InvokeAgentAsync(IAgent agent, string question)
        {
            await foreach (var message in agent.InvokeAsync(question))
            {
                string content = message.Content;
                foreach (var annotation in message.Annotations)
                {
                    content = content.Replace(annotation.Label, string.Empty, StringComparison.Ordinal);
                }

                WriteLine($"# {message.Role}: {content}");

                if (message.Annotations.Count > 0)
                {
                    WriteLine("\n# files:");
                    foreach (var annotation in message.Annotations)
                    {
                        WriteLine($"* {annotation.FileId}");
                    }
                }
            }

            WriteLine();
        }
    }

    private static IAgent Track(IAgent agent)
    {
        _agents.Add(agent);

        return agent;
    }

   
}
