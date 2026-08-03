using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using BugTracker.Api;
using BugTracker.Api.Audit;
using BugTracker.Api.Auth;
using BugTracker.Api.Bugs;
using BugTracker.Api.Database;
using BugTracker.Api.Notifications;
using BugTracker.Api.Projects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BugTracker.Api.Tests;

public sealed partial class BugEndpointsIntegrationTests
{
    private async Task<HttpClient> CreateAuthorizedClientAsync(string userId)
    {
        var client = _factory.CreateClient();
        var token = await _factory.CreateAccessTokenAsync(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> CreateProjectAsync(HttpClient adminClient, string visibility)
    {
        var response = await adminClient.PostAsJsonAsync("/api/projects", new
        {
            name = $"{visibility} project {Guid.NewGuid().ToString("N")[..8]}",
            visibility
        });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return body?["projectId"]?.GetValue<string>() ?? throw new InvalidOperationException("Project id was missing.");
    }

    private static ByteArrayContent ImageContent(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    private static async Task SendWebSocketJsonAsync(WebSocket socket, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private static async Task<JsonObject> ReceiveWebSocketJsonAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("WebSocket closed before a JSON message was received.");
            }

            memory.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var json = Encoding.UTF8.GetString(memory.ToArray());
        return JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("WebSocket message was not JSON.");
    }

    private static byte[] TinyPngBytes()
    {
        return CreatePngBytes(1, 1);
    }

    private static string TinyPngDataUrl => $"data:image/png;base64,{Convert.ToBase64String(TinyPngBytes())}";

    private static byte[] PngHeaderWithDimensions(int width, int height)
    {
        var bytes = new byte[24];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndianInt32(bytes, 16, width);
        WriteBigEndianInt32(bytes, 20, height);
        return bytes;
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.Black);
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private static void WriteBigEndianInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)((value >> 24) & 0xFF);
        bytes[offset + 1] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 3] = (byte)(value & 0xFF);
    }
}
