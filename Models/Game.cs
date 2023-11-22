using System.Collections;
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
    private Dictionary<int, Queue<SocketReceive>> PlayerReceiveQueue = new();
    private Stack<Card> Mazzo;
    private Thread start;
    private uint gameId;
    private int userDisconnected;
    private Card Briscola;
    private Stack<(Card card, int player)> Table;
    private bool Stopped = false;

    public Game(Settings settings, uint GameId)
    {
        gameId = GameId;
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
        var Mazzo_tmp = new Card[(int)gameMode == 3 ? 39 : 40];
        for (byte i = 0, j = 1, k = 0; i < Mazzo_tmp.Length; i++, j++) { // populate mazzo
            var family = (CardFamilies)k;
            if (!((int)gameMode == 3 && j == 2 && family == CardFamilies.Coppe))
            {
                Mazzo_tmp[i] = new(j, family);
                if (j == 10)
                {
                    j = 0;
                    ++k;
                }
            }
            else
            {
                i--;
            }
        }
        rnd.Shuffle<Card>(Mazzo_tmp); // shuffle the mazzo 
        foreach (var m in Mazzo_tmp)
        {
            Mazzo.Push(m);
        }
    }

    public async Task PlayerReconnectSend(WebSocket ws, int playerId, WebSocketReceiveResult wsr)
    {
        await WebSocketsController.SendWSMessage(ws, new
        {
            Status = "YourId",
            Id = playerId
        }, wsr);

        await WebSocketsController.SendWSMessage(ws, GetPlayerInfo(playerId), wsr);

        DTOCard[] cs = new DTOCard[players[playerId]!.Cards.Count];
        for (int i = 0; i < cs.Length; ++i)
        {
            cs[i] = new DTOCard()
            {
                Family = players[playerId]!.Cards[i].GetCardFamily(),
                Number = players[playerId]!.Cards[i].GetCardNumber()
            };
        }
        await WebSocketsController.SendWSMessage(ws, new
        {
            Status = "YourCard",
            Cards = cs
        }, wsr);
    }
    
    public DTOPlayerInfo GetPlayerInfo(int playerId)
    {
        PlayerCardCnt[] pCard = new PlayerCardCnt[players.Length]; 
        for (int i = 0; i < players.Length; i++)
        {
            Card? c = null;
            foreach (var card in Table)
            {
                if (card.player == i)
                {
                    c = card.card;
                    break;
                }
            }
            pCard[i] = new PlayerCardCnt
            {
                CardsNumber = players[i]!.Cards.Count,
                PlayerName = players[i]!.Name,
                PlayerId = i,
                DropCard = c is not null ? new DTOCard()
                {
                    Family = c.GetCardFamily(),
                    Number = c.GetCardNumber()
                } : null
            };
        }
        
        return new DTOPlayerInfo
        {
            PlayerName = players[playerId]!.Name,
            PlayerId = playerId,
            CardsNumber = players[playerId]!.Cards.Count,
            MazzoCount = players[playerId]!.MazzoCount(),
            PlayerPoints = players[playerId]!.GetMazzoPoints(),
            Players = pCard,
            Briscola = new DTOCard()
            {
                Family = this.Briscola.GetCardFamily(),
                Number = this.Briscola.GetCardNumber()
            }
        };
    }

    public async Task AddWS(WebSocket webSocket, int playerId, WebSocketReceiveResult result)
    {
        byte[] buffer;

        while (!result.CloseStatus.HasValue)
        {
            try
            {
                buffer = new byte[1024 * 4];
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                Console.WriteLine("Message received from Client");

                // on receive logic
                players[playerId]!.SocketReceiveResult = result;
            }
            catch (Exception e)
            {
                Console.WriteLine("Ws Closed");
                PlayerDisconnect(playerId);
                return;
            }
            
            try
            {
                var msg = JsonSerializer.Deserialize<SocketReceive>(WebSocketsController.BufferToString(buffer));

                if (msg is not null)
                {
                    switch (msg.Status)
                    {
                        case "info":
                            await WebSocketsController.SendWSMessage(webSocket, GetPlayerInfo(playerId), result);
                            break;
                        case "picked":
                            PlayerReceiveQueue[playerId].Enqueue(msg);
                            break;
                        case "drop":
                            //Console.WriteLine($"drop: {msg.Card.Family},{msg.Card.Number}");
                            PlayerReceiveQueue[playerId].Enqueue(msg);
                            break;
                        default:
                            await WebSocketsController.SendWSMessage(webSocket, new
                            {
                                Error = "Non Valid Status"
                            }, result);
                            break;
                    }

                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        
        await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        Console.WriteLine( "WebSocket connection closed");
        
        PlayerDisconnect(playerId);
    }
    
    private async Task<Card> DropCardUser(int playerId)
    {

        await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new { Status = "drop" }, players[playerId].SocketReceiveResult);
        
        await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new
        {
            Status = "Msg",
            Value = "Devi lanciare una carta"
        }, players[playerId].SocketReceiveResult);
        
        Console.WriteLine("Message sent to Client");

        DTOCard? msg = null;
        WebSocketReceiveResult result;
        
        
        bool redo;
        int indx;
        do
        {
            do
            {
                redo = false;
                if (PlayerReceiveQueue[playerId].Count == 0)
                {
                    redo = true;
                }
                else
                {
                    SocketReceive sok = PlayerReceiveQueue[playerId].Dequeue();
                    if (sok.Status != "drop")
                    {
                        await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new
                        {
                            Error = "Not Allowed Action"
                        }, players[playerId].SocketReceiveResult);
                        redo = true;
                    }
                    else
                    {
                        // OK
                        msg = sok.Card;
                    }
                }

                if (redo)
                {
                    // wait 1 sec and retry request
                    Console.WriteLine("dorpp");
                    await Task.Delay(1000);
                    
                    if (Stopped) throw new Exception("Close");
                    
                    if (players[playerId]!.Mode == PlayerModes.UserDisconnected)
                    {
                        return DropCardBot(playerId);
                    }
                }
            } while (redo);

            indx = -1;
            redo = true;
            foreach (var playerCard in players[playerId].Cards)
            {
                indx++;
                if (playerCard.Equals(msg))
                {
                    redo = false;
                    break;
                }
            }

            if (redo)
            {
                await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new
                {
                    Error = "Not Valid Card"
                }, players[playerId].SocketReceiveResult);
            }
        } while (redo);
        
        Card ret = players[playerId].Cards.ElementAt(indx);
        players[playerId].Cards.RemoveAt(indx);
        return ret;
    }
    
    private async Task PickCardsUser(Stack<Card> mazzo, int cards, int playerId)
    {
        
        await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new
        {
            Status = "pick",
            CardsNumber = cards
        }, players[playerId].SocketReceiveResult);

        await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new
        {
            Status = "Msg",
            Value = "É il tuo turno di pescare"
        }, players[playerId].SocketReceiveResult);
        
        Console.WriteLine("Message sent to Client pp");

        // string msg;
        // WebSocketReceiveResult result;
        
        // do
        // {
        //     var tr = player.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        //     Console.WriteLine("Message received from Client");
        //     //tr.Start();
        //     tr.Wait();
        //     result = tr.Result;
        //
        //     if (result.CloseStatus.HasValue)
        //     {
        //         // socket crash
        //         throw new Exception("Client crash");
        //     }
        //
        //     msg = WebSocketsController.BufferToString(buffer);
        // } while (msg.Trim() != "picked");
        
        bool redo;
        do
        {
            redo = false;
            if (PlayerReceiveQueue[playerId].Count == 0)
            {
                redo = true;
            }
            else
            {
                SocketReceive sok = PlayerReceiveQueue[playerId].Dequeue();
                if (sok.Status != "picked")
                {
                    await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new
                    {
                        Error = "Not Allowed Action"
                    }, players[playerId].SocketReceiveResult);
                    redo = true;
                }
            }
            if (redo)
            {
                // wait 1 sec and retry request
                await Task.Delay(1000);
                Console.WriteLine("picked");
                
                if(Stopped) throw new Exception("stop");
                
                if (players[playerId]!.Mode == PlayerModes.UserDisconnected)
                {
                    PickCardBot(mazzo, cards, playerId);
                    return;
                }
            }
        } while (redo);
        
        /* Card Logic */
        object[] picked = new object[cards];
        for (int i = 0; i < cards; i++)
        {
            Card c = mazzo.Pop();
            players[playerId]!.Cards.Add(c);

            picked[i] = new DTOCard()
            {
                Family = c.GetCardFamily(),
                Number = c.GetCardNumber()
            };
        }
        
        await WebSocketsController.SendWSMessage(players[playerId].WebSocket, new
        {
            Status = "Cards",
            Cards = picked
        }, players[playerId].SocketReceiveResult);
        
        Console.WriteLine("Message sent to Client");
    }

    private Card DropCardBot(int playerId)
    {
        Thread.Sleep(1500);
        var rnd = new Random();
        
        switch (this.difficulty)
        {
            case Difficulty.Hard:
                // to implement
            case Difficulty.Extreme:
                // to implement
            default: // Easy
                int indx = rnd.Next(0, players[playerId]!.Cards.Count);
                Card ret = players[playerId]!.Cards.ElementAt(indx);
                players[playerId]!.Cards.RemoveAt(indx);
                return ret;
        }
    }

    private void PickCardBot(Stack<Card> mazzo, int cards, int playerId)
    {
        for (int i = 0; i < cards; i++)
        {
            Card c = mazzo.Pop();
            players[playerId]!.Cards.Add(c);
        }
    }

    private async void Start()
    {
        try
        {
            const int NoPlayer = -1;

            {
                PlayerCardCnt[] ps = new PlayerCardCnt[players.Length];
                for (int i = 0; i < players.Length; i++)
                {
                    ps[i] = new PlayerCardCnt
                    {
                        CardsNumber = players[i]!.Cards.Count,
                        PlayerName = players[i]!.Name,
                        PlayerId = i
                    };
                }

                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i]!.Mode != PlayerModes.User) continue;

                    await WebSocketsController.SendWSMessage(players[i]!.WebSocket, new
                    {
                        Status = "YourId",
                        Id = i
                    }, players[i]!.SocketReceiveResult);

                    await WebSocketsController.SendWSMessage(players[i]!.WebSocket, new
                    {
                        Status = "playerList",
                        Players = ps
                    }, players[i]!.SocketReceiveResult);
                }
            }

            //  lascia la briscola sul tavoloù
            Table = new();
            Card tmp = Briscola = Mazzo.Pop();
            Table.Push((tmp, NoPlayer));

            for (int i = 0; i < players.Length; i++)
            {
                if (players[i]!.Mode != PlayerModes.User) continue;
                await WebSocketsController.SendWSMessage(players[i]!.WebSocket, new
                {
                    Status = "briscola",
                    Card = new DTOCard
                    {
                        Family = Briscola.GetCardFamily(),
                        Number = Briscola.GetCardNumber()
                    }
                }, players[i]!.SocketReceiveResult);
            }

            byte NCards = 3;
            int OlpPlayerTable = 0;
            bool exit = false;
            while (!exit)
            {
                // maziere distribuische carte a tutti
                if (Mazzo.Count == (int)gameMode - 1)
                {
                    // ultima mano
                    Mazzo.Push(Briscola);
                    // var m = Mazzo.ToArray();

                    for (int i = 0; i < players.Length; i++)
                    {
                        if(players[i]!.Mode != PlayerModes.User) continue;
                        await WebSocketsController.SendWSMessage(players[i]!.WebSocket, new
                        {
                            Status = "BriscolaInMazzo"
                        }, players[i]!.SocketReceiveResult);
                    }
                    
                    for (byte i = 0; i < players.Length; ++i)
                    {
                        // players[i]!.Cards.Add(m[i]);
                        if (players[i]!.Mode == PlayerModes.User)
                            await this.PickCardsUser(Mazzo, 1, i);
                        else
                        {
                            this.PickCardBot(Mazzo, 1, i);
                        }

                        for (int j = 0; j < players.Length; j++)
                        {
                            if (j == i || players[j]!.Mode != PlayerModes.User) continue;
                            await WebSocketsController.SendWSMessage(players[j]!.WebSocket, new
                            {
                                Status = "playerPick",
                                PlayerId = i,
                                NCards = 1
                            }, players[j]!.SocketReceiveResult);
                        }
                    }

                    //exit = true;
                }
                else if(Mazzo.Count != 0)
                {
                    for (byte i = 0; i < players.Length; ++i)
                    {
                        // for(byte j = 0; j < NCards; j++) players[i]!.Cards.Add(Mazzo.Pop());
                        if (players[i]!.Mode == PlayerModes.User)
                            await this.PickCardsUser(Mazzo, NCards, i);
                        else
                        {
                            this.PickCardBot(Mazzo, NCards, i);
                        }

                        for (int j = 0; j < players.Length; j++)
                        {
                            if (j == i || players[j]!.Mode != PlayerModes.User) continue;
                            await WebSocketsController.SendWSMessage(players[j]!.WebSocket, new
                            {
                                Status = "playerPick",
                                PlayerId = i,
                                NCards = NCards
                            }, players[j]!.SocketReceiveResult);
                        }
                    }
                }

                NCards = 1;
                
                
                for (var i = OlpPlayerTable; i < players.Length; i++)
                {
                    //PrintTable(Table);
                    Card c;
                    if (players[i]!.Mode == PlayerModes.User)
                        c = await this.DropCardUser(i);
                    else
                    {
                        c = this.DropCardBot(i);
                    }

                    Table.Push((c, i));

                    for (int j = 0; j < players.Length; j++)
                    {
                        if (j == i || players[j]!.Mode != PlayerModes.User) continue;
                        await WebSocketsController.SendWSMessage(players[j]!.WebSocket, new
                        {
                            Status = "playerDrop",
                            PlayerId = i,
                            Card = new DTOCard
                            {
                                Family = c.GetCardFamily(),
                                Number = c.GetCardNumber()
                            }
                        }, players[j]!.SocketReceiveResult);
                    }

                    if (OlpPlayerTable != 0)
                    {
                        if (i == OlpPlayerTable - 1) i++;
                        else if (i == OlpPlayerTable)
                        {
                            i = -1;
                        }
                    }
                }

                Thread.Sleep(1500);

                byte[] points = new byte[(int)gameMode];

                foreach (var i in Table.AsEnumerable().Reverse())
                {
                    Console.WriteLine($"Card: {i.card.GetCardFamily()},{i.card.GetCardNumber()},{i.player}");
                }
                
                Stack<(int Player, Card Card)>? WithBriscola = null;

                CardFamilies? comanda = null;
                Stack<(int Player, Card Card)>? WithComanda = null;
                
                foreach (var card in Table.AsEnumerable().Reverse())
                {
                    if (card.player == NoPlayer) continue;
                    comanda ??= card.card.GetCardFamily();
                    players[card.player]!.TurnBriscola = card.card.family == Briscola.family;
                    if (players[card.player]!.TurnBriscola)
                    {
                        WithBriscola ??= new();
                        WithBriscola.Push((card.player, card.card));
                    }

                    if (card.card.family == comanda)
                    {
                        WithComanda ??= new();
                        WithComanda.Push((card.player, card.card));
                    }

                    players[card.player]!.PointsInGame = card.card.ValueInGame;
                }

                int max = 0;
                if (WithBriscola is null)
                {
                    max = WithComanda.ElementAt(0).Player;
                    foreach (var p in WithComanda.AsEnumerable().Reverse())
                    {
                        if (p.Card.ValueInGame > players[max]!.PointsInGame) max = p.Player;
                    }
                    // for (int i = 0; i < players.Length; i++)
                    // {
                    //     if (players[i]!.PointsInGame > players[max]!.PointsInGame) max = i;
                    // }
                }
                else
                {
                    max = WithBriscola.ElementAt(0).Player;
                    foreach (var p in WithBriscola.AsEnumerable().Reverse())
                    {
                        if (p.Card.ValueInGame > players[max]!.PointsInGame) max = p.Player;
                    }
                }
                
                Console.WriteLine($"Player {players[max]!.Name}, ha preso le carte");
                OlpPlayerTable = max;
                for (int i = 0; i < (exit ? Table.Count : (int)gameMode); ++i)
                {
                    players[max]!.PushMazzo(Table.Pop().card);
                }

                if (players[max]!.Mode == PlayerModes.User)
                {
                    await WebSocketsController.SendWSMessage(players[max]!.WebSocket, new
                    {
                        Status = "pickTableCards"
                    }, players[max]!.SocketReceiveResult);
                    await WebSocketsController.SendWSMessage(players[max]!.WebSocket, new
                    {
                        Status = "Points",
                        Value = players[max]!.GetMazzoPoints()
                    }, players[max]!.SocketReceiveResult);
                }

                for (int i = 0; i < players.Length; i++)
                {
                    if (i == max || players[i]!.Mode != PlayerModes.User) continue;

                    await WebSocketsController.SendWSMessage(players[i]!.WebSocket, new
                    {
                        Status = "pickedTableCards",
                        Player = i
                    }, players[i]!.SocketReceiveResult);
                }
                
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i].Cards.Count == 0)
                    {
                        Console.WriteLine(players[i].Name);
                        exit = true; // exit while
                        break; // break the for
                    }
                }
            }

            int winnerId = 0;
            for (byte i = 0; i < players.Length; ++i)
            {
                if (players[winnerId]!.GetMazzoPoints() < players[i]!.GetMazzoPoints())
                {
                    winnerId = i;
                }
            }

            if (players[winnerId]!.Mode == PlayerModes.User)
                await WebSocketsController.SendWSMessage(players[winnerId]!.WebSocket, new
                {
                    Status = "YouWin"
                }, players[winnerId]!.SocketReceiveResult);

            for (int i = 0; i < players.Length; ++i)
            {
                if (i == winnerId) continue;
                if (players[i]!.Mode == PlayerModes.User)
                    await WebSocketsController.SendWSMessage(players[i]!.WebSocket, new
                    {
                        Status = "WinnerIs",
                        PlayerId = winnerId,
                        Name = players[winnerId]!.Name
                    }, players[i]!.SocketReceiveResult);
            }

            for (int i = 0; i < players.Length; ++i)
            {
                if (players[i]!.Mode == PlayerModes.User)
                    await players[i]!.WebSocket!.CloseAsync(WebSocketCloseStatus.Empty, null, CancellationToken.None);
            }

            CloseGame();

        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

    private void CloseGame()
    {
        start.Interrupt();
        Stopped = true;
        Console.WriteLine("interrupted");
        GameGenerationController.CloseGameId(gameId);
    }
    
    public int AddPlayer(Player player)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is null)
            {
                players[i] = player;
                
                PlayerReceiveQueue.Add(i, new Queue<SocketReceive>());
                
                if (players[i].Mode == PlayerModes.User) usersToWait--;
                if (usersToWait == 0)
                {
                    // game start...
                    
                    //Console.WriteLine($"WebSocket:{players[i-1].WebSocket.State}");
                    
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("STARTTTT!!");
                    Console.ResetColor();
                    
                    start = new Thread(this.Start);
                    start.Start();
                }
                else
                {
                    Console.WriteLine($"Player entered, waiting for {usersToWait}");
                }
                
                
                return i;
            }
        }

        throw new Exception("Game is full");
    }

    public void PlayerDisconnect(int index)
    {
        userDisconnected++;
        if (userDisconnected == userNumber)
        {
            Console.WriteLine(userDisconnected + " " + userNumber);
            CloseGame();
            return;
        }
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

    public int PlayerReconnect(WebSocket webSocket, WebSocketReceiveResult wsr)
    {
        userDisconnected--;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is not null && players[i]!.Mode == PlayerModes.UserDisconnected)
            {
                players[i]!.Mode = PlayerModes.User;
                players[i]!.WebSocket = webSocket;
                players[i]!.SocketReceiveResult = wsr;
                return i;
            }
        }

        throw new Exception("No players to be reconnected");
    }
}