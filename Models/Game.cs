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
    private Player[] players;
    
    public Game(byte playerNumber, BriscolaMode mode)
    {
        if (playerNumber < 1 || playerNumber > 4) 
            throw new ArgumentException($"{nameof(playerNumber)}, not valid");
        
        Date = DateTime.Now;
        players = new Player[playerNumber];
        gameMode = mode;
    }
}