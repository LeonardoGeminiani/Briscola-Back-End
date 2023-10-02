namespace Briscola_Back_End.Models;

public class AiPlayer : Player
{
    private bool CreatedFromUsrPlayer;
    
    public AiPlayer(string name, bool createdFromUsrPlayer = false) :base(name)
    {
        CreatedFromUsrPlayer = createdFromUsrPlayer;
    }
}