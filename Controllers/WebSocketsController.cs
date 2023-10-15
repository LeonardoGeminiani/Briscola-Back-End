using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Briscola_Back_End.Models;
using Microsoft.AspNetCore.Mvc;

namespace Briscola_Back_End.Controllers;

[ApiController]
[Route("[controller]")]
public class WebSocketsController : ControllerBase
{
    private new const int BadRequest = ((int)HttpStatusCode.BadRequest);
    private readonly ILogger<WebSocketsController> _logger;

    public WebSocketsController(ILogger<WebSocketsController> logger)
    {
        _logger = logger;
    }

    [HttpGet("/ws/{id}")]
    public async Task Get(int id)
    {
        var game = GameGenerationController.GetGame(id);
        if(game is null)
        {
            HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }
        
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _logger.Log(LogLevel.Information, "WebSocket connection established");

            try
            {
                var buffer = new byte[1024 * 4];
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                _logger.Log(LogLevel.Information, "PlayerSettings received from Client");

                var ps = JsonSerializer.Deserialize<PlayerSettings>(BufferToString(buffer));
                if (ps is not null)
                    await Echo(webSocket, game, ps, result);
            }
            catch
            {
                HttpContext.Response.StatusCode = BadRequest;
            }
        }
        else
        {
            HttpContext.Response.StatusCode = BadRequest;
        }
    }

    public static async Task SendWSMessage(WebSocket webSocket, object message, WebSocketReceiveResult wsr)
    {
        var serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await webSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length), wsr.MessageType, wsr.EndOfMessage, CancellationToken.None);
    }

    public static string BufferToString(byte[] buffer)
    {
        var msg = "";
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == 0) break;
            msg += (char)buffer[i];
        }

        return msg;
    }

    private async Task Echo(WebSocket webSocket, Game game, PlayerSettings playerSettings, WebSocketReceiveResult wsr)
    {
        try
        {
            int playerId;
            try
            {
                playerId = game.AddPlayer(new Player(playerSettings.Name, webSocket, wsr));
            }
            catch
            {
                playerId = game.PlayerReconnect(webSocket, wsr);
            }
            await game.AddWS(webSocket, playerId, wsr);
        }
        catch (Exception e)
        {
            await WebSocketsController.SendWSMessage(webSocket, new
            {
                Error = e.Message
            }, wsr);
        }
    }
}