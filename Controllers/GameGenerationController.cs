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
    
    [HttpPost("/CreateGame")]
    public IActionResult CreateGame(Settings settings)
    {
        // settingsJson -> 
        // {
        //     "briscolaMode": 2,
        //     "userNumber": 2,
        //      "difficulty": 1
        // }
        
        for (uint i = 0; i < GameIds.Length; ++i)
        {
            if (GameIds[i] is null)
            {
                try
                {
                    if ((Settings?)settings is null)
                        return StatusCode(StatusCodes.Status500InternalServerError, "settings are required");
                    GameIds[i] = new Game(settings, i);
                }
                catch (Exception ex)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, $"not valid settings: \"{ex.Message}\"");
                }

                return Ok((i + StartGameId).ToString());
            }
        }

        return StatusCode(StatusCodes.Status406NotAcceptable, "No More Game ID");
    }

    public static void CheckGameIdStatus(uint minutes)
    {
        for (uint i = 0; i < GameIds.Length; ++i)
        {
            if(GameIds[i] is null) continue;
            if (DateTime.Now.Subtract(GameIds[i]!.Date).Minutes >= minutes)
            {
                GameIds[i] = null;
            }
        }
    }
    
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
    
    public static void CloseGameId(uint id)
    {
        if(id >= GameIds.Length) throw new ArgumentOutOfRangeException(nameof(id));
        
        if (GameIds[id] is null)
            throw new Exception("you can't close a non socked Game before timeout");
        GameIds[id] = null;
    }
}