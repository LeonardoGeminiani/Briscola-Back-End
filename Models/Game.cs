using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Briscola_Back_End.Controllers;

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

    private static Card dropCardUser(ref Player player)
    {
        var buffer = new byte[1024 * 4];
        
        var serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new {
                Status = "drop",
            }
        )); // u8 for utf-8
        
        var t = player.WebSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
            WebSocketMessageType.Text, true, CancellationToken.None);
        t.Start();
        t.Wait();
        
        Console.WriteLine("Message sent to Client");

        DTOCard? msg;
        WebSocketReceiveResult result;
        
        do
        {
            var tr = player.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            Console.WriteLine("Message received from Client");
            tr.Start();
            tr.Wait();
            result = tr.Result;

            if (result.CloseStatus.HasValue)
            {
                // socket crash
                throw new Exception("Client crash");
            }
            
            msg = JsonSerializer.Deserialize<DTOCard>(WebSocketsController.BufferToString(buffer));
        } while (msg is null);

        int i = -1;
        foreach (var playerCard in player.Cards)
        {
            i++;
            if (playerCard.Equals(msg))
            {
                break;
            }
        }

        if (i == -1) throw new Exception("Not Valid Card");
        
        Card ret = player.Cards.ElementAt(i);
        player.Cards.RemoveAt(i);
        return ret;
    }
    private static void pickCardsUser(Stack<Card> mazzo, int cards, ref Player player)
    {
        var buffer = new byte[1024 * 4];
        
        var serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new {
                Status = "pick",
                CardsNumber = cards
            }
        )); // u8 for utf-8
        
        var t = player.WebSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
            WebSocketMessageType.Text, true, CancellationToken.None);
        t.Start();
        t.Wait();
        
        Console.WriteLine("Message sent to Client");

        string msg;
        WebSocketReceiveResult result;
        
        do
        {
            var tr = player.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            Console.WriteLine("Message received from Client");
            tr.Start();
            tr.Wait();
            result = tr.Result;

            if (result.CloseStatus.HasValue)
            {
                // socket crash
                throw new Exception("Client crash");
            }

            msg = WebSocketsController.BufferToString(buffer);
        } while (msg.Trim() != "picked");
        
        /* Card Logic */
        object[] picked = new object[cards];
        for (int i = 0; i < cards; i++)
        {
            Card c = mazzo.Pop();
            player.Cards.Add(c);

            picked[i] = new DTOCard()
            {
                Family = c.GetCardFamily(),
                Number = c.GetCardNumber()
            };
        }

        serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new {
                Cards = picked
            }
        ));
        
        t = player.WebSocket.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
            WebSocketMessageType.Text, result.EndOfMessage, CancellationToken.None);
        t.Start();
        t.Wait();
        
        Console.WriteLine("Message sent to Client");
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
            return;
        }
        
        throw new ArgumentException($"index: {nameof(index)}, not a player in this game");
    }

    public int PlayerReconnect()
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is not null && players[i]!.Mode == PlayerModes.UserDisconnected)
            {
                players[i]!.Mode = PlayerModes.User;
                return i;
            }
        }

        throw new Exception("No players to be reconnected");
    }
}