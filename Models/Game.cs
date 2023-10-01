using System.Net.WebSockets;

namespace Briscola_Back_End.Models;

public enum BriscolaMode
{
    P2=2,
    P3=3,
    P4=4
}

public enum Difficulty
{
    Easy,
    Hard,
    Extreme
}

public class Game
{
    public DateTime Date { get; private set; }
    public bool Socked { get; private set; }
    private BriscolaMode gameMode;
    private Player?[] players;
    private Difficulty difficulty;
    private int userNumber;
    
    public Game(Settings settings)
    {
        if (settings.userNumber < 1 || 
            settings.userNumber > 4 || 
            (int)settings.briscolaMode < settings.userNumber) 
            throw new ArgumentException($"{nameof(settings.userNumber)}, not valid");
        
        Date = DateTime.Now;
        gameMode = settings.briscolaMode;
        players = new Player[(int)gameMode];
        difficulty = settings.difficulty;
        userNumber = settings.userNumber;
        
        // create bots
        for (int i = 0; i < ((int)gameMode - userNumber); i++)
        {
            players[i] = new Player($"bot {i}", PlayerModes.Ai);
        }
    }
}