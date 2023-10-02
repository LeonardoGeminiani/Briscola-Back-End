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

    private string BufferToString(byte[] buffer)
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
        
        int playerId = game.AddPlayer(new Player("",PlayerModes.User,
            (ref Player player) =>
            {
                 
                
            },
            (Stack<Card> mazzo, int cards, ref Player player) =>
            {
                var buffer = new byte[1024 * 4];
                
                var serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new {
                        Status = "pick",
                        CardsNumber = cards
                    }
                )); // u8 for utf-8
                
                var t = webSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                t.Start();
                t.Wait();
                
                _logger.Log(LogLevel.Information, "Message sent to Client");

                string msg;
                WebSocketReceiveResult result;
                
                do
                {
                    var tr = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    _logger.Log(LogLevel.Information, "Message received from Client");
                    tr.Start();
                    tr.Wait();
                    result = tr.Result;

                    if (result.CloseStatus.HasValue)
                    {
                        // socket crash
                        throw new Exception("Client crash");
                    }

                    msg = BufferToString(buffer);
                 } while (msg.Trim() != "picked");
                
                /* Card Logic */
                object[] picked = new object[cards];
                for (int i = 0; i < cards; i++)
                {
                    Card c = mazzo.Pop();
                    player.Cards.Add(c);

                    picked[i] = new
                    {
                        Family = (int)c.GetCardFamily(),
                        Number = c.GetCardNumber()
                    };
                }

                serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new {
                        Cards = picked
                    }
                ));
                
                t = webSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
                    WebSocketMessageType.Text, result.EndOfMessage, CancellationToken.None);
                t.Start();
                t.Wait();
                
                _logger.Log(LogLevel.Information, "Message sent to Client");
            }
        ));
        
        var buffer = new byte[1024 * 4];
        
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        _logger.Log(LogLevel.Information, "Message received from Client");

        while (!result.CloseStatus.HasValue)
        {
            // var serverMsg = Encoding.UTF8.GetBytes($"Server: Hello. You said: {BufferToString(buffer)}");
            var serverMsg = Encoding.UTF8.GetBytes(BufferToString(buffer));
            await webSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length), 
                result.MessageType, result.EndOfMessage, CancellationToken.None);
            _logger.Log(LogLevel.Information, "Message sent to Client");
            
            buffer = new byte[1024 * 4];
            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            
            _logger.Log(LogLevel.Information, "Message received from Client");

            var msg = BufferToString(buffer);

            try
            {
                // JsonSerializer.Deserialize works only with propriety no with fields 
                var p = JsonSerializer.Deserialize<Player>(msg); // to convert json string in to object; webSocket.send(JSON.stringify(obj))
                _logger.Log(LogLevel.Information, $"msg: {msg}, Player {p}");
            }
            catch
            {
                _logger.Log(LogLevel.Error, $"Fail, {msg}");
            }
            
        }
        await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        _logger.Log(LogLevel.Information, "WebSocket connection closed");
    }
}