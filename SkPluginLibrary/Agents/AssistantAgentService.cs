﻿using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Experimental.Agents;
using System.Text;
using Microsoft.SemanticKernel;
using SkPluginLibrary.Agents.Models;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Azure.AI.OpenAI;
using SkPluginLibrary.Agents.Examples;
using SkPluginLibrary.Models.Hooks;

namespace SkPluginLibrary.Agents;

public class AssistantAgentService : IAsyncDisposable
{
    private static readonly List<IAgent> Agents = [];
    //public event Action<AgentMessage>? ChatMessage;
    public event Action<ChatHistory>? ChatHistoryUpdate;
    private ChatHistory _chatHistory = [];
    private AgentProxy? _chatAgent; 
    private string _currentRespondent = "Chat Agent";
    public bool IsRunning => Agents.Count > 0;
    private readonly ILogger<AssistantAgentService> _logger;
    public AssistantAgentService(ILogger<AssistantAgentService> logger)
    {
        _logger = logger;
        logger.LogInformation("Assistant Agent Service Started");
    }
    public async IAsyncEnumerable<string> ExecuteSimpleAgentChatStream(string input, AgentProxy agent, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _chatAgent = agent;
        var kernel = GenerateKernel(false, agent.Plugins);
        OpenAIPromptExecutionSettings settings = new() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions, ChatSystemPrompt = _chatAgent?.SystemPrompt ?? "Assist the user to the best of your ability with the tools available.", Temperature = 1.0 };
        await foreach (var p in ExecuteToolCallChatStream(input, kernel, settings, cancellationToken))
        {
            yield return p;
        }

        ChatHistoryUpdate?.Invoke(_chatHistory);
    }
    public async IAsyncEnumerable<string> ExecuteChatSequence(string input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Agents.Count == 0)
        {
            yield return "No agents found!";
            yield break;
        }
        
        var kernel = GenerateKernel();
        OpenAIPromptExecutionSettings settings = new() { ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions, ChatSystemPrompt = _chatAgent?.SystemPrompt ?? "Assist the user to the best of your ability with the tools available.", Temperature = 1.0 };
        await foreach (var p in ExecuteToolCallChatStream(input, kernel, settings, cancellationToken))
        {
            yield return p;
        }

        ChatHistoryUpdate?.Invoke(_chatHistory);
    
    }

    private async IAsyncEnumerable<string> ExecuteToolCallChatStream(string input, Kernel kernel, PromptExecutionSettings settings,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        _chatHistory.AddUserMessage(input);
        var sentRespondant = false;
        await foreach (var streamingChatMessageContent in chat.GetStreamingChatMessageContentsAsync(_chatHistory, settings, kernel, cancellationToken))
        {
            var update = (OpenAIStreamingChatMessageContent)streamingChatMessageContent;
            var toolCall = update.ToolCallUpdate as StreamingFunctionToolCallUpdate;
            if (toolCall?.Name is not null)
            {
                var respondant = toolCall.Name.Replace("_Ask", "");
                if (!respondant.Equals(_currentRespondent, StringComparison.InvariantCultureIgnoreCase))
                {
                    
                    yield return $"<em>{respondant}</em>:<br/> ";                   
                    _currentRespondent = respondant;
                }
            }
            var sb = new StringBuilder();
            //if (!sentRespondant)
            //{
            //    sb.AppendLine($"{_currentRespondent}: ");
            //    yield return $"<em>{_currentRespondent}</em>:<br/> ";
            //    sentRespondant = true;
            //}

            if (update.Content is not null)
            {
                sb.Append(update.Content);
                yield return update.Content;
            }
            _chatHistory.AddAssistantMessage(sb.ToString());
        }
    }

/*
    private IAgentThread? _agentThread;
*/
    //public async Task<string> ExecuteAgentThread(string input, AgentExecutionRequest agentExecutionRequest, CancellationToken cancellationToken = default)
    //{
    //    var primary = await GenerateAgent(_chatAgent?.Name ?? "Chat", _chatAgent?.Description ?? "Chat Agent", _chatAgent?.Instructions ?? "Use the tools available to respond to the user input. Take a deep breath and think step by step.");
    //    primary.Plugins.AddRange(Agents.Select(x => x.AsPlugin()));
    //    _agentThread = await primary.NewThreadAsync(cancellationToken);
    //    await _agentThread.AddUserMessageAsync(input, cancellationToken);
    //    var response = _agentThread.InvokeAsync(primary, cancellationToken: cancellationToken);
    //    var responseString = new StringBuilder();
    //    await foreach (var message in response)
    //    {
    //        responseString.AppendLine(message.Content);
    //    }
    //    return responseString.ToString();
    //}
    private Kernel GenerateKernel(bool hasAgents = true, List<KernelPlugin>? plugins = null)
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddLogging(builder => builder.AddConsole());

        var kernel = kernelBuilder.AddAIChatCompletion(AIModel.Planner).Build();
        if (hasAgents)
        {
            foreach (var agent in Agents)
            {
                kernel.Plugins.Add(agent.AsPlugin());
            }
        }
        if (plugins is not null)
        {
            kernel.Plugins.AddRange(plugins);
        }
        var functionHook = new FunctionFilterHook();
        functionHook.FunctionInvoking += HandleFunctionInvokingFilter;
        functionHook.FunctionInvoked += HandleFunctionInvokedFilter;
        return kernel;
    }

    private static async Task<IAgent> GenerateAgent(string name, string description, string? instructions = null, List<KernelPlugin>? plugins = null)
    {
        var agentBuilder = new AgentBuilder()
            .WithOpenAIChatCompletion(TestConfiguration.OpenAI.Gpt35ModelId, TestConfiguration.OpenAI.ApiKey)
            .WithName(name)
            .WithDescription(description)
            .WithInstructions(instructions ?? "");
            //.BuildAsync();
        if (plugins is not null)
        {
            agentBuilder.WithPlugins(plugins);
        }
        return Track(await agentBuilder.BuildAsync());
    }
    private void HandleFunctionInvokingFilter(object? sender, FunctionInvocationContext context)
    {
        var function = context.Function;
        Console.WriteLine($"Function Arguments: {context.Arguments.AsJson()}");
        Console.WriteLine($"Function {function.Name} Invoking");
    }
    private void HandleFunctionInvokedFilter(object? sender, FunctionInvocationContext context)
    {
        var function = context.Function;
        Console.WriteLine($"\n---------Function {function.Name} Invoked-----------\nResults:\n{context.Result.Result()}\n----------------------------");
    }
    private static IAgent Track(IAgent agent)
    {
        Console.WriteLine($"Agent {agent.Name} added");
        Agents.Add(agent);

        return agent;
    }
    private static async Task RemoveAgents()
    {
        foreach (var agent in Agents)
        {
            Console.WriteLine($"Agent {agent.Name} removed");
            await agent.DeleteAsync();
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        await RemoveAgents();
    }
}