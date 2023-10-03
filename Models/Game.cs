using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Briscola_Back_End.Controllers;
using Briscola_Back_End.Utils;

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
    private int usersToWait;

    private Stack<Card> Mazzo;

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
        userNumber = usersToWait = settings.userNumber;

        // create bots
        for (int i = 0; i < ((int)gameMode - userNumber); i++)
        {
            players[i] = new Player($"bot {i}", PlayerModes.Ai);
        }
        
        Mazzo = new Stack<Card>();

        // Mazzo creation
        var rnd = new Random();
        var Mazzo_tmp = new Card[40];
        for (byte i = 0, j = 1, k = 0; i < Mazzo_tmp.Length; i++, j++) { // populate mazzo
            var family = (CardFamilies)k;
            if(!((int)gameMode == 3 && j == 2 && family == CardFamilies.Coppe))
                Mazzo_tmp[i] = new(j, family);
            if(j == 10) {
                j = 0;
                ++k;
            }
        }
        rnd.Shuffle<Card>(Mazzo_tmp); // shuffle the mazzo 
        foreach (var m in Mazzo_tmp) Mazzo.Push(m);
    }

    private static void SendInfoToUser(ref Player player, string infoJson)
    {
        var buffer = new byte[1024 * 4];
        
        var serverMsg = Encoding.UTF8.GetBytes(infoJson);
        
        var t = player.WebSocket!.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
            WebSocketMessageType.Text, true, CancellationToken.None);
        t.Start();
        t.Wait();
        
        Console.WriteLine("SendInfoToUser");
    }
    private static Card DropCardUser(ref Player player)
    {
        var buffer = new byte[1024 * 4];
        
        var serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new {
                Status = "drop",
            }
        )); // u8 for utf-8
        
        var t = player.WebSocket!.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
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
    private static void PickCardsUser(Stack<Card> mazzo, int cards, ref Player player)
    {
        var buffer = new byte[1024 * 4];
        
        var serverMsg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new {
                Status = "pick",
                CardsNumber = cards
            }
        )); // u8 for utf-8
        
        var t = player.WebSocket!.SendAsync(new ArraySegment<byte>(serverMsg, 0, serverMsg.Length),
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

    private void Start()
    {
        //  lascia la briscola sul tavolo
        Card Briscola;
        Card tmp = Briscola = Mazzo.Pop();
        Stack<(Card card, Player? player)> Table = new();
        Table.Push((tmp, null));

        byte NCards = 3;
        bool exit = false;
        while(!exit){
            // maziere distribuische carte a tutti
            if(Mazzo.Count == (int)gameMode-1){
                // ultima mano
                Mazzo.Push(Briscola);
                // var m = Mazzo.ToArray();
                for (byte i = 0; i < players.Length; ++i){
                    // players[i]!.Cards.Add(m[i]);
                    Game.PickCardsUser(Mazzo, 1, ref players[i]);
                }
                exit = true;
            }
            else
            {
                for (byte i = 0; i < players.Length; ++i){
                    // for(byte j = 0; j < NCards; j++) players[i]!.Cards.Add(Mazzo.Pop());
                    Game.PickCardsUser(Mazzo, NCards, ref players[i]);
                }
            }
            NCards = 1;

            for (var i = 0; i < players.Length; i++)
            {
                var j = players[i];
                //PrintTable(Table);
                if(players[i].Mode == PlayerModes.User)
                    Table.Push((Game.DropCardUser(ref players[i]), j));
            }

            byte[] points = new byte[(int)gameMode];
            
            Stack<(Player Player, Card Card)>? WithBriscola = null;
            foreach(var card in Table){
                if(card.player is null) continue;
                card.player.TurnBriscola = card.card.family == Briscola.family;
                if(card.player.TurnBriscola){
                    WithBriscola ??= new();
                    WithBriscola.Push((card.player, card.card));
                }
                card.player.PointsInGame += card.card.ValueInGame;
            }

            Player? max = null;
            if(WithBriscola is null){
                foreach(var p in players) {
                    if(max is null || p.PointsInGame > max.PointsInGame) max = p;
                }
            }else {
                foreach(var p in WithBriscola){
                    if(max is null || p.Card.ValueInGame > max.PointsInGame) max = p.Player;
                }
            }

            Console.WriteLine($"Player {max!.Name}, ha preso le carte");
            for(int i = 0; i < (exit ? Table.Count : (int)gameMode); ++i){
                max!.PushMazzo(Table.Pop().card);
            }
        }

        Player winner = players[0];
        for(byte i = 0; i < (int)gameMode; ++i){
            if(winner.GetMazzoPoints() < players[i].GetMazzoPoints()){
                winner = players[i];
            }
        }
    }
    
    public int AddPlayer(Player player)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is null)
            {
                players[i] = player;
                if (players[i].Mode == PlayerModes.User) usersToWait--;
                if (usersToWait == 0)
                {
                    // game start...
                    this.Start();
                }
                
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
            players[index]!.WebSocket = null;
            return;
        }
        
        throw new ArgumentException($"index: {nameof(index)}, not a player in this game");
    }

    public int PlayerReconnect(WebSocket webSocket)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is not null && players[i]!.Mode == PlayerModes.UserDisconnected)
            {
                players[i]!.Mode = PlayerModes.User;
                players[i]!.WebSocket = webSocket;
                return i;
            }
        }

        throw new Exception("No players to be reconnected");
    }
}