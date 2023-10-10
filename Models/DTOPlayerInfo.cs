namespace Briscola_Back_End.Models;

public class PlayerCardCnt
{
    public int CardsNumber { get; set; }
    public string PlayerName { get; set; }
    public int PlayerId { get; set; }
}
    
public class DTOPlayerInfo
{
    public string Status
    {
        get
        {
            return "info";
        }
    }

    public int PlayerId { get; set; }
    public string PlayerName { get; set; }
    public int CardsNumber { get; set; }
    public int MazzoCount { get; set; }
    public byte PlayerInGamePoints { get; set; }
    public PlayerCardCnt[] Players { get; set; }
}