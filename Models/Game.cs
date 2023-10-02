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
    private DropCard DropCardAi;
    private PickCards PickCardsAi;
    
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

        DropCardAi = ;
        PickCardsAi = ;
        
        // create bots
        for (int i = 0; i < ((int)gameMode - userNumber); i++)
        {
            players[i] = new Player($"bot {i}", PlayerModes.Ai, DropCardAi, PickCardsAi);
        }
    }
    
    public int AddPlayer(Player player)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is null)
            {
                players[i] = player;
                return i;
            }
        }

        throw new Exception("Game is full");
    }

    public void PlayerDisconnect(int index)
    {
        if (players[index] is not null)
        {
            if (players[index]!.Mode != PlayerModes.User)
            {
                throw new ArgumentException($"index: {nameof(index)}, not a User player");
            }

            players[index]!.Mode = PlayerModes.UserDisconnected;
            players[index]!.SelectDropCard = DropCardAi;
            players[index]!.PickCards = PickCardsAi;
            return;
        }
        
        throw new ArgumentException($"index: {nameof(index)}, not a player in this game");
    }

    public int PlayerReconnect(DropCard selectDropCard, 
        PickCards pickCards)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is not null && players[i]!.Mode == PlayerModes.UserDisconnected)
            {
                players[i]!.Mode = PlayerModes.User;
                players[i]!.SelectDropCard = selectDropCard;
                players[i]!.PickCards = pickCards;
                return i;
            }
        }

        throw new Exception("No players to be reconnected");
    }
}