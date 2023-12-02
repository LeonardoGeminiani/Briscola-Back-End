using System.Net.WebSockets;
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
    private BriscolaMode _gameMode;
    private Player?[] _players;
    private Difficulty _difficulty;
    private int _userNumber;
    private int _usersToWait;
    private Dictionary<int, Queue<SocketReceive>> _playerReceiveQueue = new();
    private Stack<Card> _mazzo;
    private Thread _start;
    private uint _gameId;
    private int _userDisconnected;
    private Card _briscola;
    private Stack<(Card card, int player)> _table;
    private bool _stopped;

    public Game(Settings settings, uint gameId)
    {
        _gameId = gameId;
        if (settings.userNumber < 1 || 
            settings.userNumber > 4 || 
            (int)settings.briscolaMode < settings.userNumber) 
            throw new ArgumentException($"{nameof(settings.userNumber)}, not valid");
        
        Date = DateTime.Now;
        _gameMode = settings.briscolaMode;
        _players = new Player[(int)_gameMode];
        _difficulty = settings.difficulty;
        _userNumber = _usersToWait = settings.userNumber;

        // create bots
        for (int i = 0; i < ((int)_gameMode - _userNumber); i++)
        {
            _players[i] = new Player($"bot {i}", PlayerModes.Ai);
        }
        
        _mazzo = new Stack<Card>();

        // Mazzo creation
        var rnd = new Random();
        var mazzoTmp = new Card[(int)_gameMode == 3 ? 39 : 40];
        for (byte i = 0, j = 1, k = 0; i < mazzoTmp.Length; i++, j++) { // populate mazzo
            var family = (CardFamilies)k;
            if (!((int)_gameMode == 3 && j == 2 && family == CardFamilies.Coppe))
            {
                mazzoTmp[i] = new(j, family);
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
        rnd.Shuffle(mazzoTmp); // shuffle the mazzo 
        foreach (var m in mazzoTmp)
        {
            _mazzo.Push(m);
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

        DtoCard[] cs = new DtoCard[_players[playerId]!.Cards.Count];
        for (int i = 0; i < cs.Length; ++i)
        {
            cs[i] = new DtoCard()
            {
                Family = _players[playerId]!.Cards[i].GetCardFamily(),
                Number = _players[playerId]!.Cards[i].GetCardNumber()
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
        PlayerCardCnt[] pCard = new PlayerCardCnt[_players.Length]; 
        for (int i = 0; i < _players.Length; i++)
        {
            Card? c = null;
            foreach (var card in _table)
            {
                if (card.player == i)
                {
                    c = card.card;
                    break;
                }
            }
            pCard[i] = new PlayerCardCnt
            {
                CardsNumber = _players[i]!.Cards.Count,
                PlayerName = _players[i]!.Name,
                PlayerId = i,
                DropCard = c is not null ? new DtoCard()
                {
                    Family = c.GetCardFamily(),
                    Number = c.GetCardNumber()
                } : null
            };
        }
        
        return new DTOPlayerInfo
        {
            PlayerName = _players[playerId]!.Name,
            PlayerId = playerId,
            CardsNumber = _players[playerId]!.Cards.Count,
            MazzoCount = _players[playerId]!.MazzoCount(),
            PlayerPoints = _players[playerId]!.GetMazzoPoints(),
            Players = pCard,
            Briscola = new DtoCard()
            {
                Family = this._briscola.GetCardFamily(),
                Number = this._briscola.GetCardNumber()
            }
        };
    }

    public async Task AddWs(WebSocket webSocket, int playerId, WebSocketReceiveResult result)
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
                _players[playerId]!.SocketReceiveResult = result;
            }
            catch
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
                            _playerReceiveQueue[playerId].Enqueue(msg);
                            break;
                        case "drop":
                            //Console.WriteLine($"drop: {msg.Card.Family},{msg.Card.Number}");
                            _playerReceiveQueue[playerId].Enqueue(msg);
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
        await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new { Status = "drop" }, _players[playerId]!.SocketReceiveResult!);
        
        await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new
        {
            Status = "Msg",
            Value = "Devi lanciare una carta"
        }, _players[playerId]!.SocketReceiveResult!);
        
        Console.WriteLine("Message sent to Client");

        DtoCard? msg = null;
        
        bool redo;
        int indx;
        do
        {
            do
            {
                redo = false;
                if (_playerReceiveQueue[playerId].Count == 0)
                {
                    redo = true;
                }
                else
                {
                    SocketReceive sok = _playerReceiveQueue[playerId].Dequeue();
                    if (sok.Status != "drop")
                    {
                        await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new
                        {
                            Error = "Not Allowed Action"
                        }, _players[playerId]!.SocketReceiveResult!);
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
                    
                    if (_stopped) throw new Exception("Close");
                    
                    if (_players[playerId]!.Mode == PlayerModes.UserDisconnected)
                    {
                        return DropCardBot(playerId);
                    }
                }
            } while (redo);

            indx = -1;
            redo = true;
            foreach (var playerCard in _players[playerId]!.Cards)
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
                await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new
                {
                    Error = "Not Valid Card"
                }, _players[playerId]!.SocketReceiveResult!);
            }
        } while (redo);
        
        Card ret = _players[playerId]!.Cards.ElementAt(indx);
        _players[playerId]!.Cards.RemoveAt(indx);
        return ret;
    }
    
    private async Task PickCardsUser(Stack<Card> mazzo, int cards, int playerId)
    {
        
        await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new
        {
            Status = "pick",
            CardsNumber = cards
        }, _players[playerId]!.SocketReceiveResult!);

        await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new
        {
            Status = "Msg",
            Value = "É il tuo turno di pescare"
        }, _players[playerId]!.SocketReceiveResult!);
        
        Console.WriteLine("Message sent to Client pp");
        
        bool redo;
        do
        {
            redo = false;
            if (_playerReceiveQueue[playerId].Count == 0)
            {
                redo = true;
            }
            else
            {
                SocketReceive sok = _playerReceiveQueue[playerId].Dequeue();
                if (sok.Status != "picked")
                {
                    await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new
                    {
                        Error = "Not Allowed Action"
                    }, _players[playerId]!.SocketReceiveResult!);
                    redo = true;
                }
            }
            if (redo)
            {
                // wait 1 sec and retry request
                await Task.Delay(1000);
                Console.WriteLine("picked");
                
                if(_stopped) throw new Exception("stop");
                
                if (_players[playerId]!.Mode == PlayerModes.UserDisconnected)
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
            _players[playerId]!.Cards.Add(c);

            picked[i] = new DtoCard()
            {
                Family = c.GetCardFamily(),
                Number = c.GetCardNumber()
            };
        }
        
        await WebSocketsController.SendWSMessage(_players[playerId]!.WebSocket!, new
        {
            Status = "Cards",
            Cards = picked
        }, _players[playerId]!.SocketReceiveResult!);
        
        Console.WriteLine("Message sent to Client");
    }

    private Card DropCardBot(int playerId)
    {
        Thread.Sleep(1500);
        var rnd = new Random();
        
        switch (this._difficulty)
        {
            case Difficulty.Hard:
                // to implement
            case Difficulty.Extreme:
                // to implement
            default: // Easy
                int indx = rnd.Next(0, _players[playerId]!.Cards.Count);
                Card ret = _players[playerId]!.Cards.ElementAt(indx);
                _players[playerId]!.Cards.RemoveAt(indx);
                return ret;
        }
    }

    private void PickCardBot(Stack<Card> mazzo, int cards, int playerId)
    {
        for (int i = 0; i < cards; i++)
        {
            Card c = mazzo.Pop();
            _players[playerId]!.Cards.Add(c);
        }
    }

    private async void Start()
    {
        try
        {
            const int noPlayer = -1;

            {
                PlayerCardCnt[] ps = new PlayerCardCnt[_players.Length];
                for (int i = 0; i < _players.Length; i++)
                {
                    ps[i] = new PlayerCardCnt
                    {
                        CardsNumber = _players[i]!.Cards.Count,
                        PlayerName = _players[i]!.Name,
                        PlayerId = i
                    };
                }

                for (int i = 0; i < _players.Length; i++)
                {
                    if (_players[i]!.Mode != PlayerModes.User) continue;

                    await WebSocketsController.SendWSMessage(_players[i]!.WebSocket!, new
                    {
                        Status = "YourId",
                        Id = i
                    }, _players[i]!.SocketReceiveResult!);

                    await WebSocketsController.SendWSMessage(_players[i]!.WebSocket!, new
                    {
                        Status = "playerList",
                        Players = ps
                    }, _players[i]!.SocketReceiveResult!);
                }
            }

            //  lascia la briscola sul tavolo
            _table = new();
            Card tmp = _briscola = _mazzo.Pop();
            _table.Push((tmp, noPlayer));

            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i]!.Mode != PlayerModes.User) continue;
                await WebSocketsController.SendWSMessage(_players[i]!.WebSocket!, new
                {
                    Status = "briscola",
                    Card = new DtoCard
                    {
                        Family = _briscola.GetCardFamily(),
                        Number = _briscola.GetCardNumber()
                    }
                }, _players[i]!.SocketReceiveResult!);
            }

            byte nCards = 3;
            int olpPlayerTable = 0;
            bool exit = false;
            int max = 0;
            while (!exit)
            {
                // maziere distribuische carte a tutti
                if (_mazzo.Count == (int)_gameMode - 1)
                {
                    // ultima mano
                    _mazzo.Push(_briscola);
                    // var m = Mazzo.ToArray();

                    for (int i = 0; i < _players.Length; i++)
                    {
                        if(_players[i]!.Mode != PlayerModes.User) continue;
                        await WebSocketsController.SendWSMessage(_players[i]!.WebSocket!, new
                        {
                            Status = "BriscolaInMazzo"
                        }, _players[i]!.SocketReceiveResult!);
                    }
                    
                    for (int i = max; i < _players.Length; ++i)
                    {
                        if (_players[i]!.Mode == PlayerModes.User)
                            await this.PickCardsUser(_mazzo, 1, i);
                        else
                        {
                            this.PickCardBot(_mazzo, 1, i);
                        }

                        for (int j = 0; j < _players.Length; j++)
                        {
                            if (j == i || _players[j]!.Mode != PlayerModes.User) continue;
                            await WebSocketsController.SendWSMessage(_players[j]!.WebSocket!, new
                            {
                                Status = "playerPick",
                                PlayerId = i,
                                NCards = 1
                            }, _players[j]!.SocketReceiveResult!);
                        }
                    }

                }
                else if(_mazzo.Count != 0)
                {
                    for (int i = max; i < _players.Length; ++i)
                    {
                        if (_players[i]!.Mode == PlayerModes.User)
                            await this.PickCardsUser(_mazzo, nCards, i);
                        else
                        {
                            this.PickCardBot(_mazzo, nCards, i);
                        }

                        for (int j = 0; j < _players.Length; j++)
                        {
                            if (j == i || _players[j]!.Mode != PlayerModes.User) continue;
                            await WebSocketsController.SendWSMessage(_players[j]!.WebSocket!, new
                            {
                                Status = "playerPick",
                                PlayerId = i,
                                NCards = nCards
                            }, _players[j]!.SocketReceiveResult!);
                        }
                    }
                }

                nCards = 1;
                
                
                for (var i = olpPlayerTable; i < _players.Length; i++)
                {
                    Card c;
                    if (_players[i]!.Mode == PlayerModes.User)
                        c = await this.DropCardUser(i);
                    else
                    {
                        c = this.DropCardBot(i);
                    }

                    _table.Push((c, i));

                    for (int j = 0; j < _players.Length; j++)
                    {
                        if (j == i || _players[j]!.Mode != PlayerModes.User) continue;
                        await WebSocketsController.SendWSMessage(_players[j]!.WebSocket!, new
                        {
                            Status = "playerDrop",
                            PlayerId = i,
                            Card = new DtoCard
                            {
                                Family = c.GetCardFamily(),
                                Number = c.GetCardNumber()
                            }
                        }, _players[j]!.SocketReceiveResult!);
                    }

                    if (olpPlayerTable != 0)
                    {
                        if (i == olpPlayerTable - 1) i++;
                        else if (i == olpPlayerTable)
                        {
                            i = -1;
                        }
                    }
                }

                Thread.Sleep(1500);

                foreach (var i in _table.AsEnumerable().Reverse())
                {
                    Console.WriteLine($"Card: {i.card.GetCardFamily()},{i.card.GetCardNumber()},{i.player}");
                }
                
                Stack<(int Player, Card Card)>? withBriscola = null;

                CardFamilies? comanda = null;
                Stack<(int Player, Card Card)> withComanda = new Stack<(int Player, Card Card)>();
                
                foreach (var card in _table.AsEnumerable().Reverse())
                {
                    if (card.player == noPlayer) continue;
                    comanda ??= card.card.GetCardFamily();
                    _players[card.player]!.TurnBriscola = card.card.Family == _briscola.Family;
                    if (_players[card.player]!.TurnBriscola)
                    {
                        withBriscola ??= new();
                        withBriscola.Push((card.player, card.card));
                    }

                    if (card.card.Family == comanda)
                    {
                        withComanda.Push((card.player, card.card));
                    }

                    _players[card.player]!.PointsInGame = card.card.ValueInGame;
                }

                if (withBriscola is null)
                {
                    max = withComanda.ElementAt(0).Player;
                    foreach (var p in withComanda.AsEnumerable().Reverse())
                    {
                        if (p.Card.ValueInGame > _players[max]!.PointsInGame) max = p.Player;
                    }
                }
                else
                {
                    max = withBriscola.ElementAt(0).Player;
                    foreach (var p in withBriscola.AsEnumerable().Reverse())
                    {
                        if (p.Card.ValueInGame > _players[max]!.PointsInGame) max = p.Player;
                    }
                }
                
                Console.WriteLine($"Player {_players[max]!.Name}, ha preso le carte");
                olpPlayerTable = max;
                for (int i = 0; i < (exit ? _table.Count : (int)_gameMode); ++i)
                {
                    _players[max]!.PushMazzo(_table.Pop().card);
                }

                if (_players[max]!.Mode == PlayerModes.User)
                {
                    await WebSocketsController.SendWSMessage(_players[max]!.WebSocket!, new
                    {
                        Status = "pickTableCards"
                    }, _players[max]!.SocketReceiveResult!);
                    await WebSocketsController.SendWSMessage(_players[max]!.WebSocket!, new
                    {
                        Status = "Points",
                        Value = _players[max]!.GetMazzoPoints()
                    }, _players[max]!.SocketReceiveResult!);
                }

                for (int i = 0; i < _players.Length; i++)
                {
                    if (i == max || _players[i]!.Mode != PlayerModes.User) continue;

                    await WebSocketsController.SendWSMessage(_players[i]!.WebSocket!, new
                    {
                        Status = "pickedTableCards",
                        Player = i
                    }, _players[i]!.SocketReceiveResult!);
                }
                
                for (int i = 0; i < _players.Length; i++)
                {
                    if (_players[i]!.Cards.Count == 0)
                    {
                        Console.WriteLine(_players[i]!.Name);
                        exit = true; // exit while
                        break; // break the for
                    }
                }
            }

            int winnerId = 0;
            for (byte i = 0; i < _players.Length; ++i)
            {
                if (_players[winnerId]!.GetMazzoPoints() < _players[i]!.GetMazzoPoints())
                {
                    winnerId = i;
                }
            }

            if (_players[winnerId]!.Mode == PlayerModes.User)
                await WebSocketsController.SendWSMessage(_players[winnerId]!.WebSocket!, new
                {
                    Status = "YouWin"
                }, _players[winnerId]!.SocketReceiveResult!);

            for (int i = 0; i < _players.Length; ++i)
            {
                if (i == winnerId) continue;
                if (_players[i]!.Mode == PlayerModes.User)
                    await WebSocketsController.SendWSMessage(_players[i]!.WebSocket!, new
                    {
                        Status = "WinnerIs",
                        PlayerId = winnerId,
                        Name = _players[winnerId]!.Name
                    }, _players[i]!.SocketReceiveResult!);
            }

            for (int i = 0; i < _players.Length; ++i)
            {
                if (_players[i]!.Mode == PlayerModes.User)
                    await _players[i]!.WebSocket!.CloseAsync(WebSocketCloseStatus.Empty, null, CancellationToken.None);
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
        _start.Interrupt();
        _stopped = true;
        Console.WriteLine("interrupted");
        GameGenerationController.CloseGameId(_gameId);
    }
    
    public int AddPlayer(Player player)
    {
        for (int i = 0; i < _players.Length; i++)
        {
            if (_players[i] is null)
            {
                _players[i] = player;
                
                _playerReceiveQueue.Add(i, new Queue<SocketReceive>());
                
                if (_players[i]!.Mode == PlayerModes.User) _usersToWait--;
                if (_usersToWait == 0)
                {
                    // game start...
                    
                    //Console.WriteLine($"WebSocket:{players[i-1].WebSocket.State}");
                    
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("STARTTTT!!");
                    Console.ResetColor();
                    
                    _start = new Thread(this.Start);
                    _start.Start();
                }
                else
                {
                    Console.WriteLine($"Player entered, waiting for {_usersToWait}");
                }
                
                return i;
            }
        }

        throw new Exception("Game is full");
    }

    public void PlayerDisconnect(int index)
    {
        _userDisconnected++;
        if (_userDisconnected == _userNumber)
        {
            Console.WriteLine(_userDisconnected + " " + _userNumber);
            CloseGame();
            return;
        }
        if (_players[index] is not null)
        {
            if (_players[index]!.Mode != PlayerModes.User)
            {
                throw new ArgumentException($"index: {nameof(index)}, not a User player");
            }

            _players[index]!.Mode = PlayerModes.UserDisconnected;
            _players[index]!.WebSocket = null;
            return;
        }
        
        throw new ArgumentException($"index: {nameof(index)}, not a player in this game");
    }

    public int PlayerReconnect(WebSocket webSocket, WebSocketReceiveResult wsr)
    {
        _userDisconnected--;
        for (int i = 0; i < _players.Length; i++)
        {
            if (_players[i] is not null && _players[i]!.Mode == PlayerModes.UserDisconnected)
            {
                _players[i]!.Mode = PlayerModes.User;
                _players[i]!.WebSocket = webSocket;
                _players[i]!.SocketReceiveResult = wsr;
                return i;
            }
        }

        throw new Exception("No players to be reconnected");
    }
}