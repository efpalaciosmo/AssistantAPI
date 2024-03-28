using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json.Serialization;

#region settings up Http Client
var builder = new ConfigurationBuilder().AddUserSecrets<Program>();
var config = builder.Build();
var openIaApiKey = config["OAIAPIKEY"];

var httpClient = new HttpClient()
{
    BaseAddress = new Uri("https://api.openai.com/")
};
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openIaApiKey);
httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v1");
httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
Console.WriteLine($"Here is you APIKEY {openIaApiKey}");
#endregion

#region Get a list of all assistants
var assistants = await httpClient.GetFromJsonAsync<AssistantList>("v1/assistants");
var existingAssistant = assistants.Data.FirstOrDefault(assistant => assistant.Name == "Kimetsu no Yaiba");
Console.WriteLine($"Here is the list of currents");
#endregion

#region Define data for assistat

var assistant = new Assistant(
    "Kimetsu no Yaiba",
    "Assistant to know more aboute Kimetsu no Yaiba",
    "gpt-3.5-turbo-0125",
    """
    From now on you are an expert on the Kimetsu no Yaiba anime, your goal is answer all possible questions only related to the anime and use the anime as the source of true,
    every other source can be pass. If someone ask for anything else, answer "Sorry, I cant answer your question".
    """,
    []
);
#endregion

#region Update or create the assistant

HttpResponseMessage response = null;
if (existingAssistant is null)
{
    // The assistant does not exist yet, so wer create it
    Console.WriteLine("Creating assistant... ");
    response = await httpClient.PostAsJsonAsync("/v1/assistants", assistant);
}
else
{
    if (assistant.Name != existingAssistant.Name
        || assistant.Description != existingAssistant.Description
        || assistant.Model != existingAssistant.Model
        || assistant.Instructions != existingAssistant.Instructions)
    {
        Console.WriteLine("Updating the assistant");
        response = await httpClient.PostAsJsonAsync($"/v1/assistants/{existingAssistant.Id}", assistant);
    }
    else
    {
        Console.WriteLine("Assistant is up to date.");
    }
}

if (response != null)
{
    response.EnsureSuccessStatusCode();
    System.Environment.Exit(0);
    existingAssistant = await response.Content.ReadFromJsonAsync<Assistant>();
}
Console.WriteLine($"Assistant ID: {existingAssistant!.Id}");
#endregion

#region create thread

Console.WriteLine("Creating thread...");
var newThreadResponse =
    await httpClient.PostAsync("v1/threads", new StringContent("", Encoding.UTF8, "application/json"));
newThreadResponse.EnsureSuccessStatusCode();
var newThread = await newThreadResponse.Content.ReadFromJsonAsync<CreateThread>();
var threadId = newThread!.Id;
Console.WriteLine($"Thread ID: {threadId}");
#endregion

#region create message

var messageResponse = await httpClient.PostAsJsonAsync($"v1/threads/{threadId}/messages",
    new CreateThreadMessage(
        """
        Which is the most powerful character on The Beginning After The End manwha
        """));
messageResponse.EnsureSuccessStatusCode();

#endregion

#region Create run

var newRunResponse = await httpClient.PostAsJsonAsync(
    $"v1/threads/{threadId}/runs",
    new CreateRun(existingAssistant.Id!));
newRunResponse.EnsureSuccessStatusCode();
var newRun = await newRunResponse.Content.ReadFromJsonAsync<Run>();
var runId = newRun!.Id;
Console.WriteLine($"Run ID: {runId}");
#endregion


#region wait for status completed run
var loop = false;
do
{
    Console.WriteLine("Waiting for run to complete...");
    var max = 10;
    while (newRun.Status is not "completed" and not "requires_action" && max >= 0)
    {
        Console.WriteLine("\tChecking run status...");
        await Task.Delay(1000);
        max--;
        var runResponse = await httpClient.GetAsync($"v1/threads/{threadId}/runs/{newRun.Id}");
        Debug.Assert(runResponse != null);
        runResponse.EnsureSuccessStatusCode();
        newRun = await runResponse.Content.ReadFromJsonAsync<Run>();
        Debug.Assert(newRun != null);
        System.Console.WriteLine($"\tRun status: {newRun.Status}");
    }
    Console.WriteLine(newRun.Status);
    if (newRun.Status == "completed")
    {
        Console.WriteLine("\tListing messages of thread...");
        var messages = await httpClient.GetFromJsonAsync<MessageList>($"v1/threads/{threadId}/messages");
        Debug.Assert(messages != null);
        foreach (var m in messages.Data)
        {
            foreach (var c in m.Content)
            {
                Console.WriteLine($"\t\t{m.Role}: {c.Text.Value}");
            }
        }
        loop = true;
        break;
    }
}
while (loop);
#endregion

#region Delelte thread
Console.WriteLine("Deleting thread...");
await httpClient.DeleteAsync($"v1/threads/{threadId}");
#endregion

#region DTOs 

public record AssistantList(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] Assistant[] Data,
    [property: JsonPropertyName("first_id")] string FirstId,
    [property: JsonPropertyName("last_id")] string LastId,
    [property: JsonPropertyName("has_more")] bool HasMore
);


public record Assistant
{
    [property: JsonPropertyName("id")] public string? Id { get; set; }
    [property: JsonPropertyName("object")] public string? Object { get; set; }
    [property: JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }
    [property: JsonPropertyName("name")] public string Name { get; set; }
    [property: JsonPropertyName("description")]
    public string? Description { get; set; }
    [property: JsonPropertyName("model")] public string Model { get; set; }
    [property: JsonPropertyName("instructions")]
    public string Instructions { get; set; }
    [property: JsonPropertyName("tools")] public Tool[]? Tools { get; set; }
    [property: JsonPropertyName("file_ids")]
    public string[]? FileIds { get; set; }
    [property: JsonPropertyName("metadata")]
    public Metadata? Metadata { get; set; }
    public Assistant(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")]
        string? Description,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("instructions")]
        string Instructions,
        [property: JsonPropertyName("tools")] Tool[]? Tools)
    {
        this.Name = Name;
        this.Description = Description;
        this.Instructions = Instructions;
        this.Tools = Tools;
    }
}

public record Tool([property: JsonPropertyName("type")] string Type);
public record Metadata();

public record CreateThread(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created_at")]
    long CreatedAt,
    [property: JsonPropertyName("metadata")]
    Metadata Metadata
);

record CreateThreadMessage(
    string Content
)
{
    public string Role => "user";
}

record CreateRun(
    [property: JsonPropertyName("assistant_id")] string AssistantId
);
public record Run(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("object")] string? Object,
    [property: JsonPropertyName("created_at")]
    long CreatedAt,
    [property: JsonPropertyName("assistant_id")]
    string AssistantId,
    [property: JsonPropertyName("thread_id")]
    string ThreadId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("started_at")]
    long? StartedAt,
    [property: JsonPropertyName("expires_at")]
    long? ExpiresAt,
    [property: JsonPropertyName("cancelled_at")]
    long? CancelledAt,
    [property: JsonPropertyName("failed_at")]
    long? FailedAt,
    [property: JsonPropertyName("completed_at")]
    long? CompletedAt,
    [property: JsonPropertyName("last_error")]
    LastError? LastError,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("instructions")]
    string? Instructions,
    [property: JsonPropertyName("tools")] Tool[] Tools,
    [property: JsonPropertyName("file_ids")]
    string[]? FileIds,
    [property: JsonPropertyName("metadata")]
    Metadata? Metadata,
    [property: JsonPropertyName("usage")] Usage? Usage
);
public record LastError(string Code, string Message);
public record Usage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens
);

public record Message(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("thread_id")] string ThreadId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] Content[] Content,
    [property: JsonPropertyName("file_ids")] string[] FileIds,
    [property: JsonPropertyName("assistant_id")] string? AssistantId,
    [property: JsonPropertyName("run_id")] string? RunId,
    [property: JsonPropertyName("metadata")] Metadata Metadata
);

public record Content(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] Text Text
);

public record Text(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("annotations")] string[] Annotations
);
public record MessageList(
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("data")] Message[] Data,
    [property: JsonPropertyName("first_id")] string FirstId,
    [property: JsonPropertyName("last_id")] string LastId,
    [property: JsonPropertyName("has_more")] bool HasMore
);
#endregion