namespace Briscola_Back_End.Models;

public enum PlayerModes
{
    User,
    Ai,
    UserDisconnected // Ai
}

// public delegate Card DropCard(ref Player player);
// public delegate void PickCards(Stack<Card> mazzo, int nCards, ref Player player);

public interface IPlayer
{
    // public PlayerModes Mode { get; set; }
    public string Name { get; }
    
    public byte PointsInGame { get; set; }
    public bool TurnBriscola { get; set; }
    public List<Card> Cards { get; set; }
    private Stack<Card> Mazzo { get; }
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