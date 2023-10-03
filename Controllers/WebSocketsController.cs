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
            await Echo(webSocket, game, id);
        }
        else
        {
            HttpContext.Response.StatusCode = BadRequest;
        }
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

    private async Task Echo(WebSocket webSocket, Game game, int gameId)
    {
        // get player info...
        
        // add player
        
        int playerId = game.AddPlayer(new Player("",webSocket));
        
    }
}