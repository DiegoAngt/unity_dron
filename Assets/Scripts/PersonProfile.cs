using UnityEngine;

public enum ItemColor { Red, Blue, Green, Yellow, Orange, Black, White, Gray }
public enum HeadgearType { None, Cap, Hat, ConstructionHelmet }

public class PersonProfile : MonoBehaviour
{
    [Header("Chaqueta")]
    public bool hasJacket;
    public ItemColor jacketColor;

    [Header("Tocado (cabeza)")]
    public HeadgearType headgearType = HeadgearType.None;
    public ItemColor headgearColor;

    [Header("Accesorios")]
    public bool hasBackpack;

    [TextArea] public string publicDescription;

    // Puntuación de coincidencia suave
    public float ScoreMatch(PersonDescriptor target)
    {
        float score = 0f;
        if (target.requireJacket)
        {
            if (hasJacket) score += 0.5f;
            if (hasJacket && jacketColor == target.jacketColor) score += 0.75f;
        }

        if (target.headgearType != HeadgearType.None)
        {
            if (headgearType == target.headgearType) score += 0.75f;
            if (headgearType == target.headgearType && headgearColor == target.headgearColor) score += 0.75f;
        }

        if (target.requireBackpackSpecified)
            if (hasBackpack == target.hasBackpack) score += 0.5f;

        return score; // umbral sugerido 1.25–1.5
    }
}

[System.Serializable]
public struct PersonDescriptor : System.IEquatable<PersonDescriptor>
{
    // Chaqueta
    public bool requireJacket;
    public ItemColor jacketColor;

    // Tocado
    public HeadgearType headgearType;
    public ItemColor headgearColor; // ignorar si headgearType == None

    // Mochila (no importa / con / sin)
    public bool requireBackpackSpecified;
    public bool hasBackpack;

    public bool Equals(PersonDescriptor other)
    {
        bool headgearEqual = headgearType == other.headgearType &&
                             (headgearType == HeadgearType.None || headgearColor == other.headgearColor);

        bool jacketEqual = (requireJacket == other.requireJacket) &&
                           (!requireJacket || jacketColor == other.jacketColor);

        bool backpackEqual = (requireBackpackSpecified == other.requireBackpackSpecified) &&
                             (!requireBackpackSpecified || hasBackpack == other.hasBackpack);

        return jacketEqual && headgearEqual && backpackEqual;
    }

    public override bool Equals(object obj) => obj is PersonDescriptor other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + (requireJacket ? 1 : 0);
            if (requireJacket) h = h * 31 + (int)jacketColor;

            h = h * 31 + (int)headgearType;
            if (headgearType != HeadgearType.None) h = h * 31 + (int)headgearColor;

            h = h * 31 + (requireBackpackSpecified ? 1 : 0);
            if (requireBackpackSpecified) h = h * 31 + (hasBackpack ? 1 : 0);
            return h;
        }
    }

    public override string ToString()
    {
        string j = requireJacket ? $"chaqueta {jacketColor}" : "sin chaqueta";
        string h = headgearType switch
        {
            HeadgearType.None => "sin tocado",
            HeadgearType.Cap => $"gorra {headgearColor}",
            HeadgearType.Hat => $"sombrero {headgearColor}",
            HeadgearType.ConstructionHelmet => $"casco de construcción {headgearColor}",
            _ => "tocado"
        };
        string b = requireBackpackSpecified ? (hasBackpack ? "con mochila" : "sin mochila") : "mochila (no importa)";
        return $"{j}, {h}, {b}";
    }
}
