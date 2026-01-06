using OllamaSharp.Models;
using OpenAI;
using OpenAI.Models;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SQLStudio.Core.AI;

public class OpenAiModelService
{
    public async Task<List<string>> GetModelsAsync(
        string apiKey, 
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        var models = new List<string>();
        models.Add("auto");
        try
        {
            OpenAIClient client;
            
            if (!string.IsNullOrEmpty(baseUrl))
            {
                var options = new OpenAIClientOptions
                {
                    Endpoint = new Uri(baseUrl)
                };
                client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
            }
            else
            {
                client = new OpenAIClient(apiKey);
            }

            var modelClient = client.GetOpenAIModelClient();
            var result = await modelClient.GetModelsAsync(cancellationToken);
            foreach (var model in result.Value)
            {
                models.Add(model.Id);
            }
            
            models.Sort();
        }
        catch (Exception ex)
        {
            
        }
        
        return models;
    }

}
