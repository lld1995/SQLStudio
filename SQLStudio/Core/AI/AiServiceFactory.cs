using System;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace SQLStudio.Core.AI;

public enum AiProvider
{
    OpenAI,
    AzureOpenAI,
    Ollama
}

public record AiServiceConfig
{
    public required AiProvider Provider { get; init; }
    public required string ApiKey { get; init; }
    public required string ModelId { get; init; }
    public string? Endpoint { get; init; }
    public string? DeploymentName { get; init; }
}

public static class AiServiceFactory
{
    public static ISqlGeneratorAgent CreateSqlGenerator(AiServiceConfig config, SqlGeneratorOptions? options = null)
    {
        var chatService = CreateChatCompletionService(config);
        return new SqlGeneratorAgent(chatService, options);
    }

    public static IChatCompletionService CreateChatCompletionService(AiServiceConfig config)
    {
        var builder = Kernel.CreateBuilder();

        switch (config.Provider)
        {
            case AiProvider.OpenAI:
                if (!string.IsNullOrEmpty(config.Endpoint))
                {
                    var httpClient = new System.Net.Http.HttpClient
                    {
                        BaseAddress = new Uri(config.Endpoint)
                    };
                    builder.AddOpenAIChatCompletion(
                        modelId: config.ModelId,
                        apiKey: config.ApiKey,
                        httpClient: httpClient);
                }
                else
                {
                    builder.AddOpenAIChatCompletion(
                        modelId: config.ModelId,
                        apiKey: config.ApiKey);
                }
                break;

            case AiProvider.AzureOpenAI:
                if (string.IsNullOrEmpty(config.Endpoint))
                    throw new ArgumentException("Endpoint is required for Azure OpenAI");
                if (string.IsNullOrEmpty(config.DeploymentName))
                    throw new ArgumentException("DeploymentName is required for Azure OpenAI");
                    
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: config.DeploymentName,
                    endpoint: config.Endpoint,
                    apiKey: config.ApiKey);
                break;

            case AiProvider.Ollama:
                if (string.IsNullOrEmpty(config.Endpoint))
                    throw new ArgumentException("Endpoint is required for Ollama");
                    
#pragma warning disable SKEXP0070
                builder.AddOllamaChatCompletion(
                    modelId: config.ModelId,
                    endpoint: new Uri(config.Endpoint));
#pragma warning restore SKEXP0070
                break;

            default:
                throw new NotSupportedException($"AI provider '{config.Provider}' is not supported.");
        }

        var kernel = builder.Build();
        return kernel.GetRequiredService<IChatCompletionService>();
    }
}
