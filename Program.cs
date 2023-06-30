using Azure;
using Azure.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebPubSub.AspNetCore;
using Microsoft.Azure.WebPubSub.Common;
using Microsoft.Extensions.Azure;
using Microsoft.VisualBasic;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Sample_ChatApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebPubSub(
    o => o.ServiceEndpoint = new ServiceEndpoint(builder.Configuration["Azure:WebPubSub:ConnectionString"]))
    .AddWebPubSubServiceClient<Sample_ChatApp>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();

app.UseEndpoints(endpoints =>
{
    endpoints.MapGet("/negotiate", async (WebPubSubServiceClient<Sample_ChatApp> serviceClient, HttpContext context) =>
    {
        var id = context.Request.Query["id"];
        if (id.Count != 1)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("missing user id");
            return;
        }
        await context.Response.WriteAsync(serviceClient.GetClientAccessUri(userId: id).AbsoluteUri);
    });
    endpoints.MapGet("/creategroup", async (WebPubSubServiceClient<Sample_ChatApp> serviceClient, HttpContext context) =>
    {

        try
        {
            var groupName = context.Request.Query["groupName"];
            var firstUser = context.Request.Query["firstUser"];
            var secondUser = context.Request.Query["secondUser"];
            //if (await serviceClient.GroupExistsAsync(groupName))
            //{
            var response = await serviceClient.AddUserToGroupAsync(groupName, firstUser);
            var response1 = await serviceClient.AddUserToGroupAsync(groupName, secondUser);
            MessageFormat format = new MessageFormat();
            format.MessageType = "G";
            format.From = firstUser;
            format.To = secondUser;
            format.GroupName = groupName;
            format.Message = "hi welcome you all to the group";
            var response3 = await serviceClient.SendToGroupAsync(groupName, JsonSerializer.Serialize(format));
            //  }
            await context.Response.WriteAsync("Ok");
        }
        catch (Exception ex)
        {
            await context.Response.WriteAsync(JsonSerializer.Serialize(ex));
        }

    });


    endpoints.MapWebPubSubHub<Sample_ChatApp>("/eventhandler/{*path}");
});

app.Run();

sealed class Sample_ChatApp : WebPubSubHub
{
    private readonly WebPubSubServiceClient<Sample_ChatApp> _serviceClient;

    IDictionary<string, string> userConnections = new Dictionary<string, string>();


    public Sample_ChatApp(WebPubSubServiceClient<Sample_ChatApp> serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public override async Task OnConnectedAsync(ConnectedEventRequest request)
    {
        MessageFormat format = new MessageFormat();
        format.MessageType = "C";
        format.Message = $"{request.ConnectionContext.UserId} joined.";
        format.UserConnections = userConnections;
        await _serviceClient.SendToAllAsync(JsonSerializer.Serialize(format));
    }

    public override async ValueTask<UserEventResponse> OnMessageReceivedAsync(UserEventRequest request, CancellationToken cancellationToken)
    {

        var data = JsonSerializer.Deserialize<MessageFormat>(request.Data);
        if (data != null)
        {
            if (data.MessageType == "G")
            {
                await _serviceClient.SendToAllAsync($"{request.Data}");
            }
        }
        else
        {
            MessageFormat format = new MessageFormat();
            format.UserConnections = userConnections;
            format.Message = request.Data.ToString();
            await _serviceClient.SendToAllAsync(JsonSerializer.Serialize(format));

        }

        return request.CreateResponse($"");
    }

    public override async Task OnDisconnectedAsync(DisconnectedEventRequest request)
    {
        userConnections.Remove(request.ConnectionContext.UserId);
        await base.OnDisconnectedAsync(request);
    }

    public override async ValueTask<ConnectEventResponse> OnConnectAsync(ConnectEventRequest request, CancellationToken cancellationToken)
    {

        userConnections.TryAdd(request.ConnectionContext.UserId, request.ConnectionContext.ConnectionId);
        return await base.OnConnectAsync(request, cancellationToken);

    }

    public string GetAllUsers()
    {
        return string.Join(',', userConnections.Keys);
    }

    public class MessageFormat
    {
        public string From { get; set; }
        public string To { get; set; }

        public IDictionary<string, string> UserConnections { get; set; }

        public string MessageType { get; set; }

        public string Message { get; set; }

        public string GroupName { get; set; }
    }


}
