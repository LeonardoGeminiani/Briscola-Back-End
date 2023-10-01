using Briscola_Back_End.Models;
using Microsoft.AspNetCore.Mvc;

namespace Briscola_Back_End.Controllers;

[ApiController]
[Route("[controller]")]
public class GameGenerationController : ControllerBase
{
    private const int StartGameId = 1;

    private const int EndGameId = 100;
    
    private static readonly Game?[] GameIds = new Game[EndGameId + 1 - StartGameId];

    private readonly ILogger<GameGenerationController> _logger;

    public GameGenerationController(ILogger<GameGenerationController> logger)
    {
        _logger = logger;
    }

    [HttpGet("/CreateGame")]
    public IActionResult CreateGame()
    {
        // Console.WriteLine();
        // foreach (var game in GameIds)
        // {
        //     Console.WriteLine($"{game.Date} {game.Status}");
        // }

        for (uint i = 0; i < GameIds.Length; ++i)
        {
            if (GameIds[i] is null)
            {
                GameIds[i] = new Game(/* pass game settings */);
                // GameIds[i].Date = DateTime.Now;
                return Ok((i + StartGameId).ToString());
            }
        }

        return StatusCode(StatusCodes.Status406NotAcceptable, "No More Game ID");
    }

    public static void CheckGameIdStatus(uint minutes)
    {
        for (uint i = 0; i < GameIds.Length; ++i)
        {
            if(GameIds[i] is null || GameIds[i]!.Socked) continue;
            if (DateTime.Now.Subtract(GameIds[i]!.Date).Minutes >= minutes)
            {
                GameIds[i] = null;
            }
        }
    }

    // public static bool IdIsFree(uint id)
    // {
    //     try
    //     {
    //         return (GameIds[id - StartGameId] is null);
    //     }
    //     catch
    //     {
    //         return false;
    //     }
    // }

    public static Game? GetGame(int id)
    {
        try
        {
            return GameIds[id - StartGameId];
        }
        catch
        {
            return null;
        }
    }
    
    public static void CloseGameId(int id)
    {
        id -= StartGameId;
        if(id < 0 || id >= GameIds.Length) throw new ArgumentOutOfRangeException(nameof(id));
        
        if (GameIds[id] is not null && !GameIds[id]!.Socked)
            throw new Exception("you can't close a non socked Game before timeout");
        GameIds[id] = null;
    }
}