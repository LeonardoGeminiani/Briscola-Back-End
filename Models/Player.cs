using System.Net.WebSockets;

namespace Briscola_Back_End.Models;

public enum PlayerModes
{
    User,
    Ai,
    UserDisconnected // Ai
}

public class Player
{
    public PlayerModes Mode { get; set; }
    public string Name {get; private set; }

    public WebSocket? WebSocket = null;

    public WebSocketReceiveResult? SocketReceiveResult = null;
    
    public Player(string name, PlayerModes mode)
    {
        Mode = mode;
        Name = name;
    }

    public Player(string name, WebSocket webSocket, WebSocketReceiveResult webSocketReceiveResult) : this(name, PlayerModes.User)
    {
        this.WebSocket = webSocket;
        this.SocketReceiveResult = webSocketReceiveResult;
    }
    
    public byte PointsInGame = 0;
    public bool TurnBriscola = false;
    public List<Card> Cards = new();
    readonly Stack<Card> Mazzo = new();

    public int MazzoCount() => Mazzo.Count;
    
    public void PushMazzo(Card card) => Mazzo.Push(card);
    
    public byte GetMazzoPoints() {
        byte ret = 0;
        foreach(var c in Mazzo){
            ret += c.Value;
        }
        return ret;
    }
}