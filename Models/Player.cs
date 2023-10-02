namespace Briscola_Back_End.Models;

public enum PlayerModes
{
    User,
    Ai,
    UserDisconnected // Ai
}

// public delegate Card DropCard(ref Player player);
// public delegate void PickCards(Stack<Card> mazzo, int nCards, ref Player player);

public class Player
{
    // public PlayerModes Mode { get; set; }
    public string Name {get; private set; }
    
    public Player(string name /*, PlayerModes mode ,DropCard selectDropCard, PickCards pickCards*/)
    {
        // Mode = mode;
        Name = name;
        // SelectDropCard = selectDropCard;
        // PickCards = pickCards;
    }
    
    public byte PointsInGame = 0;
    public bool TurnBriscola = false;
    public List<Card> Cards = new();
    readonly Stack<Card> Mazzo = new();
    public void PushMazzo(Card card) => Mazzo.Push(card);
    
    public byte GetMazzoPoints() {
        byte ret = 0;
        foreach(var c in Mazzo){
            ret += c.Value;
        }
        return ret;
    }

    // public DropCard SelectDropCard;
    //
    // // (Stack<Card> Mazzo, int Ncards, ...)
    // public PickCards PickCards;
}