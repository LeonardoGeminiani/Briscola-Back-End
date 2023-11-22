namespace Briscola_Back_End.Models;

public enum CardFamilies {
    Spade,
    Coppe,
    Denari,
    Bastoni
}

public class Card
{
    // img
    // value --done
    // family --done
    // value in game --done
    private readonly byte _value;
    public readonly CardFamilies Family;
    private static readonly ArgumentException ExValue = new("Invalid value for a Card, must be a number between 1 and 10");
    public Card(byte value, CardFamilies family)
    {
        if(value == 0 || value > 10) throw ExValue;
        this._value = value;
        this.Family = family;
    }

    public byte ValueInGame {
        get => this._value switch
        {
            1 => 9,
            3 => 8,
            10 => 7,
            2 => 0,
            _ => (byte)(this._value - 3),
        };
    }

    public byte Value {
        get => this._value switch
        {
            1 => 11,
            3 => 10,
            10 => 4,
            9 => 3,
            8 => 2,
            _ => 0,
        };
    }

    public int GetCardNumber() => this._value;
    public CardFamilies GetCardFamily() => this.Family;
    public static string GetCardName(byte value){
        return value switch
        {
            1 => "Asso",
            8 => "Donna",
            9 => "Cavallo",
            10 => "Re",
            _ => value.ToString(),
        };
    }

    public override bool Equals(object? obj)
    {
        if (obj is DtoCard d)
        {
            return d.Family == Family && d.Number == this._value;
        }
        
        return base.Equals(obj);
    }

    override public string ToString() {
        return Card.GetCardName(this._value) + " di " + this.Family.ToString();
    }
}