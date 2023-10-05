namespace Briscola_Back_End.Models;

public struct PlayerCardCnt
{
    public int CardsNumber;
    public string PlayerName;
    public int PlayerId;
}
    
public class DTOPlayerInfo
{
    public string PlayerName { get; set; }
    public int CardsNumber { get; set; }
    public int MazzoCount { get; set; }
    public byte PlayerInGamePoints { get; set; }
    public PlayerCardCnt[] Players { get; set; }
}